using System.Text.Json;

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
        private static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "USStockDownloader");
        private static readonly string CacheFile = Path.Combine(CacheDirectory, "runtime_check.json");

        public static RuntimeCheckResult? LoadCache()
        {
            try
            {
                if (File.Exists(CacheFile))
                {
                    var json = File.ReadAllText(CacheFile);
                    var result = JsonSerializer.Deserialize<RuntimeCheckResult>(json);
                    
                    // 同じ日のキャッシュのみ有効
                    if (result?.CheckDate.Date == DateTime.Now.Date)
                    {
                        return result;
                    }
                }
            }
            catch
            {
                // キャッシュ読み込みに失敗した場合は無視
            }
            return null;
        }

        public static void SaveCache(RuntimeCheckResult result)
        {
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                var json = JsonSerializer.Serialize(result);
                File.WriteAllText(CacheFile, json);
            }
            catch
            {
                // キャッシュ保存に失敗した場合は無視
            }
        }
    }
}
