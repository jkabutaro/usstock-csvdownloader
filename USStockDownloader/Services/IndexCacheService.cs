using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System.Text.Json;
using USStockDownloader.Models;
using USStockDownloader.Utils;

namespace USStockDownloader.Services;

/// <summary>
/// 主要な指数リストを取得し、キャッシュするサービス
/// </summary>
public class IndexCacheService
{
    private readonly ILogger<IndexCacheService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _cacheFilePath;
    private readonly TimeSpan _cacheExpiry;
    
    // Yahoo Financeの主要指数一覧ページURL
    private const string YahooFinanceIndicesUrl = "https://finance.yahoo.com/world-indices";
    private const string CacheFileName = "index_symbols.json";
    
    public IndexCacheService(ILogger<IndexCacheService> logger, HttpClient httpClient)
        : this(logger, httpClient, CacheManager.GetCacheFilePath(CacheFileName), TimeSpan.FromHours(24))
    {
    }
    
    public IndexCacheService(ILogger<IndexCacheService> logger, HttpClient httpClient, string cacheFilePath, TimeSpan cacheExpiry)
    {
        _logger = logger;
        _httpClient = httpClient;
        _cacheFilePath = cacheFilePath;
        _cacheExpiry = cacheExpiry;
    }
    
    /// <summary>
    /// 主要指数リストを取得します。キャッシュがある場合はキャッシュから、
    /// ない場合またはキャッシュが古い場合はYahoo Financeから取得します。
    /// </summary>
    /// <returns>指数リスト</returns>
    public async Task<List<StockSymbol>> GetIndicesAsync()
    {
        // キャッシュをチェック
        if (File.Exists(_cacheFilePath))
        {
            var fileInfo = new FileInfo(_cacheFilePath);
            if (DateTime.Now - fileInfo.LastWriteTime < _cacheExpiry)
            {
                _logger.LogInformation("Using cached index list from {CacheFile}", PathUtils.ToRelativePath(_cacheFilePath));
                var json = await File.ReadAllTextAsync(_cacheFilePath);
                var cachedIndices = JsonSerializer.Deserialize<List<StockSymbol>>(json);
                if (cachedIndices != null && cachedIndices.Count > 0)
                {
                    return cachedIndices;
                }
            }
        }
        
        // キャッシュがない、古い、または無効な場合は新しいデータを取得
        _logger.LogInformation("Fetching index list from Yahoo Finance...");
        return await FetchIndicesFromYahooFinanceAsync();
    }
    
    /// <summary>
    /// キャッシュを無視して強制的に最新の指数リストを取得します
    /// </summary>
    /// <returns>指数リスト</returns>
    public async Task<List<StockSymbol>> ForceUpdateAsync()
    {
        _logger.LogInformation("Forcing update of index list from Yahoo Finance...");
        return await FetchIndicesFromYahooFinanceAsync();
    }
    
    /// <summary>
    /// Yahoo Financeから指数リストを取得する
    /// </summary>
    /// <returns>指数リスト</returns>
    private async Task<List<StockSymbol>> FetchIndicesFromYahooFinanceAsync()
    {

        // 使えない指標があったり、扱いが難しいものがあるので、固定で返す
        var defaultIndices = GetDefaultIndices();
        await SaveIndicesCache(defaultIndices);
        return defaultIndices;




        //try
        //{
        //    // フォールバック用のデフォルト指数リスト
        //    var defaultIndices = GetDefaultIndices();
            
        //    _logger.LogInformation("Fetching indices from Yahoo Finance using Playwright...");
            
        //    try
        //    {
        //        // Playwrightを初期化
        //        using var playwright = await Playwright.CreateAsync();
        //        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions 
        //        { 
        //            Headless = true 
        //        });
                
        //        var page = await browser.NewPageAsync();
                
        //        // タイムアウト設定を調整
        //        page.SetDefaultTimeout(60000); // 60秒に延長
                
        //        // Yahoo Financeのインデックスページにアクセス
        //        _logger.LogInformation("Navigating to Yahoo Finance page: {Url}", YahooFinanceIndicesUrl);
        //        await page.GotoAsync(YahooFinanceIndicesUrl);
                
        //        // DOM Contentがロードされるまでのみ待機（NetworkIdleは時間がかかりすぎる）
        //        _logger.LogInformation("Waiting for page to load (DOM Content)...");
        //        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                
        //        // テーブル要素が表示されるまで待機
        //        _logger.LogInformation("Waiting for table element to be visible...");
        //        try
        //        {
        //            await page.WaitForSelectorAsync("table[data-testid='table-container']", new PageWaitForSelectorOptions
        //            {
        //                Timeout = 10000 // 10秒のタイムアウト
        //            });
        //            _logger.LogInformation("Table element found");
        //        }
        //        catch (Exception ex)
        //        {
        //            _logger.LogWarning("Table element not found within timeout: {Error}", ex.Message);
        //            // テーブルが見つからなくても処理を続行
        //        }
                
        //        _logger.LogInformation("Yahoo Finance page loaded successfully");
                
        //        // インデックスデータを含むテーブルのHTMLを取得
        //        var content = await page.ContentAsync();
                
        //        // HTMLをログに記録（デバッグ用）
        //        _logger.LogDebug("HTML Content (first 500 chars): {HtmlSample}",
        //            content.Length > 500 ? content.Substring(0, 500) : content);
                
        //        // HTML解析処理
        //        var doc = new HtmlDocument();
        //        doc.LoadHtml(content);
                
        //        // 指数テーブルを検索
        //        var indices = new List<StockSymbol>();
                
        //        // Yahoo Financeのページ構造に基づいてテーブルを検索
        //        var tableNodes = doc.DocumentNode.SelectNodes("//table[@data-testid='table-container']");
        //        if (tableNodes == null || tableNodes.Count == 0)
        //        {
        //            _logger.LogWarning("Could not find index table in Yahoo Finance page. Using default indices.");
        //            await SaveIndicesCache(defaultIndices);
        //            return defaultIndices;
        //        }
                
        //        foreach (var tableNode in tableNodes)
        //        {
        //            var rows = tableNode.SelectNodes(".//tr");
        //            if (rows == null) continue;
                    
        //            // 最初の行はヘッダーなのでスキップ
        //            for (int i = 1; i < rows.Count; i++)
        //            {
        //                var row = rows[i];
        //                var cells = row.SelectNodes(".//td");
        //                if (cells == null || cells.Count < 2) continue;
                        
        //                // シンボルと名前を取得
        //                var symbolNode = cells[0].SelectSingleNode(".//a");
        //                if (symbolNode == null) continue;
                        
        //                var symbol = symbolNode.InnerText.Trim();
        //                var nameNode = cells[1];
        //                var name = nameNode?.InnerText?.Trim() ?? "";

        //                if (!string.IsNullOrEmpty(symbol))
        //                {
        //                    // 時系列データが取得可能か確認
        //                    try
        //                    {
        //                        // 時系列あるかチェック 全部受信できるの確認したのでコメントアウト いれると時間かかるからね
        //                        //_logger.LogInformation("Checking historical data availability for symbol: {Symbol}", symbol);
        //                        //var historicalUrl = $"https://finance.yahoo.com/quote/{symbol}/history";
        //                        //var response = await page.GotoAsync(historicalUrl);

        //                        //if (response == null || response.Status == 404)
        //                        //{
        //                        //    _logger.LogWarning("Historical data not available for symbol: {Symbol}", symbol);
        //                        //    continue;
        //                        //}

        //                        //// ページ内に「No data found」という表示がないか確認
        //                        //var historyContent = await page.ContentAsync();
        //                        //if (historyContent.Contains("No data found") || historyContent.Contains("Data not available"))
        //                        //{
        //                        //    _logger.LogWarning("No historical data found for symbol: {Symbol}", symbol);
        //                        //    continue;
        //                        //}

        //                        _logger.LogInformation("Historical data is available for symbol: {Symbol}", symbol);
        //                        indices.Add(new StockSymbol
        //                        {
        //                            Symbol = symbol,
        //                            Name = name,
        //                            Type = "index"
        //                        });
        //                    }
        //                    catch (Exception ex)
        //                    {
        //                        _logger.LogWarning(ex, "Failed to check historical data for symbol: {Symbol}", symbol);
        //                        continue;
        //                    }
        //                }
        //            }
        //        }
                
        //        if (indices.Count > 0)
        //        {
        //            _logger.LogInformation("Successfully fetched {Count} indices from Yahoo Finance", indices.Count);
        //            await SaveIndicesCache(indices);
        //            return indices;
        //        }
        //        else
        //        {
        //            _logger.LogWarning("No indices found in Yahoo Finance page. Using default indices.");
        //            await SaveIndicesCache(defaultIndices);
        //            return defaultIndices;
        //        }
        //    }
        //    catch (TimeoutException ex)
        //    {
        //        _logger.LogError(ex, "Timeout error while using Playwright to fetch indices from Yahoo Finance. Using default indices. Details: {Message}", ex.Message);
        //        await SaveIndicesCache(defaultIndices);
        //        return defaultIndices;
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error using Playwright to fetch indices from Yahoo Finance. Using default indices. Details: {Message}", ex.Message);
        //        await SaveIndicesCache(defaultIndices);
        //        return defaultIndices;
        //    }
        //}
        //catch (Exception ex)
        //{
        //    _logger.LogError(ex, "Failed to fetch indices from Yahoo Finance. Using default indices.");
        //    var defaultIndices = GetDefaultIndices();
        //    await SaveIndicesCache(defaultIndices);
        //    return defaultIndices;
        //}
    }
    
    /// <summary>
    /// 指数リストをキャッシュに保存します
    /// </summary>
    /// <param name="indices">保存する指数リスト</param>
    private async Task SaveIndicesCache(List<StockSymbol> indices)
    {
        try
        {
            var json = JsonSerializer.Serialize(indices, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_cacheFilePath, json);
            _logger.LogInformation("Saved {Count} indices to cache file {CacheFile}", indices.Count, PathUtils.ToRelativePath(_cacheFilePath));
        }
        catch (Exception ex)
        {
            _logger.LogError("インデックスのキャッシュファイルへの保存に失敗しました: {CacheFile} - {ErrorMessage} (Failed to save indices to cache file)", PathUtils.ToRelativePath(_cacheFilePath), ex.Message);
        }
    }
    
    /// <summary>
    /// デフォルトの主要指数リストを取得します（フォールバック用）
    /// </summary>
    /// <returns>デフォルト指数リスト</returns>
    private List<StockSymbol> GetDefaultIndices()
    {
        return new List<StockSymbol>
        {
            new StockSymbol 
            { 
                Symbol = "^DJI", 
                Name = "NYダウ（Dow Jones Industrial Average DJIA）", 
                Market = "", 
                Type = "index" 
            },
            new StockSymbol 
            { 
                Symbol = "^GSPC", 
                Name = "S&P 500", 
                Market = "", 
                Type = "index" 
            },
            new StockSymbol 
            { 
                Symbol = "^IXIC", 
                Name = "NASDAQ総合指数", 
                Market = "", 
                Type = "index" 
            },
            new StockSymbol 
            { 
                Symbol = "^N225", 
                Name = "日経平均株価", 
                Market = "", 
                Type = "index" 
            },
            new StockSymbol 
            { 
                Symbol = "^HSI", 
                Name = "香港ハンセン指数", 
                Market = "", 
                Type = "index" 
            },
            new StockSymbol 
            { 
                Symbol = "^FTSE", 
                Name = "FTSE 100", 
                Market = "", 
                Type = "index" 
            },
            new StockSymbol 
            { 
                Symbol = "^GDAXI", 
                Name = "ドイツDAX", 
                Market = "", 
                Type = "index" 
            }
        };
    }
}
