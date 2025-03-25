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
        public DateTime LastTradingDate { get; set; } 
    }

    public static class StockDataCache
    {
        private static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "USStockDownloader");
        private static readonly string CacheFile = Path.Combine(CacheDirectory, "stock_data_cache.json");

        private static readonly TimeSpan MarketOpenTime = TimeSpan.FromHours(9.5);
        private static readonly TimeSpan MarketCloseTime = TimeSpan.FromHours(16);

        private static DateTime ConvertToEasternTime(DateTime localTime)
        {
            try
            {
                var utcTime = localTime.ToUniversalTime();

                try
                {
                    var easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                    return TimeZoneInfo.ConvertTimeFromUtc(utcTime, easternZone);
                }
                catch
                {
                    try
                    {
                        var easternZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
                        return TimeZoneInfo.ConvertTimeFromUtc(utcTime, easternZone);
                    }
                    catch
                    {
                        var isDst = IsInDaylightSavingTime(utcTime);
                        var offset = isDst ? -4 : -5;  
                        return utcTime.AddHours(offset);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Time conversion failed. Using local time. Error: {ex.Message}");
                return localTime;
            }
        }

        private static bool IsInDaylightSavingTime(DateTime date)
        {
            var year = date.Year;

            var marchSecondSunday = new DateTime(year, 3, 1).AddDays((14 - (int)new DateTime(year, 3, 1).DayOfWeek) % 7);
            var dstStart = marchSecondSunday.AddHours(2); 

            var novemberFirstSunday = new DateTime(year, 11, 1).AddDays((7 - (int)new DateTime(year, 11, 1).DayOfWeek) % 7);
            var dstEnd = novemberFirstSunday.AddHours(2); 

            return date >= dstStart && date < dstEnd;
        }

        private static bool IsMarketHours()
        {
            try
            {
                var easternTime = ConvertToEasternTime(DateTime.Now);
                
                if (easternTime.DayOfWeek == DayOfWeek.Saturday || easternTime.DayOfWeek == DayOfWeek.Sunday)
                    return false;

                if (IsUsHoliday(easternTime))
                    return false;

                var timeOfDay = easternTime.TimeOfDay;
                var isMarketOpen = timeOfDay >= MarketOpenTime && timeOfDay <= MarketCloseTime;

                Console.WriteLine($"Market status check - Eastern Time: {easternTime:yyyy-MM-dd HH:mm:ss}, Market is {(isMarketOpen ? "open" : "closed")}");

                return isMarketOpen;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Market hours check failed. Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 指定された日付が将来の取引日である場合、最新の取引日を返します。
        /// 現在の東部時間の日付より後の日付や、同じ日でも市場がまだ閉じていない場合は
        /// 最新の取引日に調整します。
        /// </summary>
        /// <param name="date">調整する日付</param>
        /// <returns>調整された日付（最新の取引日）</returns>
        public static DateTime AdjustToLatestTradingDay(DateTime date)
        {
            try
            {
                // 現在の東部時間を取得
                DateTime easternTime = ConvertToEasternTime(DateTime.Now);
                
                // 指定された日付が現在の東部時間の日付より後の場合
                if (date.Date > easternTime.Date)
                {
                    // 最新の取引日を返す
                    return GetLastTradingDay();
                }
                
                // 指定された日付が現在の東部時間の日付と同じで、市場がまだ閉じていない場合
                if (date.Date == easternTime.Date && !IsMarketClosed())
                {
                    // 前営業日を返す
                    return GetPreviousTradingDay(easternTime);
                }
                
                // それ以外の場合は指定された日付をそのまま返す
                return date;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Date adjustment failed. Using original date. Error: {ex.Message}");
                return date;
            }
        }

        /// <summary>
        /// 指定された日付の前営業日を返します。
        /// </summary>
        /// <param name="date">基準日</param>
        /// <returns>前営業日</returns>
        private static DateTime GetPreviousTradingDay(DateTime date)
        {
            var previousDay = date.AddDays(-1);
            
            while (true)
            {
                if (!IsUsHoliday(previousDay) && 
                    previousDay.DayOfWeek != DayOfWeek.Saturday && 
                    previousDay.DayOfWeek != DayOfWeek.Sunday)
                {
                    return previousDay.Date;
                }
                previousDay = previousDay.AddDays(-1);
            }
        }

        private static bool IsMarketClosed()
        {
            return !IsMarketHours();
        }

        public static DateTime GetLastTradingDay()
        {
            var now = ConvertToEasternTime(DateTime.Now);
            var date = now.Date;
            
            if (now.TimeOfDay > MarketCloseTime && !IsUsHoliday(date) && 
                date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
            {
                return date;
            }
            
            while (true)
            {
                date = date.AddDays(-1);
                if (!IsUsHoliday(date) && date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                {
                    return date;
                }
            }
        }

        private static bool IsUsHoliday(DateTime date)
        {
            var month = date.Month;
            var day = date.Day;
            var dayOfWeek = date.DayOfWeek;

            if ((month == 1 && day == 1) ||   
                (month == 7 && day == 4) ||   
                (month == 12 && day == 25))   
                return true;

            if ((month == 1 && dayOfWeek == DayOfWeek.Monday && day >= 15 && day <= 21) || 
                (month == 2 && dayOfWeek == DayOfWeek.Monday && day >= 15 && day <= 21) || 
                (month == 9 && dayOfWeek == DayOfWeek.Monday && day >= 1 && day <= 7))     
                return true;

            if (month == 5 && dayOfWeek == DayOfWeek.Monday && 
                day > (31 - 7) && day <= 31)
                return true;

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
            }
        }

        public static bool NeedsUpdate(string symbol, DateTime startDate, DateTime endDate, TimeSpan maxAge)
        {
            try
            {
                // 終了日を最新の取引日に自動調整
                DateTime adjustedEndDate = AdjustToLatestTradingDay(endDate);
                
                var cache = LoadCache();
                if (cache.TryGetValue(symbol, out var info))
                {
                    bool isMarketOpen = IsMarketHours();
                    
                    var lastTradingDay = GetLastTradingDay();
                    
                    if (isMarketOpen)
                    {
                        Console.WriteLine($"Symbol {symbol}: Market is open, update required");
                        return true;
                    }

                    var timeSinceLastUpdate = DateTime.Now - info.LastUpdate;
                    if (timeSinceLastUpdate > maxAge)
                    {
                        Console.WriteLine($"Symbol {symbol}: Cache is older than {maxAge.TotalHours} hours, update required");
                        return true;
                    }
                    
                    if (info.LastTradingDate < lastTradingDay)
                    {
                        Console.WriteLine($"Symbol {symbol}: Cache doesn't have latest trading day data, update required");
                        return true;
                    }
                    
                    if (startDate < info.StartDate || adjustedEndDate > info.EndDate)
                    {
                        Console.WriteLine($"Symbol {symbol}: Requested date range is outside cache range, update required");
                        return true;
                    }

                    Console.WriteLine($"Symbol {symbol}: Using cache, no update required");
                    return false;
                }
                
                Console.WriteLine($"Symbol {symbol}: Not in cache, update required");
                return true; 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Symbol {symbol}: Error checking cache: {ex.Message}, update required");
                return true; 
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
                    EndDate = endDate,
                    LastTradingDate = GetLastTradingDay() 
                };
                SaveCache(cache);
            }
            catch
            {
            }
        }
    }
}
