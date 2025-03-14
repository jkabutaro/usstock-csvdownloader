using System.CommandLine;

namespace USStockDownloader.Options;

public class DownloadOptions
{
    public string? SymbolFile { get; set; }
    public bool UseSP500 { get; set; }
    public bool ForceSP500Update { get; set; }
    public bool UseNYD { get; set; }
    public bool ForceNYDUpdate { get; set; }
    public bool UseBuffett { get; set; }
    public string? Symbols { get; set; }
    public int MaxConcurrentDownloads { get; set; } = 3;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelay { get; set; } = 1000;
    public bool ExponentialBackoff { get; set; } = true;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public DateTime GetStartDate() => StartDate ?? DateTime.Now.AddYears(-1);
    public DateTime GetEndDate() => EndDate ?? DateTime.Now;

    public static DownloadOptions Parse(string[] args)
    {
        var options = new DownloadOptions();

        var rootCommand = new RootCommand("US Stock Price Downloader");

        var fileOption = new Option<string?>(
            new[] { "-f", "--file" },
            "Path to the symbol file");

        var sp500Option = new Option<bool>(
            new[] { "-s", "--sp500" },
            "Use S&P 500 symbols");

        var sp500ForceOption = new Option<bool>(
            "--sp500f",
            "Force update of S&P 500 symbols");

        var nydOption = new Option<bool>(
            new[] { "-n", "--nyd" },
            "Use NY Dow symbols");

        var nydForceOption = new Option<bool>(
            "--nydf",
            "Force update of NY Dow symbols");

        var buffettOption = new Option<bool>(
            new[] { "-b", "--buffett" },
            "Use Buffett's portfolio symbols");

        var symbolsOption = new Option<string?>(
            new[] { "--symbols" },
            "Comma-separated list of stock symbols");

        var parallelOption = new Option<int>(
            new[] { "-p", "--parallel" },
            () => 3,
            "Maximum number of concurrent downloads");

        var retriesOption = new Option<int>(
            new[] { "-r", "--retries" },
            () => 3,
            "Maximum number of retries");

        var delayOption = new Option<int>(
            new[] { "-d", "--delay" },
            () => 1000,
            "Retry delay in milliseconds");

        var exponentialOption = new Option<bool>(
            new[] { "-e", "--exponential" },
            () => true,
            "Use exponential backoff for retries");

        var startDateOption = new Option<DateTime?>(
            "--start-date",
            "Start date for historical data (format: yyyy-MM-dd)");

        var endDateOption = new Option<DateTime?>(
            "--end-date",
            "End date for historical data (format: yyyy-MM-dd)");

        rootCommand.AddOption(fileOption);
        rootCommand.AddOption(sp500Option);
        rootCommand.AddOption(sp500ForceOption);
        rootCommand.AddOption(nydOption);
        rootCommand.AddOption(nydForceOption);
        rootCommand.AddOption(buffettOption);
        rootCommand.AddOption(symbolsOption);
        rootCommand.AddOption(parallelOption);
        rootCommand.AddOption(retriesOption);
        rootCommand.AddOption(delayOption);
        rootCommand.AddOption(exponentialOption);
        rootCommand.AddOption(startDateOption);
        rootCommand.AddOption(endDateOption);

        rootCommand.SetHandler(
            (context) =>
            {
                options.SymbolFile = context.ParseResult.GetValueForOption(fileOption);
                options.UseSP500 = context.ParseResult.GetValueForOption(sp500Option);
                options.ForceSP500Update = context.ParseResult.GetValueForOption(sp500ForceOption);
                options.UseNYD = context.ParseResult.GetValueForOption(nydOption);
                options.ForceNYDUpdate = context.ParseResult.GetValueForOption(nydForceOption);
                options.UseBuffett = context.ParseResult.GetValueForOption(buffettOption);
                options.Symbols = context.ParseResult.GetValueForOption(symbolsOption);
                options.MaxConcurrentDownloads = context.ParseResult.GetValueForOption(parallelOption);
                options.MaxRetries = context.ParseResult.GetValueForOption(retriesOption);
                options.RetryDelay = context.ParseResult.GetValueForOption(delayOption);
                options.ExponentialBackoff = context.ParseResult.GetValueForOption(exponentialOption);
                options.StartDate = context.ParseResult.GetValueForOption(startDateOption);
                options.EndDate = context.ParseResult.GetValueForOption(endDateOption);
            });

        rootCommand.Invoke(args);
        return options;
    }

    public void Validate()
    {
        if (string.IsNullOrEmpty(SymbolFile) && !UseSP500 && !UseNYD && !UseBuffett && string.IsNullOrEmpty(Symbols))
        {
            throw new ArgumentException("Either --file, --sp500, --nyd, --buffett, or --symbols option must be specified");
        }

        var startDate = GetStartDate();
        var endDate = GetEndDate();

        if (startDate > endDate)
        {
            throw new ArgumentException("Start date must be before end date.");
        }

        if (endDate > DateTime.Now)
        {
            throw new ArgumentException("End date cannot be in the future.");
        }

        if (startDate < new DateTime(1970, 1, 1))
        {
            throw new ArgumentException("Start date cannot be before 1970-01-01.");
        }

        if (MaxConcurrentDownloads < 1 || MaxConcurrentDownloads > 10)
        {
            throw new ArgumentException("Parallel downloads must be between 1 and 10");
        }

        if (MaxRetries < 0)
        {
            throw new ArgumentException("Retries must be 0 or greater");
        }

        if (RetryDelay < 100)
        {
            throw new ArgumentException("Retry delay must be at least 100ms");
        }
    }

    public void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  USStockDownloader.exe [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -f, --file       Path to the file containing stock symbols");
        Console.WriteLine("  -s, --sp500      Download S&P 500 symbols (if this is set, --file is not required)");
        Console.WriteLine("  --sp500f         Force update of S&P 500 symbols list");
        Console.WriteLine("  -n, --nyd        Download NY Dow symbols (if this is set, --file is not required)");
        Console.WriteLine("  --nydf           Force update of NY Dow symbols list");
        Console.WriteLine("  -b, --buffett    Download Buffett's portfolio symbols (if this is set, --file is not required)");
        Console.WriteLine("  --symbols        Comma-separated list of stock symbols");
        Console.WriteLine("  -p, --parallel   Maximum number of parallel downloads (default: 3)");
        Console.WriteLine("  -r, --retries    Maximum number of retries per symbol (default: 3)");
        Console.WriteLine("  -d, --delay      Delay in milliseconds between retries (default: 1000)");
        Console.WriteLine("  -e, --exponential  Use exponential backoff for retries (default: true)");
        Console.WriteLine("  --start-date     Start date for historical data (format: yyyy-MM-dd)");
        Console.WriteLine("  --end-date       End date for historical data (format: yyyy-MM-dd)");
        Console.WriteLine();
        Console.WriteLine("Note: If not specified, start date is set to 1 year ago and end date is set to today.");
    }
}
