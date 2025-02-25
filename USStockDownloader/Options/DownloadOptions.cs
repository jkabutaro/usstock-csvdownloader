using CommandLine;

namespace USStockDownloader.Options;

public class DownloadOptions
{
    [Option('f', "file", Required = false, HelpText = "Path to the file containing stock symbols")]
    public string? SymbolFile { get; set; }

    [Option('s', "sp500", Required = false, HelpText = "Download S&P 500 symbols (if this is set, --file is not required)")]
    public bool UseSP500 { get; set; }

    [Option("sp500f", Required = false, HelpText = "Force update of S&P 500 symbols list")]
    public bool ForceSP500Update { get; set; }

    [Option('p', "parallel", Default = 3, HelpText = "Maximum number of parallel downloads")]
    public int MaxConcurrentDownloads { get; set; }

    [Option('r', "retries", Default = 3, HelpText = "Maximum number of retries per symbol")]
    public int MaxRetries { get; set; }

    [Option('d', "delay", Default = 1000, HelpText = "Delay in milliseconds between retries")]
    public int RetryDelay { get; set; }

    [Option('e', "exponential", Default = true, HelpText = "Use exponential backoff for retries")]
    public bool ExponentialBackoff { get; set; }

    public DateTime StartDate => DateTime.Now.AddYears(-1);
    public DateTime EndDate => DateTime.Now;

    public static DownloadOptions Parse(string[] args)
    {
        var parser = new Parser();
        return parser.ParseArguments<DownloadOptions>(args).Value;
    }

    public void Validate()
    {
        if (!UseSP500 && string.IsNullOrEmpty(SymbolFile))
        {
            throw new ArgumentException("Either --sp500 or --file must be specified.");
        }

        if (StartDate > EndDate)
        {
            throw new ArgumentException("Start date must be before end date.");
        }

        if (EndDate > DateTime.Now)
        {
            throw new ArgumentException("End date cannot be in the future.");
        }

        if (MaxConcurrentDownloads <= 0)
        {
            throw new ArgumentException("Maximum concurrent downloads must be greater than 0.");
        }

        if (MaxConcurrentDownloads > 10)
        {
            throw new ArgumentException("Maximum number of parallel downloads is 10 to avoid rate limiting.");
        }

        if (MaxRetries < 0)
        {
            throw new ArgumentException("Maximum retries must be non-negative.");
        }

        if (RetryDelay < 100)
        {
            throw new ArgumentException("Retry delay must be at least 100 milliseconds.");
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
        Console.WriteLine("  -p, --parallel   Maximum number of parallel downloads (default: 3)");
        Console.WriteLine("  -r, --retries    Maximum number of retries per symbol (default: 3)");
        Console.WriteLine("  -d, --delay      Delay in milliseconds between retries (default: 1000)");
        Console.WriteLine("  -e, --exponential  Use exponential backoff for retries (default: true)");
        Console.WriteLine();
        Console.WriteLine("Note: Start date is set to 1 year ago and end date is set to today by default.");
    }
}
