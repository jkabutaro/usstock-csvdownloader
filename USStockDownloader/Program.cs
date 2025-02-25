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
        var downloadManager = services.GetRequiredService<StockDownloadManager>();
        var sp500Service = services.GetRequiredService<SP500CacheService>();
        var symbolProvider = services.GetRequiredService<SymbolListProvider>();

        try
        {
            List<string> symbols;
            if (options.UseSP500)
            {
                var sp500Symbols = await sp500Service.GetSP500Symbols(options.ForceSP500Update);
                symbols = sp500Symbols.Select(s => s.Symbol).ToList();
                logger.LogInformation("Using {Count} symbols from S&P 500", symbols.Count);
            }
            else
            {
                var symbolFile = options.SymbolFile ?? "sp500"; // デフォルトはS&P500
                symbols = await symbolProvider.GetSymbols(symbolFile);
                logger.LogInformation("Loaded {Count} symbols from file: {File}", symbols.Count, symbolFile);
            }

            await downloadManager.DownloadStockDataAsync(symbols, options);
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
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddHttpClient<IStockDataService, StockDataService>();
        services.AddHttpClient<IndexSymbolService>();
        services.AddTransient<SP500CacheService>();
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

    [Option('s', "sp500", Required = false, Default = false, HelpText = "Use S&P 500 symbols")]
    public bool UseSP500 { get; set; }

    [Option('u', "update-sp500", Required = false, Default = false, HelpText = "Force update of S&P 500 symbols")]
    public bool ForceSP500Update { get; set; }

    public RetryOptions ToRetryOptions() => new(MaxRetries, RetryDelay, ExponentialBackoff);
}
