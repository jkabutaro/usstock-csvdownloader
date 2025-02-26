using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using USStockDownloader.Models;
using System.Net.Http;
using HtmlAgilityPack;

namespace USStockDownloader.Services;

public class SP500CacheService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SP500CacheService> _logger;
    private readonly string _cacheFilePath;
    private List<StockSymbol>? _cachedSymbols;
    private const string SP500_URL = "https://en.wikipedia.org/wiki/List_of_S%26P_500_companies";
    private static readonly TimeSpan CACHE_EXPIRY = TimeSpan.FromDays(1);

    public SP500CacheService(HttpClient httpClient, ILogger<SP500CacheService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cacheFilePath = Path.Combine("Cache", "sp500_symbols.json");
    }

    public async Task<List<StockSymbol>> GetSP500Symbols()
    {
        if (_cachedSymbols != null)
        {
            return _cachedSymbols;
        }

        if (File.Exists(_cacheFilePath))
        {
            var fileInfo = new FileInfo(_cacheFilePath);
            if (DateTime.Now - fileInfo.LastWriteTime < CACHE_EXPIRY)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_cacheFilePath);
                    _cachedSymbols = JsonSerializer.Deserialize<List<StockSymbol>>(json);
                    if (_cachedSymbols != null)
                    {
                        _logger.LogInformation("Loaded {Count} S&P 500 symbols from cache", _cachedSymbols.Count);
                        return _cachedSymbols;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load S&P 500 symbols from cache");
                }
            }
        }

        _cachedSymbols = await FetchSP500Symbols();
        await SaveSymbolsToCache(_cachedSymbols);
        return _cachedSymbols;
    }

    public async Task<List<string>> GetSymbolsAsync()
    {
        var symbols = await GetSP500Symbols();
        return symbols.Select(s => s.Symbol).ToList();
    }

    public async Task ForceUpdateAsync()
    {
        _logger.LogInformation("Forcing update of S&P 500 symbols");
        _cachedSymbols = await FetchSP500Symbols();
        await SaveSymbolsToCache(_cachedSymbols);
    }

    private async Task<List<StockSymbol>> FetchSP500Symbols()
    {
        try
        {
            var response = await _httpClient.GetAsync(SP500_URL);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(content);

            var table = doc.DocumentNode.SelectSingleNode("//table[@id='constituents']");
            if (table == null)
            {
                throw new Exception("Failed to find S&P 500 table");
            }

            var symbols = new List<StockSymbol>();
            var rows = table.SelectNodes(".//tr");

            if (rows == null)
            {
                throw new Exception("No rows found in S&P 500 table");
            }

            foreach (var row in rows.Skip(1)) // Skip header row
            {
                var cells = row.SelectNodes(".//td");
                if (cells != null && cells.Count >= 2)
                {
                    var symbol = cells[0].InnerText.Trim();
                    var name = cells[1].InnerText.Trim();
                    symbols.Add(new StockSymbol { Symbol = symbol, Name = name });
                }
            }

            _logger.LogInformation("Fetched {Count} S&P 500 symbols from Wikipedia", symbols.Count);
            return symbols;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch S&P 500 symbols from Wikipedia");
            throw;
        }
    }

    private async Task SaveSymbolsToCache(List<StockSymbol> symbols)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFilePath)!);
            var json = JsonSerializer.Serialize(symbols);
            await File.WriteAllTextAsync(_cacheFilePath, json);
            _logger.LogInformation("Saved {Count} S&P 500 symbols to cache", symbols.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save S&P 500 symbols to cache");
        }
    }
}
