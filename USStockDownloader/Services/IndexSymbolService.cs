using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using USStockDownloader.Models;

namespace USStockDownloader.Services;

public class IndexSymbolService
{
    private readonly ILogger<IndexSymbolService> _logger;
    private readonly SP500CacheService _sp500CacheService;
    private readonly NYDCacheService _nydCacheService;

    public IndexSymbolService(
        ILogger<IndexSymbolService> logger,
        SP500CacheService sp500CacheService,
        NYDCacheService nydCacheService)
    {
        _logger = logger;
        _sp500CacheService = sp500CacheService;
        _nydCacheService = nydCacheService;
    }

    public async Task<List<string>> GetSP500Symbols()
    {
        var symbols = await _sp500CacheService.GetSP500Symbols();
        return symbols.Select(s => s.Symbol).ToList();
    }

    public async Task<List<string>> GetNYDSymbols()
    {
        var symbols = await _nydCacheService.GetNYDSymbols();
        return symbols.Select(s => s.Symbol).ToList();
    }
}
