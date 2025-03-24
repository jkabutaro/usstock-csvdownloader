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
            "--indexf",
            "Force update of major indices list");

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

                // 引数の検証
                if (!options.UseSP500 && !options.UseNYD && !options.UseBuffett && 
                    string.IsNullOrEmpty(options.SymbolFile) && string.IsNullOrEmpty(options.Symbols) &&
                    !options.UseIndex)
                {
                    Console.WriteLine("エラー: シンボルソースが指定されていません。");
                    Console.WriteLine("以下のオプションのいずれかを指定してください: --sp500, --nyd, --buffett, --file, --symbols, --index");
                    Console.WriteLine();
                    ShowHelp();
                    Environment.Exit(1);
                }

                try
                {
                    options.Validate();
                }
                catch (ArgumentException ex)
                {
                    Console.WriteLine($"エラー: {ex.Message}");
                    Console.WriteLine();
                    ShowHelp();
                    Environment.Exit(1);
                }
            });

        try
        {
            int exitCode = rootCommand.Invoke(args);
            if (exitCode != 0)
            {
                Environment.Exit(exitCode);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"エラー: {ex.Message}");
            Console.WriteLine();
            ShowHelp();
            Environment.Exit(1);
        }

        return options;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("米国株価データダウンローダー (US Stock Price Downloader)");
        Console.WriteLine();
        Console.WriteLine("使用方法 (Usage):");
        Console.WriteLine("  USStockDownloader.exe [オプション]");
        Console.WriteLine();
        Console.WriteLine("オプション (Options):");
        Console.WriteLine("  -f, --file <path>         銘柄リストファイルのパス");
        Console.WriteLine("  --sp500                   S&P 500銘柄を使用");
        Console.WriteLine("  --sp500f                  S&P 500銘柄リストを強制的に更新");
        Console.WriteLine("  -n, --nyd                 NYダウ銘柄を使用");
        Console.WriteLine("  --nydf                    NYダウ銘柄リストを強制的に更新");
        Console.WriteLine("  -b, --buffett             バフェットポートフォリオ銘柄を使用");
        Console.WriteLine("  --buffett-f               バフェットポートフォリオ銘柄リストを強制的に更新");
        Console.WriteLine("  --index                   主要指数を使用");
        Console.WriteLine("  --indexf                  主要指数リストを強制的に更新");
        Console.WriteLine("  --symbols <symbols>       カンマ区切りの銘柄リスト (例: AAPL,MSFT,GOOGL)");
        Console.WriteLine("  -p, --parallel <num>      並列ダウンロード数 (デフォルト: 3)");
        Console.WriteLine("  -r, --retries <num>       リトライ回数 (デフォルト: 3)");
        Console.WriteLine("  -d, --delay <ms>          リトライ間隔 (ミリ秒) (デフォルト: 1000)");
        Console.WriteLine("  -e, --exponential         指数バックオフを使用 (デフォルト: true)");
        Console.WriteLine("  --start-date <date>       データ取得開始日 (yyyy-MM-dd形式)");
        Console.WriteLine("  --end-date <date>         データ取得終了日 (yyyy-MM-dd形式)");
        Console.WriteLine("  -o, --output <dir>        出力ディレクトリ");
        Console.WriteLine("  --listcsv <path>          銘柄リストをCSVファイルに出力 (相対パスを指定)");
        Console.WriteLine("  -h, --help                ヘルプを表示");
        Console.WriteLine();
        Console.WriteLine("例 (Examples):");
        Console.WriteLine("  USStockDownloader.exe --sp500");
        Console.WriteLine("  USStockDownloader.exe --nyd --output ./data");
        Console.WriteLine("  USStockDownloader.exe --symbols AAPL,MSFT,GOOGL --start-date 2020-01-01");
        Console.WriteLine("  USStockDownloader.exe --file symbols.txt --parallel 5");
        Console.WriteLine("  USStockDownloader.exe --index --listcsv");
        Console.WriteLine("  USStockDownloader.exe --index --indexf");
    }

    public void Validate()
    {
        if (MaxConcurrentDownloads <= 0)
        {
            throw new ArgumentException("並列ダウンロード数は1以上である必要があります。");
        }

        if (MaxRetries < 0)
        {
            throw new ArgumentException("リトライ回数は0以上である必要があります。");
        }

        if (RetryDelay < 0)
        {
            throw new ArgumentException("リトライ間隔は0以上である必要があります。");
        }

        if (StartDate.HasValue && EndDate.HasValue && StartDate.Value > EndDate.Value)
        {
            throw new ArgumentException("開始日は終了日より前である必要があります。");
        }

        if (!string.IsNullOrEmpty(ExportListCsv) && (System.IO.Path.IsPathRooted(ExportListCsv) || ExportListCsv.Contains(":")))
        {
            throw new ArgumentException("--listcsvオプションには相対パスを指定してください。絶対パスは使用できません。");
        }
    }
}
