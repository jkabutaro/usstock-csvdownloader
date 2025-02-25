using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using USStockDownloader.Options;
using USStockDownloader.Services;
using System.IO;

class Program
{
    static async Task Main(string[] args)
    {
        var serviceProvider = ConfigureServices();

        try
        {
            await Parser.Default.ParseArguments<Options>(args)
                .WithParsedAsync(async options => await RunAsync(options, serviceProvider));
        }
        catch (Exception ex)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred while running the application");
            Environment.Exit(1);
        }
    }

    private static async Task RunAsync(Options options, IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        var symbolProvider = services.GetRequiredService<SymbolListProvider>();
        var downloadManager = services.GetRequiredService<StockDownloadManager>();

        try
        {
            var symbolFile = options.SymbolFile ?? "sp500"; // デフォルトはS&P500
            var symbols = await symbolProvider.GetSymbols(symbolFile);
            await downloadManager.DownloadStockDataAsync(symbols);
            logger.LogInformation("Download completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while downloading stock data");
            Environment.Exit(1);
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddHttpClient<IStockDataService, StockDataService>();
        services.AddHttpClient<IndexSymbolService>();
        services.AddSingleton<SymbolListProvider>();
        services.AddSingleton<StockDownloadManager>();

        return services.BuildServiceProvider();
    }
}

public class Options
{
    [Option('f', "file", Required = false, HelpText = "Path to the symbol file, or use 'sp500' for S&P 500 symbols, 'nyd' for NY Dow symbols")]
    public string? SymbolFile { get; set; }

    [Option('p', "parallel", Required = false, Default = 3, HelpText = "Maximum number of concurrent downloads")]
    public int MaxConcurrentDownloads { get; set; }

    [Option('r', "retries", Required = false, Default = 3, HelpText = "Maximum number of retries")]
    public int MaxRetries { get; set; }

    [Option('d', "delay", Required = false, Default = 1000, HelpText = "Retry delay in milliseconds")]
    public int RetryDelay { get; set; }

    [Option('e', "exponential", Required = false, Default = true, HelpText = "Use exponential backoff for retries")]
    public bool ExponentialBackoff { get; set; }

    public RetryOptions ToRetryOptions() => new(MaxRetries, RetryDelay, ExponentialBackoff);
}
