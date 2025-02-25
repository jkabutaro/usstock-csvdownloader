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
using System.Collections.Concurrent;

namespace USStockDownloader.Services;

public class StockDownloadManager
{
    private readonly IStockDataService _stockDataService;
    private readonly ILogger<StockDownloadManager> _logger;
    private readonly string _outputPath;
    private readonly int _maxConcurrentDownloads;
    private readonly RetryOptions _retryOptions;
    private static readonly Random _jitter = new Random();

    public StockDownloadManager(
        IStockDataService stockDataService,
        ILogger<StockDownloadManager> logger)
    {
        _stockDataService = stockDataService;
        _logger = logger;
        _outputPath = Path.Combine(Environment.CurrentDirectory, "output");
        _maxConcurrentDownloads = 3;
        _retryOptions = new RetryOptions(5, 1000, true); // リトライ回数を5回に増やす

        Directory.CreateDirectory(_outputPath);
    }

    public async Task DownloadStockDataAsync(List<StockSymbol> symbols)
    {
        _logger.LogInformation("Starting download for {Count} symbols with {MaxConcurrent} parallel downloads", symbols.Count, _maxConcurrentDownloads);

        var semaphore = new SemaphoreSlim(_maxConcurrentDownloads);
        var tasks = new List<Task>();
        var successCount = 0;
        var failureCount = 0;
        var failedSymbols = new ConcurrentBag<(string Symbol, Exception Exception)>();

        foreach (var symbol in symbols)
        {
            await semaphore.WaitAsync();
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var retryPolicy = Policy
                        .Handle<Exception>()
                        .WaitAndRetryAsync(
                            _retryOptions.MaxRetries,
                            retryAttempt => 
                            {
                                var baseDelay = _retryOptions.ExponentialBackoff
                                    ? _retryOptions.RetryDelay * Math.Pow(2, retryAttempt - 1)
                                    : _retryOptions.RetryDelay;
                                return TimeSpan.FromMilliseconds(baseDelay) + 
                                       TimeSpan.FromMilliseconds(_jitter.Next(0, 1000)); // ジッターを追加
                            },
                            onRetry: (ex, timeSpan, retryCount, context) =>
                            {
                                _logger.LogWarning(
                                    ex,
                                    "Retry {RetryCount} for {Symbol} after {Delay}ms. Error: {Error}",
                                    retryCount,
                                    symbol.Symbol,
                                    timeSpan.TotalMilliseconds,
                                    ex.Message);
                            });

                    await retryPolicy.ExecuteAsync(async () =>
                    {
                        var data = await _stockDataService.GetStockDataAsync(symbol.Symbol);
                        
                        // データの検証
                        if (data == null || data.Count == 0)
                        {
                            throw new Exception($"No data received for {symbol.Symbol}");
                        }

                        var filePath = Path.Combine(_outputPath, $"{symbol.Symbol}.csv");
                        await SaveToCsvAsync(data, filePath);
                        Interlocked.Increment(ref successCount);
                        _logger.LogInformation("Successfully downloaded data for {Symbol}", symbol.Symbol);
                    });
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failureCount);
                    failedSymbols.Add((symbol.Symbol, ex));
                    _logger.LogError(ex, "Failed to download data for {Symbol} after all retries", symbol.Symbol);
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);

        // 失敗した銘柄の詳細なレポート
        if (failedSymbols.Count > 0)
        {
            _logger.LogError("Failed downloads details:");
            foreach (var (symbol, ex) in failedSymbols)
            {
                _logger.LogError("Symbol: {Symbol}, Error: {Error}", symbol, ex.Message);
            }
        }

        _logger.LogInformation(
            "Download completed. Success: {SuccessCount}, Failure: {FailureCount}. Success rate: {SuccessRate}%", 
            successCount, 
            failureCount,
            (double)successCount / (successCount + failureCount) * 100);
    }

    private async Task SaveToCsvAsync(List<StockData> data, string filePath)
    {
        try
        {
            using var writer = new StreamWriter(filePath);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            await csv.WriteRecordsAsync(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save data to CSV file: {FilePath}", filePath);
            throw;
        }
    }
}
