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
using USStockDownloader.Utils;

namespace USStockDownloader.Services;

public class StockDownloadManager
{
    private readonly IStockDataService _stockDataService;
    private readonly IndexSymbolService _indexSymbolService;
    private readonly SP500CacheService _sp500CacheService;
    private readonly NYDCacheService _nydCacheService;
    private readonly BuffettCacheService _buffettCacheService;
    private readonly ILogger<StockDownloadManager> _logger;
    private SemaphoreSlim _semaphore;
    private static readonly TimeSpan MaxCacheAge = TimeSpan.FromHours(24); // キャッシュの有効期限

    public StockDownloadManager(
        ILogger<StockDownloadManager> logger,
        IStockDataService stockDataService,
        IndexSymbolService indexSymbolService,
        SP500CacheService sp500CacheService,
        NYDCacheService nydCacheService,
        BuffettCacheService buffettCacheService)
    {
        _logger = logger;
        _stockDataService = stockDataService;
        _indexSymbolService = indexSymbolService;
        _sp500CacheService = sp500CacheService;
        _nydCacheService = nydCacheService;
        _buffettCacheService = buffettCacheService;
        _semaphore = new SemaphoreSlim(3); // デフォルトの並列数
    }

    public async Task DownloadStockDataAsync(DownloadOptions options)
    {
        List<string> symbols = new List<string>();
        if (options.UseSP500)
        {
            symbols = await _indexSymbolService.GetSP500Symbols();
            _logger.LogInformation("Loaded {Count} S&P 500 symbols", symbols.Count);
        }

        if (options.UseNYD)
        {
            var nydSymbols = await _nydCacheService.GetSymbolsAsync();
            symbols.AddRange(nydSymbols);
        }

        if (options.UseBuffett)
        {
            var buffettSymbols = await _buffettCacheService.GetSymbolsAsync();
            symbols.AddRange(buffettSymbols);
        }

        if (!string.IsNullOrEmpty(options.SymbolFile))
        {
            var fileSymbols = (await File.ReadAllLinesAsync(options.SymbolFile)).ToList();
            symbols.AddRange(fileSymbols);
            _logger.LogInformation("Loaded {Count} symbols from file: {File}", fileSymbols.Count, options.SymbolFile);
        }

        if (!string.IsNullOrEmpty(options.Symbols))
        {
            var individualSymbols = options.Symbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToUpper());
            symbols.AddRange(individualSymbols);
            _logger.LogInformation("Added {Count} individual symbols", individualSymbols.Count());
        }

        if (symbols.Count == 0)
        {
            throw new ArgumentException("No symbol source specified. Use --sp500, --nyd, --buffett, or --file");
        }

        _semaphore = new SemaphoreSlim(options.MaxConcurrentDownloads);
        var tasks = symbols.Select(symbol => DownloadSymbolDataAsync(symbol, options)).ToList();
        await Task.WhenAll(tasks);
    }

    private async Task DownloadSymbolDataAsync(string symbol, DownloadOptions options)
    {
        try
        {
            // キャッシュをチェック
            var startDate = options.StartDate ?? DateTime.Now.AddYears(-1);
            var endDate = options.EndDate ?? DateTime.Now;

            if (!StockDataCache.NeedsUpdate(symbol, startDate, endDate, MaxCacheAge))
            {
                _logger.LogInformation("Using cached data for {Symbol}", symbol);
                return;
            }

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

            // キャッシュを更新
            StockDataCache.UpdateCache(symbol, startDate, endDate);

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
