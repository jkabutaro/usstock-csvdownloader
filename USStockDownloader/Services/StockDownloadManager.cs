using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using USStockDownloader.Models;
using USStockDownloader.Options;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using CsvHelper;
using System.Globalization;

namespace USStockDownloader.Services;

public class StockDownloadManager
{
    private readonly IStockDataService _stockDataService;
    private readonly ILogger<StockDownloadManager> _logger;
    private readonly string _outputPath;
    private readonly int _maxConcurrentDownloads;
    private readonly RetryOptions _retryOptions;

    public StockDownloadManager(
        IStockDataService stockDataService,
        ILogger<StockDownloadManager> logger,
        string outputPath,
        int maxConcurrentDownloads,
        RetryOptions retryOptions)
    {
        _stockDataService = stockDataService;
        _logger = logger;
        _outputPath = outputPath;
        _maxConcurrentDownloads = maxConcurrentDownloads;
        _retryOptions = retryOptions;

        Directory.CreateDirectory(_outputPath);
    }

    public async Task DownloadStockDataAsync(List<string> symbols)
    {
        // ヘッダー行を除外
        symbols = symbols.Skip(1).ToList();
        _logger.LogInformation("Starting download for {Count} symbols with {MaxConcurrent} parallel downloads", symbols.Count, _maxConcurrentDownloads);

        var semaphore = new SemaphoreSlim(_maxConcurrentDownloads);
        var tasks = new List<Task>();
        var successCount = 0;
        var failureCount = 0;

        foreach (var symbol in symbols)
        {
            await semaphore.WaitAsync();
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await DownloadSymbolDataAsync(symbol);
                    Interlocked.Increment(ref successCount);
                    _logger.LogInformation("Successfully downloaded data for {Symbol}", symbol);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to download data for symbol {Symbol}", symbol);
                    Interlocked.Increment(ref failureCount);
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);
        _logger.LogInformation("Download completed. Success: {SuccessCount}, Failures: {FailureCount}", successCount, failureCount);
    }

    private async Task DownloadSymbolDataAsync(string symbol)
    {
        var retryPolicy = Policy<List<StockData>>
            .Handle<Exception>()
            .WaitAndRetryAsync(
                _retryOptions.MaxRetries,
                retryAttempt => TimeSpan.FromMilliseconds(_retryOptions.RetryDelay * (_retryOptions.ExponentialBackoff ? Math.Pow(2, retryAttempt - 1) : 1)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        "Retry {RetryCount} of {MaxRetries} for symbol {Symbol}, waiting {DelayMs}ms",
                        retryCount,
                        _retryOptions.MaxRetries,
                        symbol,
                        timeSpan.TotalMilliseconds);
                });

        var endDate = DateTime.Now;
        var startDate = endDate.AddYears(-1);

        var result = await retryPolicy.ExecuteAndCaptureAsync(async () =>
            await _stockDataService.GetStockDataAsync(symbol, startDate, endDate));

        if (result.Outcome == OutcomeType.Failure)
        {
            _logger.LogError(result.FinalException, "Failed to download data for symbol {Symbol} after {MaxRetries} retries", symbol, _retryOptions.MaxRetries);
            throw result.FinalException;
        }

        await _stockDataService.SaveToCsvAsync(symbol, result.Result, _outputPath);
        _logger.LogInformation("Successfully saved data for symbol {Symbol}", symbol);
    }
}
