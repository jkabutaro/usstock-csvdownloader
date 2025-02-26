using System;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using USStockDownloader.Models;
using USStockDownloader.Options;
using USStockDownloader.Exceptions;
using System.Text.RegularExpressions;

namespace USStockDownloader.Services;

public class StockDataService : IStockDataService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StockDataService> _logger;
    private readonly RetryOptions _retryOptions;
    private readonly DownloadOptions _downloadOptions;
    private const string BASE_URL = "https://query1.finance.yahoo.com/v8/finance/chart/";
    private static readonly Regex _symbolPattern = new Regex(@"^[A-Z\-.]+$", RegexOptions.Compiled);
    private static readonly Random _random = new Random();

    public StockDataService(
        HttpClient httpClient,
        ILogger<StockDataService> logger,
        RetryOptions retryOptions,
        DownloadOptions downloadOptions)
    {
        _httpClient = httpClient;
        _logger = logger;
        _retryOptions = retryOptions;
        _downloadOptions = downloadOptions;

        // ブラウザのような振る舞いをするためのヘッダー設定
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
        _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
        _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
        _httpClient.DefaultRequestHeaders.Add("Pragma", "no-cache");
        _httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
    }

    public async Task<List<StockData>> GetStockDataAsync(string symbol)
    {
        _logger.LogInformation("DEBUG: Starting GetStockDataAsync for {Symbol}", symbol);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));
        }

        var normalizedSymbol = symbol.Trim().ToUpper();
        if (!_symbolPattern.IsMatch(normalizedSymbol))
        {
            throw new InvalidSymbolException($"Invalid symbol format: {symbol}");
        }

        // リクエスト前に少しランダムな待機を入れる（0.5秒〜1.5秒）
        await Task.Delay(TimeSpan.FromMilliseconds(500 + _random.Next(1000)));

        var retryPolicy = CreateRetryPolicy();
        return await retryPolicy.ExecuteAsync(async () =>
        {
            _logger.LogInformation("DEBUG: Making request for {Symbol}", symbol);
            
            var url = $"{BASE_URL}{normalizedSymbol}?interval=1d&range=1y";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            
            // リファラーをランダムに設定
            request.Headers.Referrer = new Uri("https://finance.yahoo.com/quote/" + normalizedSymbol);

            var response = await _httpClient.SendAsync(request);
            
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Rate limit hit for symbol {Symbol}", symbol);
                throw new RateLimitException($"Rate limit exceeded for {symbol}");
            }

            response.EnsureSuccessStatusCode();
            var jsonString = await response.Content.ReadAsStringAsync();

            try
            {
                var data = JsonSerializer.Deserialize<YahooFinanceResponse>(jsonString);
                if (data?.Chart?.Result == null || data.Chart.Result.Count == 0)
                {
                    throw new NoDataException($"No data available for symbol {symbol}");
                }

                var result = data.Chart.Result[0];
                if (result.Timestamp == null || result.Indicators?.Quote == null || result.Indicators.Quote.Count == 0)
                {
                    throw new NoDataException($"Incomplete data for symbol {symbol}");
                }

                var quote = result.Indicators.Quote[0];
                var stockDataList = new List<StockData>();

                for (int i = 0; i < result.Timestamp.Count; i++)
                {
                    if (quote.High?[i] == null || quote.Low?[i] == null || 
                        quote.Open?[i] == null || quote.Close?[i] == null || 
                        quote.Volume?[i] == null)
                    {
                        continue;
                    }

                    var dateTime = DateTimeOffset.FromUnixTimeSeconds(result.Timestamp[i]).DateTime;
                    stockDataList.Add(new StockData
                    {
                        Symbol = symbol,
                        Date = dateTime,
                        Open = quote.Open[i].Value,
                        High = quote.High[i].Value,
                        Low = quote.Low[i].Value,
                        Close = quote.Close[i].Value,
                        Volume = quote.Volume[i].Value
                    });
                }

                return stockDataList;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse JSON response for symbol {Symbol}", symbol);
                throw new DataParsingException($"Failed to parse data for {symbol}", ex);
            }
        });
    }

    private AsyncRetryPolicy<List<StockData>> CreateRetryPolicy()
    {
        return Policy<List<StockData>>
            .Handle<HttpRequestException>()
            .Or<JsonException>()
            .Or<NoDataException>()
            .WaitAndRetryAsync(
                _retryOptions.MaxRetries,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + // 指数バックオフ
                               TimeSpan.FromMilliseconds(_random.Next(100)), // ジッター
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        exception,
                        "Retry {RetryCount} after {DelaySeconds}s for error: {ErrorMessage}",
                        retryCount,
                        timeSpan.TotalSeconds,
                        exception.Message);
                }
            );
    }
}
