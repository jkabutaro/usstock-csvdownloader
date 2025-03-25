using System.Net;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Polly;
using Polly.Retry;
using USStockDownloader.Models;
using USStockDownloader.Exceptions;
using System.IO;
using System.Collections.Concurrent;

namespace USStockDownloader.Services;

public class StockDataService : IStockDataService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StockDataService> _logger;
    private static readonly Random _random = new Random();
    private readonly AsyncRetryPolicy<List<StockData>> _retryPolicy;
    
    // シンボルごとのキャッシュデータを保持
    private readonly ConcurrentDictionary<string, List<StockData>> _dataCache = new ConcurrentDictionary<string, List<StockData>>();
    private readonly string _cacheDirPath;

    public StockDataService(HttpClient httpClient, ILogger<StockDataService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // デフォルトのヘッダーを設定
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9,ja;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://finance.yahoo.com/");

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
            
        // キャッシュディレクトリのパスを設定（相対パスを使用）
        _cacheDirPath = Path.Combine("Cache", "StockDataCache");
            
        // ディレクトリが存在しない場合は作成
        if (!Directory.Exists(_cacheDirPath))
        {
            Directory.CreateDirectory(_cacheDirPath);
        }
    }

    public async Task<List<StockData>> GetStockDataAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        // 日付の正規化（時刻情報を削除）
        startDate = startDate.Date;
        endDate = endDate.Date;
        
        // キャッシュからデータを読み込む
        LoadCacheForSymbol(symbol);
        
        // キャッシュにデータがある場合、日付範囲をチェック
        if (_dataCache.TryGetValue(symbol, out var cachedData) && cachedData.Any())
        {
            var cachedStartDate = cachedData.Min(d => d.DateTime);
            var cachedEndDate = cachedData.Max(d => d.DateTime);
            
            _logger.LogDebug("キャッシュデータの日付範囲 - {Symbol}: {StartDate} から {EndDate} (Cached date range for {Symbol})",
                symbol, cachedStartDate.ToString("yyyy-MM-dd"), cachedEndDate.ToString("yyyy-MM-dd"), symbol);
            
            // キャッシュが要求された日付範囲を完全にカバーしている場合
            if (startDate >= cachedStartDate && endDate <= cachedEndDate)
            {
                _logger.LogInformation("キャッシュからデータを返します - {Symbol}: {StartDate} から {EndDate} (Returning data from cache for {Symbol})",
                    symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), symbol);
                
                return cachedData
                    .Where(d => d.DateTime >= startDate && d.DateTime <= endDate)
                    .OrderBy(d => d.DateTime)
                    .ToList();
            }
            
            // 部分的な重複がある場合、必要な部分だけを取得
            if ((startDate < cachedStartDate && endDate >= cachedStartDate) ||
                (startDate <= cachedEndDate && endDate > cachedEndDate))
            {
                _logger.LogInformation("キャッシュと部分的に重複する日付範囲です - {Symbol} (Partially overlapping date range with cache for {Symbol})", symbol, symbol);
                
                DateTime newStartDate = startDate;
                DateTime newEndDate = endDate;
                
                // 開始日が重複している場合、キャッシュの開始日の前日までを取得
                if (startDate < cachedStartDate && endDate >= cachedStartDate)
                {
                    newStartDate = startDate;
                    newEndDate = cachedStartDate.AddDays(-1);
                    _logger.LogDebug("開始日が重複: 新しい日付範囲 {StartDate} から {EndDate} (Start date overlap: new range)",
                        newStartDate.ToString("yyyy-MM-dd"), newEndDate.ToString("yyyy-MM-dd"));
                    
                    // 取得する日付範囲に営業日があるかチェック
                    if (!await CheckTradingDayRangeAsync(newStartDate, newEndDate))
                    {
                        _logger.LogWarning("取得する日付範囲に営業日がないため、キャッシュのみを返します - {Symbol} (No trading days in date range, returning cache only for {Symbol})",
                            symbol, symbol);
                        
                        // キャッシュの日付範囲を更新（開始日を新しい開始日に設定）
                        var updatedCachedData = cachedData.ToList();
                        
                        // キャッシュを更新
                        _dataCache[symbol] = updatedCachedData;
                        SaveCacheForSymbol(symbol);
                        
                        // キャッシュから要求された日付範囲のデータを返す
                        return cachedData
                            .Where(d => d.DateTime >= startDate && d.DateTime <= endDate)
                            .OrderBy(d => d.DateTime)
                            .ToList();
                    }
                }
                // 終了日が重複している場合、キャッシュの終了日の翌日からを取得
                else if (startDate <= cachedEndDate && endDate > cachedEndDate)
                {
                    newStartDate = cachedEndDate.AddDays(1);
                    newEndDate = endDate;
                    _logger.LogDebug("終了日が重複: 新しい日付範囲 {StartDate} から {EndDate} (End date overlap: new range)",
                        newStartDate.ToString("yyyy-MM-dd"), newEndDate.ToString("yyyy-MM-dd"));
                    
                    // 取得する日付範囲に営業日があるかチェック
                    if (!await CheckTradingDayRangeAsync(newStartDate, newEndDate))
                    {
                        _logger.LogWarning("取得する日付範囲に営業日がないため、キャッシュのみを返します - {Symbol} (No trading days in date range, returning cache only for {Symbol})",
                            symbol, symbol);
                        
                        // キャッシュの日付範囲を更新（終了日を新しい終了日に設定）
                        var updatedCachedData = cachedData.ToList();
                        
                        // キャッシュを更新
                        _dataCache[symbol] = updatedCachedData;
                        SaveCacheForSymbol(symbol);
                        
                        // キャッシュから要求された日付範囲のデータを返す
                        return cachedData
                            .Where(d => d.DateTime >= startDate && d.DateTime <= endDate)
                            .OrderBy(d => d.DateTime)
                            .ToList();
                    }
                }
                
                // 新しい日付範囲のデータを取得
                var newData = await FetchStockDataAsync(symbol, newStartDate, newEndDate);
                
                // キャッシュと新しいデータを結合
                var combinedData = new List<StockData>(cachedData);
                combinedData.AddRange(newData);
                
                // 重複を除去して日付順にソート
                var result = combinedData
                    .GroupBy(d => d.Date)
                    .Select(g => g.First())
                    .Where(d => d.DateTime >= startDate && d.DateTime <= endDate)
                    .OrderBy(d => d.DateTime)
                    .ToList();
                
                // 更新されたデータをキャッシュに保存
                _dataCache[symbol] = combinedData
                    .GroupBy(d => d.Date)
                    .Select(g => g.First())
                    .OrderBy(d => d.DateTime)
                    .ToList();
                
                // キャッシュをファイルに保存
                SaveCacheForSymbol(symbol);
                
                return result;
            }
        }
        
        // キャッシュにデータがない場合、または日付範囲が完全に異なる場合は新しくデータを取得
        var stockData = await FetchStockDataAsync(symbol, startDate, endDate);
        
        // 取得したデータをキャッシュに追加または更新
        if (stockData.Any())
        {
            if (_dataCache.TryGetValue(symbol, out var existingData))
            {
                // 既存のキャッシュデータと結合
                var combinedData = new List<StockData>(existingData);
                combinedData.AddRange(stockData);
                
                // 重複を除去して日付順にソート
                _dataCache[symbol] = combinedData
                    .GroupBy(d => d.Date)
                    .Select(g => g.First())
                    .OrderBy(d => d.DateTime)
                    .ToList();
            }
            else
            {
                // 新しくキャッシュに追加
                _dataCache[symbol] = new List<StockData>(stockData);
            }
            
            // キャッシュをファイルに保存
            SaveCacheForSymbol(symbol);
        }
        
        return stockData;
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
            _logger.LogInformation("NYダウのデータを取得して営業日かどうかを判断しています: {StartDate}から{EndDate}まで (Checking if date range contains trading days using DJI)",
                startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
            
            // NYダウ（^DJI）のデータを取得（リトライなし）
            var djiData = await FetchStockDataWithoutRetryAsync("^DJI", startDate, endDate);
            
            // データが取得できた場合は営業日あり
            if (djiData != null && djiData.Any())
            {
                _logger.LogInformation("指定された日付範囲に営業日があります: {Count}日 (Date range contains trading days)", djiData.Count);
                return true;
            }
            
            _logger.LogWarning("指定された日付範囲に営業日がありません (Date range does not contain trading days)");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "営業日判断中にエラーが発生しました (Error occurred while checking trading days)");
            // エラーが発生した場合は、念のため営業日ありと判断
            return true;
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
                    _logger.LogWarning("開始日が終了日よりも後になっています。日付を入れ替えます (Start date is after end date, swapping dates)");
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
                string yahooSymbol = symbol;
                if (symbol.Contains("."))
                {
                    yahooSymbol = symbol.Replace(".", "-");
                    _logger.LogInformation("ピリオドを含むシンボルをYahoo Finance用に変換: {OriginalSymbol} -> {YahooSymbol} (Symbol contains period, converting for Yahoo Finance)", 
                        symbol, yahooSymbol);
                }

                // URLエンコーディングを適用
                string encodedSymbol = Uri.EscapeDataString(yahooSymbol);
                _logger.LogDebug("元のシンボル: {Symbol}, Yahoo用シンボル: {YahooSymbol}, エンコード後: {EncodedSymbol} (Original symbol, Yahoo symbol, Encoded symbol)", 
                    symbol, yahooSymbol, encodedSymbol);

                // 追加のパラメータを含めたURLを構築
                var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{encodedSymbol}?period1={unixStartTime}&period2={unixEndTime}&interval=1d&includePrePost=false&events=div%2Csplit";
                _logger.LogDebug("リクエストURL: {Url} (Request URL)", url);

                // HttpRequestMessageを使用して詳細なリクエスト設定
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Origin", "https://finance.yahoo.com");
                
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

                // JsonSerializerOptionsを設定
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var yahooResponse = JsonSerializer.Deserialize<YahooFinanceResponse>(content, options);
                
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
                        Date = dateTime.Year * 10000 + dateTime.Month * 100 + dateTime.Day,
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
            catch (Exception ex) when (ex is not RateLimitException)
            {
                _logger.LogError(ex, "{Symbol}のデータ取得中にエラーが発生しました (Error fetching data for symbol)", symbol);
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
            // 日付の正規化（時刻情報を削除）
            startDate = startDate.Date;
            endDate = endDate.Date;
            
            _logger.LogDebug("リトライなしでデータを取得しています - {Symbol}: {StartDate}から{EndDate}まで (Fetching data without retry)",
                symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

            // 日付の順序を確認し、必要に応じて入れ替え
            if (startDate > endDate)
            {
                var temp = startDate;
                startDate = endDate;
                endDate = temp;
            }

            // 日付を米国東部標準時（EST、UTC-5）の正午に設定してUnixタイムスタンプに変換
            var startDateEST = new DateTime(startDate.Year, startDate.Month, startDate.Day, 12, 0, 0);
            var endDateEST = new DateTime(endDate.Year, endDate.Month, endDate.Day, 12, 0, 0);
            var unixStartTime = new DateTimeOffset(startDateEST, TimeSpan.FromHours(-5)).ToUnixTimeSeconds();
            var unixEndTime = new DateTimeOffset(endDateEST, TimeSpan.FromHours(-5)).ToUnixTimeSeconds();

            // ピリオドを含むシンボルの特別処理（例：BRK.B → BRK-B）
            string yahooSymbol = symbol;
            if (symbol.Contains("."))
            {
                yahooSymbol = symbol.Replace(".", "-");
            }

            // URLエンコーディングを適用
            string encodedSymbol = Uri.EscapeDataString(yahooSymbol);

            // Yahoo Finance APIのURL
            string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{encodedSymbol}?period1={unixStartTime}&period2={unixEndTime}&interval=1d&includePrePost=false&events=div%2Csplit";

            // HTTPリクエストの作成
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            request.Headers.Add("Origin", "https://finance.yahoo.com");

            // HTTPリクエストの送信
            using var response = await _httpClient.SendAsync(request);

            // レスポンスの確認
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Yahoo Finance APIからのレスポンスが失敗しました - {Symbol}: StatusCode={StatusCode} (Yahoo Finance API response failed)",
                    symbol, response.StatusCode);
                return new List<StockData>();
            }

            // レスポンスの読み取り
            var content = await response.Content.ReadAsStringAsync();

            // JsonSerializerOptionsを設定
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            var yahooResponse = JsonSerializer.Deserialize<YahooFinanceResponse>(content, options);
            
            // レスポンスの基本構造を検証
            if (yahooResponse?.Chart == null || 
                yahooResponse.Chart.Result == null || 
                !yahooResponse.Chart.Result.Any() ||
                yahooResponse.Chart.Error != null)
            {
                _logger.LogWarning("{Symbol}のレスポンスが無効です (Invalid response for symbol)", symbol);
                return new List<StockData>();
            }

            var result = yahooResponse.Chart.Result[0];
            
            // 必要なデータが存在するか確認
            if (result.Timestamp == null || result.Timestamp.Count == 0 ||
                result.Indicators?.Quote == null || !result.Indicators.Quote.Any() ||
                result.Indicators.Quote[0].Open == null || result.Indicators.Quote[0].High == null ||
                result.Indicators.Quote[0].Low == null || result.Indicators.Quote[0].Close == null ||
                result.Indicators.Quote[0].Volume == null)
            {
                _logger.LogWarning("{Symbol}の必要なデータが不足しています (Missing required data for symbol)", symbol);
                return new List<StockData>();
            }

            var quote = result.Indicators.Quote[0];
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
                
                // Null許容値型の安全な処理
                decimal open = quote.Open[i].HasValue ? quote.Open[i].Value : 0m;
                decimal high = quote.High[i].HasValue ? quote.High[i].Value : 0m;
                decimal low = quote.Low[i].HasValue ? quote.Low[i].Value : 0m;
                decimal close = quote.Close[i].HasValue ? quote.Close[i].Value : 0m;
                long volume = quote.Volume[i].HasValue ? quote.Volume[i].Value : 0L;

                var stockData = new StockData
                {
                    Symbol = symbol,
                    DateTime = dateTime,
                    Date = dateTime.Year * 10000 + dateTime.Month * 100 + dateTime.Day,
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
            }

            return stockDataList;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "リトライなしのデータ取得中にエラーが発生しました - {Symbol} (Error in data fetch without retry)", symbol);
            return new List<StockData>();
        }
    }
    
    /// <summary>
    /// シンボルのキャッシュデータをファイルから読み込む
    /// </summary>
    private void LoadCacheForSymbol(string symbol)
    {
        try
        {
            var cacheFilePath = GetCacheFilePath(symbol);
            if (File.Exists(cacheFilePath))
            {
                var json = File.ReadAllText(cacheFilePath);
                var cachedData = JsonSerializer.Deserialize<List<StockData>>(json);
                
                if (cachedData != null && cachedData.Any())
                {
                    _dataCache[symbol] = cachedData;
                    _logger.LogDebug("{Symbol}のキャッシュデータを読み込みました: {Count}データポイント, {StartDate}から{EndDate}まで (Loaded cache data for symbol)",
                        symbol, cachedData.Count, cachedData.Min(d => d.DateTime).ToString("yyyy-MM-dd"), cachedData.Max(d => d.DateTime).ToString("yyyy-MM-dd"));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Symbol}のキャッシュデータの読み込み中にエラーが発生しました (Error occurred while loading cache data for symbol)", symbol);
            // キャッシュの読み込みに失敗した場合は、キャッシュを使用しない
            _dataCache.TryRemove(symbol, out _);
        }
    }
    
    /// <summary>
    /// シンボルのキャッシュデータをファイルに保存する
    /// </summary>
    private void SaveCacheForSymbol(string symbol)
    {
        try
        {
            if (_dataCache.TryGetValue(symbol, out var data) && data.Any())
            {
                var cacheFilePath = GetCacheFilePath(symbol);
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(cacheFilePath, json);
                
                _logger.LogDebug("{Symbol}のキャッシュデータを保存しました: {Count}データポイント (Saved cache data for symbol)",
                    symbol, data.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Symbol}のキャッシュデータの保存中にエラーが発生しました (Error occurred while saving cache data for symbol)", symbol);
            // キャッシュの保存に失敗しても、処理は続行
        }
    }
    
    /// <summary>
    /// シンボルのキャッシュファイルパスを取得する
    /// </summary>
    private string GetCacheFilePath(string symbol)
    {
        // シンボルに無効な文字が含まれている場合は置換
        string safeSymbol = string.Join("_", symbol.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_cacheDirPath, $"{safeSymbol}_cache.json");
    }

    private bool ValidateStockData(StockData data)
    {
        // 基本的なデータ検証
        if (data.Open <= 0 || data.High <= 0 || data.Low <= 0 || data.Close <= 0 || data.Volume <= 0)
        {
            _logger.LogWarning("{Symbol}の{Date}の価格またはボリューム値が無効です (Invalid price or volume values for symbol at date)", data.Symbol, data.DateTime);
            return false;
        }

        // 高値が安値より低い場合
        if (data.High < data.Low)
        {
            _logger.LogWarning("{Symbol}の{Date}の高値が安値より低いです (High is lower than Low for symbol at date)", data.Symbol, data.DateTime);
            return false;
        }

        // 始値または終値が高値より高い、または安値より低い場合
        if (data.Open > data.High || data.Open < data.Low || data.Close > data.High || data.Close < data.Low)
        {
            _logger.LogWarning("{Symbol}の{Date}の始値または終値が範囲外です (Open or Close is out of range for symbol at date)", data.Symbol, data.DateTime);
            return false;
        }

        return true;
    }
}
