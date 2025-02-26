using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using USStockDownloader.Models;

namespace USStockDownloader.Services;

public class BuffettCacheService
{
    private readonly ILogger<BuffettCacheService> _logger;
    private readonly HttpClient _httpClient;
    private const string CacheFileName = "buffett_symbols.csv";
    private const string WikipediaUrl = "https://en.wikipedia.org/wiki/List_of_assets_owned_by_Berkshire_Hathaway";

    public BuffettCacheService(ILogger<BuffettCacheService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<List<StockSymbol>> GetSymbolsAsync(bool forceUpdate = false)
    {
        var cachePath = Path.Combine(Directory.GetCurrentDirectory(), CacheFileName);
        var cacheExists = File.Exists(cachePath);
        var cacheExpired = false;

        if (cacheExists)
        {
            var fileInfo = new FileInfo(cachePath);
            cacheExpired = (DateTime.Now - fileInfo.LastWriteTime).TotalHours > 24;
        }

        if (forceUpdate || !cacheExists || cacheExpired)
        {
            _logger.LogInformation("Fetching Buffett portfolio symbols from Wikipedia");
            var fetchedSymbols = await FetchFromWikipediaAsync();
            await SaveToCacheAsync(fetchedSymbols, cachePath);
            return ApplySymbolMappings(fetchedSymbols);
        }

        _logger.LogInformation("Loading Buffett portfolio symbols from cache");
        var cachedSymbols = await LoadFromCacheAsync(cachePath);
        _logger.LogInformation("Loaded {Count} Buffett portfolio symbols", cachedSymbols.Count);
        return ApplySymbolMappings(cachedSymbols);
    }

    public async Task ForceUpdateAsync()
    {
        _logger.LogInformation("Forcing update of Buffett portfolio symbols...");
        var cachePath = Path.Combine(Directory.GetCurrentDirectory(), CacheFileName);
        var fetchedSymbols = await FetchFromWikipediaAsync();
        await SaveToCacheAsync(fetchedSymbols, cachePath);
    }

    private async Task<List<StockSymbol>> FetchFromWikipediaAsync()
    {
        var html = await _httpClient.GetStringAsync(WikipediaUrl);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var symbols = new List<StockSymbol>();
        
        // まずは直接ティッカーシンボルリンクを探す（最も信頼性が高い）
        var stockLinks = doc.DocumentNode.SelectNodes("//a[contains(@href, 'market-activity/stocks/') or contains(@href, 'quote/XNYS:')]");

        if (stockLinks != null && stockLinks.Count > 0)
        {
            // 見つかったリンクからティッカーシンボルを抽出
            foreach (var link in stockLinks)
            {
                var href = link.GetAttributeValue("href", "");
                var symbol = ExtractSymbolFromUrl(href);
                
                if (!string.IsNullOrEmpty(symbol) && !symbols.Any(s => s.Symbol == symbol))
                {
                    symbols.Add(new StockSymbol { Symbol = symbol });
                }
            }
        }
        else
        {
            // 代替手段：U.S.-listed public company and ETF holdingsというセクションを探す
            var headers = doc.DocumentNode.SelectNodes("//h2");
            var stockSection = headers?.FirstOrDefault(h => h.InnerText.Contains("U.S.-listed public company"));
            
            if (stockSection == null)
            {
                _logger.LogWarning("Could not find portfolio section on Wikipedia page");
                return AddHardcodedSymbols();  // 最終手段としてハードコードされたシンボルを使用
            }

            // このセクションの直後にある株式銘柄のリンクを取得
            var section = stockSection.ParentNode;
            var sectionLinks = section.SelectNodes(".//a[contains(@href, 'market-activity/stocks') or contains(@href, 'quote/XNYS')]");

            if (sectionLinks != null && sectionLinks.Count > 0)
            {
                foreach (var link in sectionLinks)
                {
                    var href = link.GetAttributeValue("href", "");
                    var symbol = ExtractSymbolFromUrl(href);
                    
                    if (!string.IsNullOrEmpty(symbol) && !symbols.Any(s => s.Symbol == symbol))
                    {
                        symbols.Add(new StockSymbol { Symbol = symbol });
                    }
                }
            }
        }

        if (symbols.Count == 0)
        {
            _logger.LogWarning("No symbols found. Using hardcoded fallback list.");
            return AddHardcodedSymbols();
        }

        _logger.LogInformation("Found {Count} Buffett portfolio symbols", symbols.Count);
        return symbols;
    }

    private string ExtractSymbolFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;

        // NASDAQのURLパターン: https://www.nasdaq.com/market-activity/stocks/aapl
        if (url.Contains("nasdaq.com/market-activity/stocks/"))
        {
            var parts = url.Split('/');
            return parts[parts.Length - 1].ToUpper();
        }
        
        // NYSEのURLパターン: https://www.nyse.com/quote/XNYS:AAPL
        else if (url.Contains("nyse.com/quote/"))
        {
            var parts = url.Split(':');
            if (parts.Length > 1)
            {
                return parts[parts.Length - 1].ToUpper();
            }
        }
        
        return string.Empty;
    }

    private List<StockSymbol> AddHardcodedSymbols()
    {
        // バフェットポートフォリオの主要な銘柄をハードコードしておく（フォールバック用）
        var hardcoded = new List<string>
        {
            "AAPL", // アップル
            "BAC",  // バンク・オブ・アメリカ
            "KO",   // コカ・コーラ
            "AXP",  // アメリカン・エキスプレス
            "BK",   // バンク・オブ・ニューヨーク・メロン
            "CHTR", // チャーター・コミュニケーションズ
            "CVX",  // シェブロン
            "KHC",  // クラフト・ハインツ
            "MCO",  // ムーディーズ
            "OXY",  // オキシデンタル・ペトロリアム
            "VZ"    // ベライゾン
        };

        _logger.LogInformation("Using {Count} hardcoded Buffett portfolio symbols", hardcoded.Count);
        return hardcoded.Select(s => new StockSymbol { Symbol = s }).ToList();
    }

    private Dictionary<string, string> GetSymbolMappings()
    {
        // 特定のシンボルがYahoo Financeでは異なる形式で参照される場合のマッピング
        return new Dictionary<string, string>
        {
            // Liberty SiriusXMは企業再編によりSIRIに変更
            { "LSXMA", "SIRI" },
            { "LSXMK", "SIRI" },
        };
    }

    private List<StockSymbol> ApplySymbolMappings(List<StockSymbol> originalSymbols)
    {
        var mappings = GetSymbolMappings();
        var result = new List<StockSymbol>();
        
        _logger.LogInformation("Applying symbol mappings to {Count} symbols", originalSymbols.Count);

        foreach (var symbol in originalSymbols)
        {
            if (mappings.TryGetValue(symbol.Symbol, out var mappedSymbol))
            {
                _logger.LogInformation("Mapping symbol {Original} to {Mapped}", symbol.Symbol, mappedSymbol);
                // マッピングされたシンボルを追加（元のは追加しない）
                result.Add(new StockSymbol { Symbol = mappedSymbol });
            }
            else
            {
                result.Add(symbol);
            }
        }

        _logger.LogInformation("After mapping: {Count} symbols", result.Count);
        return result;
    }

    private async Task<List<StockSymbol>> LoadFromCacheAsync(string cachePath)
    {
        var symbols = new List<StockSymbol>();
        var lines = await File.ReadAllLinesAsync(cachePath);
        foreach (var line in lines.Skip(1))
        {
            var symbol = line.Trim();
            if (!string.IsNullOrEmpty(symbol))
            {
                symbols.Add(new StockSymbol { Symbol = symbol });
            }
        }
        return symbols;
    }

    private async Task SaveToCacheAsync(List<StockSymbol> symbols, string cachePath)
    {
        var cacheDir = Path.GetDirectoryName(cachePath);
        if (!Directory.Exists(cacheDir))
        {
            Directory.CreateDirectory(cacheDir!);
        }

        var lines = new[] { "Symbol" }.Concat(symbols.Select(s => s.Symbol));
        await File.WriteAllLinesAsync(cachePath, lines);
        _logger.LogInformation("Saved {Count} symbols to cache", symbols.Count);
    }
}
