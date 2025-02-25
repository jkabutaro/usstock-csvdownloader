using System.Text.Json;
using Microsoft.Extensions.Logging;
using USStockDownloader.Models;

namespace USStockDownloader.Services;

public class SP500CacheService
{
    private readonly ILogger<SP500CacheService> _logger;
    private readonly IndexSymbolService _indexSymbolService;
    private const string CACHE_FILE = "sp500_symbols.json";

    public SP500CacheService(ILogger<SP500CacheService> logger, IndexSymbolService indexSymbolService)
    {
        _logger = logger;
        _indexSymbolService = indexSymbolService;
    }

    public async Task<List<StockSymbol>> GetSP500Symbols(bool forceUpdate = false)
    {
        if (!forceUpdate && File.Exists(CACHE_FILE))
        {
            try
            {
                var json = await File.ReadAllTextAsync(CACHE_FILE);
                var symbols = JsonSerializer.Deserialize<List<StockSymbol>>(json);
                if (symbols != null && symbols.Any())
                {
                    _logger.LogInformation("Loaded {Count} S&P 500 symbols from cache", symbols.Count);
                    return symbols;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load S&P 500 symbols from cache. Will fetch from web.");
            }
        }

        // キャッシュが無効か、強制更新が要求された場合はウェブから取得
        var updatedSymbols = await _indexSymbolService.GetSP500Symbols();
        await SaveToCache(updatedSymbols);
        return updatedSymbols;
    }

    private async Task SaveToCache(List<StockSymbol> symbols)
    {
        try
        {
            var json = JsonSerializer.Serialize(symbols, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(CACHE_FILE, json);
            _logger.LogInformation("Saved {Count} S&P 500 symbols to cache", symbols.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save S&P 500 symbols to cache");
        }
    }
}
