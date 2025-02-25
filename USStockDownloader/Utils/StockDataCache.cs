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
            try
            {
                var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(utcTime.ToUniversalTime(), easternZone);
            }
            catch
            {
                // タイムゾーンIDがない場合（例：Linux環境）は "America/New_York" を試す
                try
                {
                    var easternZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
                    return TimeZoneInfo.ConvertTimeFromUtc(utcTime.ToUniversalTime(), easternZone);
                }
                catch
                {
                    // どちらのタイムゾーンIDも利用できない場合は、UTCからの固定オフセットを使用
                    // 夏時間中（3月第2日曜日から11月第1日曜日まで）はUTC-4、それ以外はUTC-5
                    var year = utcTime.Year;
                    var isDst = IsInDaylightSavingTime(utcTime);
                    var offset = isDst ? -4 : -5;
                    return utcTime.ToUniversalTime().AddHours(offset);
                }
            }
        }

        private static bool IsInDaylightSavingTime(DateTime date)
        {
            var year = date.Year;

            // 3月の第2日曜日を計算
            var marchSecondSunday = new DateTime(year, 3, 1).AddDays((14 - (int)new DateTime(year, 3, 1).DayOfWeek) % 7);
            var dstStart = marchSecondSunday.AddHours(2); // 午前2時に開始

            // 11月の第1日曜日を計算
            var novemberFirstSunday = new DateTime(year, 11, 1).AddDays((7 - (int)new DateTime(year, 11, 1).DayOfWeek) % 7);
            var dstEnd = novemberFirstSunday.AddHours(2); // 午前2時に終了

            return date >= dstStart && date < dstEnd;
        }

        private static bool IsMarketHours()
        {
            var easternTime = ConvertToEasternTime(DateTime.Now);
            
            // 土日は市場が閉まっている
            if (easternTime.DayOfWeek == DayOfWeek.Saturday || easternTime.DayOfWeek == DayOfWeek.Sunday)
                return false;

            // 主要な米国の祝日をチェック
            if (IsUsHoliday(easternTime))
                return false;

            var timeOfDay = easternTime.TimeOfDay;
            return timeOfDay >= MarketOpenTime && timeOfDay <= MarketCloseTime;
        }

        private static bool IsUsHoliday(DateTime date)
        {
            var month = date.Month;
            var day = date.Day;
            var dayOfWeek = date.DayOfWeek;

            // 固定の祝日
            if ((month == 1 && day == 1) ||   // 元日
                (month == 7 && day == 4) ||   // 独立記念日
                (month == 12 && day == 25))   // クリスマス
                return true;

            // 第3月曜日の祝日
            if ((month == 1 && dayOfWeek == DayOfWeek.Monday && day >= 15 && day <= 21) || // マーティン・ルーサー・キング・ジュニアの日
                (month == 2 && dayOfWeek == DayOfWeek.Monday && day >= 15 && day <= 21) || // プレジデントデー
                (month == 9 && dayOfWeek == DayOfWeek.Monday && day >= 1 && day <= 7))     // レイバーデー
                return true;

            // メモリアルデー（5月の最終月曜日）
            if (month == 5 && dayOfWeek == DayOfWeek.Monday && 
                day > (31 - 7) && day <= 31)
                return true;

            // サンクスギビング（11月の第4木曜日）
            if (month == 11 && dayOfWeek == DayOfWeek.Thursday && 
                day >= 22 && day <= 28)
                return true;

            return false;
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
