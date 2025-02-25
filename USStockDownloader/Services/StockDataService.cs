using System.Net.Http;
using System.Text.Json;
using USStockDownloader.Models;
using Microsoft.Extensions.Logging;
using USStockDownloader.Exceptions;
using USStockDownloader.Options;
using Polly;
using Polly.Retry;
using System.Threading;

namespace USStockDownloader.Services;

public class StockDataService : IStockDataService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StockDataService> _logger;
    private readonly AsyncRetryPolicy<List<StockData>> _retryPolicy;
    private static readonly Random _jitter = new Random();
    private static readonly SemaphoreSlim _requestThrottler = new SemaphoreSlim(1, 1);
    private static readonly string[] _endpoints = new[]
    {
        "query1.finance.yahoo.com",
        "query2.finance.yahoo.com",
        "finance.yahoo.com",           // 直接のエンドポイント
        "query.yahoo.com"              // 代替エンドポイント
    };

    private static readonly string[] _symbolFormats = new[]
    {
        "{0}",       // 標準形式: ETR
        "{0}.N",     // NYSE形式: ETR.N
        "{0}.NY",    // 代替NYSE形式: ETR.NY
        "{0}.NE",    // 別の代替NYSE形式: ETR.NE
        "{0}:US",    // US市場形式: ETR:US
        "{0}.NYSE"   // 完全なNYSE形式: ETR.NYSE
    };

    public StockDataService(HttpClient httpClient, ILogger<StockDataService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        // HTTPヘッダーの設定
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
        _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9,ja;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Sec-Ch-Ua", "\"Chromium\";v=\"122\", \"Not(A:Brand\";v=\"24\", \"Google Chrome\";v=\"122\"");
        _httpClient.DefaultRequestHeaders.Add("Sec-Ch-Ua-Mobile", "?0");
        _httpClient.DefaultRequestHeaders.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
        _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        
        // リトライポリシーの設定
        _retryPolicy = Policy<List<StockData>>
            .Handle<HttpRequestException>()
            .Or<StockDataException>()
            .WaitAndRetryAsync(
                5, // リトライ回数を増やす
                retryAttempt => 
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt + 1)) + // 基本待機時間を増やす
                               TimeSpan.FromMilliseconds(_jitter.Next(0, 2000)); // ジッターも増やす
                    _logger.LogWarning("Retry attempt {RetryCount}, waiting {DelayMs}ms", 
                        retryAttempt, delay.TotalMilliseconds);
                    return delay;
                });
    }

    public async Task<List<StockData>> GetStockDataAsync(string symbol, DateTime? startDate = null, DateTime? endDate = null)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                endDate ??= DateTime.Now;
                startDate ??= endDate.Value.AddYears(-1);

                var cleanSymbol = NormalizeSymbol(symbol.Trim());
                if (!IsValidSymbol(cleanSymbol))
                {
                    throw new StockDataException($"Invalid symbol format: {symbol}");
                }

                // シンボルの検証と代替シンボルの取得
                var validSymbol = await ValidateAndGetSymbol(cleanSymbol);
                if (validSymbol != cleanSymbol)
                {
                    _logger.LogInformation("Using alternative symbol {ValidSymbol} for {OriginalSymbol}", validSymbol, cleanSymbol);
                }

                // 最初に1年分のデータを一括で取得を試みる
                try
                {
                    var data = await FetchDataFromEndpoints(validSymbol, startDate.Value, endDate.Value);
                    if (data.Any())
                    {
                        return data;
                    }
                }
                catch (StockDataException ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch full year data for {Symbol}, trying with shorter periods", validSymbol);
                }

                // 失敗した場合、3ヶ月ごとに分割して取得を試みる
                var allData = new List<StockData>();
                var currentStart = startDate.Value;
                while (currentStart < endDate.Value)
                {
                    var currentEnd = currentStart.AddMonths(3);
                    if (currentEnd > endDate.Value)
                    {
                        currentEnd = endDate.Value;
                    }

                    try
                    {
                        var periodData = await FetchDataFromEndpoints(validSymbol, currentStart, currentEnd);
                        allData.AddRange(periodData);
                        _logger.LogInformation("Successfully fetched data for {Symbol} from {Start} to {End}", 
                            validSymbol, currentStart.ToString("yyyy-MM-dd"), currentEnd.ToString("yyyy-MM-dd"));
                    }
                    catch (StockDataException ex)
                    {
                        _logger.LogError(ex, "Failed to fetch data for {Symbol} from {Start} to {End}", 
                            validSymbol, currentStart.ToString("yyyy-MM-dd"), currentEnd.ToString("yyyy-MM-dd"));
                    }

                    currentStart = currentEnd.AddDays(1);
                }

                if (!allData.Any())
                {
                    throw new StockDataException($"Failed to fetch any data for {validSymbol} in all attempted periods");
                }

                return allData.OrderByDescending(d => d.DateNumber).ToList();
            }
            catch (Exception ex) when (ex is not StockDataException)
            {
                _logger.LogError(ex, "Unexpected error while fetching data for {Symbol}", symbol);
                throw new StockDataException($"Unexpected error while fetching data for {symbol}: {ex.Message}", ex);
            }
        });
    }

    private async Task<HttpResponseMessage> SendRequestWithThrottlingAsync(string url)
    {
        await _requestThrottler.WaitAsync();
        try
        {
            // リクエスト間隔を設定（2秒に増やす）
            await Task.Delay(2000);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            
            // リクエスト固有のヘッダーを設定
            request.Headers.Referrer = new Uri("https://finance.yahoo.com/");
            request.Headers.Add("Cache-Control", "max-age=0");
            
            // Yahoo Finance APIのエンドポイントの場合は追加のヘッダーを設定
            if (url.Contains("/v8/finance/chart/") || url.Contains("/v1/finance/search"))
            {
                request.Headers.Add("Origin", "https://finance.yahoo.com");
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            }

            var response = await _httpClient.SendAsync(request);
            
            // エラーの詳細をログに記録
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogError("Request to {Url} failed with status code {StatusCode}. Response: {Response}", 
                    url, response.StatusCode, content);
            }
            
            return response;
        }
        finally
        {
            _requestThrottler.Release();
        }
    }

    private async Task<string> ValidateAndGetSymbol(string symbol)
    {
        // まず、直接URLでの存在確認を試みる
        try
        {
            var quoteUrl = $"https://finance.yahoo.com/quote/{symbol}";
            var response = await SendRequestWithThrottlingAsync(quoteUrl);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Symbol {Symbol} validated via direct URL check", symbol);
                return symbol;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while checking direct URL for symbol {Symbol}", symbol);
        }

        // 各シンボル形式を試行
        foreach (var format in _symbolFormats)
        {
            var testSymbol = string.Format(format, symbol);
            try
            {
                // Yahoo Finance の検索APIを使用してシンボルを検証
                var searchUrls = new[]
                {
                    $"https://query2.finance.yahoo.com/v1/finance/search?q={testSymbol}&quotesCount=1&newsCount=0&enableFuzzyQuery=false&quotesQueryId=tss_match_phrase_query",
                    $"https://finance.yahoo.com/lookup?s={testSymbol}"
                };

                foreach (var url in searchUrls)
                {
                    var response = await SendRequestWithThrottlingAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        
                        // 検索結果からシンボルを抽出
                        if (TryExtractSymbolFromResponse(content, testSymbol, out var foundSymbol))
                        {
                            _logger.LogInformation("Found valid symbol {Symbol} for search term {SearchTerm} using {Url}", 
                                foundSymbol, testSymbol, url);
                            return foundSymbol;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Symbol validation request failed for {Symbol} with status code {StatusCode}", 
                            testSymbol, response.StatusCode);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while validating symbol {Symbol}", testSymbol);
            }
        }

        // 有効なシンボルが見つからない場合は元のシンボルを返す
        return symbol;
    }

    private async Task<List<StockData>> FetchDataFromEndpoints(string symbol, DateTime startDate, DateTime endDate)
    {
        var endpoints = new[]
        {
            "query1.finance.yahoo.com",
            "query2.finance.yahoo.com",
            "finance.yahoo.com"
        };

        List<StockData> stockDataList = null;
        Exception lastException = null;

        foreach (var endpoint in endpoints)
        {
            try
            {
                _logger.LogInformation("Fetching data for {Symbol} from {StartDate} to {EndDate} using endpoint {Endpoint}", 
                    symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), endpoint);

                var url = $"https://{endpoint}/v8/finance/chart/{symbol}?period1={ToUnixTimestamp(startDate)}&period2={ToUnixTimestamp(endDate)}&interval=1d";
                var response = await SendRequestWithThrottlingAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    stockDataList = ParseStockData(content, symbol);
                    if (stockDataList != null && stockDataList.Any())
                    {
                        return stockDataList;
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                {
                    _logger.LogWarning("Server error for {Symbol} on endpoint {Endpoint}. Trying next endpoint.", 
                        symbol, endpoint);
                    continue;
                }
                else
                {
                    var content = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Request failed for {Symbol} with status code {StatusCode}. Response: {Response}", 
                        symbol, response.StatusCode, content);
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogError(ex, "Error fetching data for {Symbol} from endpoint {Endpoint}", symbol, endpoint);
            }
        }

        if (lastException != null)
        {
            throw new StockDataException($"Failed to fetch data for symbol {symbol} from all endpoints", lastException);
        }

        return new List<StockData>();
    }

    private async Task<List<StockData>> ParseYahooFinanceResponse(string content, string symbol)
    {
        var jsonDocument = JsonDocument.Parse(content);
        var root = jsonDocument.RootElement;

        if (!root.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var result) ||
            result.GetArrayLength() == 0)
        {
            throw new StockDataException($"Invalid response format for symbol {symbol}");
        }

        var data = result[0];
        if (!data.TryGetProperty("timestamp", out var timestamps) ||
            !data.TryGetProperty("indicators", out var indicators) ||
            !indicators.TryGetProperty("quote", out var quotes) ||
            quotes.GetArrayLength() == 0)
        {
            throw new StockDataException($"Missing required data for symbol {symbol}");
        }

        var quote = quotes[0];
        var timestampArray = timestamps.EnumerateArray().Select(t => t.GetInt64()).ToArray();
        var opens = quote.GetProperty("open").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var highs = quote.GetProperty("high").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var lows = quote.GetProperty("low").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var closes = quote.GetProperty("close").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var volumes = quote.GetProperty("volume").EnumerateArray().Select(v => v.GetInt64()).ToArray();

        var stockDataList = new List<StockData>();
        for (int i = 0; i < timestampArray.Length; i++)
        {
            if (opens[i] == 0 || double.IsNaN(opens[i])) continue;

            var date = DateTimeOffset.FromUnixTimeSeconds(timestampArray[i]).DateTime;
            stockDataList.Add(new StockData
            {
                DateNumber = int.Parse(date.ToString("yyyyMMdd")),
                Open = opens[i],
                High = highs[i],
                Low = lows[i],
                Close = closes[i],
                Volume = volumes[i]
            });
        }

        if (stockDataList.Count == 0)
        {
            throw new StockDataException($"No valid data points found for symbol {symbol}");
        }

        return stockDataList;
    }

    private bool TryExtractSymbolFromResponse(string content, string searchSymbol, out string foundSymbol)
    {
        foundSymbol = null;
        try
        {
            var jsonDocument = JsonDocument.Parse(content);
            
            // 検索APIのレスポンス形式をチェック
            if (jsonDocument.RootElement.TryGetProperty("quotes", out var quotes) && 
                quotes.GetArrayLength() > 0)
            {
                var firstQuote = quotes[0];
                if (firstQuote.TryGetProperty("symbol", out var symbolElement))
                {
                    foundSymbol = symbolElement.GetString();
                    return !string.IsNullOrEmpty(foundSymbol);
                }
            }
            
            // ルックアップページのレスポンス形式をチェック
            if (jsonDocument.RootElement.TryGetProperty("results", out var results) && 
                results.GetArrayLength() > 0)
            {
                var firstResult = results[0];
                if (firstResult.TryGetProperty("symbol", out var symbolElement))
                {
                    foundSymbol = symbolElement.GetString();
                    return !string.IsNullOrEmpty(foundSymbol);
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON response while searching for symbol {Symbol}", searchSymbol);
        }
        
        return false;
    }

    private string NormalizeSymbol(string symbol)
    {
        return symbol.Replace(".", "-");
    }

    private bool IsValidSymbol(string symbol)
    {
        return !string.IsNullOrWhiteSpace(symbol) && 
               symbol.All(c => char.IsLetterOrDigit(c) || c == '-') &&
               symbol.Length <= 10; 
    }

    private long ToUnixTimestamp(DateTime date)
    {
        return ((DateTimeOffset)date).ToUnixTimeSeconds();
    }

    private List<StockData> ParseStockData(string content, string symbol)
    {
        try
        {
            return ParseYahooFinanceResponse(content, symbol).Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse stock data for symbol {Symbol}", symbol);
            return new List<StockData>();
        }
    }
}
