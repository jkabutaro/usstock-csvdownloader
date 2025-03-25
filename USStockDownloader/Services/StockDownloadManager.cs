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
    private readonly ConcurrentDictionary<string, string> _failedSymbols = new ConcurrentDictionary<string, string>();

    public StockDownloadManager(IStockDataService stockDataService, ILogger<StockDownloadManager> logger)
    {
        _stockDataService = stockDataService;
        _logger = logger;
        _semaphore = new SemaphoreSlim(MAX_CONCURRENT_DOWNLOADS);
    }

    public async Task DownloadStockDataAsync(List<string> symbols, string? outputDirectory = null, DateTime? startDate = null, DateTime? endDate = null, bool quickMode = false)
    {
        _logger.LogInformation("{Count}件の銘柄のダウンロードを開始します (Starting download for {Count} symbols)", symbols.Count, symbols.Count);
        
        _logger.LogInformation("【日付追跡】StockDownloadManager - 開始時 - start: {StartDate}, Year: {Year}, end: {EndDate}, Year: {Year} (Date tracking at start)",
            startDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "", startDate?.Year ?? 0, endDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "", endDate?.Year ?? 0);

        // 出力ディレクトリの設定
        var outputDir = outputDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), "StockData");
        Directory.CreateDirectory(outputDir);

        _logger.LogInformation("出力ディレクトリ: {OutputDir} (Output directory)", outputDir);

        // 日付範囲の設定
        var start = startDate ?? DateTime.Now.AddYears(-1);
        var end = endDate ?? DateTime.Now;

        // 終了日を最新の取引日に自動調整
        DateTime originalEndDate = end;
        DateTime adjustedEndDate = StockDataCache.AdjustToLatestTradingDay(end);
        
        _logger.LogInformation("【日付追跡】StockDownloadManager - 終了日調整前 - originalEndDate: {OrigEndDate}, Year: {Year}, adjustedEndDate: {AdjEndDate}, Year: {Year} (Date tracking before end date adjustment)",
            originalEndDate.ToString("yyyy-MM-dd HH:mm:ss"), originalEndDate.Year, adjustedEndDate.ToString("yyyy-MM-dd HH:mm:ss"), adjustedEndDate.Year);
        
        // 調整が行われた場合はユーザーに通知
        if (adjustedEndDate != originalEndDate)
        {
            _logger.LogWarning(
                $"指定された終了日（{originalEndDate:yyyy-MM-dd}）はまだデータが存在しないため、最新の取引日（{adjustedEndDate:yyyy-MM-dd}）に調整しました。 " +
                $"(Adjusted end date from {originalEndDate:yyyy-MM-dd} to latest trading day {adjustedEndDate:yyyy-MM-dd})");
            
            // 調整された終了日を使用
            end = adjustedEndDate;
            
            _logger.LogInformation("【日付追跡】StockDownloadManager - 終了日調整後 - end: {EndDate}, Year: {Year} (Date tracking after end date adjustment)",
                end.ToString("yyyy-MM-dd HH:mm:ss"), end.Year);
        }

        _logger.LogInformation("日付範囲: {StartDate}から{EndDate}まで (Date range)", 
            start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"));

        // クイックモードの場合は、更新が必要な銘柄のみをフィルタリング
        if (quickMode)
        {
            var filteredSymbols = new List<string>();
            int skippedCount = 0;

            foreach (var symbol in symbols)
            {
                // ファイル名に使用できない文字を置換
                string safeSymbol = symbol.Replace(".", "_");
                var fileName = Path.Combine(outputDir, $"{safeSymbol}.csv");
                
                // ファイルが存在し、更新が不要な場合はスキップ
                if (File.Exists(fileName) && !StockDataCache.NeedsUpdate(symbol, start, end, TimeSpan.FromHours(4)))
                {
                    skippedCount++;
                    continue;
                }
                
                filteredSymbols.Add(symbol);
            }
            
            _logger.LogInformation("クイックモード: {SkippedCount}件の銘柄は最新のため処理をスキップします。{FilteredCount}件の銘柄を処理します。 (Quick mode: Skipping up-to-date symbols, processing others)",
                skippedCount, filteredSymbols.Count);
            
            // 更新が必要な銘柄がない場合は終了
            if (filteredSymbols.Count == 0)
            {
                _logger.LogInformation("全ての銘柄が最新です。処理を終了します。 (All symbols are up-to-date. Exiting.)");
                return;
            }
            
            symbols = filteredSymbols;
        }

        // 並列ダウンロードの設定
        var tasks = new List<Task>();
        var failedSymbols = new ConcurrentBag<string>();

        foreach (var symbol in symbols)
        {
            await _semaphore.WaitAsync();

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await DownloadSymbolWithRetryAsync(symbol, start, end, outputDir);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "リトライ後も銘柄{Symbol}のダウンロードに失敗しました (Failed to download after retries)", symbol);
                    failedSymbols.Add(symbol);
                    _failedSymbols.TryAdd(symbol, ex.Message);
                }
                finally
                {
                    _semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);

        // 失敗した銘柄の特別リトライ処理
        if (failedSymbols.Any())
        {
            _logger.LogWarning("{Count}件の銘柄のダウンロードに失敗しました。特別リトライを試みます... (symbols failed to download. Attempting special retry...)", failedSymbols.Count);
            
            // 特別リトライ処理のための待機
            await Task.Delay(TimeSpan.FromSeconds(5));
            
            var specialRetryTasks = new List<Task>();
            var remainingFailedSymbols = new ConcurrentBag<string>();
            
            foreach (var symbol in failedSymbols)
            {
                await _semaphore.WaitAsync();
                
                specialRetryTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        // 特別リトライ処理（より長い待機時間と追加のリトライ回数）
                        _logger.LogInformation("銘柄{Symbol}の特別リトライを実行します (Special retry for symbol)", symbol);
                        await SpecialRetryForSymbolAsync(symbol, start, end, outputDir);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "特別リトライでも銘柄{Symbol}のダウンロードに失敗しました (Symbol failed even with special retry)", symbol);
                        remainingFailedSymbols.Add(symbol);
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }));
            }
            
            await Task.WhenAll(specialRetryTasks);
            
            // 最終的な結果の報告
            if (remainingFailedSymbols.Any())
            {
                _logger.LogError("特別リトライ後も{Count}件の銘柄のダウンロードに失敗しました: {Symbols} (symbols still failed after special retry)", 
                    remainingFailedSymbols.Count, string.Join(", ", remainingFailedSymbols));
                
                // 失敗した銘柄のレポートを作成
                await CreateFailedSymbolsReportAsync(outputDir);
            }
            else
            {
                _logger.LogInformation("特別リトライ後、全ての銘柄のダウンロードに成功しました (All symbols successfully downloaded after special retry)");
            }
        }
        else
        {
            _logger.LogInformation("全ての銘柄のダウンロードに成功しました (All symbols successfully downloaded)");
        }
    }

    private async Task DownloadSymbolWithRetryAsync(string symbol, DateTime startDate, DateTime endDate, string outputDir)
    {
        int retryCount = 0;
        Exception? lastException = null;

        while (retryCount < MAX_RETRY_ATTEMPTS)
        {
            try
            {
                await ProcessSymbolAsync(symbol, startDate, endDate, outputDir);
                return; // 成功したら終了
            }
            catch (RateLimitException)
            {
                // レート制限の場合は長めに待機
                retryCount++;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount) + _random.Next(0, 1000) / 1000.0);
                _logger.LogWarning("銘柄{Symbol}のレート制限に達しました、リトライ {Retry}/{MaxRetry} を{Delay}秒後に実行します (Rate limit hit, retry after delay)", 
                    symbol, retryCount, MAX_RETRY_ATTEMPTS, delay.TotalSeconds);
                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                lastException = ex;
                retryCount++;
                
                if (retryCount < MAX_RETRY_ATTEMPTS)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount) + _random.Next(0, 1000) / 1000.0);
                    _logger.LogWarning(ex, "銘柄{Symbol}のダウンロード中にエラーが発生しました、リトライ {Retry}/{MaxRetry} を{Delay}秒後に実行します (Error downloading, retry after delay)", 
                        symbol, retryCount, MAX_RETRY_ATTEMPTS, delay.TotalSeconds);
                    await Task.Delay(delay);
                }
            }
        }

        // 全てのリトライが失敗
        if (lastException != null)
        {
            throw new Exception($"{MAX_RETRY_ATTEMPTS}回の試行後も銘柄{symbol}のダウンロードに失敗しました (Failed to download {symbol} after {MAX_RETRY_ATTEMPTS} attempts)", lastException);
        }
    }

    private async Task SpecialRetryForSymbolAsync(string symbol, DateTime startDate, DateTime endDate, string outputDir)
    {
        const int SPECIAL_MAX_RETRY = 5;
        int retryCount = 0;
        
        while (retryCount < SPECIAL_MAX_RETRY)
        {
            try
            {
                // 特別リトライでは長めの待機時間を設定
                await Task.Delay(TimeSpan.FromSeconds(3 + retryCount * 2));
                
                await ProcessSymbolAsync(symbol, startDate, endDate, outputDir);
                _logger.LogInformation("銘柄{Symbol}の特別リトライが成功しました (Special retry succeeded)", symbol);
                return; // 成功したら終了
            }
            catch (Exception ex)
            {
                retryCount++;
                
                if (retryCount < SPECIAL_MAX_RETRY)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount + 2) + _random.Next(0, 2000) / 1000.0);
                    _logger.LogWarning(ex, "銘柄{Symbol}の特別リトライ {Retry}/{MaxRetry} が失敗しました、{Delay}秒待機します (Special retry failed, waiting)", 
                        symbol, retryCount, SPECIAL_MAX_RETRY, delay.TotalSeconds);
                    await Task.Delay(delay);
                }
                else
                {
                    _logger.LogError(ex, "銘柄{Symbol}の全ての特別リトライが失敗しました (All special retries failed)", symbol);
                    throw;
                }
            }
        }
    }

    private async Task ProcessSymbolAsync(string symbol, DateTime startDate, DateTime endDate, string outputDir)
    {
        try
        {
            _logger.LogInformation("銘柄を処理中: {Symbol} (Processing symbol)", symbol);
            
            // ファイル名に使用できない文字を置換
            string safeSymbol = symbol.Replace(".", "_");
            
            // CSVファイル名を作成
            var fileName = Path.Combine(outputDir, $"{safeSymbol}.csv");
            
            _logger.LogDebug("元のシンボル: {Symbol}, 安全なファイル名: {SafeSymbol} (Original symbol, Safe filename)", symbol, safeSymbol);

            // キャッシュチェックの前にファイルの存在を確認
            bool fileExists = File.Exists(fileName);

            // キャッシュが有効でかつファイルが存在する場合のみキャッシュを使用
            if (!StockDataCache.NeedsUpdate(symbol, startDate, endDate, TimeSpan.FromHours(4)) && fileExists)
            {
                _logger.LogInformation("銘柄{Symbol}のキャッシュデータを使用します (Using cached data)", symbol);
                return;
            }

            // キャッシュが無効またはファイルが存在しない場合はダウンロード
            var stockDataList = await _stockDataService.GetStockDataAsync(symbol, startDate, endDate);

            if (stockDataList.Any())
            {
                _logger.LogDebug("データを{OutputPath}に書き込んでいます (Writing data to)", PathUtils.ToRelativePath(fileName));

                await using var writer = new StreamWriter(fileName);
                var config = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    ShouldQuote = args => false
                };
                await using var csv = new CsvWriter(writer, config);
                
                // 明示的にマッピングを登録
                csv.Context.RegisterClassMap<StockDataMap>();
                
                await csv.WriteRecordsAsync(stockDataList);

                _logger.LogInformation("銘柄{Symbol}のデータを正常に保存しました (Successfully saved data)", symbol);
                
                // キャッシュを更新（実際のデータリストを渡す）
                StockDataCache.UpdateCache(symbol, startDate, endDate, stockDataList);
            }
            else
            {
                _logger.LogWarning("銘柄{Symbol}のデータがありません (No data available)", symbol);
                throw new DataParsingException($"銘柄{symbol}のデータがありません (No data available for {symbol})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "銘柄{Symbol}の処理に失敗しました (Failed to process)", symbol);
            throw;
        }
    }

    private async Task CreateFailedSymbolsReportAsync(string outputDir)
    {
        try
        {
            var reportPath = Path.Combine(outputDir, "failed_symbols_report.csv");
            _logger.LogInformation("失敗した銘柄のレポートを{ReportPath}に作成しています (Creating failed symbols report)", PathUtils.ToRelativePath(reportPath));
            
            await using var writer = new StreamWriter(reportPath);
            await using var csv = new CsvWriter(writer, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture));
            
            // ヘッダーの書き込み
            csv.WriteField("Symbol");
            csv.WriteField("Error");
            csv.NextRecord();
            
            // 失敗した銘柄の詳細を書き込み
            foreach (var pair in _failedSymbols)
            {
                csv.WriteField(pair.Key);
                csv.WriteField(pair.Value);
                csv.NextRecord();
            }
            
            _logger.LogInformation("失敗した銘柄のレポートが正常に作成されました (Failed symbols report created successfully)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失敗した銘柄のレポート作成に失敗しました (Failed to create failed symbols report)");
        }
    }
}
