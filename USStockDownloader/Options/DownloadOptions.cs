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
    public bool ForceBuffettUpdate { get; set; }
    public string? Symbols { get; set; }
    public int MaxConcurrentDownloads { get; set; } = 3;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelay { get; set; } = 1000;
    public bool ExponentialBackoff { get; set; } = true;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? OutputDirectory { get; set; }
    public string? ExportListCsv { get; set; } = null;
    public bool UseIndex { get; set; } = false;
    public bool ForceIndexUpdate { get; set; } = false;
    public bool UseSBI { get; set; } = false;
    public bool ForceSBIUpdate { get; set; } = false;

    public DateTime GetStartDate() => StartDate ?? DateTime.Now.AddYears(-1);
    public DateTime GetEndDate() => EndDate ?? DateTime.Now;

    public static DownloadOptions Parse(string[] args)
    {
        // 引数がない場合や、ヘルプオプションがある場合は、ヘルプを表示して終了
        if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
        {
            ShowHelp();
            Environment.Exit(args.Length == 0 ? 1 : 0);
        }

        var options = new DownloadOptions();

        var rootCommand = new RootCommand("US Stock Price Downloader");

        var fileOption = new Option<string?>(
            new[] { "-f", "--file" },
            "Path to the symbol file");

        var sp500Option = new Option<bool>(
            new[] { "--sp500" },
            "Use S&P 500 symbols");

        var sp500ForceOption = new Option<bool>(
            "--sp500-f",
            "Force update of S&P 500 symbols");

        var nydOption = new Option<bool>(
            new[] { "-n", "--nyd" },
            "Use NY Dow symbols");

        var nydForceOption = new Option<bool>(
            "--nyd-f",
            "Force update of NY Dow symbols");

        var buffettOption = new Option<bool>(
            new[] { "-b", "--buffett" },
            "Use Buffett's portfolio symbols");

        var buffettForceOption = new Option<bool>(
            "--buffett-f",
            "Force update of Buffett's portfolio symbols");

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

        var outputDirOption = new Option<string?>(
            new[] { "-o", "--output" },
            "Output directory for the downloaded data");

        var listCsvOption = new Option<string?>(
            "--listcsv",
            "Export symbol list to CSV file (specify relative path)");

        var indexOption = new Option<bool>(
            new[] { "--index" },
            "Use major indices");
            
        var indexForceOption = new Option<bool>(
            "--index-f",
            "Force update of major indices list");

        var useSBIOption = new Option<bool>(
            "--sbi",
            "Use SBI Securities to fetch US stock symbols");

        var sbiForceOption = new Option<bool>(
            "--sbi-f",
            "Force update of SBI Securities US stock symbols");

        rootCommand.AddOption(fileOption);
        rootCommand.AddOption(sp500Option);
        rootCommand.AddOption(sp500ForceOption);
        rootCommand.AddOption(nydOption);
        rootCommand.AddOption(nydForceOption);
        rootCommand.AddOption(buffettOption);
        rootCommand.AddOption(buffettForceOption);
        rootCommand.AddOption(symbolsOption);
        rootCommand.AddOption(parallelOption);
        rootCommand.AddOption(retriesOption);
        rootCommand.AddOption(delayOption);
        rootCommand.AddOption(exponentialOption);
        rootCommand.AddOption(startDateOption);
        rootCommand.AddOption(endDateOption);
        rootCommand.AddOption(outputDirOption);
        rootCommand.AddOption(listCsvOption);
        rootCommand.AddOption(indexOption);
        rootCommand.AddOption(indexForceOption);
        rootCommand.AddOption(useSBIOption);
        rootCommand.AddOption(sbiForceOption);

        rootCommand.SetHandler(
            (context) =>
            {
                options.SymbolFile = context.ParseResult.GetValueForOption(fileOption);
                options.UseSP500 = context.ParseResult.GetValueForOption(sp500Option);
                options.ForceSP500Update = context.ParseResult.GetValueForOption(sp500ForceOption);
                options.UseNYD = context.ParseResult.GetValueForOption(nydOption);
                options.ForceNYDUpdate = context.ParseResult.GetValueForOption(nydForceOption);
                options.UseBuffett = context.ParseResult.GetValueForOption(buffettOption);
                options.ForceBuffettUpdate = context.ParseResult.GetValueForOption(buffettForceOption);
                options.Symbols = context.ParseResult.GetValueForOption(symbolsOption);
                options.MaxConcurrentDownloads = context.ParseResult.GetValueForOption(parallelOption);
                options.MaxRetries = context.ParseResult.GetValueForOption(retriesOption);
                options.RetryDelay = context.ParseResult.GetValueForOption(delayOption);
                options.ExponentialBackoff = context.ParseResult.GetValueForOption(exponentialOption);
                options.StartDate = context.ParseResult.GetValueForOption(startDateOption);
                options.EndDate = context.ParseResult.GetValueForOption(endDateOption);
                options.OutputDirectory = context.ParseResult.GetValueForOption(outputDirOption);
                options.ExportListCsv = context.ParseResult.GetValueForOption(listCsvOption);
                options.UseIndex = context.ParseResult.GetValueForOption(indexOption);
                options.ForceIndexUpdate = context.ParseResult.GetValueForOption(indexForceOption);
                options.UseSBI = context.ParseResult.GetValueForOption(useSBIOption);
                options.ForceSBIUpdate = context.ParseResult.GetValueForOption(sbiForceOption);
            });

        rootCommand.Invoke(args);

        // 相互に排他的なオプションのチェック
        int sourceCount = 0;
        if (!string.IsNullOrEmpty(options.SymbolFile)) sourceCount++;
        if (!string.IsNullOrEmpty(options.Symbols)) sourceCount++;
        if (options.UseSP500) sourceCount++;
        if (options.UseNYD) sourceCount++;
        if (options.UseBuffett) sourceCount++;
        if (options.UseIndex) sourceCount++;
        if (options.UseSBI) sourceCount++;

        // 複数のソースが指定されている場合はエラー
        if (sourceCount > 1)
        {
            Console.WriteLine("Error: Multiple symbol sources specified. Please use only one of: --file, --symbols, --sp500, --nyd, --buffett, --index, --sbi");
            Environment.Exit(1);
        }

        return options;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("US Stock Price Downloader");
        Console.WriteLine("========================");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  USStockDownloader [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -f, --file <path>        Path to the symbol file");
        Console.WriteLine("  --sp500                  Use S&P 500 symbols");
        Console.WriteLine("  --sp500-f                Force update of S&P 500 symbols");
        Console.WriteLine("  -n, --nyd                Use NY Dow symbols");
        Console.WriteLine("  --nyd-f                  Force update of NY Dow symbols");
        Console.WriteLine("  -b, --buffett            Use Buffett's portfolio symbols");
        Console.WriteLine("  --buffett-f              Force update of Buffett's portfolio symbols");
        Console.WriteLine("  --symbols <symbols>      Comma-separated list of stock symbols");
        Console.WriteLine("  -p, --parallel <count>   Maximum number of concurrent downloads (default: 3)");
        Console.WriteLine("  -r, --retries <count>    Maximum number of retries (default: 3)");
        Console.WriteLine("  -d, --delay <ms>         Retry delay in milliseconds (default: 1000)");
        Console.WriteLine("  -e, --exponential        Use exponential backoff for retries (default: true)");
        Console.WriteLine("  --start-date <date>      Start date for historical data (format: yyyy-MM-dd)");
        Console.WriteLine("  --end-date <date>        End date for historical data (format: yyyy-MM-dd)");
        Console.WriteLine("  -o, --output <dir>       Output directory for the downloaded data");
        Console.WriteLine("  --listcsv <path>         Export symbol list to CSV file");
        Console.WriteLine("  --index                  Use major indices");
        Console.WriteLine("  --index-f                Force update of major indices list");
        Console.WriteLine("  --sbi                    Use SBI Securities to fetch US stock symbols");
        Console.WriteLine("  --sbi-f                  Force update of SBI Securities US stock symbols");
        Console.WriteLine("  -h, --help               Show help information");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  USStockDownloader --sp500");
        Console.WriteLine("  USStockDownloader --nyd --start-date 2020-01-01 --end-date 2020-12-31");
        Console.WriteLine("  USStockDownloader --symbols AAPL,MSFT,GOOG");
        Console.WriteLine("  USStockDownloader --file symbols.txt");
        Console.WriteLine("  USStockDownloader --nyd --listcsv us_stock_list.csv");
        Console.WriteLine("  USStockDownloader --index");
        Console.WriteLine("  USStockDownloader --sbi");
    }
}
