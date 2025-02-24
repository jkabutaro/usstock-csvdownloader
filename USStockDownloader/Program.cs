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
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            await Parser.Default.ParseArguments<DownloadOptions>(args)
                .WithParsedAsync(async options => await RunAsync(options, serviceProvider, logger));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while running the application");
            Environment.Exit(1);
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddHttpClient<IStockDataService, StockDataService>();
    }

    private static async Task RunAsync(DownloadOptions options, ServiceProvider serviceProvider, ILogger<Program> logger)
    {
        try
        {
            var outputPath = Path.Combine(Environment.CurrentDirectory, "output");
            var downloadManager = ActivatorUtilities.CreateInstance<StockDownloadManager>(
                serviceProvider,
                outputPath,
                options.MaxConcurrentDownloads,
                new RetryOptions(options.MaxRetries, options.RetryDelay, options.ExponentialBackoff));

            var symbols = await File.ReadAllLinesAsync(options.SymbolFile);
            await downloadManager.DownloadStockDataAsync(symbols.Where(s => !string.IsNullOrWhiteSpace(s)).ToList());

            logger.LogInformation("Download completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download stock data");
            throw;
        }
    }
}
