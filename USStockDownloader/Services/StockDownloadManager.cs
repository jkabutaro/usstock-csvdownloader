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
using USStockDownloader.Exceptions;

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
        _semaphore = new SemaphoreSlim(1); // 並列数を1に制限
    }

    public async Task DownloadStockDataAsync(DownloadOptions options)
    {
        _logger.LogInformation("DEBUG: Starting DownloadStockDataAsync");
        List<string> symbols = new List<string>();
        if (options.UseSP500)
        {
            symbols.AddRange(await _sp500CacheService.GetSymbolsAsync());
        }
        else if (options.UseNYD)
        {
            symbols.AddRange(await _nydCacheService.GetSymbolsAsync());
        }
        else if (options.UseBuffett)
        {
            symbols.AddRange(await _buffettCacheService.GetSymbolsAsync());
        }
        else if (!string.IsNullOrEmpty(options.SymbolFile))
        {
            symbols.AddRange(await File.ReadAllLinesAsync(options.SymbolFile));
        }

        if (!symbols.Any())
        {
            _logger.LogError("No symbols to process");
            return;
        }

        _logger.LogInformation("Loaded {Count} symbols from file: {File}", symbols.Count, options.SymbolFile ?? "cache");

        // レート制限チェック用の最初のシンボル
        if (symbols.Count > 0)
        {
            _logger.LogInformation("Testing rate limit with symbol: {Symbol}", symbols[0]);
            try
            {
                await DownloadSymbolDataAsync(symbols[0], options);
            }
            catch (RateLimitException)
            {
                _logger.LogError("レート制限に達しています。しばらく待ってから再実行してください。");
                _logger.LogError("Rate limit reached. Please wait for a while before trying again.");
                _logger.LogInformation("DEBUG: About to exit program");
                return;
            }
        }

        // バッチサイズを10に設定
        const int batchSize = 10;
        for (int i = 1; i < symbols.Count; i += batchSize)
        {
            var batch = symbols.Skip(i).Take(batchSize).ToList();
            _logger.LogInformation("Processing batch {BatchNumber} ({Start}-{End} of {Total})",
                i / batchSize + 1, i + 1, Math.Min(i + batchSize, symbols.Count), symbols.Count);

            var tasks = batch.Select(symbol => ProcessSymbolAsync(symbol, options)).ToList();
            await Task.WhenAll(tasks);

            // バッチ間で待機（2-3秒）
            if (i + batchSize < symbols.Count)
            {
                var delay = Random.Shared.Next(2000, 3000);
                _logger.LogInformation("Waiting {Delay}ms before next batch", delay);
                await Task.Delay(delay);
            }
        }
    }

    private async Task ProcessSymbolAsync(string symbol, DownloadOptions options)
    {
        try
        {
            await DownloadSymbolDataAsync(symbol, options);
        }
        catch (RateLimitException)
        {
            _logger.LogWarning("Rate limit hit for {Symbol}, skipping", symbol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing {Symbol}", symbol);
        }
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

            try
            {
                var data = await _stockDataService.GetStockDataAsync(symbol);
                
                if (data == null || !data.Any())
                {
                    _logger.LogWarning("No data available for {Symbol}", symbol);
                    return;
                }
                
                var outputDirectory = string.IsNullOrEmpty(options.OutputDirectory) ? "Data" : options.OutputDirectory;
                var filePath = Path.Combine(outputDirectory, $"{symbol}.csv");
                Directory.CreateDirectory(outputDirectory);

                await using var writer = new StreamWriter(filePath);
                await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
                await csv.WriteRecordsAsync(data);

                // キャッシュを更新
                StockDataCache.UpdateCache(symbol, startDate, endDate);

                _logger.LogInformation("Successfully downloaded data for {Symbol}", symbol);
            }
            catch (RateLimitException ex)
            {
                _logger.LogError(ex, "Rate limit exceeded for {Symbol}", symbol);
                throw; // 上位のハンドラに伝播させる
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download data for {Symbol}", symbol);
            if (ex is RateLimitException)
            {
                throw; // レート制限例外は上位に伝播させる
            }
        }
    }
}
