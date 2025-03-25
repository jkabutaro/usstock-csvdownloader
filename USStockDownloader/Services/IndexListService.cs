using Microsoft.Extensions.Logging;
using USStockDownloader.Models;

namespace USStockDownloader.Services;

public class IndexListService
{
    private readonly ILogger<IndexListService> _logger;
    private readonly IndexCacheService _indexCacheService;
    
    public IndexListService(
        ILogger<IndexListService> logger,
        IndexCacheService indexCacheService)
    {
        _logger = logger;
        _indexCacheService = indexCacheService;
    }
    
    /// <summary>
    /// 主要な指標リストを取得します
    /// </summary>
    /// <returns>指標リスト</returns>
    public async Task<List<StockSymbol>> GetMajorIndicesAsync()
    {
        _logger.LogInformation("Getting major indices list");
        return await _indexCacheService.GetIndicesAsync();
    }
    
    /// <summary>
    /// 主要な指標リストを強制的に更新して取得します
    /// </summary>
    /// <returns>指標リスト</returns>
    public async Task<List<StockSymbol>> ForceUpdateMajorIndicesAsync()
    {
        _logger.LogInformation("Forcing update of major indices list");
        return await _indexCacheService.ForceUpdateAsync();
    }
    
    /// <summary>
    /// 主要な指標リストを取得します（同期版）
    /// </summary>
    /// <returns>指標リスト</returns>
    public List<StockSymbol> GetMajorIndices()
    {
        _logger.LogInformation("Getting major indices list (synchronous)");
        // 非同期メソッドを同期的に呼び出す
        return GetMajorIndicesAsync().GetAwaiter().GetResult();
    }
}
