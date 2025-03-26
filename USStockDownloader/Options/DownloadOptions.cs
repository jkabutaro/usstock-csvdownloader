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
    public bool QuickMode { get; set; } = true;
    public bool ForceUpdate { get; set; } = false;
    public bool CacheClear { get; set; } = false;

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

        var quickModeOption = new Option<bool>(
            "--quick-mode",
            () => true,
            "Use quick mode for downloading (skip existing files)");

        var forceUpdateOption = new Option<bool>(
            "--force-update",
            "Force update all data");

        var cacheClearOption = new Option<bool>(
            "--cache-clear",
            "Clear all cache files");

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
        rootCommand.AddOption(quickModeOption);
        rootCommand.AddOption(forceUpdateOption);
        rootCommand.AddOption(cacheClearOption);

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
                options.QuickMode = context.ParseResult.GetValueForOption(quickModeOption);
                options.ForceUpdate = context.ParseResult.GetValueForOption(forceUpdateOption);
                options.CacheClear = context.ParseResult.GetValueForOption(cacheClearOption);
                
                // ForceUpdateが指定されている場合は、QuickModeを無効にする
                if (options.ForceUpdate)
                {
                    options.QuickMode = false;
                }
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
        Console.WriteLine("米国株価データダウンローダー (US Stock Price Downloader)");
        Console.WriteLine("================================================");
        Console.WriteLine();
        Console.WriteLine("使用方法 (Usage):");
        Console.WriteLine("  USStockDownloader [options]");
        Console.WriteLine();
        Console.WriteLine("オプション (Options):");
        Console.WriteLine("  --index                 主要指数を取得 (Use major indices)");
        Console.WriteLine("  --sp500                 S&P 500の銘柄を取得 (Use S&P 500 symbols)");
        Console.WriteLine("  --nyd               NYダウの銘柄を取得 (Use NY Dow symbols)");
        Console.WriteLine("  --buffett           バフェットのポートフォリオ銘柄を取得 (Use Buffett's portfolio symbols)");
        Console.WriteLine("  --sbi                   SBI証券取扱いの米国株銘柄を取得 (Use SBI Securities US stock symbols)");
        Console.WriteLine("  --file <path>       ファイル指定で取得 銘柄シンボルファイルのパス (Path to the symbol file)");
        Console.WriteLine("  --symbols <symbols>     カンマ区切りに直接指定で取得　銘柄シンボルリスト (Comma-separated list of stock symbols)");
        Console.WriteLine("  --listcsv <path>        銘柄リストをCSVファイルにエクスポート (Export symbol list to CSV file)");
        Console.WriteLine("  --output <path>      ダウンロードしたデータの出力ディレクトリ (Output directory)");
        Console.WriteLine("  --start-date <date>     履歴データの開始日 yyyy-MM-dd形式 (Start date for historical data)");
        Console.WriteLine("  --end-date <date>       履歴データの終了日 yyyy-MM-dd形式 (End date for historical data)");
        Console.WriteLine("  --cache-clear           実行前にすべてのキャッシュファイルを削除（キャッシュ関連の問題のトラブルシューティングに使用）(Clear all cache files before running)");
        Console.WriteLine("  --parallel <num>    最大同時ダウンロード数 (Maximum number of concurrent downloads)");
        Console.WriteLine("  --retries <num>     最大リトライ回数 (Maximum number of retries)");
        Console.WriteLine("  --delay <ms>        リトライ間隔（ミリ秒） (Retry delay in milliseconds)");
        Console.WriteLine("  --exponential       リトライに指数バックオフを使用 (Use exponential backoff for retries)");
        Console.WriteLine();
        Console.WriteLine("例 (Examples):");
        Console.WriteLine("  USStockDownloader --index --output ./data");
        Console.WriteLine("  USStockDownloader --sp500 --output ./data");
        Console.WriteLine("  USStockDownloader --symbols AAPL,MSFT,GOOG --output ./data");
        Console.WriteLine("  USStockDownloader --sp500 --output ./data");
        Console.WriteLine("  USStockDownloader --cache-clear --sp500 --output ./data");
    }
}
