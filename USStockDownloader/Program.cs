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
                var sp500Service = serviceProvider.GetRequiredService<SP500CacheService>();

                // コマンドライン引数の解析
                var symbolFile = GetSymbolFileFromArgs(args);
                var useSP500 = args.Contains("--sp500");

                if (!useSP500 && string.IsNullOrEmpty(symbolFile))
                {
                    Console.WriteLine("Error: No symbol file specified.");
                    Console.WriteLine("Usage: dotnet run -- --file <symbol_file>");
                    Console.WriteLine("   or: dotnet run -- --sp500");
                    return;
                }

                List<string> symbols;
                if (useSP500)
                {
                    Console.WriteLine("Fetching S&P 500 symbols...");
                    symbols = (await sp500Service.GetSymbolsAsync()).ToList();
                    Console.WriteLine($"Found {symbols.Count} S&P 500 symbols.");
                }
                else
                {
                    Console.WriteLine($"Symbol file argument: {symbolFile}");
                    Console.WriteLine("Reading symbols from file...");
                    symbols = (await File.ReadAllLinesAsync(symbolFile)).ToList();
                    Console.WriteLine($"Found {symbols.Count} symbols in file.");
                }

                Console.WriteLine("Download manager initialized.");
                Console.WriteLine("Starting download process...");

                await downloadManager.DownloadStockDataAsync(symbols);

                Console.WriteLine("Download process completed.");
            }
            catch (Exception ex)
            {
                var logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.Console()
                    .CreateLogger();

                logger.Error(ex, "Fatal error occurred");
                Console.WriteLine($"Fatal error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private static ServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // ロガーの設定
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // HTTPクライアントの設定
            services.AddHttpClient();
            Console.WriteLine("HTTP client configured.");

            // サービスの登録
            services.AddTransient<IStockDataService, StockDataService>();
            services.AddTransient<StockDownloadManager>();
            services.AddSingleton<SP500CacheService>();
            Console.WriteLine("Services registered.");

            var serviceProvider = services.BuildServiceProvider();
            Console.WriteLine("Service provider built.");

            return serviceProvider;
        }

        private static string GetSymbolFileFromArgs(string[] args)
        {
            var fileIndex = Array.IndexOf(args, "--file");
            if (fileIndex >= 0 && fileIndex < args.Length - 1)
            {
                return args[fileIndex + 1];
            }
            return string.Empty;
        }
    }
}
