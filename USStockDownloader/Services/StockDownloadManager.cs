using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using CsvHelper;
using System.Globalization;
using USStockDownloader.Models;
using USStockDownloader.Exceptions;
using System.IO;
using USStockDownloader.Utils;

namespace USStockDownloader.Services;

public class StockDownloadManager
{
    private readonly IStockDataService _stockDataService;
    private readonly ILogger<StockDownloadManager> _logger;
    private readonly SemaphoreSlim _semaphore;
    private const int MAX_CONCURRENT_DOWNLOADS = 3;
    private const int MAX_RETRY_ATTEMPTS = 3;
    private static readonly Random _random = new Random();

    public StockDownloadManager(IStockDataService stockDataService, ILogger<StockDownloadManager> logger)
    {
        _stockDataService = stockDataService;
        _logger = logger;
        _semaphore = new SemaphoreSlim(MAX_CONCURRENT_DOWNLOADS);
    }

    public async Task DownloadStockDataAsync(List<string> symbols)
    {
        _logger.LogInformation("Starting download for {Count} symbols with max concurrency {MaxConcurrent}", 
            symbols.Count, MAX_CONCURRENT_DOWNLOADS);

        var endDate = DateTime.Today;
        var startDate = endDate.AddYears(-1);
        var outputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "output"));
        Directory.CreateDirectory(outputDir);

        _logger.LogInformation("Date range: {StartDate} to {EndDate}", 
            startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
        _logger.LogInformation("Output directory: {OutputDir}", outputDir);

        var failedSymbols = new ConcurrentBag<(string Symbol, Exception Error)>();
        var tasks = new List<Task>();

        // 最初のダウンロード試行
        foreach (var symbol in symbols)
        {
            await _semaphore.WaitAsync();
            
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await ProcessSymbolAsync(symbol, startDate, endDate, outputDir);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process {Symbol}", symbol);
                    failedSymbols.Add((symbol, ex));
                }
                finally
                {
                    _semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);

        // 失敗した銘柄の再試行
        if (failedSymbols.Any())
        {
            _logger.LogWarning("Retrying failed symbols: {Count} symbols", failedSymbols.Count);
            var retriedSymbols = new ConcurrentBag<(string Symbol, Exception Error)>();
            tasks.Clear();

            foreach (var (symbol, _) in failedSymbols)
            {
                await _semaphore.WaitAsync();

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        // 再試行前に長めの待機を入れる
                        await Task.Delay(TimeSpan.FromSeconds(2 + _random.NextDouble() * 3));
                        await ProcessSymbolAsync(symbol, startDate, endDate, outputDir);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Retry failed for {Symbol}", symbol);
                        retriedSymbols.Add((symbol, ex));
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);
            failedSymbols = retriedSymbols;
        }

        if (failedSymbols.Any())
        {
            var failureReport = GenerateFailureReport(failedSymbols);
            var reportPath = Path.Combine(outputDir, "failed_symbols_report.txt");
            await File.WriteAllTextAsync(reportPath, failureReport);

            _logger.LogWarning("Failed to download data for {Count} symbols. See report at: {ReportPath}", 
                failedSymbols.Count, reportPath);
        }

        _logger.LogInformation("Download process completed. Successfully downloaded {SuccessCount} out of {TotalCount} symbols",
            symbols.Count - failedSymbols.Count, symbols.Count);
    }

    private async Task ProcessSymbolAsync(string symbol, DateTime startDate, DateTime endDate, string outputDir)
    {
        _logger.LogInformation("Processing symbol: {Symbol}", symbol);
        
        // キャッシュをチェック - 更新が必要かどうか確認
        var needsUpdate = StockDataCache.NeedsUpdate(symbol, startDate, endDate, TimeSpan.FromHours(1));
        
        if (!needsUpdate)
        {
            _logger.LogInformation("Using cached data for {Symbol}", symbol);
            return; // キャッシュが有効なら処理をスキップ
        }
        
        var stockDataList = await _stockDataService.GetStockDataAsync(symbol, startDate, endDate);

        if (stockDataList.Any())
        {
            var outputPath = Path.Combine(outputDir, $"{symbol}.csv");
            _logger.LogDebug("Writing data to {OutputPath}", outputPath);

            await using var writer = new StreamWriter(outputPath);
            await using var csv = new CsvWriter(writer, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
            {
                ShouldQuote = args => false
            });
            await csv.WriteRecordsAsync(stockDataList);

            _logger.LogInformation("Successfully saved data for {Symbol}", symbol);
            
            // キャッシュを更新
            StockDataCache.UpdateCache(symbol, startDate, endDate);
        }
        else
        {
            _logger.LogWarning("No data available for {Symbol}", symbol);
            throw new DataParsingException($"No data available for {symbol}");
        }
    }

    private string GenerateFailureReport(ConcurrentBag<(string Symbol, Exception Error)> failures)
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("Stock Download Failure Report");
        report.AppendLine($"Generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine("----------------------------------------");
        report.AppendLine();

        var groupedFailures = failures
            .GroupBy(f => f.Error.GetType().Name)
            .OrderByDescending(g => g.Count());

        foreach (var group in groupedFailures)
        {
            report.AppendLine($"Error Type: {group.Key}");
            report.AppendLine($"Count: {group.Count()}");
            report.AppendLine("Affected Symbols:");
            foreach (var failure in group)
            {
                report.AppendLine($"- {failure.Symbol}: {failure.Error.Message}");
            }
            report.AppendLine();
        }

        return report.ToString();
    }
}
