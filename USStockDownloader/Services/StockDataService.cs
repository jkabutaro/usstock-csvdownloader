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

public class StockDataService : IStockDataService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly Random _random = new Random();
    private readonly string _cacheDirPath;
    private readonly StockDataCacheSqliteService _stockDataCache;
    private readonly ITradingDayCacheService _tradingDayCache;
    private readonly AsyncRetryPolicy<List<StockData>> _retryPolicy;

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
    }

    // キャッシュデータクラスを拡張
    private class SymbolCacheData
    {
        public List<StockData> Data { get; set; } = new List<StockData>();
        public List<DateRange> NoDataPeriods { get; set; } = new List<DateRange>();
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
        
        // SQLiteキャッシュサービスの初期化
        var loggerFactory = LoggerFactory.Create(builder => {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _stockDataCache = new StockDataCacheSqliteService(
            cacheDirectory, 
            loggerFactory.CreateLogger<StockDataCacheSqliteService>());
            
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
            loggerFactory.CreateLogger<TradingDayCacheSqliteService>(),
            new HttpClientFactoryWrapper(_httpClient));

        // リトライポリシーの設定
        _retryPolicy = Policy<List<StockData>>
            .Handle<HttpRequestException>()
            .Or<DataParsingException>()
            .WaitAndRetryAsync(
                3, // 最大リトライ回数
                retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + // 指数バックオフ
                    TimeSpan.FromMilliseconds(_random.Next(0, 1000)) // ジッター
            );

        // キャッシュディレクトリのパスを設定
        if (!Directory.Exists(_cacheDirPath))
        {
            Directory.CreateDirectory(_cacheDirPath);
            _logger.LogInformation("キャッシュディレクトリを作成しました (Created cache directory): {CacheDir}", _cacheDirPath);
        }
        
        //// 既存のJSONファイルからデータをインポート（初回のみ）
        //Task.Run(async () => await MigrateFromJsonIfNeededAsync()).ConfigureAwait(false);
    }

    ///// <summary>
    ///// 既存のJSONファイルからSQLiteデータベースにデータを移行します（必要な場合のみ）
    ///// </summary>
    //private async Task MigrateFromJsonIfNeededAsync()
    //{
    //    var stockDataDir = Path.Combine(_cacheDirPath, "output");
    //    if (Directory.Exists(stockDataDir) && Directory.GetFiles(stockDataDir, "*.json").Length > 0)
    //    {
    //        _logger.LogInformation("既存のJSONファイルからSQLiteデータベースへの移行を開始します (Starting migration from JSON files to SQLite database)");
    //        await _stockDataCache.ImportFromJsonFilesAsync(_cacheDirPath);
    //    }
    //}

    public async Task<List<StockData>> GetStockDataAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        if (_delistedSymbols.Contains(symbol))
        {
            _logger.LogWarning($"シンボル {symbol} は上場廃止されているためスキップします (Symbol {symbol} is delisted, skipping)");
            return new List<StockData>();
        }

        // データが存在しない期間のチェック
        if (IsInNoDataPeriod(symbol, startDate, endDate))
        {
            _logger.LogInformation($"シンボル {symbol} の期間 {startDate:yyyy-MM-dd} から {endDate:yyyy-MM-dd} はデータが存在しないことが既知です (No data exists for symbol {symbol} from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd})");
            return new List<StockData>();
        }

        try
        {
            // SQLiteキャッシュからデータを取得
            var stockDataPoints = await _stockDataCache.GetStockDataAsync(symbol, startDate, endDate);
            int count = stockDataPoints.Count();
            if (count > 0)
            {
                _logger.LogInformation($"シンボル {symbol} のSQLiteキャッシュデータを使用します: {count}件 (Using SQLite cached data for symbol {symbol}: {count} items)");
                
                // StockDataPointからStockDataへの変換
                var stockData = stockDataPoints.Select(p => new StockData
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
                }).ToList();
                
                // メモリキャッシュも更新
                _dataCache[symbol] = new SymbolCacheData { Data = stockData };
                
                return stockData;
            }

            // メモリキャッシュをチェック（移行期間中の互換性のため）
            if (_dataCache.TryGetValue(symbol, out var cachedData) && cachedData.Data.Count > 0)
            {
                var cachedStartDate = cachedData.Data.Min(d => d.DateTime);
                var cachedEndDate = cachedData.Data.Max(d => d.DateTime);

                // キャッシュが要求された期間をカバーしている場合
                if (startDate >= cachedStartDate && endDate <= cachedEndDate)
                {
                    _logger.LogDebug($"シンボル {symbol} のメモリキャッシュデータを使用します (Using memory cached data for symbol {symbol})");
                    
                    // メモリキャッシュのデータをSQLiteにも保存
                    var dataPoints = cachedData.Data.Select(d => new StockDataPoint
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
                    
                    return cachedData.Data.Where(d => d.DateTime >= startDate && d.DateTime <= endDate).ToList();
                }
            }

            // 新しい日付範囲を計算
            DateTime newStartDate = startDate;
            DateTime newEndDate = endDate;

            if (cachedData != null && cachedData.Data.Count > 0)
            {
                var cachedStartDate = cachedData.Data.Min(d => d.DateTime);
                var cachedEndDate = cachedData.Data.Max(d => d.DateTime);

                // キャッシュされたデータと重複する部分を避ける
                if (startDate < cachedStartDate && endDate >= cachedStartDate)
                {
                    newEndDate = cachedStartDate.AddDays(-1);
                }
                else if (startDate <= cachedEndDate && endDate > cachedEndDate)
                {
                    newStartDate = cachedEndDate.AddDays(1);
                }
            }

            // 取引日があるか確認
            if (!await CheckTradingDayRangeAsync(newStartDate, newEndDate))
            {
                _logger.LogInformation($"シンボル {symbol} の期間 {newStartDate:yyyy-MM-dd} から {newEndDate:yyyy-MM-dd} に取引日がありません (No trading days for symbol {symbol} from {newStartDate:yyyy-MM-dd} to {newEndDate:yyyy-MM-dd})");
                
                // データが存在しない期間として記録（SQLiteとメモリの両方）
                await _stockDataCache.RecordNoDataPeriodAsync(symbol, newStartDate, newEndDate);
                RecordNoDataPeriod(symbol, newStartDate, newEndDate);
                
                // キャッシュされたデータを返す（あれば）
                if (cachedData != null)
                {
                    return cachedData.Data.Where(d => d.DateTime >= startDate && d.DateTime <= endDate).ToList();
                }
                return new List<StockData>();
            }

            // データをフェッチ
            var newData = await FetchStockDataAsync(symbol, newStartDate, newEndDate);

            // データが取得できなかった場合
            if (newData.Count == 0)
            {
                _logger.LogWarning($"シンボル {symbol} の期間 {newStartDate:yyyy-MM-dd} から {newEndDate:yyyy-MM-dd} のデータが取得できませんでした (No data available for symbol {symbol} from {newStartDate:yyyy-MM-dd} to {newEndDate:yyyy-MM-dd})");
                
                // データが存在しない期間として記録（SQLiteとメモリの両方）
                await _stockDataCache.RecordNoDataPeriodAsync(symbol, newStartDate, newEndDate);
                RecordNoDataPeriod(symbol, newStartDate, newEndDate);
                
                // キャッシュされたデータを返す（あれば）
                if (cachedData != null)
                {
                    return cachedData.Data.Where(d => d.DateTime >= startDate && d.DateTime <= endDate).ToList();
                }
                return new List<StockData>();
            }

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

            // メモリキャッシュを更新
            if (cachedData != null)
            {
                var combinedData = cachedData.Data.Concat(newData).DistinctBy(d => d.DateTime).ToList();
                _dataCache[symbol] = new SymbolCacheData { Data = combinedData };
                
                return combinedData.Where(d => d.DateTime >= startDate && d.DateTime <= endDate).ToList();
            }
            else
            {
                _dataCache[symbol] = new SymbolCacheData { Data = newData };
                return newData;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
        {
            _logger.LogError(ex, $"シンボル {symbol} のデータ取得中にエラーが発生しました (Error fetching data for symbol {symbol})");
            
            if (_dataCache.TryGetValue(symbol, out var cachedData) && cachedData != null)
            {
                return cachedData.Data.Where(d => d.DateTime >= startDate && d.DateTime <= endDate).ToList();
            }
            
            throw;
        }
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
            // TradingDayCacheServiceが利用可能な場合はそれを使用
            if (_tradingDayCache != null)
            {
                _logger.LogInformation("TradingDayCacheServiceを使用して営業日をチェックしています: {StartDate}から{EndDate}まで (Checking trading days using cache service)",
                    startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
                
                return await _tradingDayCache.CheckTradingDayRangeAsync(startDate, endDate);
            }
            
            // キャッシュサービスが利用できない場合は直接APIを呼び出す
            _logger.LogInformation("NYダウのデータを取得して営業日かどうかを判断しています: {StartDate}から{EndDate}まで (Checking if date range contains trading days using DJI)",
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
        return await _retryPolicy.ExecuteAsync(async (context) =>
        {
            context["Symbol"] = symbol;

            try
            {
                // 日付の正規化（時刻情報を削除）
                startDate = startDate.Date;
                endDate = endDate.Date;

                _logger.LogInformation("{Symbol}のデータを取得しています: {StartDate}から{EndDate}まで (Fetching data for symbol)",
                    symbol,
                    startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

                _logger.LogInformation("【日付追跡】StockDataService - FetchStockDataAsync開始 - startDate: {StartDate}, Year: {Year}, endDate: {EndDate}, Year: {Year} (Date tracking)",
                    startDate.ToString("yyyy-MM-dd HH:mm:ss"), startDate.Year, endDate.ToString("yyyy-MM-dd HH:mm:ss"), endDate.Year);

                // ETRシンボルの特別処理
                if (symbol == "ETR")
                {
                    _logger.LogInformation("ETRシンボルの特別処理を適用します (Special handling for ETR symbol)");

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

                _logger.LogInformation("【日付追跡】StockDataService - Unix変換後 - startDate: {StartDate}, unixStartTime: {UnixStart}, endDate: {EndDate}, unixEndTime: {UnixEnd} (Date tracking after Unix conversion)",
                    startDateEST.ToString("yyyy-MM-dd HH:mm:ss"), unixStartTime, endDateEST.ToString("yyyy-MM-dd HH:mm:ss"), unixEndTime);

                // 変換後の日付を逆算して確認
                var checkStartDate = DateTimeOffset.FromUnixTimeSeconds(unixStartTime).DateTime;
                var checkEndDate = DateTimeOffset.FromUnixTimeSeconds(unixEndTime).DateTime;
                _logger.LogInformation("【日付追跡】StockDataService - Unix変換チェック - 元のstartDate: {OrigStartDate}, 変換後: {ConvStartDate}, 元のendDate: {OrigEndDate}, 変換後: {ConvEndDate} (Date tracking - Unix conversion check)",
                    startDateEST.ToString("yyyy-MM-dd HH:mm:ss"), checkStartDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    endDateEST.ToString("yyyy-MM-dd HH:mm:ss"), checkEndDate.ToString("yyyy-MM-dd HH:mm:ss"));

                _logger.LogDebug("Unixタイムスタンプ - 開始: {StartTime}, 終了: {EndTime} (Unix timestamps)", unixStartTime, unixEndTime);

                // ピリオドを含むシンボルの特別処理（例：BRK.B → BRK-B）
                // なんでもかんでもではないらしい。もしかしたら個別株だけのルールかも。指数だと"."のままの指数があった。
                string yahooSymbol = symbol;
                //if (symbol.Contains("."))
                //{
                //    yahooSymbol = symbol.Replace(".", "-");
                //    _logger.LogInformation("ピリオドを含むシンボルをYahoo Finance用に変換: {OriginalSymbol} -> {YahooSymbol} (Symbol contains period, converting for Yahoo Finance)",
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

                    // "No data found, symbol may be delisted"エラーの検出
                    if (errorContent.Contains("\"code\":\"Not Found\"") && errorContent.Contains("\"description\":\"No data found, symbol may be delisted\""))
                    {

                        if (yahooSymbol.Contains("."))
                        {
                            yahooSymbol = yahooSymbol.Replace(".", "-");
                            _logger.LogInformation("ピリオドを含むシンボルをYahoo Finance用に変換して再挑戦: {OriginalSymbol} -> {YahooSymbol} (Symbol contains period, converting for Yahoo Finance)",
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
                        _logger.LogInformation("ピリオドを含むシンボルをYahoo Finance用に変換して再挑戦: {OriginalSymbol} -> {YahooSymbol} (Symbol contains period, converting for Yahoo Finance)",
                            symbol, yahooSymbol);

                        goto label_retry;

                    }


                    _logger.LogError("{Symbol}のTimestampデータがありません (No Timestamp data for symbol)", symbol);
                    throw new DataParsingException($"No Timestamp data for {symbol}");
                }

                if (result.Indicators == null)
                {
                    _logger.LogError("{Symbol}のIndicatorsデータがありません (No Indicators data for symbol)", symbol);
                    throw new DataParsingException($"No Indicators data for {symbol}");
                }

                if (result.Indicators.Quote == null || !result.Indicators.Quote.Any())
                {
                    _logger.LogError("{Symbol}のQuoteデータがありません (No Quote data for symbol)", symbol);
                    throw new DataParsingException($"No Quote data for {symbol}");
                }

                var quote = result.Indicators.Quote[0];

                // 各価格データの存在を個別に確認
                if (quote.High == null)
                {
                    _logger.LogError("{Symbol}の高値データがありません (No High data for symbol)", symbol);
                    throw new DataParsingException($"No High data for {symbol}");
                }

                if (quote.Low == null)
                {
                    _logger.LogError("{Symbol}の安値データがありません (No Low data for symbol)", symbol);
                    throw new DataParsingException($"No Low data for {symbol}");
                }

                if (quote.Open == null)
                {
                    _logger.LogError("{Symbol}の始値データがありません (No Open data for symbol)", symbol);
                    throw new DataParsingException($"No Open data for {symbol}");
                }

                if (quote.Close == null)
                {
                    _logger.LogError("{Symbol}の終値データがありません (No Close data for symbol)", symbol);
                    throw new DataParsingException($"No Close data for {symbol}");
                }

                if (quote.Volume == null)
                {
                    _logger.LogError("{Symbol}の出来高データがありません (No Volume data for symbol)", symbol);
                    throw new DataParsingException($"No Volume data for {symbol}");
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

                _logger.LogInformation("{Symbol}の{Count}データポイントの取得に成功しました (Successfully fetched data points for symbol)",
                    stockDataList.Count, symbol);

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
            _logger.LogInformation("{Symbol}の{Date}のデータが自動修正されました (Data was automatically corrected for symbol at date)", data.Symbol, data.DateTime);
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
        _logger.LogInformation("{Symbol}の{StartDate}から{EndDate}までのデータは存在しません (No data exists for symbol from start date to end date)",
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

        // TradingDayCacheServiceは全体的な休業日を管理している
        // ここで追加するべきものではない。どこかで"^DJI"のようなメジャー指数のカレンダーで調整すべき

        //// TradingDayCacheServiceが利用可能な場合、そちらにも記録
        //if (_tradingDayCache != null)
        //{
        //    // 日単位でデータなし期間を記録
        //    var currentDate = start;

        //    while (currentDate <= end)
        //    {
        //        _tradingDayCache.RecordNoDataPeriod(currentDate);
        //        _logger.LogInformation($"日付 {currentDate:yyyy-MM-dd} をTradingDayCacheServiceにデータなし期間として記録しました (Date recorded as no-data period in cache service)");
        //        currentDate = currentDate.AddDays(1);
        //    }
        //}
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
                    _logger.LogInformation($"日付 {currentDate:yyyy-MM-dd} はTradingDayCacheServiceによって全体的なデータなし期間と判断されました (Date marked as no-data period by cache service)");
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
                _logger.LogInformation($"期間 {startDate:yyyy-MM-dd} から {endDate:yyyy-MM-dd} は、ローカルキャッシュのデータなし期間 {period.Start:yyyy-MM-dd} から {period.End:yyyy-MM-dd} に含まれています (Date range is within a known no-data period)");
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

    private static long ToUnixTimestamp(DateTime dateTime)
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (long)(dateTime.ToUniversalTime() - epoch).TotalSeconds;
    }

    private static DateTime FromUnixTimestamp(long unixTime)
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return epoch.AddSeconds(unixTime).ToLocalTime();
    }

    private void RecordDelistedSymbol(string symbol)
    {
        if (!_delistedSymbols.Contains(symbol))
        {
            _delistedSymbols.Add(symbol);
            _logger.LogInformation("銘柄{symbol}を上場廃止リストに追加しました (Added symbol {symbol} to delisted symbols list)", symbol);
        }
    }
}
