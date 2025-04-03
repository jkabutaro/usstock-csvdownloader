using System.Net;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Polly;
using Polly.Retry;
using USStockDownloader.Models;
using USStockDownloader.Exceptions;
using System.IO;
using System.Collections.Concurrent;
using USStockDownloader.Utils;
using USStockDownloader.Interfaces;
using USStockDownloader.Options;
using System.Linq;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.DependencyInjection;

namespace USStockDownloader.Services;

/// <summary>
/// データ取得の目的を指定する列挙型
/// </summary>
public enum DataRetrievalPurpose
{
    /// <summary>通常のデータ取得</summary>
    Normal,
    
    /// <summary>取引日カレンダー更新用</summary>
    CalendarUpdate
}

public class StockDataService : IStockDataService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly Random _random = new Random();
    private readonly string _cacheDirPath;
    private readonly StockDataCacheSqliteService _stockDataCache;
    private readonly ITradingDayCacheService _tradingDayCache;
    private readonly AsyncRetryPolicy<List<StockData>> _retryPolicy;
    private readonly RetryOptions _retryOptions;
    
    // 取引日カレンダーサービス
    private TradingDayCalendarService? _tradingDayCalendar;

    // 上場廃止された銘柄を追跡するためのセット
    private readonly HashSet<string> _delistedSymbols = new HashSet<string>();

    // シンボルごとのキャッシュデータを保持（メモリキャッシュ）
    private readonly ConcurrentDictionary<string, SymbolCacheData> _dataCache = new ConcurrentDictionary<string, SymbolCacheData>();
    
    // 銘柄ごとの「データが存在しない期間」を追跡
    private readonly ConcurrentDictionary<string, List<DateRange>> _noDataPeriods = 
        new ConcurrentDictionary<string, List<DateRange>>();

    // 日付範囲を表すクラス
    private class DateRange
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        public DateRange(DateTime start, DateTime end)
        {
            Start = start;
            End = end;
        }

        public bool Contains(DateTime date)
        {
            return date >= Start && date <= End;
        }

        public bool Overlaps(DateRange other)
        {
            return (Start <= other.End && End >= other.Start);
        }

        public override string ToString()
        {
            return $"{Start:yyyy-MM-dd} to {End:yyyy-MM-dd}";
        }
    }

    // キャッシュデータクラスを拡張
    private class SymbolCacheData
    {
        public List<StockData> Data { get; set; } = new List<StockData>();
        public List<DateRange> NoDataPeriods { get; set; } = new List<DateRange>();
        public DateTime LastUpdateTime { get; set; } = DateTime.Now;
    }

    public StockDataService(
        HttpClient httpClient,
        ILogger<StockDataService> logger,
        string cacheDirectory,
        RetryOptions retryOptions)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cacheDirPath = cacheDirectory;
        _retryOptions = retryOptions;
        
        // SQLiteキャッシュサービスの初期化 - 共通LoggerFactoryを使用
        _stockDataCache = new StockDataCacheSqliteService(
            cacheDirectory, 
            AppLoggerFactory.CreateLogger<StockDataCacheSqliteService>());
            
        // HttpClientFactoryの作成
        var services = new ServiceCollection();
        services.AddHttpClient("YahooFinance", client =>
        {
            // 必要最小限のヘッダーのみを設定
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("Referer", "https://finance.yahoo.com/");
        });
        var serviceProvider = services.BuildServiceProvider();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            
        _tradingDayCache = new TradingDayCacheSqliteService(
            AppLoggerFactory.CreateLogger<TradingDayCacheSqliteService>(),
            new HttpClientFactoryWrapper(_httpClient));

        // リトライポリシーの設定
        _retryPolicy = Policy<List<StockData>>
            .Handle<HttpRequestException>()
            .Or<DataParsingException>()
            .WaitAndRetryAsync(
                retryOptions.MaxRetries, // 最大リトライ回数
                retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + // 指数バックオフ
                    TimeSpan.FromMilliseconds(_random.Next(0, 1000)), // ジッター
                onRetry: (outcome, timeSpan, retryCount, context) =>
                {
                    string symbol = context.ContainsKey("Symbol") ? context["Symbol"].ToString() ?? "Unknown" : "Unknown";
                    _logger.LogWarning("株価データ取得リトライ {RetryCount}/{MaxRetry}: シンボル {Symbol}, 例外: {Exception} (Retrying stock data fetch)",
                        retryCount, retryOptions.MaxRetries, symbol, outcome.Exception?.Message);
                }
            );

        // キャッシュディレクトリのパスを設定
        if (!Directory.Exists(_cacheDirPath))
        {
            Directory.CreateDirectory(_cacheDirPath);
            _logger.LogDebug("キャッシュディレクトリを作成しました (Created cache directory): {CacheDir}", _cacheDirPath);
        }
        
        // 取引日カレンダーサービスを初期化（遅延初期化）
        InitializeTradingDayCalendarService();
    }
    
    /// <summary>
    /// 取引日カレンダーサービスを初期化します（遅延初期化）
    /// </summary>
    private void InitializeTradingDayCalendarService()
    {
        // 循環参照を避けるため、TradingDayCalendarServiceの初期化はプロパティで遅延して行う
        if (_tradingDayCalendar == null)
        {
            var calendarLogger = AppLoggerFactory.CreateLogger<TradingDayCalendarService>();
            _tradingDayCalendar = new TradingDayCalendarService(calendarLogger, this);
            
            // 過去3年のデータを事前に取得してカレンダーを初期化
            var endDate = DateTime.Today;
            var startDate = endDate.AddYears(-3);
            
            // 非同期での初期化（完了を待たない）
            Task.Run(async () => {
                try
                {
                    await _tradingDayCalendar.UpdateCalendarAsync(startDate, endDate, false, true);
                    _logger.LogDebug("取引日カレンダーの初期化が完了しました: 期間 {StartDate} ～ {EndDate} (Trading day calendar initialized)",
                        startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "取引日カレンダーの初期化中にエラーが発生しました (Error initializing trading day calendar)");
                }
            });
        }
    }

    /// <summary>
    /// 指定された銘柄と日付範囲の株価データを取得します。
    /// キャッシュから利用可能なデータを最大限に活用し、不足部分のみをYahoo Financeから取得します。
    /// </summary>
    /// <param name="symbol">銘柄シンボル</param>
    /// <param name="startDate">開始日</param>
    /// <param name="endDate">終了日</param>
    /// <param name="forceUpdate">強制更新フラグ</param>
    /// <param name="purpose">データ取得の目的</param>
    /// <returns>株価データのリスト</returns>
    public async Task<List<StockData>> GetStockDataAsync(
        string symbol, 
        DateTime startDate, 
        DateTime endDate, 
        bool forceUpdate = false,
        DataRetrievalPurpose purpose = DataRetrievalPurpose.Normal)
    {
        _logger.LogDebug($"GetStockDataAsync開始: 銘柄={symbol}, 開始日={startDate:yyyy-MM-dd}, 終了日={endDate:yyyy-MM-dd}, 強制更新={forceUpdate} (Starting GetStockDataAsync)");

        // 日付の正規化（時刻情報を削除）
        startDate = startDate.Date;
        endDate = endDate.Date;

        // 上場廃止チェック
        if (_delistedSymbols.Contains(symbol))
        {
            _logger.LogWarning($"シンボル {symbol} は上場廃止されているためスキップします (Symbol is delisted, skipping)");
            return new List<StockData>();
        }

        // データが存在しない期間のチェック
        if (IsInNoDataPeriod(symbol, startDate, endDate))
        {
            _logger.LogDebug($"シンボル {symbol} の期間 {startDate:yyyy-MM-dd} から {endDate:yyyy-MM-dd} はデータが存在しないことが既知です (No data exists for this period)");
            return new List<StockData>();
        }
        
        // ^DJIの場合は特別扱い - 取引日カレンダーに使用されるので、最新データを取得する必要があるかチェック
        if (symbol == "^DJI")
        {
            _logger.LogDebug("^DJIのデータは取引日カレンダーに使用されるため、特別処理を適用します (Special handling for ^DJI as it is used for trading day calendar)");
            // 取引日カレンダーサービスを初期化
            InitializeTradingDayCalendarService();
            
            // 取引日カレンダーを更新（カレンダーサービス内でキャッシュ判断）
            if (_tradingDayCalendar != null)
            {
                await _tradingDayCalendar.UpdateCalendarAsync(startDate, endDate, forceUpdate);
            }
        }
        else
        {
            // ^DJI以外の場合は取引日カレンダーを確認
            if (_tradingDayCalendar != null)
            {
                // カレンダーが範囲をカバーしていない場合は更新（カレンダーサービス内でキャッシュ判断）
                bool calendarUpdate = await _tradingDayCalendar.UpdateCalendarAsync(startDate, endDate);
                
                // カレンダー更新に失敗した場合はログに記録（処理は続行）
                if (!calendarUpdate)
                {
                    _logger.LogWarning("取引日カレンダーの更新に失敗しました。取引日のフィルタリングが正確でない可能性があります (Failed to update trading day calendar)");
                }
                
                // 指定期間に取引日がない場合は空のリストを返す
                if (!_tradingDayCalendar.HasTradingDays(startDate, endDate))
                {
                    _logger.LogDebug($"シンボル {symbol} の期間 {startDate:yyyy-MM-dd} から {endDate:yyyy-MM-dd} に取引日がないため、空のデータを返します (No trading days in period, returning empty data)");
                    return new List<StockData>();
                }
            }
        }

        // 最終的に返すデータのリスト
        List<StockData> resultData = new List<StockData>();
        
        // 取得が必要な日付範囲のリスト
        List<DateRange> requiredRanges = new List<DateRange>();

        // 強制更新が指定されていない場合、キャッシュをチェック
        if (!forceUpdate)
        {
            try
            {
                // まずSQLiteキャッシュをチェック
                var cachedData = await GetDataFromCacheAsync(symbol, startDate, endDate);
                
                if (cachedData.Any())
                {
                    _logger.LogDebug($"シンボル {symbol} のキャッシュデータを取得しました: {cachedData.Count}件 (Retrieved cached data)");
                    
                    // 見つかったデータを結果リストに追加
                    resultData.AddRange(cachedData);
                    
                    // キャッシュにある日付範囲を見つける
                    var cachedDates = new HashSet<DateTime>(cachedData.Select(d => d.DateTime.Date));
                    
                    // 欠けている日付範囲を特定（取引日カレンダーを使用）
                    requiredRanges = FindMissingDateRanges(startDate, endDate, cachedDates);
                    
                    if (requiredRanges.Count == 0)
                    {
                        _logger.LogDebug($"シンボル {symbol} の要求された期間のデータは全てキャッシュにあります (All requested data in cache)");
                        // ソートして返却
                        return resultData.OrderBy(d => d.DateTime).ToList();
                    }
                    
                    _logger.LogDebug($"シンボル {symbol} の欠けているデータ範囲: {requiredRanges.Count}件 (Missing date ranges)");
                    foreach (var range in requiredRanges)
                    {
                        _logger.LogDebug($"欠けている範囲: {range.Start:yyyy-MM-dd} から {range.End:yyyy-MM-dd} (Missing range)");
                    }
                }
                else
                {
                    _logger.LogDebug($"シンボル {symbol} のキャッシュデータはありません。全期間をダウンロードします (No cache data available)");
                    // キャッシュに何もない場合は、全範囲が必要
                    requiredRanges = new List<DateRange> { new DateRange(startDate, endDate) };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"キャッシュ確認中にエラーが発生しました: {ex.Message} (Error checking cache)");
                // エラー時は全範囲を取得
                requiredRanges = new List<DateRange> { new DateRange(startDate, endDate) };
            }
        }
        else
        {
            _logger.LogDebug($"シンボル {symbol} の強制更新が指定されたため、キャッシュをスキップします (Force update specified, skipping cache)");
            // 強制更新時は全範囲を取得
            requiredRanges = new List<DateRange> { new DateRange(startDate, endDate) };
        }

        // 各必要範囲に対して処理（取引日のみに絞り込み）
        foreach (var range in requiredRanges)
        {
            try
            {
                // ^DJI以外の場合、取引日のみをフィルタリング
                if (symbol != "^DJI" && _tradingDayCalendar != null)
                {
                    // 範囲内の取引日のみを取得
                    var tradingDays = _tradingDayCalendar.GetTradingDays(range.Start, range.End);
                    
                    if (tradingDays.Count == 0)
                    {
                        _logger.LogDebug($"シンボル {symbol} の期間 {range.Start:yyyy-MM-dd} から {range.End:yyyy-MM-dd} に取引日がありません (No trading days in period)");
                        continue; // 次の範囲へ
                    }
                    
                    // 最小の取引日と最大の取引日で範囲を再設定
                    range.Start = tradingDays.Min();
                    range.End = tradingDays.Max();
                    
                    _logger.LogDebug($"シンボル {symbol} の取引日に絞り込まれた範囲: {range.Start:yyyy-MM-dd} から {range.End:yyyy-MM-dd} (Range narrowed to trading days)");
                }
                
                // 取引日があるか確認
                if (!await CheckTradingDayRangeAsync(range.Start, range.End))
                {
                    _logger.LogDebug($"シンボル {symbol} の期間 {range.Start:yyyy-MM-dd} から {range.End:yyyy-MM-dd} に取引日がありません (No trading days in this period)");
                    
                    // データが存在しない期間として記録
                    await _stockDataCache.RecordNoDataPeriodAsync(symbol, range.Start, range.End);
                    RecordNoDataPeriod(symbol, range.Start, range.End);
                    
                    continue;  // 次の範囲へ
                }
                
                // Yahoo Financeからデータを取得
                _logger.LogDebug($"シンボル {symbol} の期間 {range.Start:yyyy-MM-dd} から {range.End:yyyy-MM-dd} のデータをダウンロードします (Downloading data)");
                var newData = await FetchStockDataAsync(symbol, range.Start, range.End);
                
                if (newData.Count > 0)
                {
                    _logger.LogDebug($"シンボル {symbol} の {newData.Count} 件のデータをダウンロードしました (Downloaded data)");
                    
                    // SQLiteキャッシュに保存
                    var newDataPoints = newData.Select(d => new StockDataPoint
                    {
                        Date = d.DateTime,
                        Open = d.Open,
                        High = d.High,
                        Low = d.Low,
                        Close = d.Close,
                        AdjClose = d.AdjClose,
                        Volume = d.Volume
                    }).ToList();
                    
                    await _stockDataCache.SaveStockDataAsync(symbol, newDataPoints);
                    
                    // 既存の結果にマージ（重複を避ける）
                    var existingDates = new HashSet<DateTime>(resultData.Select(d => d.DateTime.Date));
                    var uniqueNewData = newData.Where(d => !existingDates.Contains(d.DateTime.Date)).ToList();
                    resultData.AddRange(uniqueNewData);
                    
                    _logger.LogDebug($"シンボル {symbol} の結果データに {uniqueNewData.Count} 件のデータを追加しました (Added unique new data)");
                    
                    // メモリキャッシュも更新
                    if (_dataCache.TryGetValue(symbol, out var cachedData))
                    {
                        var combinedData = cachedData.Data.Concat(uniqueNewData)
                            .GroupBy(d => d.DateTime.Date)
                            .Select(g => g.First()) // 重複がある場合は最初の要素を使用
                            .ToList();
                            
                        _dataCache[symbol] = new SymbolCacheData { 
                            Data = combinedData,
                            LastUpdateTime = DateTime.Now
                        };
                    }
                    else
                    {
                        _dataCache[symbol] = new SymbolCacheData { 
                            Data = newData,
                            LastUpdateTime = DateTime.Now
                        };
                    }
                    
                    // ^DJIデータの場合は取引日カレンダーを更新
                    if (symbol == "^DJI" && _tradingDayCalendar != null)
                    {
                        await _tradingDayCalendar.UpdateCalendarAsync(range.Start, range.End, true);
                    }
                }
                else
                {
                    _logger.LogWarning($"シンボル {symbol} の期間 {range.Start:yyyy-MM-dd} から {range.End:yyyy-MM-dd} のデータが取得できませんでした (No data available)");
                    
                    // データが存在しない期間として記録
                    await _stockDataCache.RecordNoDataPeriodAsync(symbol, range.Start, range.End);
                    RecordNoDataPeriod(symbol, range.Start, range.End);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"シンボル {symbol} の期間 {range.Start:yyyy-MM-dd} から {range.End:yyyy-MM-dd} のデータ取得中にエラーが発生しました (Error fetching data)");
                // エラーが発生しても続行（他の範囲を試す）
            }
        }
        
        // 最終結果を日付順にソートして返す
        var finalData = resultData
            .Where(d => d.DateTime >= startDate && d.DateTime <= endDate)
            .OrderBy(d => d.DateTime)
            .ToList();
        
        _logger.LogDebug($"GetStockDataAsync完了: 銘柄={symbol}, 返却件数={finalData.Count} (Completed GetStockDataAsync)");
        
        return finalData;
    }

    /// <summary>
    /// キャッシュから指定された日付範囲のデータを取得します
    /// </summary>
    private async Task<List<StockData>> GetDataFromCacheAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        List<StockData> result = new List<StockData>();
        
        // SQLiteキャッシュから取得
        var stockDataPoints = await _stockDataCache.GetStockDataAsync(symbol, startDate, endDate);
        if (stockDataPoints != null && stockDataPoints.Any())
        {
            foreach (var p in stockDataPoints)
            {
                result.Add(new StockData
                {
                    Symbol = symbol,
                    DateTime = p.Date,
                    Date = p.Date.Year * 10000 + p.Date.Month * 100 + p.Date.Day,
                    Open = p.Open,
                    High = p.High,
                    Low = p.Low,
                    Close = p.Close,
                    AdjClose = p.AdjClose,
                    Volume = p.Volume
                });
            }
            
            _logger.LogDebug($"SQLiteキャッシュから {result.Count} 件のデータを取得しました (Retrieved data from SQLite cache)");
            return result;
        }
        
        // SQLiteに無い場合はメモリキャッシュも確認
        if (_dataCache.TryGetValue(symbol, out var cachedData) && cachedData.Data.Any())
        {
            var filteredData = cachedData.Data
                .Where(d => d.DateTime >= startDate && d.DateTime <= endDate)
                .ToList();
            
            if (filteredData.Any())
            {
                _logger.LogDebug($"メモリキャッシュから {filteredData.Count} 件のデータを取得しました (Retrieved data from memory cache)");
                
                // メモリキャッシュにあるデータをSQLiteにも保存
                var dataPoints = filteredData.Select(d => new StockDataPoint
                {
                    Date = d.DateTime,
                    Open = d.Open,
                    High = d.High,
                    Low = d.Low,
                    Close = d.Close,
                    AdjClose = d.AdjClose,
                    Volume = d.Volume
                }).ToList();
                
                await _stockDataCache.SaveStockDataAsync(symbol, dataPoints);
                
                return filteredData;
            }
        }
        
        return result;  // 空のリスト
    }

    /// <summary>
    /// キャッシュされたデータに基づいて、欠けている日付範囲を特定します
    /// 取引日カレンダーを使用して、休業日を除外します
    /// </summary>
    private List<DateRange> FindMissingDateRanges(DateTime startDate, DateTime endDate, HashSet<DateTime> cachedDates)
    {
        var result = new List<DateRange>();
        
        // 取引日カレンダーがない場合は通常のロジックを使用
        if (_tradingDayCalendar == null)
        {
            // 取引日だけを対象にするため、平日のみをチェック
            DateTime currentDate = startDate;
            DateTime? rangeStart = null;
            
            while (currentDate <= endDate)
            {
                // 土日をスキップ
                if (currentDate.DayOfWeek != DayOfWeek.Saturday && currentDate.DayOfWeek != DayOfWeek.Sunday)
                {
                    bool isDateCached = cachedDates.Contains(currentDate.Date);
                    
                    if (!isDateCached)
                    {
                        // キャッシュにない日付が見つかった場合、範囲の開始点を記録
                        if (rangeStart == null)
                        {
                            rangeStart = currentDate;
                        }
                    }
                    else if (rangeStart != null)
                    {
                        // キャッシュにある日付が見つかり、範囲の開始点が設定されている場合、
                        // その範囲を結果に追加し、範囲の開始点をリセット
                        result.Add(new DateRange(rangeStart.Value, currentDate.AddDays(-1)));
                        rangeStart = null;
                    }
                }
                
                currentDate = currentDate.AddDays(1);
            }
            
            // 最後の範囲が終了していない場合、終了日までの範囲を追加
            if (rangeStart != null)
            {
                result.Add(new DateRange(rangeStart.Value, endDate));
            }
        }
        else
        {
            // 取引日カレンダーを使用する場合は、取引日のみをチェック
            var tradingDays = _tradingDayCalendar.GetTradingDays(startDate, endDate);
            DateTime? rangeStart = null;
            
            foreach (var date in tradingDays)
            {
                bool isDateCached = cachedDates.Contains(date);
                
                if (!isDateCached)
                {
                    // キャッシュにない取引日が見つかった場合、範囲の開始点を記録
                    if (rangeStart == null)
                    {
                        rangeStart = date;
                    }
                }
                else if (rangeStart != null)
                {
                    // キャッシュにある取引日が見つかり、範囲の開始点が設定されている場合、
                    // その範囲を結果に追加し、範囲の開始点をリセット
                    var prevDate = tradingDays
                        .Where(d => d < date)
                        .OrderByDescending(d => d)
                        .FirstOrDefault();
                        
                    if (prevDate != default)
                    {
                        result.Add(new DateRange(rangeStart.Value, prevDate));
                    }
                    rangeStart = null;
                }
            }
            
            // 最後の範囲が終了していない場合、終了日までの範囲を追加
            if (rangeStart != null)
            {
                result.Add(new DateRange(rangeStart.Value, tradingDays.Last()));
            }
        }
        
        // 短すぎる範囲を統合（例: 3日未満の隙間は単一の範囲にマージ）
        result = MergeShortRanges(result, TimeSpan.FromDays(3));
        
        return result;
    }

    /// <summary>
    /// 短い日付範囲を統合します
    /// </summary>
    private List<DateRange> MergeShortRanges(List<DateRange> ranges, TimeSpan threshold)
    {
        if (ranges.Count <= 1)
            return ranges;
        
        var result = new List<DateRange>();
        var sortedRanges = ranges.OrderBy(r => r.Start).ToList();
        
        var currentRange = sortedRanges[0];
        
        for (int i = 1; i < sortedRanges.Count; i++)
        {
            var nextRange = sortedRanges[i];
            var gap = nextRange.Start - currentRange.End;
            
            if (gap <= threshold)
            {
                // 範囲間のギャップが閾値以下なら統合
                currentRange = new DateRange(currentRange.Start, nextRange.End);
            }
            else
            {
                // そうでなければ現在の範囲を追加し、次の範囲に進む
                result.Add(currentRange);
                currentRange = nextRange;
            }
        }
        
        // 最後の範囲を追加
        result.Add(currentRange);
        
        return result;
    }

    /// <summary>
    /// 指定された日付範囲に営業日があるかどうかを確認します
    /// </summary>
    /// <param name="startDate">開始日</param>
    /// <param name="endDate">終了日</param>
    /// <returns>営業日がある場合はtrue、ない場合はfalse</returns>
    public async Task<bool> CheckTradingDayRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            // TradingDayCalendarServiceが利用可能な場合はそれを使用
            if (_tradingDayCalendar != null)
            {
                _logger.LogDebug("TradingDayCalendarServiceを使用して営業日をチェックしています: {StartDate}から{EndDate}まで (Checking trading days using calendar service)",
                    startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
                
                // カレンダーを更新（既に最新ならキャッシュを使用）
                await _tradingDayCalendar.UpdateCalendarAsync(startDate, endDate);
                
                // カレンダー内に取引日があるかどうかをチェック
                return _tradingDayCalendar.HasTradingDays(startDate, endDate);
            }
            
            // TradingDayCacheServiceが利用可能な場合はそれを使用
            if (_tradingDayCache != null)
            {
                _logger.LogDebug("TradingDayCacheServiceを使用して営業日をチェックしています: {StartDate}から{EndDate}まで (Checking trading days using cache service)",
                    startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
                
                return await _tradingDayCache.CheckTradingDayRangeAsync(startDate, endDate);
            }
            
            // キャッシュサービスが利用できない場合は直接APIを呼び出す
            _logger.LogDebug("NYダウのデータを取得して営業日かどうかを判断しています: {StartDate}から{EndDate}まで (Checking if date range contains trading days using DJI)",
                startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
            
            var djiData = await FetchStockDataWithoutRetryAsync("^DJI", startDate, endDate);
            return djiData.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "営業日チェック中にエラーが発生しました (Error occurred during trading day check)");
            // エラーが発生した場合は、安全のためfalseを返す
            return false;
        }
    }

    private async Task<List<StockData>> FetchStockDataAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        // リトライポリシーの外側にダウンロード準備ログを追加
        _logger.LogDebug("RETRY外: {Symbol}のデータ取得準備を開始します: {StartDate}から{EndDate}まで (Preparing to fetch data for symbol)",
            symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

        _logger.LogDebug("RETRY外: 【Yahoo Financeからダウンロード】銘柄: {Symbol}, 期間: {StartDate} から {EndDate} までのデータを取得中 (Downloading data from Yahoo Finance)",
            symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

        _logger.LogDebug("RETRY外: 【日付追跡】StockDataService - FetchStockDataAsync開始 - startDate: {StartDate}, Year: {StartYear}, endDate: {EndDate}, Year: {EndYear}",
            startDate.ToString("yyyy-MM-dd HH:mm:ss"), startDate.Year, endDate.ToString("yyyy-MM-dd HH:mm:ss"), endDate.Year);

        return await _retryPolicy.ExecuteAsync(async (context) =>
        {
            context["Symbol"] = symbol;

            try
            {
                // 日付の正規化（時刻情報を削除）
                startDate = startDate.Date;
                endDate = endDate.Date;

                _logger.LogDebug("RETRY内: {Symbol}のデータを取得しています: {StartDate}から{EndDate}まで (Fetching data for symbol)",
                    symbol,
                    startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

                _logger.LogDebug("RETRY内: 【Yahoo Financeからダウンロード】銘柄: {Symbol}, 期間: {StartDate} から {EndDate} までのデータを取得中 (Downloading data from Yahoo Finance)",
                    symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

                _logger.LogDebug("RETRY内: 【日付追跡】StockDataService - FetchStockDataAsync開始 - startDate: {StartDate}, Year: {StartYear}, endDate: {EndDate}, Year: {EndYear}",
                    startDate.ToString("yyyy-MM-dd HH:mm:ss"), startDate.Year, endDate.ToString("yyyy-MM-dd HH:mm:ss"), endDate.Year);

                // ETRシンボルの特別処理
                if (symbol == "ETR")
                {
                    _logger.LogDebug("ETRシンボルの特別処理を適用します (Special handling for ETR symbol)");

                    // ETRはEntergy Corporation、Yahoo FinanceでもETRのまま使用
                    // 追加の待機時間を設定（レート制限回避のため）
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }

                // ランダムな遅延を追加してレート制限を回避
                await Task.Delay(TimeSpan.FromMilliseconds(_random.Next(500, 1500)));

                // 日付の処理前に元の日付をログに記録
                _logger.LogDebug("元の日付 - 開始日: {StartDate}, 終了日: {EndDate} (Original dates)",
                    startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

                // 日付の順序を確認し、必要に応じて入れ替え
                if (startDate > endDate)
                {
                    _logger.LogWarning("開始日が終了日よりも後になっています。日付を入れ替えます (Start date is after end date, swapping dates). StartDate: {StartDate}, EndDate: {EndDate}",
                        startDate, endDate);
                    var temp = startDate;
                    startDate = endDate;
                    endDate = temp;
                }

                // 日付を米国東部標準時（EST、UTC-5）の正午に設定してUnixタイムスタンプに変換
                var startDateEST = new DateTime(startDate.Year, startDate.Month, startDate.Day, 12, 0, 0);
                var endDateEST = new DateTime(endDate.Year, endDate.Month, endDate.Day, 12, 0, 0);
                var unixStartTime = new DateTimeOffset(startDateEST, TimeSpan.FromHours(-5)).ToUnixTimeSeconds();
                var unixEndTime = new DateTimeOffset(endDateEST, TimeSpan.FromHours(-5)).ToUnixTimeSeconds();

                _logger.LogDebug("【日付追跡】StockDataService - Unix変換後 - startDate: {StartDate}, unixStartTime: {UnixStart}, endDate: {EndDate}, unixEndTime: {UnixEnd} (Date tracking after Unix conversion)",
                    startDateEST.ToString("yyyy-MM-dd HH:mm:ss"), unixStartTime, endDateEST.ToString("yyyy-MM-dd HH:mm:ss"), unixEndTime);

                // 変換後の日付を逆算して確認
                var checkStartDate = DateTimeOffset.FromUnixTimeSeconds(unixStartTime).DateTime;
                var checkEndDate = DateTimeOffset.FromUnixTimeSeconds(unixEndTime).DateTime;
                _logger.LogDebug("【日付追跡】StockDataService - Unix変換チェック - 元のstartDate: {OrigStartDate}, 変換後: {ConvStartDate}, 元のendDate: {OrigEndDate}, 変換後: {ConvEndDate} (Date tracking - Unix conversion check)",
                    startDateEST.ToString("yyyy-MM-dd HH:mm:ss"), checkStartDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    endDateEST.ToString("yyyy-MM-dd HH:mm:ss"), checkEndDate.ToString("yyyy-MM-dd HH:mm:ss"));

                _logger.LogDebug("Unixタイムスタンプ - 開始: {StartTime}, 終了: {EndTime} (Unix timestamps)", unixStartTime, unixEndTime);

                // ピリオドを含むシンボルの特別処理（例：BRK.B → BRK-B）
                // なんでもかんでもではないらしい。もしかしたら個別株だけのルールかも。指数だと"."のままの指数があった。
                string yahooSymbol = symbol;
                //if (symbol.Contains("."))
                //{
                //    yahooSymbol = symbol.Replace(".", "-");
                //    _logger.LogDebug("ピリオドを含むシンボルをYahoo Finance用に変換: {OriginalSymbol} -> {YahooSymbol} (Symbol contains period, converting for Yahoo Finance)",
                //        symbol, yahooSymbol);
                //}

label_retry:
                // URLエンコーディングを適用
                string encodedSymbol = Uri.EscapeDataString(yahooSymbol);
                _logger.LogDebug("元のシンボル: {Symbol}, Yahoo用シンボル: {YahooSymbol}, エンコード後: {EncodedSymbol} (Original symbol, Yahoo symbol, Encoded symbol)",
                    symbol, yahooSymbol, encodedSymbol);

                // 追加のパラメータを含めたURLを構築
                var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{encodedSymbol}?period1={unixStartTime}&period2={unixEndTime}&interval=1d&includePrePost=false&events=div%2Csplit";
                _logger.LogDebug("リクエストURL: {Url} (Request URL)", url);

                // HttpRequestMessageを使用して詳細なリクエスト設定
                var request = new HttpRequestMessage(HttpMethod.Get, url);

                // 必要最小限のヘッダー
                request.Headers.Add("User-Agent", "Mozilla/5.0");
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add("Referer", "https://finance.yahoo.com/");

                var response = await _httpClient.SendAsync(request);
                _logger.LogDebug("レスポンスステータスコード: {StatusCode} (Response status code)", response.StatusCode);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("{Symbol}のレート制限に達しました (Rate limit hit for symbol)", symbol);
                    throw new RateLimitException($"Rate limit exceeded for {symbol}");
                }

                // エラー応答の詳細をログに記録
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("{Symbol}のリクエストが失敗しました。ステータスコード: {StatusCode}, エラー内容: {ErrorContent} (Request failed for symbol)",
                        symbol, response.StatusCode, errorContent);

                    // Internal Server Errorの検出
                    if (errorContent.Contains("Internal Server Error") || 
                        (response.StatusCode == HttpStatusCode.InternalServerError) ||
                        (errorContent.Contains("\"code\":\"Internal Server Error\"")))
                    {
                        _logger.LogWarning("{Symbol}のリクエストでInternal Server Errorが発生しました。リトライを中止します。 (Internal Server Error for symbol. Skipping retry.)", symbol);
                        return new List<StockData>(); // リトライせずに空のリストを返す
                    }

                    // "No data found, symbol may be delisted"エラーの検出
                    if (errorContent.Contains("\"code\":\"Not Found\"") && errorContent.Contains("\"description\":\"No data found, symbol may be delisted\""))
                    {

                        if (yahooSymbol.Contains("."))
                        {
                            yahooSymbol = yahooSymbol.Replace(".", "-");
                            _logger.LogDebug("ピリオドを含むシンボルをYahoo Finance用に変換して再挑戦: {OriginalSymbol} -> {YahooSymbol} (Symbol contains period, converting for Yahoo Finance)",
                                symbol, yahooSymbol);

                            goto label_retry;

                        }
                        else
                        {
                            _logger.LogWarning("{Symbol}のデータが見つかりません。シンボルは上場廃止されている可能性があります。リトライを中止します。 (No data found for symbol, may be delisted. Skipping retry.)", symbol);
                            _delistedSymbols.Add(symbol); // 上場廃止されたシンボルを追跡
                            return new List<StockData>(); // 空のリストを返して処理を続行
                        }
                    }
                }

                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("レスポンスコンテンツ長: {Length} バイト (Response content length)", content.Length);

                // 特定のシンボルでレスポンスの詳細をログに記録
                if (symbol == "ETR" || symbol.Contains("."))
                {
                    _logger.LogDebug("{Symbol}のレスポンス内容: {Content} (Response content for symbol)", symbol, content);
                }

                // すべてのシンボルのレスポンス内容をログに記録（デバッグ用）
                _logger.LogDebug("{Symbol}のレスポンス内容: {Content} (Response content for symbol)", symbol, content);

                // JsonSerializerを使用してデシリアライズ
                var yahooResponse = JsonSerializer.Deserialize<YahooFinanceResponse>(content);

                // レスポンスの基本構造を検証
                if (yahooResponse?.Chart == null)
                {
                    _logger.LogError("{Symbol}のレスポンスにChartプロパティがありません (No Chart property in response for symbol)", symbol);
                    throw new DataParsingException($"No Chart property in response for {symbol}");
                }

                // エラーオブジェクトの確認
                if (yahooResponse.Chart.Error != null)
                {
                    var errorJson = JsonSerializer.Serialize(yahooResponse.Chart.Error);
                    _logger.LogError("{Symbol}のレスポンスにエラーが含まれています: {Error} (Response contains error for symbol)",
                        symbol, errorJson);
                    throw new DataParsingException($"Response contains error for {symbol}: {errorJson}");
                }

                if (yahooResponse.Chart.Result == null || !yahooResponse.Chart.Result.Any())
                {
                    _logger.LogError("{Symbol}のデータが返されませんでした (No data returned for symbol)", symbol);
                    throw new DataParsingException($"No data returned for {symbol}");
                }

                var result = yahooResponse.Chart.Result[0];

                // 各プロパティの存在を個別に確認
                if (result.Timestamp == null)
                {

                    if (yahooSymbol.Contains("."))
                    {
                        yahooSymbol = yahooSymbol.Replace(".", "-");
                        _logger.LogDebug("ピリオドを含むシンボルをYahoo Finance用に変換して再挑戦: {OriginalSymbol} -> {YahooSymbol} (Symbol contains period, converting for Yahoo Finance)",
                            symbol, yahooSymbol);

                        goto label_retry;

                    }

                    _logger.LogError("{Symbol}のTimestampデータがありません (No Timestamp data for symbol)", symbol);
                    //throw new DataParsingException($"No Timestamp data for {symbol}");
                    return new List<StockData>(); // リトライせずに0個の配列返す
                }

                if (result.Indicators == null)
                {
                    _logger.LogError("{Symbol}のIndicatorsデータがありません (No Indicators data for symbol)", symbol);
                    //throw new DataParsingException($"No Indicators data for {symbol}");
                    return new List<StockData>(); // リトライせずに0個の配列返す
                }

                if (result.Indicators.Quote == null || !result.Indicators.Quote.Any())
                {
                    _logger.LogError("{Symbol}のQuoteデータがありません (No Quote data for symbol)", symbol);
                    //throw new DataParsingException($"No Quote data for {symbol}");
                    return new List<StockData>(); // リトライせずに0個の配列返す
                }

                var quote = result.Indicators.Quote[0];

                // 各価格データの存在を個別に確認
                if (quote.High == null)
                {
                    _logger.LogError("{Symbol}の高値データがありません (No High data for symbol)", symbol);
                    //throw new DataParsingException($"No High data for {symbol}");
                    return new List<StockData>(); // リトライせずに0個の配列返す
                }

                if (quote.Low == null)
                {
                    _logger.LogError("{Symbol}の安値データがありません (No Low data for symbol)", symbol);
                    //throw new DataParsingException($"No Low data for {symbol}");
                    return new List<StockData>(); // リトライせずに0個の配列返す
                }

                if (quote.Open == null)
                {
                    _logger.LogError("{Symbol}の始値データがありません (No Open data for symbol)", symbol);
                    //throw new DataParsingException($"No Open data for {symbol}");
                    return new List<StockData>(); // リトライせずに0個の配列返す
                }

                if (quote.Close == null)
                {
                    _logger.LogError("{Symbol}の終値データがありません (No Close data for symbol)", symbol);
                    //throw new DataParsingException($"No Close data for {symbol}");
                    return new List<StockData>(); // リトライせずに0個の配列返す
                }

                if (quote.Volume == null)
                {
                    _logger.LogError("{Symbol}の出来高データがありません (No Volume data for symbol)", symbol);
                    //throw new DataParsingException($"No Volume data for {symbol}");
                    return new List<StockData>(); // リトライせずに0個の配列返す
                }

                var stockDataList = new List<StockData>();

                for (int i = 0; i < result.Timestamp.Count; i++)
                {
                    if (quote.Open?[i] == null || quote.High?[i] == null ||
                        quote.Low?[i] == null || quote.Close?[i] == null ||
                        quote.Volume?[i] == null)
                    {
                        continue;
                    }

                    var dateTime = DateTimeOffset.FromUnixTimeSeconds(result.Timestamp[i] ?? 0).Date;

                    // CS8629警告の抑制
#pragma warning disable CS8629 // Null許容値型はNullになる場合があります

                    // Null許容値型の安全な処理
                    decimal open = quote.Open[i].HasValue ? quote.Open[i].Value : 0m;
                    decimal high = quote.High[i].HasValue ? quote.High[i].Value : 0m;
                    decimal low = quote.Low[i].HasValue ? quote.Low[i].Value : 0m;
                    decimal close = quote.Close[i].HasValue ? quote.Close[i].Value : 0m;
                    long volume = quote.Volume[i].HasValue ? quote.Volume[i].Value : 0L;

#pragma warning restore CS8629 // 警告の抑制を解除

                    var stockData = new StockData
                    {
                        Symbol = symbol,
                        DateTime = dateTime,
                        Date = int.Parse(dateTime.ToString("yyyyMMdd")),
                        Open = open,
                        High = high,
                        Low = low,
                        Close = close,
                        Volume = volume
                    };

                    // データの検証
                    if (ValidateStockData(stockData))
                    {
                        stockDataList.Add(stockData);
                    }
                    else
                    {
                        _logger.LogWarning("{Symbol}の{Date}のデータポイントが無効です (Invalid data point for symbol at date)", symbol, stockData.DateTime);
                    }
                }

                if (!stockDataList.Any())
                {
                    throw new DataParsingException($"No valid data points found for {symbol}");
                }

                _logger.LogDebug("{Symbol}の{Count}データポイントの取得に成功しました (Successfully fetched data points for symbol)",
                    symbol, stockDataList.Count);

                return stockDataList;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError("{Symbol}のデータ取得中にエラーが発生しました: {ErrorMessage} (Error fetching data for symbol)", symbol, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("{Symbol}のデータ取得中に予期しないエラーが発生しました: {ErrorMessage} (Unexpected error fetching data for symbol)", symbol, ex.Message);
                throw;
            }
        }, new Context());
    }

    /// <summary>
    /// リトライなしで株価データを取得するメソッド（営業日判断用）
    /// </summary>
    private async Task<List<StockData>> FetchStockDataWithoutRetryAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        try
        {
            _logger.LogDebug("リトライなしで{Symbol}のデータを取得しています (Fetching data without retry for symbol)", symbol);

            // 日付の正規化（時刻情報を削除）
            startDate = startDate.Date;
            endDate = endDate.Date;

            _logger.LogDebug("【Yahoo Financeからダウンロード】銘柄: {Symbol}, 期間: {StartDate} から {EndDate} までのデータを取得中 (Downloading data from Yahoo Finance)",
                symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

            // 日付を米国東部標準時（EST、UTC-5）の正午に設定してUnixタイムスタンプに変換
            var startDateEST = new DateTime(startDate.Year, startDate.Month, startDate.Day, 12, 0, 0);
            var endDateEST = new DateTime(endDate.Year, endDate.Month, endDate.Day, 12, 0, 0);
            var unixStartTime = new DateTimeOffset(startDateEST, TimeSpan.FromHours(-5)).ToUnixTimeSeconds();
            var unixEndTime = new DateTimeOffset(endDateEST, TimeSpan.FromHours(-5)).ToUnixTimeSeconds();

            // URLを構築
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?period1={unixStartTime}&period2={unixEndTime}&interval=1d&includePrePost=false&events=div%2Csplit";

            // リクエストを送信
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            // 必要最小限のヘッダー
            request.Headers.Add("User-Agent", "Mozilla/5.0");
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("Referer", "https://finance.yahoo.com/");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("{Symbol}のリクエストが失敗しました。ステータスコード: {StatusCode} (Request failed for symbol)",
                    symbol, response.StatusCode);
                return new List<StockData>();
            }

            var content = await response.Content.ReadAsStringAsync();

            // JsonSerializerを使用してデシリアライズ
            var yahooResponse = JsonSerializer.Deserialize<YahooFinanceResponse>(content);

            // null チェックを個別に行い、安全にアクセス
            if (yahooResponse == null || yahooResponse.Chart == null ||
                yahooResponse.Chart.Result == null || !yahooResponse.Chart.Result.Any())
            {
                _logger.LogDebug("{Symbol}のデータが取得できませんでした (No data available for symbol)", symbol);
                return new List<StockData>();
            }

            var result = yahooResponse.Chart.Result[0];

            // 必要なプロパティのnullチェック
            if (result.Timestamp == null || result.Indicators == null ||
                result.Indicators.Quote == null || !result.Indicators.Quote.Any())
            {
                _logger.LogDebug("{Symbol}の必要なデータが欠けています (Missing required data for symbol)", symbol);
                return new List<StockData>();
            }

            var timestamps = result.Timestamp;
            var quote = result.Indicators.Quote[0];

            var stockDataList = new List<StockData>();

            // Timestampの長さを使用してループ
            for (int i = 0; i < timestamps.Count; i++)
            {
                // 各データ要素のnullチェック
                if (i < timestamps.Count && timestamps[i].HasValue &&
                    quote.Open != null && i < quote.Open.Count && quote.Open[i].HasValue &&
                    quote.High != null && i < quote.High.Count && quote.High[i].HasValue &&
                    quote.Low != null && i < quote.Low.Count && quote.Low[i].HasValue &&
                    quote.Close != null && i < quote.Close.Count && quote.Close[i].HasValue &&
                    quote.Volume != null && i < quote.Volume.Count && quote.Volume[i].HasValue)
                {
                    // null値でないことを確認したので、安全にValue プロパティにアクセス可能
                    long timestamp = timestamps[i]!.Value;
                    decimal openValue = quote.Open[i]!.Value;
                    decimal highValue = quote.High[i]!.Value;
                    decimal lowValue = quote.Low[i]!.Value;
                    decimal closeValue = quote.Close[i]!.Value;
                    long volumeValue = quote.Volume[i]!.Value;
                    
                    var dateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;

                    stockDataList.Add(new StockData
                    {
                        Symbol = symbol,
                        DateTime = dateTime,
                        Date = int.Parse(dateTime.ToString("yyyyMMdd")),
                        Open = openValue,
                        High = highValue,
                        Low = lowValue,
                        Close = closeValue,
                        Volume = volumeValue
                    });
                }
            }

            return stockDataList;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("リトライなしのデータ取得中にエラーが発生しました: {ErrorMessage} (Error occurred during fetch without retry)", ex.Message);
            return new List<StockData>();
        }
    }
    
    private bool ValidateStockData(StockData data)
    {
        bool dataWasCorrected = false;

        // 基本的なデータ検証と修正
        if (data.Open <= 0 || data.High <= 0 || data.Low <= 0 || data.Close <= 0
        //    || data.Volume <= 0   指数だとVolumeないのもある
        )
        {
            _logger.LogWarning("{Symbol}の{Date}の価格が無効です (Invalid price or volume values for symbol at date)", data.Symbol, data.DateTime);

            // // 無効な値を修正（最小値1に設定）
            // if (data.Open <= 0) { data.Open = 1; dataWasCorrected = true; }
            // if (data.High <= 0) { data.High = 1; dataWasCorrected = true; }
            // if (data.Low <= 0) { data.Low = 1; dataWasCorrected = true; }
            // if (data.Close <= 0) { data.Close = 1; dataWasCorrected = true; }
            // if (data.Volume <= 0) { data.Volume = 1; dataWasCorrected = true; }
        }

        // 高値が安値より低い場合の修正
        if (data.High < data.Low)
        {
            _logger.LogWarning("{Symbol}の{Date}の高値が安値より低いです (High is lower than Low for symbol at date)", data.Symbol, data.DateTime);

            // 高値と安値を入れ替える
            var temp = data.High;
            data.High = data.Low;
            data.Low = temp;
            dataWasCorrected = true;
        }

        // 始値または終値が高値より高い場合の修正
        if (data.Open > data.High)
        {
            _logger.LogWarning("{Symbol}の{Date}の始値が高値より高いです (Open is higher than High for symbol at date)", data.Symbol, data.DateTime);
            data.High = data.Open;
            dataWasCorrected = true;
        }

        // 始値が安値より低い場合の修正
        if (data.Open < data.Low)
        {
            _logger.LogWarning("{Symbol}の{Date}の始値が安値より低いです (Open is lower than Low for symbol at date)", data.Symbol, data.DateTime);
            data.Low = data.Open;
            dataWasCorrected = true;
        }

        // 終値が高値より高い場合の修正
        if (data.Close > data.High)
        {
            _logger.LogWarning("{Symbol}の{Date}の終値が高値より高いです (Close is higher than High for symbol at date)", data.Symbol, data.DateTime);
            data.High = data.Close;
            dataWasCorrected = true;
        }

        // 終値が安値より低い場合の修正
        if (data.Close < data.Low)
        {
            _logger.LogWarning("{Symbol}の{Date}の終値が安値より低いです (Close is lower than Low for symbol at date)", data.Symbol, data.DateTime);
            data.Low = data.Close;
            dataWasCorrected = true;
        }

        if (dataWasCorrected)
        {
            _logger.LogDebug("{Symbol}の{Date}のデータが自動修正されました (Data was automatically corrected for symbol at date)", data.Symbol, data.DateTime);
        }

        return true;
    }

    /// <summary>
    /// 指定されたシンボルが上場廃止されているかどうかを確認します
    /// </summary>
    /// <param name="symbol">確認するシンボル</param>
    /// <returns>上場廃止されている場合はtrue、そうでない場合はfalse</returns>
    public bool IsSymbolDelisted(string symbol)
    {
        return _delistedSymbols.Contains(symbol);
    }

    /// <summary>
    /// データが存在しない期間を記録するヘルパーメソッド
    /// </summary>
    /// <param name="symbol">シンボル</param>
    /// <param name="start">開始日</param>
    /// <param name="end">終了日</param>
    private void RecordNoDataPeriod(string symbol, DateTime start, DateTime end)
    {
        _logger.LogDebug("{Symbol}の{StartDate}から{EndDate}までのデータは存在しません (No data exists for symbol from start date to end date)",
            symbol, start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"));

        // ローカルキャッシュに記録
        _noDataPeriods.AddOrUpdate(
            symbol,
            new List<DateRange> { new DateRange(start, end) },
            (_, existingPeriods) => {
                // 既存の期間と重複・隣接する場合はマージ
                var newPeriod = new DateRange(start, end);
                var merged = MergeOverlappingPeriods(existingPeriods, newPeriod);
                return merged;
            }
        );
    }

    /// <summary>
    /// 重複・隣接する期間をマージするヘルパーメソッド
    /// </summary>
    /// <param name="periods">既存の期間リスト</param>
    /// <param name="newPeriod">新しい期間</param>
    /// <returns>マージされた期間リスト</returns>
    private List<DateRange> MergeOverlappingPeriods(
        List<DateRange> periods, 
        DateRange newPeriod)
    {
        var result = new List<DateRange>(periods);
        result.Add(newPeriod);
        
        // 期間をソート
        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        
        // 重複・隣接する期間をマージ
        var merged = new List<DateRange>();
        if (result.Count > 0)
        {
            var current = result[0];
            for (int i = 1; i < result.Count; i++)
            {
                // 期間が重複または隣接している場合
                if (result[i].Start <= current.End.AddDays(1))
                {
                    // 終了日を更新
                    current = new DateRange(current.Start, result[i].End > current.End ? result[i].End : current.End);
                }
                else
                {
                    // 重複していない場合は現在の期間を追加し、次の期間に移動
                    merged.Add(current);
                    current = result[i];
                }
            }
            merged.Add(current);
        }
        
        return merged;
    }

    /// <summary>
    /// 指定された期間がデータが存在しない期間に含まれるかチェックします
    /// </summary>
    /// <param name="symbol">銘柄シンボル</param>
    /// <param name="startDate">開始日</param>
    /// <param name="endDate">終了日</param>
    /// <returns>データが存在しない期間に含まれる場合はtrue</returns>
    private bool IsInNoDataPeriod(string symbol, DateTime startDate, DateTime endDate)
    {
        // TradingDayCacheServiceが利用可能な場合、そちらも確認
        if (_tradingDayCache != null)
        {
            // 指定された日が全てデータなし期間に含まれるかを確認
            var currentDate = startDate;
            
            while (currentDate <= endDate)
            {
                if (_tradingDayCache.IsDateInNoDataPeriod(currentDate))
                {
                    _logger.LogDebug($"日付 {currentDate:yyyy-MM-dd} はTradingDayCacheServiceによって全体的なデータなし期間と判断されました (Date marked as no-data period by cache service)");
                    return true;
                }
                currentDate = currentDate.AddDays(1);
            }
        }

        // ローカルキャッシュを確認
        if (!_noDataPeriods.TryGetValue(symbol, out var periods) || periods.Count == 0)
        {
            return false;
        }

        // 完全に含まれる場合
        foreach (var period in periods)
        {
            if (startDate >= period.Start && endDate <= period.End)
            {
                _logger.LogDebug($"期間 {startDate:yyyy-MM-dd} から {endDate:yyyy-MM-dd} は、ローカルキャッシュのデータなし期間 {period.Start:yyyy-MM-dd} から {period.End:yyyy-MM-dd} に含まれています (Date range is within a known no-data period)");
                return true;
            }
        }

        // 部分的に重なる場合は、重なる部分だけをチェックする必要があるため、falseを返す
        return false;
    }

    /// <summary>
    /// リトライなしで株価データを取得するメソッド（インターフェース要件）
    /// </summary>
    /// <param name="symbol">取得する銘柄のシンボル</param>
    /// <param name="startDate">開始日</param>
    /// <param name="endDate">終了日</param>
    /// <returns>株価データのリスト</returns>
    public async Task<List<StockData>> GetStockDataWithoutRetryAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        return await FetchStockDataWithoutRetryAsync(symbol, startDate, endDate);
    }
}
