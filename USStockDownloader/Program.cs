using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using USStockDownloader.Options;
using USStockDownloader.Services;
using USStockDownloader.Utils;

class Program
{
    static async Task Main(string[] args)
    {
        // Windows 10以降のバージョンチェック
        if (!WindowsVersionChecker.IsWindows10OrLater())
        {
            Console.WriteLine(WindowsVersionChecker.GetRequiredWindowsVersionMessage());
            Environment.ExitCode = 1;
            return;
        }

        // .NET Runtimeのチェックとインストール
        if (!DotNetRuntimeChecker.IsRequiredRuntimeInstalled())
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
        services.AddSingleton<RetryOptions>();

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

        var rootCommand = new RootCommand("US Stock Price Downloader");
        rootCommand.AddOption(sp500Option);
        rootCommand.AddOption(nydOption);
        rootCommand.AddOption(buffettOption);
        rootCommand.AddOption(fileOption);
        rootCommand.AddOption(maxConcurrentOption);
        rootCommand.AddOption(maxRetriesOption);
        rootCommand.AddOption(retryDelayOption);
        rootCommand.AddOption(exponentialBackoffOption);

        rootCommand.SetHandler(async (bool sp500, bool nyd, bool buffett, string? file, int maxConcurrent, int maxRetries, int retryDelay, bool exponentialBackoff) =>
        {
            try
            {
                var options = new DownloadOptions
                {
                    UseSP500 = sp500,
                    UseNYD = nyd,
                    UseBuffett = buffett,
                    SymbolFile = file,
                    MaxConcurrentDownloads = maxConcurrent,
                    MaxRetries = maxRetries,
                    RetryDelay = retryDelay,
                    ExponentialBackoff = exponentialBackoff
                };

                var downloadManager = serviceProvider.GetRequiredService<StockDownloadManager>();
                await downloadManager.DownloadStockDataAsync(options);
            }
            catch (Exception ex)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "An error occurred while downloading stock data");
                Environment.ExitCode = 1;
            }
        }, sp500Option, nydOption, buffettOption, fileOption, maxConcurrentOption, maxRetriesOption, retryDelayOption, exponentialBackoffOption);

        await rootCommand.InvokeAsync(args);
    }
}
