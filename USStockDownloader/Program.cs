using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Runtime.Versioning;
using USStockDownloader.Options;
using USStockDownloader.Services;
using USStockDownloader.Utils;

namespace USStockDownloader
{
    [SupportedOSPlatform("windows")]
    internal class Program
    {
        static async Task Main(string[] args)
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

            var services = new ServiceCollection();

            // ロギング設定
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // HTTPクライアント設定
            services.AddHttpClient();

            // サービス登録
            services.AddSingleton<IStockDataService, StockDataService>();
            services.AddSingleton<IndexSymbolService>();
            services.AddSingleton<SP500CacheService>();
            services.AddSingleton<NYDCacheService>();
            services.AddSingleton<BuffettCacheService>();
            services.AddSingleton<StockDownloadManager>();
            services.AddSingleton<DownloadOptions>();
            services.AddSingleton(new RetryOptions());  // デフォルト値で登録

            var serviceProvider = services.BuildServiceProvider();

            // コマンドライン引数の設定
            var sp500Option = new Option<bool>("--sp500", "Download S&P 500 stocks");
            var nydOption = new Option<bool>("--nyd", "Download NY Dow stocks");
            var buffettOption = new Option<bool>("--buffett", "Download Buffett's portfolio stocks");
            var fileOption = new Option<string?>("--file", "File containing stock symbols");
            var maxConcurrentOption = new Option<int>("--max-concurrent", () => 3, "Maximum number of concurrent downloads");
            var maxRetriesOption = new Option<int>("--max-retries", () => 3, "Maximum number of retry attempts");
            var retryDelayOption = new Option<int>("--retry-delay", () => 1000, "Base delay between retries in milliseconds");
            var exponentialBackoffOption = new Option<bool>("--exponential-backoff", () => true, "Use exponential backoff for retries");
            var rateLimitDelayOption = new Option<int>("--rate-limit-delay", () => 60000, "Delay when rate limit is hit in milliseconds");
            var symbolsOption = new Option<string?>("--symbols", "Comma-separated list of stock symbols");

            var rootCommand = new RootCommand("US Stock Price Downloader");
            rootCommand.AddOption(sp500Option);
            rootCommand.AddOption(nydOption);
            rootCommand.AddOption(buffettOption);
            rootCommand.AddOption(fileOption);
            rootCommand.AddOption(maxConcurrentOption);
            rootCommand.AddOption(maxRetriesOption);
            rootCommand.AddOption(retryDelayOption);
            rootCommand.AddOption(exponentialBackoffOption);
            rootCommand.AddOption(rateLimitDelayOption);
            rootCommand.AddOption(symbolsOption);

            rootCommand.SetHandler(async (InvocationContext context) =>
            {
                try
                {
                    var options = new DownloadOptions
                    {
                        UseSP500 = context.ParseResult.GetValueForOption(sp500Option),
                        UseNYD = context.ParseResult.GetValueForOption(nydOption),
                        UseBuffett = context.ParseResult.GetValueForOption(buffettOption),
                        SymbolFile = context.ParseResult.GetValueForOption(fileOption),
                        Symbols = context.ParseResult.GetValueForOption(symbolsOption),
                        MaxConcurrentDownloads = context.ParseResult.GetValueForOption(maxConcurrentOption),
                        MaxRetries = context.ParseResult.GetValueForOption(maxRetriesOption),
                        RetryDelay = context.ParseResult.GetValueForOption(retryDelayOption),
                        ExponentialBackoff = context.ParseResult.GetValueForOption(exponentialBackoffOption)
                    };

                    // RetryOptionsを更新
                    var retryOptions = serviceProvider.GetRequiredService<RetryOptions>();
                    retryOptions.MaxRetries = context.ParseResult.GetValueForOption(maxRetriesOption);
                    retryOptions.RetryDelay = context.ParseResult.GetValueForOption(retryDelayOption);
                    retryOptions.ExponentialBackoff = context.ParseResult.GetValueForOption(exponentialBackoffOption);
                    retryOptions.RateLimitDelay = context.ParseResult.GetValueForOption(rateLimitDelayOption);

                    var downloadManager = serviceProvider.GetRequiredService<StockDownloadManager>();
                    await downloadManager.DownloadStockDataAsync(options);
                }
                catch (Exception ex)
                {
                    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
                    logger.LogError(ex, "An error occurred while downloading stock data");
                    Environment.ExitCode = 1;
                }
            });

            await rootCommand.InvokeAsync(args);
        }
    }
}
