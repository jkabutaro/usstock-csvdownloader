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
    private const string WikipediaUrl = "https://en.wikipedia.org/wiki/Berkshire_Hathaway";

    public BuffettCacheService(ILogger<BuffettCacheService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<List<string>> GetSymbolsAsync()
    {
        var cacheDir = Path.Combine(Directory.GetCurrentDirectory(), "Cache");
        var cachePath = Path.Combine(cacheDir, CacheFileName);

        if (File.Exists(cachePath))
        {
            _logger.LogInformation("Loading Buffett portfolio symbols from cache");
            var symbols = await LoadFromCacheAsync(cachePath);
            return symbols.Select(s => s.Symbol).ToList();
        }

        _logger.LogInformation("Fetching Buffett portfolio symbols from Wikipedia");
        var fetchedSymbols = await FetchFromWikipediaAsync();
        await SaveToCacheAsync(fetchedSymbols, cachePath);
        return fetchedSymbols.Select(s => s.Symbol).ToList();
    }

    private async Task<List<StockSymbol>> FetchFromWikipediaAsync()
    {
        var html = await _httpClient.GetStringAsync(WikipediaUrl);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var symbols = new List<StockSymbol>();
        var portfolioTable = doc.DocumentNode.SelectNodes("//table[contains(@class, 'wikitable')]")
            .FirstOrDefault(t => t.InnerText.Contains("Investment portfolio"));

        if (portfolioTable == null)
        {
            _logger.LogWarning("Could not find portfolio table on Wikipedia page");
            return symbols;
        }

        foreach (var row in portfolioTable.SelectNodes(".//tr").Skip(1))
        {
            var cells = row.SelectNodes(".//td");
            if (cells?.Count >= 2)
            {
                var symbol = cells[1].InnerText.Trim();
                if (!string.IsNullOrEmpty(symbol) && Regex.IsMatch(symbol, @"^[A-Z.]+$"))
                {
                    symbols.Add(new StockSymbol { Symbol = symbol });
                }
            }
        }

        _logger.LogInformation("Found {Count} Buffett portfolio symbols", symbols.Count);
        return symbols;
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
