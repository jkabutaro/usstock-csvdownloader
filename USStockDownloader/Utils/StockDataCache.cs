using System.Text.Json;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using USStockDownloader.Services;

namespace USStockDownloader.Utils
{
    public class StockDataCacheInfo
    {
        public string Symbol { get; set; } = string.Empty;
        public DateTime LastUpdate { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime LastTradingDate { get; set; } 
    }

    public static class StockDataCache
    {
        // SQLiteサービスのシングルトンインスタンス
        private static StockDataCacheSqliteService? _sqliteService;
        private static readonly object _lock = new object();
        // 静的なロガーインスタンス
        private static ILogger<StockDataCacheSqliteService> _logger;

        private static readonly TimeSpan MarketOpenTime = TimeSpan.FromHours(9.5);
        private static readonly TimeSpan MarketCloseTime = TimeSpan.FromHours(16);

        // クラス初期化
        static StockDataCache()
        {
            // 共通LoggerFactoryを使用
            _logger = AppLoggerFactory.CreateLogger<StockDataCacheSqliteService>();
        }

        // SQLiteサービスの初期化
        private static StockDataCacheSqliteService GetSqliteService()
        {
            if (_sqliteService == null)
            {
                lock (_lock)
                {
                    if (_sqliteService == null)
                    {
                        _sqliteService = new StockDataCacheSqliteService(CacheManager.CacheDirectory, _logger);
                    }
                }
            }
            return _sqliteService;
        }

        private static DateTime ConvertToEasternTime(DateTime localTime)
        {
            try
            {
                var utcTime = localTime.ToUniversalTime();

                try
                {
                    var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                    return TimeZoneInfo.ConvertTimeFromUtc(utcTime, easternZone);
                }
                catch
                {
                    try
                    {
                        var easternZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
                        return TimeZoneInfo.ConvertTimeFromUtc(utcTime, easternZone);
                    }
                    catch
                    {
                        var isDst = IsInDaylightSavingTime(utcTime);
                        var offset = isDst ? -4 : -5;  
                        return utcTime.AddHours(offset);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"時間変換に失敗しました。ローカル時間を使用します。エラー: {ex.Message} (Time conversion failed. Using local time. Error: {ex.Message})");
                return localTime;
            }
        }

        private static bool IsInDaylightSavingTime(DateTime date)
        {
            var year = date.Year;

            var marchSecondSunday = new DateTime(year, 3, 1).AddDays((14 - (int)new DateTime(year, 3, 1).DayOfWeek) % 7);
            var dstStart = marchSecondSunday.AddHours(2); 

            var novemberFirstSunday = new DateTime(year, 11, 1).AddDays((7 - (int)new DateTime(year, 11, 1).DayOfWeek) % 7);
            var dstEnd = novemberFirstSunday.AddHours(2); 

            return date >= dstStart && date < dstEnd;
        }

        private static bool IsMarketHours()
        {
            try
            {
                var easternTime = ConvertToEasternTime(DateTime.Now);
                
                if (easternTime.DayOfWeek == DayOfWeek.Saturday || easternTime.DayOfWeek == DayOfWeek.Sunday)
                    return false;

                if (IsUsHoliday(easternTime))
                    return false;

                var timeOfDay = easternTime.TimeOfDay;
                var isMarketOpen = timeOfDay >= MarketOpenTime && timeOfDay <= MarketCloseTime;

                _logger.LogDebug($"市場状態チェック - 東部時間: {easternTime:yyyy-MM-dd HH:mm:ss}、市場は{(isMarketOpen ? "開いています" : "閉じています")} (Market status check - Eastern Time: {easternTime:yyyy-MM-dd HH:mm:ss}, Market is {(isMarketOpen ? "open" : "closed")})");
                return isMarketOpen;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"市場時間チェックに失敗しました。エラー: {ex.Message} (Market hours check failed. Error: {ex.Message})");
                return false;
            }
        }

        /// <summary>
        /// 市場が現在閉じているかどうかを確認します
        /// </summary>
        /// <returns>市場が閉じている場合はtrue、開いている場合はfalse</returns>
        public static bool IsMarketClosed()
        {
            return !IsMarketHours();
        }

        /// <summary>
        /// 指定された日付が将来の取引日である場合、最新の取引日を返します。
        /// 現在の東部時間の日付より後の日付や、同じ日でも市場がまだ閉じていない場合は
        /// 最新の取引日に調整します。
        /// </summary>
        /// <param name="date">調整する日付</param>
        /// <returns>調整された日付（最新の取引日）</returns>
        public static DateTime AdjustToLatestTradingDay(DateTime date)
        {
            try
            {
                // 現在の東部時間を取得
                DateTime easternTime = ConvertToEasternTime(DateTime.Now);
                
                // 指定された日付が現在の東部時間の日付より後の場合
                if (date.Date > easternTime.Date)
                {
                    // 最新の取引日を返す
                    return GetLastTradingDay();
                }
                
                // 指定された日付が現在の東部時間の日付と同じで、市場がまだ閉じていない場合
                if (date.Date == easternTime.Date && !IsMarketClosed())
                {
                    // 前営業日を返す
                    return GetPreviousTradingDay(easternTime);
                }
                
                // それ以外の場合は指定された日付をそのまま返す
                return date;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"日付調整に失敗しました。元の日付を使用します。エラー: {ex.Message} (Date adjustment failed. Using original date. Error: {ex.Message})");
                return date;
            }
        }

        /// <summary>
        /// 指定された日付の前営業日を返します。
        /// </summary>
        /// <param name="date">基準日</param>
        /// <returns>前営業日</returns>
        private static DateTime GetPreviousTradingDay(DateTime date)
        {
            var previousDay = date.AddDays(-1);
            
            while (true)
            {
                if (!IsUsHoliday(previousDay) && 
                    previousDay.DayOfWeek != DayOfWeek.Saturday && 
                    previousDay.DayOfWeek != DayOfWeek.Sunday)
                {
                    return previousDay.Date;
                }
                previousDay = previousDay.AddDays(-1);
            }
        }

        /// <summary>
        /// 指定された日付の翌営業日を返します。
        /// </summary>
        /// <param name="date">基準日</param>
        /// <returns>翌営業日</returns>
        public static DateTime GetNextTradingDay(DateTime date)
        {
            var nextDay = date.AddDays(1);

            while (true)
            {
                if (!IsUsHoliday(nextDay) &&
                    nextDay.DayOfWeek != DayOfWeek.Saturday &&
                    nextDay.DayOfWeek != DayOfWeek.Sunday)
                {
                    return nextDay.Date;
                }
                nextDay = nextDay.AddDays(1);
            }
        }

        public static DateTime GetLastTradingDay()
        {
            var now = ConvertToEasternTime(DateTime.Now);
            var date = now.Date;
            
            if (now.TimeOfDay > MarketCloseTime && !IsUsHoliday(date) && 
                date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
            {
                return date;
            }
            
            while (true)
            {
                date = date.AddDays(-1);
                if (!IsUsHoliday(date) && date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                {
                    return date;
                }
            }
        }

        private static bool IsUsHoliday(DateTime date)
        {
            var month = date.Month;
            var day = date.Day;
            var dayOfWeek = date.DayOfWeek;

            if ((month == 1 && day == 1) ||   
                (month == 7 && day == 4) ||   
                (month == 12 && day == 25))   
                return true;

            if ((month == 1 && dayOfWeek == DayOfWeek.Monday && day >= 15 && day <= 21) || 
                (month == 2 && dayOfWeek == DayOfWeek.Monday && day >= 15 && day <= 21) || 
                (month == 9 && dayOfWeek == DayOfWeek.Monday && day >= 1 && day <= 7))     
                return true;

            if (month == 5 && dayOfWeek == DayOfWeek.Monday && 
                day > (31 - 7) && day <= 31)
                return true;

            if (month == 11 && dayOfWeek == DayOfWeek.Thursday && 
                day >= 22 && day <= 28)
                return true;

            return false;
        }

        private static async Task<Dictionary<string, StockDataCacheInfo>> LoadCacheAsync()
        {
            try
            {
                var sqliteService = GetSqliteService();
                return await sqliteService.GetStockDataCacheInfoAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"キャッシュの読み込みに失敗しました: {ex.Message} (Failed to load cache: {ex.Message})");
                return new Dictionary<string, StockDataCacheInfo>();
            }
        }

        private static Dictionary<string, StockDataCacheInfo> LoadCache()
        {
            return LoadCacheAsync().GetAwaiter().GetResult();
        }

        private static async Task SaveCacheAsync(Dictionary<string, StockDataCacheInfo> cache)
        {
            try
            {
                var sqliteService = GetSqliteService();
                await sqliteService.SaveStockDataCacheInfoAsync(cache);
            }
            catch (Exception ex)
            {
                _logger.LogError($"キャッシュの保存に失敗しました: {ex.Message} (Failed to save cache: {ex.Message})");
            }
        }

        private static void SaveCache(Dictionary<string, StockDataCacheInfo> cache)
        {
            SaveCacheAsync(cache).GetAwaiter().GetResult();
        }

        public static bool NeedsUpdate(string symbol, DateTime startDate, DateTime endDate, TimeSpan maxAge)
        {
            try
            {
                // 終了日を最新の取引日に自動調整
                DateTime adjustedEndDate = AdjustToLatestTradingDay(endDate);
                
                var cache = LoadCache();
                if (cache.TryGetValue(symbol, out var info))
                {
                    bool isMarketOpen = IsMarketHours();
                    
                    var lastTradingDay = GetLastTradingDay();
                    
                    if (isMarketOpen)
                    {
                        _logger.LogDebug($"銘柄 {symbol}: 市場が開いているため更新が必要です (Symbol {symbol}: Market is open, update required)");
                        return true;
                    }

                    // 前回DLしてから数時間はダウンロードしないロジックを削除
                    
                    if (info.LastTradingDate < lastTradingDay)
                    {
                        _logger.LogDebug($"銘柄 {symbol}: キャッシュに最新の取引日データがないため更新が必要です (Symbol {symbol}: Cache doesn't have latest trading day data, update required)");
                        return true;
                    }
                    
                    // 既存のキャッシュの日付範囲の確認
                    DateTime cacheStartDate = info.StartDate;
                    DateTime cacheEndDate = info.EndDate;
                    
                    // リクエストがキャッシュの範囲内に完全に含まれる場合
                    if (startDate >= cacheStartDate && adjustedEndDate <= cacheEndDate)
                    {
                        _logger.LogDebug($"銘柄 {symbol}: リクエストがキャッシュの範囲内に完全に含まれています。キャッシュを使用します (Symbol {symbol}: Request completely within cache range, using cache)");
                        return false;
                    }
                    
                    // 部分的なキャッシュが存在する場合のインクリメンタルダウンロード処理
                    if (startDate < cacheStartDate && adjustedEndDate > cacheEndDate)
                    {
                        // 要求が両端で範囲外の場合 - 両端分をダウンロードする必要あり
                        _logger.LogDebug($"銘柄 {symbol}: リクエストがキャッシュの両端を超えています。更新が必要です (Symbol {symbol}: Request exceeds cache range on both ends, update required)");
                        _logger.LogDebug($"  リクエスト: {startDate:yyyy-MM-dd} ～ {adjustedEndDate:yyyy-MM-dd}, キャッシュ: {cacheStartDate:yyyy-MM-dd} ～ {cacheEndDate:yyyy-MM-dd} (Request: {startDate:yyyy-MM-dd} to {adjustedEndDate:yyyy-MM-dd}, Cache: {cacheStartDate:yyyy-MM-dd} to {cacheEndDate:yyyy-MM-dd})");
                        return true;
                    }
                    else if (startDate < cacheStartDate)
                    {
                        // 開始日のみ範囲外の場合 - 過去データ分をダウンロードする必要あり
                        _logger.LogDebug($"銘柄 {symbol}: リクエスト開始日がキャッシュ範囲前です。過去データの更新が必要です (Symbol {symbol}: Request start date before cache range, historical update required)");
                        _logger.LogDebug($"  リクエスト開始日: {startDate:yyyy-MM-dd}, キャッシュ開始日: {cacheStartDate:yyyy-MM-dd} (Request start: {startDate:yyyy-MM-dd}, Cache start: {cacheStartDate:yyyy-MM-dd})");
                        return true;
                    }
                    else if (adjustedEndDate > cacheEndDate)
                    {
                        // 終了日のみ範囲外の場合 - 新規データ分をダウンロードする必要あり
                        
                        // 特別なケース: リクエストの終了日が現在日付で、キャッシュの終了日が前日（最新取引日）である場合
                        bool isSpecialCase = adjustedEndDate.Date == DateTime.Now.Date && 
                                            cacheEndDate.Date == lastTradingDay.Date;
                        
                        if (isSpecialCase)
                        {
                            _logger.LogDebug($"銘柄 {symbol}: リクエスト終了日が現在日付で、キャッシュの終了日が最新取引日のため、キャッシュを使用します (Symbol {symbol}: Request end date is today, cache end date is the latest trading day, using cache)");
                            return false;
                        }
                        
                        _logger.LogDebug($"銘柄 {symbol}: リクエスト終了日がキャッシュ範囲後です。新規データの更新が必要です (Symbol {symbol}: Request end date after cache range, new data update required)");
                        _logger.LogDebug($"  リクエスト終了日: {adjustedEndDate:yyyy-MM-dd}, キャッシュ終了日: {cacheEndDate:yyyy-MM-dd} (Request end: {adjustedEndDate:yyyy-MM-dd}, Cache end: {cacheEndDate:yyyy-MM-dd})");
                        return true;
                    }

                    _logger.LogDebug($"銘柄 {symbol}: キャッシュを使用します、更新は不要です (Symbol {symbol}: Using cache, no update required)");
                    return false;
                }
                
                _logger.LogDebug($"銘柄 {symbol}: キャッシュにないため更新が必要です (Symbol {symbol}: Not in cache, update required)");
                return true; 
            }
            catch (Exception ex)
            {
                _logger.LogError($"銘柄 {symbol}: キャッシュ確認中にエラーが発生しました: {ex.Message}、更新が必要です (Symbol {symbol}: Error checking cache: {ex.Message}, update required)");
                return true; 
            }
        }

        public static async Task UpdateCacheAsync(string symbol, DateTime startDate, DateTime endDate, List<Models.StockData>? stockDataList = null)
        {
            try
            {
                var sqliteService = GetSqliteService();
                var cache = await sqliteService.GetStockDataCacheInfoAsync();
                
                // データが取得できなかった場合はキャッシュから削除
                if (stockDataList == null || !stockDataList.Any())
                {
                    if (cache.ContainsKey(symbol))
                    {
                        await sqliteService.RemoveStockDataCacheInfoAsync(symbol);
                        _logger.LogDebug($"銘柄 {symbol}: データが取得できなかったためキャッシュから削除しました (Symbol {symbol}: Removed from cache because no data was retrieved)");
                    }
                    return;
                }
                
                // 常に要求した日付範囲でキャッシュを更新する
                _logger.LogDebug($"銘柄 {symbol}: リクエスト範囲 {startDate:yyyy-MM-dd} ～ {endDate:yyyy-MM-dd} でキャッシュを更新します (Symbol {symbol}: Updating cache with requested range {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd})");
                
                var cacheInfo = new StockDataCacheInfo
                {
                    Symbol = symbol,
                    LastUpdate = DateTime.Now,
                    StartDate = startDate,
                    EndDate = endDate,
                    LastTradingDate = GetLastTradingDay() 
                };
                
                await sqliteService.UpdateStockDataCacheInfoAsync(symbol, cacheInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError($"銘柄 {symbol}: キャッシュの更新に失敗しました: {ex.Message} (Symbol {symbol}: Failed to update cache: {ex.Message})");
            }
        }

        public static void UpdateCache(string symbol, DateTime startDate, DateTime endDate, List<Models.StockData>? stockDataList = null)
        {
            UpdateCacheAsync(symbol, startDate, endDate, stockDataList).GetAwaiter().GetResult();
        }
    }
}
