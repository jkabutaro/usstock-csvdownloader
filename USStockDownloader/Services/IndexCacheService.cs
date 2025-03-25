using System.Text.Json;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using USStockDownloader.Models;

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
    
    public IndexCacheService(ILogger<IndexCacheService> logger, HttpClient httpClient)
        : this(logger, httpClient, "index_symbols.json", TimeSpan.FromHours(24))
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
                _logger.LogInformation("Using cached index list from {CacheFile}", _cacheFilePath);
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
    /// Yahoo Financeから主要指数リストを取得します
    /// </summary>
    /// <returns>指数リスト</returns>
    private async Task<List<StockSymbol>> FetchIndicesFromYahooFinanceAsync()
    {
        try
        {
            // フォールバック用のデフォルト指数リスト
            var defaultIndices = GetDefaultIndices();
            
            // Yahoo Financeのページを取得
            var response = await _httpClient.GetAsync(YahooFinanceIndicesUrl);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch indices from Yahoo Finance. Status code: {StatusCode}. Using default indices.", response.StatusCode);
                await SaveIndicesCache(defaultIndices);
                return defaultIndices;
            }
            
            var html = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            
            // 指数テーブルを検索
            var indices = new List<StockSymbol>();
            
            // Yahoo Financeのページ構造に基づいてテーブルを検索
            // 主要指数テーブルのノードを特定
            var tableNodes = doc.DocumentNode.SelectNodes("//table[contains(@class, 'W(100%)')]");
            if (tableNodes == null || tableNodes.Count == 0)
            {
                _logger.LogWarning("Could not find index table in Yahoo Finance page. Using default indices.");
                await SaveIndicesCache(defaultIndices);
                return defaultIndices;
            }
            
            foreach (var tableNode in tableNodes)
            {
                var rows = tableNode.SelectNodes(".//tr");
                if (rows == null) continue;
                
                foreach (var row in rows.Skip(1)) // ヘッダー行をスキップ
                {
                    var cells = row.SelectNodes(".//td");
                    if (cells == null || cells.Count < 2) continue;
                    
                    // シンボルと名前を取得
                    var nameCell = cells[0];
                    var symbolLink = nameCell.SelectSingleNode(".//a");
                    
                    if (symbolLink != null)
                    {
                        var href = symbolLink.GetAttributeValue("href", "");
                        var name = symbolLink.InnerText.Trim();
                        
                        // URLからシンボルを抽出 (例: /quote/%5EGSPC -> ^GSPC)
                        var symbol = "";
                        var parts = href.Split('/');
                        if (parts.Length > 0)
                        {
                            var lastPart = parts[parts.Length - 1];
                            symbol = Uri.UnescapeDataString(lastPart).Replace("%5E", "^");
                        }
                        
                        if (!string.IsNullOrEmpty(symbol) && !string.IsNullOrEmpty(name))
                        {
                            indices.Add(new StockSymbol
                            {
                                Symbol = symbol,
                                Name = name,
                                Market = "", // Yahoo Financeからは市場情報が取得できない
                                Type = "index"
                            });
                        }
                    }
                }
            }
            
            // 指数が見つからなかった場合はデフォルトリストを使用
            if (indices.Count == 0)
            {
                _logger.LogWarning("No indices found in Yahoo Finance page. Using default indices.");
                await SaveIndicesCache(defaultIndices);
                return defaultIndices;
            }
            
            _logger.LogInformation("Successfully fetched {Count} indices from Yahoo Finance", indices.Count);
            
            // キャッシュに保存
            await SaveIndicesCache(indices);
            
            return indices;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching indices from Yahoo Finance. Using default indices.");
            var defaultIndices = GetDefaultIndices();
            await SaveIndicesCache(defaultIndices);
            return defaultIndices;
        }
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
            _logger.LogInformation("Saved {Count} indices to cache file {CacheFile}", indices.Count, _cacheFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save indices to cache file {CacheFile}", _cacheFilePath);
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
