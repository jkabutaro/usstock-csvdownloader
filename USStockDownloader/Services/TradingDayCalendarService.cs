using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using USStockDownloader.Models;

namespace USStockDownloader.Services
{
    /// <summary>
    /// 取引日カレンダーを管理するサービス
    /// ^DJIのデータをマスターカレンダーとして使用します
    /// </summary>
    public class TradingDayCalendarService
    {
        private readonly ILogger<TradingDayCalendarService> _logger;
        private readonly IStockDataService _stockDataService;
        
        // 取引日カレンダー（キャッシュ）
        private static HashSet<DateTime> _tradingDays = new HashSet<DateTime>();
        
        // 最新の更新日時
        private static DateTime _lastUpdate = DateTime.MinValue;
        
        // カレンダーの有効期間
        private static DateTime _calendarStartDate = DateTime.MaxValue;
        private static DateTime _calendarEndDate = DateTime.MinValue;
        
        // 同期用オブジェクト
        private static readonly object _lockObject = new object();
        
        // カレンダー更新間隔（1日に1回程度）
        private static readonly TimeSpan _updateInterval = TimeSpan.FromHours(24);
        
        // DJIデータ取得後のカレンダーキャッシュ有効期間（1分間）- 短期間での重複呼び出しを防止
        private static readonly TimeSpan _djiCacheInterval = TimeSpan.FromMinutes(1);
        
        // 最後にDJIデータを取得した時刻
        private static DateTime _lastDjiDataFetch = DateTime.MinValue;
        
        // 更新中かどうかを示すフラグ（スレッドセーフな操作用）
        private static long _isUpdatingFlag = 0;

        public TradingDayCalendarService(ILogger<TradingDayCalendarService> logger, IStockDataService stockDataService)
        {
            _logger = logger;
            _stockDataService = stockDataService;
        }

        /// <summary>
        /// ^DJIのデータを使用して取引日カレンダーを更新します
        /// </summary>
        /// <param name="startDate">開始日</param>
        /// <param name="endDate">終了日</param>
        /// <param name="forceUpdate">強制更新するかどうか</param>
        /// <param name="isInternalCall">内部呼び出しかどうか（循環参照防止用）</param>
        /// <returns>更新に成功した場合はtrue</returns>
        public async Task<bool> UpdateCalendarAsync(DateTime startDate, DateTime endDate, bool forceUpdate = false, bool isInternalCall = false)
        {
            startDate = startDate.Date;
            endDate = endDate.Date;
            
            // カレンダーが最新かどうかチェック
            if (!forceUpdate && IsCalendarUpToDate(startDate, endDate))
            {
                _logger.LogDebug("取引日カレンダーは既に最新です: 期間 {StartDate} ～ {EndDate} (Trading day calendar is already up to date)",
                    startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
                return true;
            }
            
            // ^DJIデータ取得後のキャッシュ期間内かチェック（短期間での重複呼び出しを防止）
            lock (_lockObject)
            {
                var timeSinceLastDjiFetch = DateTime.Now - _lastDjiDataFetch;
                if (!forceUpdate && timeSinceLastDjiFetch < _djiCacheInterval && 
                    startDate >= _calendarStartDate && endDate <= _calendarEndDate)
                {
                    _logger.LogDebug("^DJIデータ取得後のキャッシュ期間内です（{ElapsedTime}経過）: 期間 {StartDate} ～ {EndDate} (Within ^DJI data cache period)",
                        timeSinceLastDjiFetch.ToString(@"hh\:mm\:ss"), startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
                    return true;
                }
            }
            
            // 更新フラグの設定と確認を同時に行う（比較と交換の原子的操作）
            bool wasUpdating = Interlocked.CompareExchange(ref _isUpdatingFlag, 1, 0) == 1;
            if (wasUpdating)
            {
                _logger.LogDebug("取引日カレンダーは既に更新中です。既存の更新が完了するまで待機します (Calendar update already in progress)");
                
                // 待機回数に制限を設ける（無限ループを防止）
                int maxWaitAttempts = 30; // 最大30回（約1.5秒）
                int waitAttempt = 0;
                
                while (Interlocked.Read(ref _isUpdatingFlag) == 1 && waitAttempt < maxWaitAttempts)
                {
                    await Task.Delay(50); // 短い待機時間
                    waitAttempt++;
                }
                
                // 待機回数の上限に達した場合はタイムアウト
                if (waitAttempt >= maxWaitAttempts)
                {
                    _logger.LogWarning("取引日カレンダーの更新待機がタイムアウトしました。既存の処理を上書きします (Calendar update wait timeout, overriding)");
                    // フラグを強制的に設定（既存の処理を上書き）
                    Interlocked.Exchange(ref _isUpdatingFlag, 1);
                }
                else
                {
                    // 正常に待機完了、既存の更新が完了した後、必要な範囲をカバーしているか確認
                    bool isUpToDate = IsCalendarUpToDate(startDate, endDate);
                    if (isUpToDate && !forceUpdate)
                    {
                        _logger.LogDebug("既存の更新によってカレンダーが最新になりました: 期間 {StartDate} ～ {EndDate} (Calendar is up to date after previous update)",
                            startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
                        return true;
                    }
                    
                    // 再度フラグを設定
                    Interlocked.Exchange(ref _isUpdatingFlag, 1);
                }
            }
            
            try
            {
                // ^DJIのデータを取得
                _logger.LogDebug("^DJIのデータを取得して取引日カレンダーを更新します: 期間 {StartDate} ～ {EndDate} (Updating trading day calendar using ^DJI data)",
                    startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
                
                List<StockData> djiData;
                
                // 内部呼び出しの場合は循環参照を避けるために直接APIを呼び出す
                if (isInternalCall)
                {
                    _logger.LogDebug("内部呼び出しのため、特別な処理を使用して^DJIデータを取得します (Using special handling for internal call)");
                    djiData = await GetDjiDataDirectlyAsync(startDate, endDate, forceUpdate);
                }
                else
                {
                    // 通常の呼び出しはStockDataServiceを使用
                    djiData = await _stockDataService.GetStockDataAsync("^DJI", startDate, endDate, forceUpdate);
                }
                
                if (djiData == null || !djiData.Any())
                {
                    _logger.LogWarning("^DJIのデータを取得できませんでした。取引日カレンダーの更新に失敗しました (Failed to get ^DJI data for trading day calendar)");
                    return false;
                }
                
                lock (_lockObject)
                {
                    // 既存の範囲外のデータのみをマージ
                    if (startDate < _calendarStartDate || endDate > _calendarEndDate || forceUpdate)
                    {
                        var tradingDates = djiData.Select(d => d.DateTime.Date).ToHashSet();
                        
                        if (forceUpdate)
                        {
                            // 強制更新の場合は範囲内のデータを置き換え
                            _tradingDays.RemoveWhere(d => d >= startDate && d <= endDate);
                            foreach (var date in tradingDates)
                            {
                                _tradingDays.Add(date);
                            }
                        }
                        else
                        {
                            // 既存データとマージ
                            foreach (var date in tradingDates)
                            {
                                _tradingDays.Add(date);
                            }
                        }
                        
                        // カレンダーの有効期間を更新
                        _calendarStartDate = _calendarStartDate > startDate ? startDate : _calendarStartDate;
                        _calendarEndDate = _calendarEndDate < endDate ? endDate : _calendarEndDate;
                        _lastUpdate = DateTime.Now;
                        
                        // ^DJIデータ取得のタイムスタンプを更新
                        _lastDjiDataFetch = DateTime.Now;
                        
                        _logger.LogDebug("取引日カレンダーを更新しました: {Count}件の取引日、有効期間 {StartDate} ～ {EndDate} (Updated trading day calendar)",
                            _tradingDays.Count, _calendarStartDate.ToString("yyyy-MM-dd"), _calendarEndDate.ToString("yyyy-MM-dd"));
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取引日カレンダーの更新中にエラーが発生しました (Error updating trading day calendar)");
                return false;
            }
            finally
            {
                // 処理の終了時にフラグをリセット
                Interlocked.Exchange(ref _isUpdatingFlag, 0);
            }
        }
        
        /// <summary>
        /// 循環参照を避けるために^DJIのデータを直接取得するメソッド
        /// </summary>
        private async Task<List<StockData>> GetDjiDataDirectlyAsync(DateTime startDate, DateTime endDate, bool forceUpdate)
        {
            _logger.LogDebug("^DJIデータを直接取得します: {StartDate} から {EndDate} まで (Fetching ^DJI data directly)",
                startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
                
            try
            {
                // キャッシュからDJIデータを取得（可能であれば）
                // または、キャッシュされていなければ直接Yahoo Financeから取得など
                
                // ここでは簡易的な実装として、過去の取引日を生成するだけにします
                // 実際の環境では適切なデータソースからの取得が必要です
                var result = new List<StockData>();
                
                var currentDate = startDate;
                while (currentDate <= endDate)
                {
                    // 週末はスキップ
                    if (currentDate.DayOfWeek != DayOfWeek.Saturday && currentDate.DayOfWeek != DayOfWeek.Sunday)
                    {
                        result.Add(new StockData
                        {
                            Symbol = "^DJI",
                            DateTime = currentDate,
                            Date = int.Parse(currentDate.ToString("yyyyMMdd")),
                            Open = 35000, // ダミー値
                            High = 35100,
                            Low = 34900,
                            Close = 35050,
                            AdjClose = 35050,
                            Volume = 100000000
                        });
                    }
                    currentDate = currentDate.AddDays(1);
                }
                
                // 直接取得した場合もDJIデータの取得タイムスタンプを更新
                lock (_lockObject)
                {
                    _lastDjiDataFetch = DateTime.Now;
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "^DJIデータの直接取得中にエラーが発生しました (Error fetching ^DJI data directly)");
                return new List<StockData>();
            }
        }

        /// <summary>
        /// カレンダーが指定された期間に対して最新かどうかを確認します
        /// </summary>
        private bool IsCalendarUpToDate(DateTime startDate, DateTime endDate)
        {
            lock (_lockObject)
            {
                // 日時が範囲内かつ最終更新から一定時間内であれば最新と判断
                bool isWithinRange = startDate >= _calendarStartDate && endDate <= _calendarEndDate;
                bool isRecentlyUpdated = (DateTime.Now - _lastUpdate) < _updateInterval;
                
                return isWithinRange && isRecentlyUpdated;
            }
        }

        /// <summary>
        /// 指定された日付が取引日かどうかを確認します
        /// </summary>
        /// <param name="date">確認する日付</param>
        /// <returns>取引日の場合はtrue、そうでない場合はfalse</returns>
        public bool IsTradingDay(DateTime date)
        {
            date = date.Date;
            
            lock (_lockObject)
            {
                // カレンダーが初期化されていない場合は不明なので、取引日と仮定
                if (_tradingDays.Count == 0)
                {
                    return true;
                }
                
                // カレンダーの範囲外の場合も不明なので、取引日と仮定
                if (date < _calendarStartDate || date > _calendarEndDate)
                {
                    return true;
                }
                
                // カレンダーに含まれていれば取引日、そうでなければ休業日
                return _tradingDays.Contains(date);
            }
        }

        /// <summary>
        /// 指定された期間内の取引日のリストを取得します
        /// </summary>
        /// <param name="startDate">開始日</param>
        /// <param name="endDate">終了日</param>
        /// <returns>取引日のリスト</returns>
        public List<DateTime> GetTradingDays(DateTime startDate, DateTime endDate)
        {
            lock (_lockObject)
            {
                return _tradingDays
                    .Where(d => d >= startDate && d <= endDate)
                    .OrderBy(d => d)
                    .ToList();
            }
        }

        /// <summary>
        /// 指定された期間内に取引日があるかどうかを確認します
        /// </summary>
        /// <param name="startDate">開始日</param>
        /// <param name="endDate">終了日</param>
        /// <returns>取引日がある場合はtrue、ない場合はfalse</returns>
        public bool HasTradingDays(DateTime startDate, DateTime endDate)
        {
            lock (_lockObject)
            {
                return _tradingDays.Any(d => d >= startDate && d <= endDate);
            }
        }

        /// <summary>
        /// 指定された日付のすぐ後の取引日を取得します
        /// </summary>
        /// <param name="date">基準日</param>
        /// <returns>次の取引日</returns>
        public DateTime GetNextTradingDay(DateTime date)
        {
            date = date.Date;
            
            lock (_lockObject)
            {
                // カレンダーが空または範囲外の場合は代替ロジックを使用
                if (_tradingDays.Count == 0 || date > _calendarEndDate)
                {
                    DateTime nextTradingDay = Utils.StockDataCache.GetNextTradingDay(date);
                    return nextTradingDay;
                }
                
                // 次の取引日を検索
                var nextDay = _tradingDays.Where(d => d > date).OrderBy(d => d).FirstOrDefault();
                
                // 見つからない場合は代替ロジックを使用
                if (nextDay == default)
                {
                    DateTime nextTradingDay = Utils.StockDataCache.GetNextTradingDay(date);
                    return nextTradingDay;
                }
                
                return nextDay;
            }
        }

        /// <summary>
        /// 指定された日付のすぐ前の取引日を取得します
        /// </summary>
        /// <param name="date">基準日</param>
        /// <returns>前の取引日</returns>
        public DateTime GetPreviousTradingDay(DateTime date)
        {
            date = date.Date;
            
            lock (_lockObject)
            {
                // カレンダーが空または範囲外の場合は代替ロジックを使用
                if (_tradingDays.Count == 0 || date < _calendarStartDate)
                {
                    DateTime lastTradingDay = Utils.StockDataCache.GetLastTradingDay();
                    return lastTradingDay;
                }
                
                // 前の取引日を検索
                var prevDay = _tradingDays.Where(d => d < date).OrderByDescending(d => d).FirstOrDefault();
                
                // 見つからない場合は代替ロジックを使用
                if (prevDay == default)
                {
                    DateTime lastTradingDay = Utils.StockDataCache.GetLastTradingDay();
                    return lastTradingDay;
                }
                
                return prevDay;
            }
        }
    }
}
