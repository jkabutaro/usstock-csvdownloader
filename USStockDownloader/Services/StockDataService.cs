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
    private const string BASE_URL = "https://query1.finance.yahoo.com/v8/finance/chart/";
    private static readonly Regex _symbolPattern = new Regex(@"^[A-Z\-.]+$", RegexOptions.Compiled);

    public StockDataService(
        HttpClient httpClient,
        ILogger<StockDataService> logger,
        RetryOptions retryOptions)
    {
        _httpClient = httpClient;
        _logger = logger;
        _retryOptions = retryOptions;
    }

    public async Task<List<StockData>> GetStockDataAsync(string symbol)
    {
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
                var endDate = DateTimeOffset.UtcNow;
                var startDate = endDate.AddYears(-1);

                var url = $"{BASE_URL}{normalizedSymbol}?period1={ToUnixTimestamp(startDate)}&period2={ToUnixTimestamp(endDate)}&interval=1d";
                
                var response = await _httpClient.GetAsync(url);
                
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("Rate limit hit for symbol {Symbol}", symbol);
                    throw new RateLimitException($"Rate limit exceeded for {symbol}");
                }

                response.EnsureSuccessStatusCode();
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
            .Handle<Exception>()
            .WaitAndRetryAsync(
                _retryOptions.MaxRetries,
                retryAttempt =>
                {
                    var delay = CalculateDelay(retryAttempt, null);
                    return TimeSpan.FromMilliseconds(delay);
                },
                (exception, timeSpan, retryCount, _) =>
                {
                    _logger.LogWarning(
                        "Retry attempt {RetryAttempt} of {MaxRetries}. Waiting {Delay}ms before next attempt",
                        retryCount,
                        _retryOptions.MaxRetries,
                        timeSpan.TotalMilliseconds);
                });
    }

    private int CalculateDelay(int retryAttempt, Exception? exception)
    {
        var baseDelay = _retryOptions.RetryDelay;
        if (_retryOptions.ExponentialBackoff)
        {
            baseDelay = (int)(baseDelay * Math.Pow(2, retryAttempt - 1));
        }

        if (exception?.Message.Contains("429") == true)
        {
            baseDelay = _retryOptions.RateLimitDelay;
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
