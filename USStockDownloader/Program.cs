using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Runtime.Versioning;
using USStockDownloader.Options;
using USStockDownloader.Services;
using USStockDownloader.Utils;
using Serilog;
using Serilog.Events;
using System.IO;

namespace USStockDownloader
{
    [SupportedOSPlatform("windows")]
    internal class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
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

                var serviceProvider = ConfigureServices();

                var logger = serviceProvider.GetService<ILogger<Program>>();
                var downloadManager = serviceProvider.GetRequiredService<StockDownloadManager>();

                // コマンドライン引数の解析
                var options = DownloadOptions.Parse(args);

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

                    // 日付範囲の設定
                    DateTime startDate = options.StartDate ?? DateTime.Now.AddYears(-1);
                    DateTime endDate = options.EndDate ?? DateTime.Now;

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
                var logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.Console()
                    .WriteTo.File("error.log", rollingInterval: RollingInterval.Day)
                    .CreateLogger();
                
                logger.Error(ex, "処理されていない例外が発生しました (An unhandled exception occurred)");
                Console.WriteLine($"エラーが発生しました: {ex.Message} (An error occurred)");
                Console.WriteLine("詳細はerror.logを確認してください。 (See error.log for details.)");
                Environment.ExitCode = 1;
            }
        }

        private static IServiceProvider ConfigureServices()
        {
            Console.WriteLine("HTTPクライアントを設定しました。 (HTTP client configured.)");
            
            // サービスコレクションを作成
            var services = new ServiceCollection();
            
            // ロガーを登録
            services.AddLogging(builder =>
            {
                builder.AddSerilog(new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.Console()
                    .WriteTo.File("app.log", rollingInterval: RollingInterval.Day)
                    .CreateLogger());
            });
            
            // HTTPクライアントを登録
            services.AddHttpClient();
            
            // サービスを登録
            services.AddSingleton<IStockDataService, StockDataService>();
            services.AddSingleton<StockDownloadManager>();
            services.AddSingleton<IndexSymbolService>();
            services.AddSingleton<SymbolListProvider>();
            services.AddSingleton<SP500CacheService>();
            services.AddSingleton<NYDCacheService>();
            services.AddSingleton<BuffettCacheService>();
            services.AddSingleton<IndexCacheService>();
            services.AddSingleton<IndexListService>();
            services.AddSingleton<SymbolListExportService>();
            services.AddSingleton<SBIStockFetcher>();
            
            Console.WriteLine("サービスを登録しました。 (Services registered.)");
            
            // サービスプロバイダーを構築
            var serviceProvider = services.BuildServiceProvider();
            Console.WriteLine("サービスプロバイダーを構築しました。 (Service provider built.)");
            
            return serviceProvider;
        }
    }
}
