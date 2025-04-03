using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Runtime.Versioning;
using USStockDownloader.Options;
using USStockDownloader.Services;
using USStockDownloader.Services.YahooFinance;
using USStockDownloader.Utils;
using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Threading.Tasks;
using USStockDownloader.Interfaces;
using Microsoft.Extensions.Configuration;
using Serilog.Settings.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;

namespace USStockDownloader
{
    [SupportedOSPlatform("windows")]
    internal class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                // コマンドライン引数の解析
                var options = DownloadOptions.Parse(args);
                
                // キャッシュクリアオプションが指定されている場合
                if (options.CacheClear)
                {
                    // キャッシュディレクトリが存在することを確認
                    CacheManager.EnsureCacheDirectoryExists();
                    
                    // すべてのキャッシュファイルを削除
                    int deletedCount = CacheManager.ClearAllCaches();
                    Console.WriteLine($"キャッシュをクリアしました: {deletedCount} ファイルを削除しました (Cache cleared: {deletedCount} files deleted)");
                }
                
                // キャッシュディレクトリを初期化
                // (Initialize cache directory)
                CacheManager.EnsureCacheDirectoryExists();
                
                // ログディレクトリを初期化
                // (Initialize log directory)
                try 
                {
                    var logDir = Path.Combine(AppContext.BaseDirectory, "USStockDownloader_logs");
                    if (!Directory.Exists(logDir))
                    {
                        Directory.CreateDirectory(logDir);
                        Console.WriteLine($"ログディレクトリを作成しました: {logDir} (Created log directory)");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ログディレクトリの作成に失敗しました: {ex.Message} (Failed to create log directory)");
                }
                
                // キャッシュをチェック
                var cache = RuntimeCheckCache.LoadCache();
                if (cache != null)
                {
                    if (!cache.WindowsVersionValid)
                    {
                        Console.WriteLine(WindowsVersionChecker.GetRequiredWindowsVersionMessage());
                        Environment.ExitCode = 1;
                        return;
                    }
                    if (!cache.DotNetRuntimeValid)
                    {
                        Console.WriteLine("必要な .NET Runtime がインストールされていません。 (Required .NET Runtime is not installed.)");
                        Console.WriteLine("手動でインストールしてください: https://dotnet.microsoft.com/download/dotnet/9.0 (Please install manually: https://dotnet.microsoft.com/download/dotnet/9.0)");
                        Environment.ExitCode = 1;
                        return;
                    }
                }
                else
                {
                    // Windows 10以降のバージョンチェック
                    var windowsVersionValid = WindowsVersionChecker.IsWindows10OrLater();
                    if (!windowsVersionValid)
                    {
                        Console.WriteLine(WindowsVersionChecker.GetRequiredWindowsVersionMessage());
                        Environment.ExitCode = 1;
                        return;
                    }

                    // .NET Runtimeのチェックとインストール
                    var dotNetRuntimeValid = DotNetRuntimeChecker.IsRequiredRuntimeInstalled();
                    if (!dotNetRuntimeValid)
                    {
                        Console.WriteLine("必要な .NET Runtime がインストールされていません。 (Required .NET Runtime is not installed.)");
                        Console.WriteLine("必要な .NET Runtime がインストールされていません。 (Required .NET Runtime is not installed.)");

                        if (await DotNetRuntimeChecker.InstallRuntimeAsync())
                        {
                            Console.WriteLine("アプリケーションを再起動します... (Restarting application...)");
                            Console.WriteLine("Restarting application...");
                            DotNetRuntimeChecker.RestartApplication(args);
                            return;
                        }
                        else
                        {
                            Console.WriteLine("必要な .NET Runtime のインストールに失敗しました。 (Failed to install required .NET Runtime.)");
                            Console.WriteLine("手動でインストールしてください: https://dotnet.microsoft.com/download/dotnet/9.0 (Please install manually: https://dotnet.microsoft.com/download/dotnet/9.0)");
                            Console.WriteLine("Failed to install required .NET Runtime.");
                            Console.WriteLine("Please install manually: https://dotnet.microsoft.com/download/dotnet/9.0");
                            Environment.ExitCode = 1;
                            return;
                        }
                    }

                    // チェック結果をキャッシュに保存
                    RuntimeCheckCache.SaveCache(new RuntimeCheckResult
                    {
                        CheckDate = DateTime.Now,
                        WindowsVersionValid = windowsVersionValid,
                        DotNetRuntimeValid = dotNetRuntimeValid
                    });
                }

                var serviceProvider = ConfigureServices(options);

                var logger = serviceProvider.GetService<ILogger<Program>>();
                var downloadManager = serviceProvider.GetRequiredService<StockDownloadManager>();
                
                // 市場休場日を初期化（処理を開始する前に）
                try
                {
                    Console.WriteLine("市場休場日データを初期化しています... (Initializing market holidays data...)");
                    var stockDataService = serviceProvider.GetRequiredService<IStockDataService>();
                    await stockDataService.InitializeMarketHolidaysAsync();
                    Console.WriteLine("市場休場日データの初期化が完了しました (Market holidays data initialization completed)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"市場休場日データの初期化に失敗しました: {ex.Message} (Failed to initialize market holidays data)");
                    Console.WriteLine("アプリケーションを終了します... (Exiting application...)");
                    Environment.ExitCode = 1;
                    return;
                }

                List<string> symbols = new List<string>();
                
                var symbolProvider = serviceProvider.GetRequiredService<SymbolListProvider>();

                try
                {
                    // CSVリスト出力
                    if (!string.IsNullOrEmpty(options.ExportListCsv))
                    {
                        var symbolListExportService = serviceProvider.GetRequiredService<SymbolListExportService>();
                        
                        if (options.UseNYD)
                        {
                            // NYダウ構成銘柄リストをCSVに出力
                            if (logger != null)
                            {
                                logger.LogInformation("NYダウ構成銘柄リストをCSVファイルに出力します... (Exporting NY Dow symbol list to CSV...)");
                                var outputPath = options.ExportListCsv;
                                await symbolListExportService.ExportNYDListToCsvAsync(outputPath, options.ForceNYDUpdate);
                                logger.LogInformation("NYダウ構成銘柄リストをCSVファイルに出力しました: {Path} (Exported NY Dow symbol list to CSV)", outputPath);
                            }
                            else
                            {
                                Console.WriteLine("NYダウ構成銘柄リストをCSVファイルに出力します... (Exporting NY Dow symbol list to CSV...)");
                                var outputPath = options.ExportListCsv;
                                await symbolListExportService.ExportNYDListToCsvAsync(outputPath, options.ForceNYDUpdate);
                                Console.WriteLine($"NYダウ構成銘柄リストをCSVファイルに出力しました: {outputPath} (Exported NY Dow symbol list to CSV)");
                            }
                            return;
                        }
                        else if (options.UseSP500)
                        {
                            // S&P 500銘柄リストをCSVに出力
                            Console.WriteLine("S&P 500銘柄リストをCSVファイルに出力しています... (Exporting S&P 500 symbol list to CSV...)");
                            if (options.ForceSP500Update)
                            {
                                Console.WriteLine("S&P 500銘柄リストを強制更新しています... (Forcing update of S&P 500 symbols...)");
                                await serviceProvider.GetRequiredService<SP500CacheService>().ForceUpdateAsync();
                            }
                            await symbolListExportService.ExportSymbolListToCsv(options.ExportListCsv);
                            Console.WriteLine($"S&P 500銘柄リストをCSVファイルに出力しました: {options.ExportListCsv} (Exported S&P 500 symbol list)");
                            return;
                        }
                        else if (options.UseBuffett)
                        {
                            // バフェットポートフォリオ銘柄リストをCSVに出力
                            Console.WriteLine("バフェットポートフォリオ銘柄リストをCSVファイルに出力しています... (Exporting Buffett portfolio symbol list to CSV...)");
                            await symbolListExportService.ExportBuffettListToCsvAsync(options.ExportListCsv, options.ForceBuffettUpdate);
                            Console.WriteLine($"バフェットポートフォリオ銘柄リストをCSVファイルに出力しました: {options.ExportListCsv} (Exported Buffett portfolio symbol list)");
                            return;
                        }
                        else if (options.UseIndex)
                        {
                            // 主要指数リストをCSVに出力
                            Console.WriteLine("主要指数リストをCSVファイルに出力しています... (Exporting major indices list to CSV...)");
                            await symbolListExportService.ExportIndexListToCsvAsync(options.ExportListCsv, options.ForceIndexUpdate);
                            Console.WriteLine($"主要指数リストをCSVファイルに出力しました: {options.ExportListCsv} (Exported major indices list)");
                            return;
                        }
                        else if (options.UseSBI)
                        {
                            // SBI証券取扱いの銘柄リストをCSVに出力
                            Console.WriteLine("SBI証券から米国株銘柄リストをCSVファイルに出力しています... (Exporting SBI Securities US stock list to CSV...)");
                            await symbolListExportService.ExportSBIListToCsvAsync(options.ExportListCsv);
                            Console.WriteLine($"SBI証券から米国株銘柄リストをCSVファイルに出力しました: {options.ExportListCsv} (Exported SBI Securities US stock list)");
                            return;
                        }
                        else
                        {
                            if (logger != null)
                            {
                                logger.LogError("CSVリスト出力には銘柄ソースの指定が必要です (--sp500, --nyd, --buffett, --index, --sbi) (CSV list export requires symbol source specification)");
                            }
                            else
                            {
                                Console.WriteLine("CSVリスト出力には銘柄ソースの指定が必要です (--sp500, --nyd, --buffett, --index, --sbi) (CSV list export requires symbol source specification)");
                            }
                            return;
                        }
                    }
                    
                    // 銘柄リストを取得
                    if (options.UseSP500)
                    {
                        Console.WriteLine("S&P 500銘柄リストを取得しています... (Fetching S&P 500 symbols...)");
                        if (options.ForceSP500Update)
                        {
                            Console.WriteLine("S&P 500銘柄リストを強制更新しています... (Forcing update of S&P 500 symbols...)");
                            await serviceProvider.GetRequiredService<SP500CacheService>().ForceUpdateAsync();
                        }
                        symbols = await symbolProvider.GetSymbolsAsync(true, false, false, null);
                    }
                    else if (options.UseNYD)
                    {
                        Console.WriteLine("NYダウ銘柄リストを取得しています... (Fetching NY Dow symbols...)");
                        var nydService = serviceProvider.GetRequiredService<NYDCacheService>();
                        if (options.ForceNYDUpdate)
                        {
                            Console.WriteLine("NYダウ銘柄リストを強制更新しています... (Forcing update of NY Dow symbols...)");
                            await nydService.ForceUpdateAsync();
                        }
                        symbols = await symbolProvider.GetSymbolsAsync(false, true, false, null);
                    }
                    else if (options.UseBuffett)
                    {
                        Console.WriteLine("バフェットポートフォリオ銘柄リストを取得しています... (Fetching Buffett portfolio symbols...)");
                        var buffettService = serviceProvider.GetRequiredService<BuffettCacheService>();
                        if (options.ForceBuffettUpdate)
                        {
                            Console.WriteLine("バフェットポートフォリオ銘柄リストを強制更新しています... (Forcing update of Buffett portfolio symbols...)");
                            await buffettService.ForceUpdateAsync();
                        }
                        symbols = await symbolProvider.GetSymbolsAsync(false, false, true, null);
                    }
                    else if (options.UseIndex)
                    {
                        Console.WriteLine("主要指数リストを使用しています... (Using major indices...)");
                        
                        // 指数シンボルを動的に取得
                        var indexService = serviceProvider.GetRequiredService<IndexListService>();
                        var indexSymbols = options.ForceIndexUpdate 
                            ? await indexService.ForceUpdateMajorIndicesAsync()
                            : await indexService.GetMajorIndicesAsync();
                        
                        symbols = indexSymbols.Select(i => i.Symbol).ToList();
                        Console.WriteLine($"主要指数シンボルを{symbols.Count}件取得しました。 (Retrieved {symbols.Count} index symbols.)");
                    }
                    else if (options.UseSBI)
                    {
                        Console.WriteLine("SBI証券から米国株銘柄リストを取得しています... (Fetching stock symbols from SBI Securities...)");
                        
                        // SBI証券から銘柄リストを取得
                        var sbiStockFetcher = serviceProvider.GetRequiredService<SBIStockFetcher>();
                        
                        if (options.ForceSBIUpdate)
                        {
                            Console.WriteLine("SBI証券の米国株銘柄リストを強制更新しています... (Forcing update of SBI Securities stock symbols...)");
                            var sbiSymbols = await sbiStockFetcher.ForceUpdateAsync();
                            symbols = sbiSymbols.Select(s => s.Symbol).ToList();
                        }
                        else
                        {
                            var sbiSymbols = await sbiStockFetcher.FetchStockSymbolsAsync();
                            symbols = sbiSymbols.Select(s => s.Symbol).ToList();
                        }

                        Console.WriteLine($"SBI証券から{symbols.Count}件の銘柄シンボルを取得しました。 (Retrieved {symbols.Count} symbols from SBI Securities.)");
                    }
                    else if (!string.IsNullOrEmpty(options.SymbolFile))
                    {
                        Console.WriteLine($"銘柄ファイルを使用: {options.SymbolFile} (Symbol file)");
                        symbols = await symbolProvider.GetSymbolsAsync(false, false, false, options.SymbolFile);
                    }
                    else if (!string.IsNullOrEmpty(options.Symbols))
                    {
                        Console.WriteLine($"指定された銘柄シンボルを使用: {options.Symbols} (Using provided symbols)");
                        symbols = options.Symbols.Split(',').Select(s => s.Trim()).ToList();
                    }
                    else
                    {
                        Console.WriteLine("銘柄が指定されていません。--sp500, --nyd, --buffett, --index, --file, または --symbolsオプションを使用してください。 (No symbols specified.)");
                        return;
                    }

                    Console.WriteLine($"{symbols.Count}件の銘柄シンボルが見つかりました。 (Found {symbols.Count} symbols.)");
                    Console.WriteLine("ダウンロードプロセスを開始しています... (Starting download process...)");

                    // 終了日の設定：オプションで指定されていない場合はYahoo Financeの最新取引日を使用
                    DateTime endDate;
                    if (options.EndDate.HasValue)
                    {
                        endDate = options.EndDate.Value;
                        Console.WriteLine($"指定された終了日を使用します: {endDate:yyyy-MM-dd} (Using specified end date)");
                    }
                    else
                    {
                        // Yahoo Financeから最新取引日を取得
                        var yahooFinanceLatestTradingDateService = serviceProvider.GetRequiredService<YahooFinanceLatestTradingDateService>();
                        var latestTradingDate = await yahooFinanceLatestTradingDateService.GetLatestTradingDateAsync();
                        
                        // 最新取引日を使用
                        endDate = latestTradingDate.Date;
                        Console.WriteLine($"Yahoo Financeから取得した最新取引日を使用します: {endDate:yyyy-MM-dd} (Using latest trading date from Yahoo Finance)");
                    }

                    // 日付範囲の設定：開始日が指定されていない場合は終了日の1年前を使用
                    DateTime startDate;
                    if (options.StartDate.HasValue)
                    {
                        startDate = options.StartDate.Value;
                        Console.WriteLine($"指定された開始日を使用します: {startDate:yyyy-MM-dd} (Using specified start date)");
                    }
                    else
                    {
                        startDate = endDate.AddYears(-1);
                        Console.WriteLine($"開始日が指定されていないため、終了日の1年前を使用します: {startDate:yyyy-MM-dd} (Using one year before end date as start date)");
                    }
                    
                    Console.WriteLine($"日付範囲: {startDate:yyyy-MM-dd}から{endDate:yyyy-MM-dd}まで (Date range)");
                    Console.WriteLine($"【日付追跡】Program.cs - ダウンロード前 - endDate: {endDate:yyyy-MM-dd HH:mm:ss}, Year: {endDate.Year} (Date tracking)");

                    // 日付情報のダンプ
                    Console.WriteLine("====== 日付情報のダンプ (Date information dump) ======");
                    Console.WriteLine($"現在のシステム日付: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Current system date)");
                    Console.WriteLine($"endDate: {endDate:yyyy-MM-dd HH:mm:ss}, Kind: {endDate.Kind}, Year: {endDate.Year} (End date details)");
                    Console.WriteLine($"startDate: {startDate:yyyy-MM-dd HH:mm:ss}, Kind: {startDate.Kind}, Year: {startDate.Year} (Start date details)");
                    Console.WriteLine("=================================================");

                    await downloadManager.DownloadStockDataAsync(
                        symbols, 
                        options.OutputDirectory, 
                        startDate, 
                        endDate,
                        options.QuickMode);

                    Console.WriteLine("ダウンロードプロセスが完了しました。 (Download process completed.)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"エラー: {ex.Message} (Error)");
                    Environment.ExitCode = 1;
                }
            }
            catch (Exception ex)
            {
                //var logger = new LoggerConfiguration()
                //    .MinimumLevel.Debug()
                //    .WriteTo.Console()
                //    .WriteTo.File("error.log", rollingInterval: RollingInterval.Day)
                //    .CreateLogger();
                
                //logger.Error(ex, "処理されていない例外が発生しました (An unhandled exception occurred)");
                Console.WriteLine($"エラーが発生しました: {ex.Message} (An error occurred)");
                Console.WriteLine("詳細はerror.logを確認してください。 (See error.log for details.)");
                Environment.ExitCode = 1;
            }
        }

        private static IServiceProvider ConfigureServices(DownloadOptions options)
        {
            Console.WriteLine("サービス設定を開始します... (Configuring services...)");
            
            // サービスコレクションを作成
            var services = new ServiceCollection();

            // 設定ファイルを読み込む
            var configPath = Path.Combine(AppContext.BaseDirectory, "USStockDownloader_settings.json");
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(configPath, optional: false, reloadOnChange: true)
                .Build();

            // ロガーを登録 - AppLoggerFactoryの設定を反映
            services.AddLogging(builder =>
            {
                // 共通LoggerFactoryを使用（AppLoggerFactoryが処理する）
                // これにより、デバッグログはファイル出力のみとなり、コンソールには出力されない
                var loggerFactory = AppLoggerFactory.GetLoggerFactory();
                
                // Serilogの設定（アプリログ用）
                // コンソールにはエラーレベル以上のみ、ファイルには情報レベル以上を出力
                var logConfig = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .WriteTo.Console(
                        restrictedToMinimumLevel: LogEventLevel.Error,
                        outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

                try
                {
                    // ログディレクトリを確実に作成
                    var logDir = Path.Combine(AppContext.BaseDirectory, "USStockDownloader_logs");
                    if (!Directory.Exists(logDir))
                    {
                        Directory.CreateDirectory(logDir);
                        Console.WriteLine($"ログディレクトリを作成しました: {logDir} (Created log directory)");
                    }
                    
                    string logFilePath = Path.Combine(logDir, "USStockDownloader_app_.log");
                    logConfig = logConfig.WriteTo.File(
                        logFilePath,
                        rollingInterval: RollingInterval.Day,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                        restrictedToMinimumLevel: LogEventLevel.Debug);
                    
                    // デバッグログ用のファイルも追加
                    string debugLogFilePath = Path.Combine(logDir, "USStockDownloader_debug_.log");
                    logConfig = logConfig.WriteTo.File(
                        debugLogFilePath,
                        rollingInterval: RollingInterval.Day,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                        restrictedToMinimumLevel: LogEventLevel.Debug);
                    
                    Console.WriteLine($"アプリログファイルパス: {logFilePath} (App log file path)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"アプリログファイル設定エラー: {ex.Message} (App log file configuration error)");
                    // コンソールログのみ使用
                }
                
                // 既存のプロバイダーをクリア
                builder.ClearProviders();
                
                // すべての設定が終わった後にロガーを作成
                var logger = logConfig.CreateLogger();
                
                // Serilogプロバイダーを追加
                builder.AddSerilog(logger, dispose: true);
                
                // 既に設定されたAppLoggerFactoryも使用する
                builder.AddProvider(new LoggerFactoryProvider(loggerFactory));
            });
            
            // 設定を登録
            services.AddSingleton<IConfiguration>(configuration);

            // HTTPクライアントを登録
            services.AddHttpClient();
            
            // サービスを登録
            services.AddSingleton<IStockDataService>(provider => 
            {
                var httpClient = provider.GetRequiredService<HttpClient>();
                var logger = provider.GetRequiredService<ILogger<StockDataService>>();
                var cacheDir = Path.Combine(AppContext.BaseDirectory, "Cache");
                var retryOptions = new RetryOptions 
                { 
                    MaxRetries = 5, 
                    Delay = TimeSpan.FromSeconds(2),
                    Timeout = TimeSpan.FromSeconds(30)
                };
                return new StockDataService(httpClient, logger, cacheDir, retryOptions);
            });
            
            // コマンドラインから解析したオプションを登録
            services.AddSingleton(options);
            
            services.AddSingleton<StockDownloadManager>(provider => {
                var stockDataService = provider.GetRequiredService<IStockDataService>();
                var logger = provider.GetRequiredService<ILogger<StockDownloadManager>>();
                var downloadOptions = provider.GetRequiredService<DownloadOptions>();
                return new StockDownloadManager(stockDataService, logger, downloadOptions);
            });
            services.AddSingleton<IndexSymbolService>();
            services.AddSingleton<SymbolListProvider>();
            services.AddSingleton<SP500CacheService>();
            services.AddSingleton<NYDCacheService>();
            services.AddSingleton<BuffettCacheService>();
            services.AddSingleton<IndexCacheService>();
            services.AddSingleton<IndexListService>();
            services.AddSingleton<SymbolListExportService>();
            services.AddSingleton<SBIStockFetcher>();
            
            // SQLiteキャッシュサービスを登録
            services.AddSingleton<StockDataCacheSqliteService>(provider => {
                var logger = provider.GetRequiredService<ILogger<StockDataCacheSqliteService>>();
                var cacheDir = Path.Combine(AppContext.BaseDirectory, "Cache");
                return new StockDataCacheSqliteService(cacheDir, logger);
            });
            
            // YahooFinanceLatestTradingDateServiceを登録（SQLiteサービスを注入）
            services.AddSingleton<YahooFinanceLatestTradingDateService>(provider => {
                var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient();
                var logger = provider.GetRequiredService<ILogger<YahooFinanceLatestTradingDateService>>();
                var sqliteService = provider.GetRequiredService<StockDataCacheSqliteService>();
                return new YahooFinanceLatestTradingDateService(httpClient, logger, sqliteService);
            });
            
            // 取引日キャッシュサービスの登録（SQLite版のみを使用）
            services.AddSingleton<ITradingDayCacheService, TradingDayCacheSqliteService>();
            Console.WriteLine("SQLite版の取引日キャッシュサービスを使用します。 (Using SQLite trading day cache service.)");
            
            Console.WriteLine("サービスを登録しました。 (Services registered.)");
            
            // サービスプロバイダーを構築
            var serviceProvider = services.BuildServiceProvider();
            Console.WriteLine("サービスプロバイダーを構築しました。 (Service provider built.)");
            
            return serviceProvider;
        }
    }
}
