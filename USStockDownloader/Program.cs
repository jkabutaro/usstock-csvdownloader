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
                        Console.WriteLine("必要な .NET Runtime がインストールされていません。");
                        Console.WriteLine("手動でインストールしてください: https://dotnet.microsoft.com/download/dotnet/9.0");
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
                        Console.WriteLine("必要な .NET Runtime がインストールされていません。");
                        Console.WriteLine("Required .NET Runtime is not installed.");

                        if (await DotNetRuntimeChecker.InstallRuntimeAsync())
                        {
                            Console.WriteLine("アプリケーションを再起動します...");
                            Console.WriteLine("Restarting application...");
                            DotNetRuntimeChecker.RestartApplication(args);
                            return;
                        }
                        else
                        {
                            Console.WriteLine("必要な .NET Runtime のインストールに失敗しました。");
                            Console.WriteLine("手動でインストールしてください: https://dotnet.microsoft.com/download/dotnet/9.0");
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

                List<string> symbols;
                var symbolProvider = serviceProvider.GetRequiredService<SymbolListProvider>();

                try
                {
                    if (options.UseSP500)
                    {
                        Console.WriteLine("Fetching S&P 500 symbols...");
                        if (options.ForceSP500Update)
                        {
                            Console.WriteLine("Forcing update of S&P 500 symbols...");
                            await serviceProvider.GetRequiredService<SP500CacheService>().ForceUpdateAsync();
                        }
                        symbols = await symbolProvider.GetSymbolsAsync(true, false, false, null);
                        
                        // --listcsvオプションが指定されている場合は、銘柄リストをCSVファイルに出力
                        if (options.ExportListCsv != null)
                        {
                            Console.WriteLine($"Exporting symbol list to CSV in {options.ExportListCsv}...");
                            await serviceProvider.GetRequiredService<SymbolListExportService>().ExportSymbolListToCsv(options.ExportListCsv);
                            Console.WriteLine("Symbol list exported successfully.");
                            
                            // CSVエクスポートのみを行う場合は、ここで終了
                            if (!args.Contains("--download"))
                            {
                                return;
                            }
                        }
                    }
                    else if (options.UseNYD)
                    {
                        Console.WriteLine("Fetching NY Dow symbols...");
                        var nydService = serviceProvider.GetRequiredService<NYDCacheService>();
                        if (options.ForceNYDUpdate)
                        {
                            Console.WriteLine("Forcing update of NY Dow symbols...");
                            await nydService.ForceUpdateAsync();
                        }
                        symbols = await symbolProvider.GetSymbolsAsync(false, true, false, null);
                        
                        // --listcsvオプションが指定されている場合は、NYダウ構成銘柄リストをCSVファイルに出力
                        if (options.ExportListCsv != null)
                        {
                            Console.WriteLine($"Exporting NY Dow symbol list to CSV in {options.ExportListCsv}...");
                            await serviceProvider.GetRequiredService<SymbolListExportService>()
                                .ExportNYDListToCsvAsync(options.ExportListCsv, options.ForceNYDUpdate);
                            Console.WriteLine("NY Dow symbol list exported successfully.");
                            
                            // CSVエクスポートのみを行う場合は、ここで終了
                            if (!args.Contains("--download"))
                            {
                                return;
                            }
                        }
                    }
                    else if (options.UseBuffett)
                    {
                        Console.WriteLine("Fetching Buffett portfolio symbols...");
                        var buffettService = serviceProvider.GetRequiredService<BuffettCacheService>();
                        if (options.ForceBuffettUpdate)
                        {
                            Console.WriteLine("Forcing update of Buffett portfolio symbols...");
                            await buffettService.ForceUpdateAsync();
                        }
                        symbols = await symbolProvider.GetSymbolsAsync(false, false, true, null);
                        
                        // --listcsvオプションが指定されている場合は、バフェットポートフォリオ銘柄リストをCSVファイルに出力
                        if (options.ExportListCsv != null)
                        {
                            Console.WriteLine($"Exporting Buffett portfolio list to CSV in {options.ExportListCsv}...");
                            await serviceProvider.GetRequiredService<SymbolListExportService>()
                                .ExportBuffettListToCsvAsync(options.ExportListCsv, options.ForceBuffettUpdate);
                            Console.WriteLine("Buffett portfolio list exported successfully.");
                            
                            // CSVエクスポートのみを行う場合は、ここで終了
                            if (!args.Contains("--download"))
                            {
                                return;
                            }
                        }
                    }
                    else if (options.UseIndex)
                    {
                        Console.WriteLine("Using major indices...");
                        
                        // 指数シンボルを動的に取得
                        var indexService = serviceProvider.GetRequiredService<IndexListService>();
                        var indexSymbols = options.ForceIndexUpdate 
                            ? await indexService.ForceUpdateMajorIndicesAsync()
                            : await indexService.GetMajorIndicesAsync();
                        
                        symbols = indexSymbols.Select(i => i.Symbol).ToList();
                        Console.WriteLine($"Retrieved {symbols.Count} index symbols.");
                        
                        // --listcsvオプションが指定されている場合は、指数リストをCSVファイルに出力
                        if (options.ExportListCsv != null)
                        {
                            Console.WriteLine($"Exporting index list to CSV in {options.ExportListCsv}...");
                            await serviceProvider.GetRequiredService<SymbolListExportService>()
                                .ExportIndexListToCsvAsync(options.ExportListCsv, options.ForceIndexUpdate);
                            Console.WriteLine("Index list exported successfully.");
                            
                            // CSVエクスポートのみを行う場合は、ここで終了
                            if (!args.Contains("--download"))
                            {
                                return;
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(options.SymbolFile))
                    {
                        Console.WriteLine($"Symbol file: {options.SymbolFile}");
                        symbols = await symbolProvider.GetSymbolsAsync(false, false, false, options.SymbolFile);
                    }
                    else if (!string.IsNullOrEmpty(options.Symbols))
                    {
                        Console.WriteLine($"Using provided symbols: {options.Symbols}");
                        symbols = options.Symbols.Split(',').Select(s => s.Trim()).ToList();
                    }
                    else
                    {
                        Console.WriteLine("No symbols specified. Use --sp500, --nyd, --buffett, --index, --file, or --symbols.");
                        return;
                    }

                    Console.WriteLine($"Found {symbols.Count} symbols.");
                    Console.WriteLine("Starting download process...");

                    await downloadManager.DownloadStockDataAsync(
                        symbols, 
                        options.OutputDirectory, 
                        options.StartDate, 
                        options.EndDate);

                    Console.WriteLine("Download process completed.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
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
                
                logger.Error(ex, "An unhandled exception occurred");
                Console.WriteLine($"An error occurred: {ex.Message}");
                Console.WriteLine("詳細はerror.logを確認してください。");
                Environment.ExitCode = 1;
            }
        }

        private static IServiceProvider ConfigureServices()
        {
            Console.WriteLine("HTTP client configured.");
            
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
            
            Console.WriteLine("Services registered.");
            
            // サービスプロバイダーを構築
            var serviceProvider = services.BuildServiceProvider();
            Console.WriteLine("Service provider built.");
            
            return serviceProvider;
        }
    }
}
