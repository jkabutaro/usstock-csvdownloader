using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using USStockDownloader.Services;

namespace USStockDownloader.Utils
{
    public class RuntimeCheckResult
    {
        public DateTime CheckDate { get; set; }
        public bool WindowsVersionValid { get; set; }
        public bool DotNetRuntimeValid { get; set; }
    }

    public static class RuntimeCheckCache
    {
        // SQLiteサービスのシングルトンインスタンス
        private static StockDataCacheSqliteService? _sqliteService;
        private static readonly object _lock = new object();
        private static readonly string RUNTIME_CHECK_KEY = "runtime_check";

        // SQLiteサービスの初期化
        private static StockDataCacheSqliteService GetSqliteService()
        {
            if (_sqliteService == null)
            {
                lock (_lock)
                {
                    if (_sqliteService == null)
                    {
                        // 共通LoggerFactoryを使用
                        var logger = AppLoggerFactory.CreateLogger<StockDataCacheSqliteService>();
                        _sqliteService = new StockDataCacheSqliteService(CacheManager.CacheDirectory, logger);
                    }
                }
            }
            return _sqliteService;
        }

        public static async Task<RuntimeCheckResult?> LoadCacheAsync()
        {
            try
            {
                var sqliteService = GetSqliteService();
                var runtimeChecks = await sqliteService.GetRuntimeChecksAsync();
                
                if (runtimeChecks.TryGetValue(RUNTIME_CHECK_KEY, out var jsonValue))
                {
                    var result = JsonSerializer.Deserialize<RuntimeCheckResult>(jsonValue);
                    
                    // 同じ日のキャッシュのみ有効
                    if (result?.CheckDate.Date == DateTime.Now.Date)
                    {
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ランタイムチェックキャッシュの読み込みに失敗しました: {ex.Message} (Failed to load runtime check cache: {ex.Message})");
            }
            return null;
        }

        public static RuntimeCheckResult? LoadCache()
        {
            return LoadCacheAsync().GetAwaiter().GetResult();
        }

        public static async Task SaveCacheAsync(RuntimeCheckResult result)
        {
            try
            {
                var sqliteService = GetSqliteService();
                var json = JsonSerializer.Serialize(result);
                await sqliteService.SaveRuntimeCheckAsync(RUNTIME_CHECK_KEY, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ランタイムチェックキャッシュの保存に失敗しました: {ex.Message} (Failed to save runtime check cache: {ex.Message})");
            }
        }

        public static void SaveCache(RuntimeCheckResult result)
        {
            SaveCacheAsync(result).GetAwaiter().GetResult();
        }
    }
}
