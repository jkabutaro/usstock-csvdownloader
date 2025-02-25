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
    private readonly IndexSymbolService _indexSymbolService;
    private readonly ILogger<StockDownloadManager> _logger;
    private SemaphoreSlim _semaphore;

    public StockDownloadManager(
        IStockDataService stockDataService,
        IndexSymbolService indexSymbolService,
        ILogger<StockDownloadManager> logger)
    {
        _stockDataService = stockDataService;
        _indexSymbolService = indexSymbolService;
        _logger = logger;
        _semaphore = new SemaphoreSlim(3); // デフォルトの並列数
    }

    public async Task DownloadStockDataAsync(DownloadOptions options)
    {
        List<string> symbols;
        if (options.UseSP500)
        {
            symbols = await _indexSymbolService.GetSP500Symbols();
            _logger.LogInformation("Loaded {Count} S&P 500 symbols", symbols.Count);
        }
        else if (options.UseNYD)
        {
            symbols = await _indexSymbolService.GetNYDSymbols();
            _logger.LogInformation("Loaded {Count} NY Dow symbols", symbols.Count);
        }
        else if (!string.IsNullOrEmpty(options.SymbolFile))
        {
            symbols = (await File.ReadAllLinesAsync(options.SymbolFile)).ToList();
            _logger.LogInformation("Loaded {Count} symbols from file: {File}", symbols.Count, options.SymbolFile);
        }
        else
        {
            throw new ArgumentException("No symbol source specified. Use --sp500, --nyd, or --file");
        }

        _semaphore = new SemaphoreSlim(options.MaxConcurrentDownloads);
        var tasks = symbols.Select(symbol => DownloadSymbolDataAsync(symbol, options)).ToList();
        await Task.WhenAll(tasks);
    }

    private async Task DownloadSymbolDataAsync(string symbol, DownloadOptions options)
    {
        try
        {
            await _semaphore.WaitAsync();
            _logger.LogInformation("Starting download for {Symbol}", symbol);

            var retryPolicy = Policy<List<StockData>>
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    options.MaxRetries,
                    retryAttempt => 
                    {
                        var baseDelay = options.ExponentialBackoff
                            ? options.RetryDelay * Math.Pow(2, retryAttempt - 1)
                            : options.RetryDelay;
                        return TimeSpan.FromMilliseconds(baseDelay) + 
                               TimeSpan.FromMilliseconds(new Random().Next(0, 1000));
                    },
                    (exception, timeSpan, retryCount, _) =>
                    {
                        _logger.LogWarning(
                            "Retry attempt {RetryAttempt} of {MaxRetries} for {Symbol}. Waiting {Delay}ms before next attempt",
                            retryCount,
                            options.MaxRetries,
                            symbol,
                            timeSpan.TotalMilliseconds);
                    });

            var data = await retryPolicy.ExecuteAsync(async () =>
            {
                var result = await _stockDataService.GetStockDataAsync(symbol);
                if (result == null || !result.Any())
                {
                    throw new Exception($"No data available for {symbol}");
                }
                return result;
            });

            var filePath = Path.Combine("Data", $"{symbol}.csv");
            Directory.CreateDirectory("Data");

            await using var writer = new StreamWriter(filePath);
            await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            await csv.WriteRecordsAsync(data);

            _logger.LogInformation("Successfully downloaded data for {Symbol}", symbol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download data for {Symbol}", symbol);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
