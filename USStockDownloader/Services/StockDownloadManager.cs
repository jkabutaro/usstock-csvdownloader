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
        _semaphore = new SemaphoreSlim(3); // デフォルトの並列数
    }

    public async Task DownloadStockDataAsync(DownloadOptions options)
    {
        _logger.LogInformation("DEBUG: Starting DownloadStockDataAsync");
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
        
        // 最初に1つの銘柄でテスト
        if (symbols.Count > 0)
        {
            _logger.LogInformation("DEBUG: Starting rate limit test");
            try
            {
                var testSymbol = symbols[0];
                _logger.LogInformation("Testing rate limit with symbol: {Symbol}", testSymbol);
                _logger.LogInformation("DEBUG: Before GetStockDataAsync call");
                await _stockDataService.GetStockDataAsync(testSymbol);
                _logger.LogInformation("DEBUG: After GetStockDataAsync call - No rate limit hit");
            }
            catch (RateLimitException)
            {
                _logger.LogInformation("DEBUG: Rate limit exception caught");
                _logger.LogError("レート制限に達しています。しばらく待ってから再実行してください。");
                _logger.LogError("Rate limit reached. Please wait for a while before trying again.");
                Console.WriteLine("\n===================================================");
                Console.WriteLine("⚠️ レート制限に達しています ⚠️");
                Console.WriteLine("Yahoo Finance APIのレート制限に達しています。");
                Console.WriteLine("少なくとも15分以上待ってから再実行してください。");
                Console.WriteLine("\n⚠️ Rate limit reached ⚠️");
                Console.WriteLine("Yahoo Finance API rate limit has been reached.");
                Console.WriteLine("Please wait at least 15 minutes before trying again.");
                Console.WriteLine("===================================================\n");
                _logger.LogInformation("DEBUG: About to exit program");
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                _logger.LogInformation("DEBUG: Other exception caught: {ExType}", ex.GetType().Name);
                throw;
            }
            _logger.LogInformation("DEBUG: After try-catch block");
        }

        try
        {
            _logger.LogInformation("DEBUG: Starting main download loop");
            var tasks = symbols.Select(symbol => DownloadSymbolDataAsync(symbol, options)).ToList();
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download stock data");
            throw;
        }
        _logger.LogInformation("DEBUG: End of DownloadStockDataAsync");
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
