using Microsoft.Extensions.Logging;
using USStockDownloader.Models;
using System.Net.Http;
using HtmlAgilityPack;

namespace USStockDownloader.Services;

public class NYDCacheService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NYDCacheService> _logger;
    private readonly string _cacheFile;
    private List<StockSymbol>? _cachedSymbols;
    private const string NYD_URL = "https://en.wikipedia.org/wiki/Dow_Jones_Industrial_Average";

    public NYDCacheService(HttpClient httpClient, ILogger<NYDCacheService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cacheFile = Path.Combine("Cache", "nyd_symbols.json");
    }

    public async Task<List<string>> GetSymbolsAsync()
    {
        var symbols = await GetNYDSymbols();
        return symbols.Select(s => s.Symbol).ToList();
    }

    public async Task ForceUpdateAsync()
    {
        _logger.LogInformation("Forcing update of NY Dow symbols");
        _cachedSymbols = await FetchNYDSymbols();
        await SaveToCache(_cachedSymbols);
    }

    public async Task<List<StockSymbol>> GetNYDSymbols()
    {
        if (_cachedSymbols != null)
        {
            return _cachedSymbols;
        }

        if (File.Exists(_cacheFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_cacheFile);
                _cachedSymbols = System.Text.Json.JsonSerializer.Deserialize<List<StockSymbol>>(json);
                if (_cachedSymbols != null)
                {
                    _logger.LogInformation("Loaded {Count} NY Dow symbols from cache", _cachedSymbols.Count);
                    
                    // デバッグ用：キャッシュから読み込んだ銘柄情報をログに出力
                    foreach (var symbol in _cachedSymbols)
                    {
                        _logger.LogDebug("Symbol: {Symbol}, Name: {Name}, Market: {Market}, Type: {Type}", 
                            symbol.Symbol, symbol.Name, symbol.Market, symbol.Type);
                    }
                    
                    return _cachedSymbols;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load NY Dow symbols from cache");
            }
        }

        _cachedSymbols = await FetchNYDSymbols();
        await SaveToCache(_cachedSymbols);
        return _cachedSymbols;
    }

    private async Task<List<StockSymbol>> FetchNYDSymbols()
    {
        try
        {
            var response = await _httpClient.GetAsync(NYD_URL);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(content);

            var table = doc.DocumentNode.SelectSingleNode("//table[contains(@class, 'wikitable') and contains(., 'Symbol')]");
            if (table == null)
            {
                throw new Exception("Failed to find NY Dow constituents table");
            }

            var symbols = new List<StockSymbol>();
            foreach (var row in table.SelectNodes(".//tr").Skip(1))
            {
                var cells = row.SelectNodes(".//td");
                if (cells != null && cells.Count >= 2)
                {
                    var symbol = cells[1].InnerText.Trim();
                    var name = cells[0].InnerText.Trim();
                    if (!string.IsNullOrWhiteSpace(symbol))
                    {
                        symbols.Add(new StockSymbol { Symbol = symbol, Name = name });
                    }
                }
            }

            _logger.LogInformation("Fetched {Count} NY Dow symbols from Wikipedia", symbols.Count);
            return symbols;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch NY Dow symbols from Wikipedia");
            throw;
        }
    }

    private async Task SaveToCache(List<StockSymbol> symbols)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
            var json = System.Text.Json.JsonSerializer.Serialize(symbols);
            await File.WriteAllTextAsync(_cacheFile, json);
            _logger.LogInformation("Saved {Count} NY Dow symbols to cache", symbols.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save NY Dow symbols to cache");
        }
    }
}
