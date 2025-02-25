using System.Text.Json;
using System;

namespace USStockDownloader.Utils
{
    public class StockDataCacheInfo
    {
        public string Symbol { get; set; } = string.Empty;
        public DateTime LastUpdate { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public static class StockDataCache
    {
        private static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "USStockDownloader");
        private static readonly string CacheFile = Path.Combine(CacheDirectory, "stock_data_cache.json");

        // 米国東部時間の取引時間（9:30-16:00）
        private static readonly TimeSpan MarketOpenTime = TimeSpan.FromHours(9.5);
        private static readonly TimeSpan MarketCloseTime = TimeSpan.FromHours(16);

        private static DateTime ConvertToEasternTime(DateTime utcTime)
        {
            var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(utcTime.ToUniversalTime(), easternZone);
        }

        private static bool IsMarketHours()
        {
            var easternTime = ConvertToEasternTime(DateTime.Now);
            
            // 土日は市場が閉まっている
            if (easternTime.DayOfWeek == DayOfWeek.Saturday || easternTime.DayOfWeek == DayOfWeek.Sunday)
                return false;

            var timeOfDay = easternTime.TimeOfDay;
            return timeOfDay >= MarketOpenTime && timeOfDay <= MarketCloseTime;
        }

        public static Dictionary<string, StockDataCacheInfo> LoadCache()
        {
            try
            {
                if (File.Exists(CacheFile))
                {
                    var json = File.ReadAllText(CacheFile);
                    return JsonSerializer.Deserialize<Dictionary<string, StockDataCacheInfo>>(json) 
                           ?? new Dictionary<string, StockDataCacheInfo>();
                }
            }
            catch
            {
                // キャッシュ読み込みに失敗した場合は無視
            }
            return new Dictionary<string, StockDataCacheInfo>();
        }

        public static void SaveCache(Dictionary<string, StockDataCacheInfo> cache)
        {
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                var json = JsonSerializer.Serialize(cache);
                File.WriteAllText(CacheFile, json);
            }
            catch
            {
                // キャッシュ保存に失敗した場合は無視
            }
        }

        public static bool NeedsUpdate(string symbol, DateTime startDate, DateTime endDate, TimeSpan maxAge)
        {
            try
            {
                var cache = LoadCache();
                if (cache.TryGetValue(symbol, out var info))
                {
                    // 取引時間内は常に更新
                    if (IsMarketHours())
                        return true;

                    // 取引時間外は1時間以上経過している場合のみ更新
                    var timeSinceLastUpdate = DateTime.Now - info.LastUpdate;
                    if (timeSinceLastUpdate > TimeSpan.FromHours(1))
                        return true;

                    // 要求された期間がキャッシュの範囲外の場合は更新
                    if (startDate < info.StartDate || endDate > info.EndDate)
                        return true;

                    return false;
                }
                return true; // キャッシュにない場合は更新が必要
            }
            catch
            {
                return true; // エラーの場合は安全のため更新
            }
        }

        public static void UpdateCache(string symbol, DateTime startDate, DateTime endDate)
        {
            try
            {
                var cache = LoadCache();
                cache[symbol] = new StockDataCacheInfo
                {
                    Symbol = symbol,
                    LastUpdate = DateTime.Now,
                    StartDate = startDate,
                    EndDate = endDate
                };
                SaveCache(cache);
            }
            catch
            {
                // キャッシュ更新に失敗した場合は無視
            }
        }
    }
}
