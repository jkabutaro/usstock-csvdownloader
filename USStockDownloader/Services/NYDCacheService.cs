using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using USStockDownloader.Models;
using System.Net.Http;
using HtmlAgilityPack;

namespace USStockDownloader.Services;

public class NYDCacheService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NYDCacheService> _logger;
    private readonly string _cacheFilePath;
    private readonly TimeSpan _cacheExpiry;
    private List<StockSymbol>? _cachedSymbols;
    private const string NYD_URL = "https://en.wikipedia.org/wiki/Dow_Jones_Industrial_Average";

    public NYDCacheService(HttpClient httpClient, ILogger<NYDCacheService> logger)
        : this(httpClient, logger, Path.Combine("Cache", "nyd_symbols.json"), TimeSpan.FromDays(1))
    {
    }

    public NYDCacheService(HttpClient httpClient, ILogger<NYDCacheService> logger, string cacheFilePath, TimeSpan cacheExpiry)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cacheFilePath = cacheFilePath;
        _cacheExpiry = cacheExpiry;
    }

    public async Task<List<StockSymbol>> GetNYDSymbols()
    {
        if (_cachedSymbols != null)
        {
            return _cachedSymbols;
        }

        if (File.Exists(_cacheFilePath))
        {
            var fileInfo = new FileInfo(_cacheFilePath);
            if (DateTime.Now - fileInfo.LastWriteTime < _cacheExpiry)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_cacheFilePath);
                    _cachedSymbols = JsonSerializer.Deserialize<List<StockSymbol>>(json);
                    if (_cachedSymbols != null)
                    {
                        _logger.LogInformation("Loaded {Count} NY Dow symbols from cache", _cachedSymbols.Count);
                        return _cachedSymbols;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load NY Dow symbols from cache");
                }
            }
        }

        _cachedSymbols = await FetchNYDSymbols();
        await SaveSymbolsToCache(_cachedSymbols);
        return _cachedSymbols;
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
        await SaveSymbolsToCache(_cachedSymbols);
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

            var table = doc.DocumentNode.SelectSingleNode("//table[contains(@class, 'wikitable') and contains(@class, 'sortable')]");
            if (table == null)
            {
                throw new Exception("Failed to find NY Dow table");
            }

            var symbols = new List<StockSymbol>();
            var rows = table.SelectNodes(".//tr");

            if (rows == null)
            {
                throw new Exception("No rows found in NY Dow table");
            }

            foreach (var row in rows.Skip(1)) // Skip header row
            {
                var cells = row.SelectNodes(".//td");
                if (cells != null && cells.Count >= 2) // 少なくとも2列（会社名、シンボル）が必要
                {
                    var nameCell = cells[0];
                    var symbolCell = cells[1];
                    
                    var name = nameCell.InnerText.Trim();
                    var symbol = symbolCell.InnerText.Trim();
                    
                    // 市場情報の判定（NY DowはほとんどがNYSE）
                    string market = "NYSE";
                    
                    // 種別の判定（NY Dowは全て個別株）
                    string type = "stock";
                    
                    symbols.Add(new StockSymbol { 
                        Symbol = symbol, 
                        Name = name,
                        Market = market,
                        Type = type
                    });
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

    private async Task SaveSymbolsToCache(List<StockSymbol> symbols)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFilePath)!);
            var json = JsonSerializer.Serialize(symbols);
            await File.WriteAllTextAsync(_cacheFilePath, json);
            _logger.LogInformation("Saved {Count} NY Dow symbols to cache", symbols.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save NY Dow symbols to cache");
        }
    }
}
