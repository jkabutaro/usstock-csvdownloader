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

        var retryPolicy = CreateRetryPolicy();
        return await retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                _logger.LogInformation("DEBUG: Making request for {Symbol}", symbol);
                var startDate = _downloadOptions.GetStartDate();
                var endDate = _downloadOptions.GetEndDate();

                var url = $"{BASE_URL}{normalizedSymbol}?period1={ToUnixTimestamp(startDate)}&period2={ToUnixTimestamp(endDate)}&interval=1d";
                
                var response = await _httpClient.GetAsync(url);
                
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.LogInformation("DEBUG: Rate limit detected for {Symbol}", symbol);
                    _logger.LogWarning("Rate limit hit for symbol {Symbol}", symbol);
                    throw new RateLimitException($"Rate limit exceeded for {symbol}");
                }

                response.EnsureSuccessStatusCode();
                _logger.LogInformation("DEBUG: Successfully got response for {Symbol}", symbol);
                var content = await response.Content.ReadAsStringAsync();

                using var document = JsonDocument.Parse(content);
                var root = document.RootElement;
                var result = root.GetProperty("chart").GetProperty("result")[0];
                var timestamp = result.GetProperty("timestamp").EnumerateArray().ToList();
                var quotes = result.GetProperty("indicators").GetProperty("quote")[0];
                var opens = quotes.GetProperty("open").EnumerateArray().ToList();
                var highs = quotes.GetProperty("high").EnumerateArray().ToList();
                var lows = quotes.GetProperty("low").EnumerateArray().ToList();
                var closes = quotes.GetProperty("close").EnumerateArray().ToList();
                var volumes = quotes.GetProperty("volume").EnumerateArray().ToList();

                var stockData = new List<StockData>();
                for (var i = 0; i < timestamp.Count; i++)
                {
                    if (opens[i].ValueKind == JsonValueKind.Null ||
                        highs[i].ValueKind == JsonValueKind.Null ||
                        lows[i].ValueKind == JsonValueKind.Null ||
                        closes[i].ValueKind == JsonValueKind.Null ||
                        volumes[i].ValueKind == JsonValueKind.Null)
                    {
                        continue;
                    }

                    stockData.Add(new StockData
                    {
                        Symbol = symbol,
                        Date = DateTimeOffset.FromUnixTimeSeconds(timestamp[i].GetInt64()).Date,
                        Open = Convert.ToDecimal(opens[i].GetDouble()),
                        High = Convert.ToDecimal(highs[i].GetDouble()),
                        Low = Convert.ToDecimal(lows[i].GetDouble()),
                        Close = Convert.ToDecimal(closes[i].GetDouble()),
                        Volume = volumes[i].GetInt64()
                    });
                }

                return stockData;
            }
            catch (RateLimitException)
            {
                // レート制限例外はそのまま上位に伝播させる
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get stock data for {Symbol}", symbol);
                throw;
            }
        });
    }

    private AsyncRetryPolicy<List<StockData>> CreateRetryPolicy()
    {
        return Policy<List<StockData>>
            .Handle<HttpRequestException>(ex => ex.StatusCode != HttpStatusCode.TooManyRequests)
            .Or<JsonException>()
            .WaitAndRetryAsync(
                3,
                attempt =>
                {
                    var baseDelay = TimeSpan.FromSeconds(1);
                    var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                    var jitter = Random.Shared.Next(0, 1000);
                    var actualDelay = delay + TimeSpan.FromMilliseconds(jitter);
                    _logger.LogInformation("Calculating delay for attempt {Attempt}: base={BaseMs}, actual={ActualMs}",
                        attempt, baseDelay.TotalMilliseconds, actualDelay.TotalMilliseconds);
                    return actualDelay;
                },
                (exception, timeSpan, retryCount, _) =>
                {
                    _logger.LogWarning("Error occurred. Retry attempt {RetryCount} of 3. Waiting {DelayMs}ms before next attempt",
                        retryCount, timeSpan.TotalMilliseconds);
                }
            );
    }

    private int CalculateDelay(int retryAttempt)
    {
        var baseDelay = _retryOptions.RetryDelay;

        // レート制限の場合は、より長い遅延を使用
        if (retryAttempt > 1)
        {
            baseDelay = _retryOptions.RateLimitDelay;
        }

        if (_retryOptions.ExponentialBackoff)
        {
            baseDelay = (int)(baseDelay * Math.Pow(2, retryAttempt - 1));
        }

        var random = new Random();
        var jitter = (int)(random.NextDouble() * _retryOptions.JitterFactor * baseDelay);
        return baseDelay + jitter;
    }

    private static long ToUnixTimestamp(DateTimeOffset date)
    {
        return date.ToUnixTimeSeconds();
    }
}
