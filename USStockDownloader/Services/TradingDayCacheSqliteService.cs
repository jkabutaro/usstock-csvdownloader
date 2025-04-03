using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using USStockDownloader.Models;
using USStockDownloader.Options;
using USStockDownloader.Interfaces;

namespace USStockDownloader.Services
{
    public class TradingDayCacheSqliteService : ITradingDayCacheService
    {
        private readonly ILogger<TradingDayCacheSqliteService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _dbPath;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromDays(7); // キャッシュの有効期間（7日間）
        private readonly string _connectionString;

        public TradingDayCacheSqliteService(
            ILogger<TradingDayCacheSqliteService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("YahooFinance");
            _dbPath = Path.Combine("Cache", "StockDataCache", "TradingDays.db");
            
            // DBディレクトリの作成
            var directory = Path.GetDirectoryName(_dbPath);
            if (directory != null)
            {
                Directory.CreateDirectory(directory);
            }
            
            // 接続文字列の設定
            _connectionString = $"Data Source={_dbPath};Pooling=True";
            
            InitializeDatabase();
        }
        
        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            
            using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS TradingDays (
                    YearMonth TEXT,
                    Day INTEGER,
                    PRIMARY KEY (YearMonth, Day)
                );
                
                CREATE TABLE IF NOT EXISTS NoDataPeriods (
                    Symbol TEXT,
                    YearMonth TEXT,
                    StartDate TEXT,
                    EndDate TEXT
                );
                
                CREATE TABLE IF NOT EXISTS CacheMetadata (
                    YearMonth TEXT PRIMARY KEY,
                    LastUpdated TEXT
                );
                
                CREATE INDEX IF NOT EXISTS idx_nodata_symbol ON NoDataPeriods(Symbol);
                CREATE INDEX IF NOT EXISTS idx_nodata_yearmonth ON NoDataPeriods(YearMonth);
                CREATE INDEX IF NOT EXISTS idx_nodata_dates ON NoDataPeriods(StartDate, EndDate);
                CREATE INDEX IF NOT EXISTS idx_tradingdays_yearmonth ON TradingDays(YearMonth);";
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// 指定された日付が取引日かどうかを確認します
        /// </summary>
        /// <param name="date">確認する日付</param>
        /// <returns>取引日の場合はtrue</returns>
        public async Task<bool> CheckTradingDayExistsAsync(DateTime date)
        {
            var normalizedDate = date.Date;
            var yearMonthKey = GetYearMonthKey(normalizedDate);
            
            // データなし期間かチェック
            if (IsDateInNoDataPeriod(normalizedDate))
            {
                _logger.LogDebug("{Date} はデータなし期間に含まれています (Date is in no-data period)", 
                    normalizedDate.ToString("yyyy-MM-dd"));
                return false;
            }
            
            // キャッシュから取引日を確認
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM TradingDays WHERE YearMonth = @YearMonth AND Day = @Day";
            command.Parameters.AddWithValue("@YearMonth", yearMonthKey);
            command.Parameters.AddWithValue("@Day", normalizedDate.Day);
            
            var count = Convert.ToInt32(await command.ExecuteScalarAsync());
            if (count > 0)
            {
                return true;
            }
            
            // キャッシュに存在しない場合は更新
            bool isCacheExpired = await IsCacheExpiredAsync(yearMonthKey);
            if (isCacheExpired)
            {
                _logger.LogDebug("{Year}年{Month}月のキャッシュが期限切れまたは存在しないため更新します (Updating cache for {Year}-{Month} as it is expired or does not exist)",
                    normalizedDate.Year, normalizedDate.Month, normalizedDate.Year, normalizedDate.Month);
                await UpdateTradingDaysForMonthAsync(normalizedDate);
                
                // 再度確認
                command.CommandText = "SELECT COUNT(*) FROM TradingDays WHERE YearMonth = @YearMonth AND Day = @Day";
                count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
            
            return false;
        }

        /// <summary>
        /// 指定された日付範囲内に取引日が存在するかを確認します
        /// </summary>
        /// <param name="startDate">開始日</param>
        /// <param name="endDate">終了日</param>
        /// <returns>範囲内に取引日が存在する場合はtrue</returns>
        public async Task<bool> CheckTradingDayRangeAsync(DateTime startDate, DateTime endDate)
        {
            var normalizedStartDate = startDate.Date;
            var normalizedEndDate = endDate.Date;
            
            try
            {
                // チェックを最適化：最初に月単位で前処理
                var currentMonth = new DateTime(normalizedStartDate.Year, normalizedStartDate.Month, 1);
                var endMonth = new DateTime(normalizedEndDate.Year, normalizedEndDate.Month, 1);
                
                while (currentMonth <= endMonth)
                {
                    var yearMonthKey = GetYearMonthKey(currentMonth);
                    bool isCacheExpired = await IsCacheExpiredAsync(yearMonthKey);
                    
                    if (isCacheExpired)
                    {
                        _logger.LogDebug("{Year}年{Month}月のキャッシュが期限切れまたは存在しないため一括更新します (Updating cache for month as it is expired or does not exist)",
                            currentMonth.Year, currentMonth.Month);
                        
                        try
                        {
                            await UpdateTradingDaysForMonthAsync(currentMonth);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "{Year}年{Month}月の取引日データの更新中にエラーが発生しましたが、処理を続行します (Error updating trading days for month, continuing anyway)",
                                currentMonth.Year, currentMonth.Month);
                        }
                    }
                    
                    currentMonth = currentMonth.AddMonths(1);
                }
                
                // 日付範囲が単一日かつ平日(月〜金)の場合は ^DJI の時系列データを直接確認
                if (normalizedStartDate == normalizedEndDate && 
                    normalizedStartDate.DayOfWeek != DayOfWeek.Saturday && 
                    normalizedStartDate.DayOfWeek != DayOfWeek.Sunday)
                {
                    // 今日または昨日の平日は通常取引日として扱う（短縮取引やホリデーを除く）
                    var today = DateTime.Now.Date;
                    if (normalizedStartDate == today || normalizedStartDate == today.AddDays(-1))
                    {
                        _logger.LogDebug("今日または昨日の日付 ({Date}) は平日のため、取引日として扱います (Date is a weekday and today/yesterday, treating as trading day)",
                            normalizedStartDate.ToString("yyyy-MM-dd"));
                        return true;
                    }
                    
                    // ^DJI の時系列データが存在するか確認
                    try
                    {
                        // まず指定日付が取引日データとして既に保存されているか確認
                        var existingYearMonth = GetYearMonthKey(normalizedStartDate);
                        using var checkConnection = new SqliteConnection(_connectionString);
                        await checkConnection.OpenAsync();
                        
                        using var checkCommand = checkConnection.CreateCommand();
                        checkCommand.CommandText = "SELECT COUNT(*) FROM TradingDays WHERE YearMonth = @YearMonth AND Day = @Day";
                        checkCommand.Parameters.AddWithValue("@YearMonth", existingYearMonth);
                        checkCommand.Parameters.AddWithValue("@Day", normalizedStartDate.Day);
                        
                        var count_dji = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                        if (count_dji > 0)
                        {
                            _logger.LogDebug("日付 {Date} は取引日データに存在します (Date exists in trading days data)",
                                normalizedStartDate.ToString("yyyy-MM-dd"));
                            return true;
                        }
                        
                        // APIから直接データを取得して確認
                        _logger.LogDebug("日付 {Date} の取引日データを^DJIから確認します (Checking trading day data for date directly from ^DJI)",
                            normalizedStartDate.ToString("yyyy-MM-dd"));
                            
                        var httpClient = new HttpClient();
                        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
                        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                        httpClient.DefaultRequestHeaders.Add("Referer", "https://finance.yahoo.com/");
                        
                        // 指定日の終値を取得（日付をUNIXタイムスタンプに変換）
                        var startUnixTime = ToUnixTimestamp(normalizedStartDate);
                        var endUnixTime = ToUnixTimestamp(normalizedStartDate.AddDays(1).AddSeconds(-1)); // 同日の終わりまで
                        
                        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/%5EDJI?period1={startUnixTime}&period2={endUnixTime}&interval=1d";
                        var response = await httpClient.GetAsync(url);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsStringAsync();
                            using var doc = JsonDocument.Parse(content);
                            
                            if (doc.RootElement.TryGetProperty("chart", out var chart) &&
                                chart.TryGetProperty("result", out var results) &&
                                results.GetArrayLength() > 0 &&
                                results[0].TryGetProperty("timestamp", out var timestamps) &&
                                timestamps.GetArrayLength() > 0)
                            {
                                // タイムスタンプが存在する = 取引日である
                                _logger.LogDebug("日付 {Date} は^DJIのデータにタイムスタンプが存在するため、取引日です (Date is a trading day as timestamp exists in ^DJI data)",
                                    normalizedStartDate.ToString("yyyy-MM-dd"));
                                    
                                // DBに保存して次回から使えるようにする
                                var tradingDays = new List<DateTime> { normalizedStartDate };
                                await InsertTradingDaysBatchAsync(existingYearMonth, tradingDays);
                                
                                return true;
                            }
                        }
                        
                        _logger.LogDebug("日付 {Date} は^DJIのデータに存在しないため、取引日ではありません (Date is not a trading day as it doesn't exist in ^DJI data)",
                            normalizedStartDate.ToString("yyyy-MM-dd"));
                            
                        return false;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "日付 {Date} の^DJIデータ取得中にエラーが発生しました。平日なのでtrue返します (Error checking ^DJI data for date, returning true as it's a weekday)",
                            normalizedStartDate.ToString("yyyy-MM-dd"));
                        
                        // エラーが発生した場合は平日なのでtrueを返す
                        return true;
                    }
                }
                
                // データベースから直接範囲検索（日付単位の繰り返し処理を避ける）
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT COUNT(*) FROM TradingDays 
                    WHERE (YearMonth || '-' || Day) >= @StartDate AND (YearMonth || '-' || Day) <= @EndDate";
                command.Parameters.AddWithValue("@StartDate", normalizedStartDate.ToString("yyyy-MM-dd"));
                command.Parameters.AddWithValue("@EndDate", normalizedEndDate.ToString("yyyy-MM-dd"));
                
                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "日付範囲 {StartDate} から {EndDate} の取引日チェック中にエラーが発生しました。セーフティとしてtrueを返します。 (Error during trading day range check, returning true as safety)",
                    normalizedStartDate.ToString("yyyy-MM-dd"), normalizedEndDate.ToString("yyyy-MM-dd"));
                
                // エラー時はセーフティとしてtrueを返す（データ取得を試みる）
                return true;
            }
        }

        /// <summary>
        /// 指定された日付がデータなし期間に含まれるかを確認します
        /// </summary>
        /// <param name="date">確認する日付</param>
        /// <returns>データなし期間に含まれる場合はtrue</returns>
        public bool IsDateInNoDataPeriod(DateTime date)
        {
            return IsDateInNoDataPeriod("ALL", date);
        }

        /// <summary>
        /// 指定された日付がデータなし期間に含まれるかを確認します
        /// </summary>
        /// <param name="symbol">銘柄シンボル</param>
        /// <param name="date">確認する日付</param>
        /// <returns>データなし期間に含まれる場合はtrue</returns>
        public bool IsDateInNoDataPeriod(string symbol, DateTime date)
        {
            var normalizedDate = date.Date;
            
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*) FROM NoDataPeriods 
                WHERE (Symbol = @Symbol OR Symbol = 'ALL')
                AND date(@CheckDate) >= date(StartDate) 
                AND date(@CheckDate) <= date(EndDate)";
            command.Parameters.AddWithValue("@Symbol", symbol);
            command.Parameters.AddWithValue("@CheckDate", normalizedDate.ToString("yyyy-MM-dd"));
            
            var count = Convert.ToInt32(command.ExecuteScalar());
            
            if (count > 0)
            {
                _logger.LogDebug("銘柄{Symbol}の日付 {Date} はデータなし期間に含まれています (Date is in no-data period for symbol)", 
                    symbol, normalizedDate.ToString("yyyy-MM-dd"));
            }
            
            return count > 0;
        }

        /// <summary>
        /// 指定された月全体がデータなし期間に含まれるかを確認します
        /// </summary>
        /// <param name="monthStart">確認する月の初日</param>
        /// <returns>月全体がデータなし期間に含まれる場合はtrue</returns>
        public bool IsMonthFullyInNoDataPeriod(DateTime monthStart)
        {
            return IsMonthFullyInNoDataPeriod("ALL", monthStart);
        }

        /// <summary>
        /// 指定された月全体がデータなし期間に含まれるかを確認します
        /// </summary>
        /// <param name="symbol">銘柄シンボル</param>
        /// <param name="monthStart">確認する月の初日</param>
        /// <returns>月全体がデータなし期間に含まれる場合はtrue</returns>
        public bool IsMonthFullyInNoDataPeriod(string symbol, DateTime monthStart)
        {
            // 月の初日に正規化
            var normalizedDate = new DateTime(monthStart.Year, monthStart.Month, 1);
            
            // 月の最終日を取得
            var lastDayOfMonth = DateTime.DaysInMonth(normalizedDate.Year, normalizedDate.Month);
            var monthEnd = new DateTime(normalizedDate.Year, normalizedDate.Month, lastDayOfMonth);
            
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*) FROM NoDataPeriods 
                WHERE (Symbol = @Symbol OR Symbol = 'ALL')
                AND date(@MonthStart) >= date(StartDate) 
                AND date(@MonthEnd) <= date(EndDate)";
            command.Parameters.AddWithValue("@Symbol", symbol);
            command.Parameters.AddWithValue("@MonthStart", normalizedDate.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("@MonthEnd", monthEnd.ToString("yyyy-MM-dd"));
            
            var count = Convert.ToInt32(command.ExecuteScalar());
            
            if (count > 0)
            {
                _logger.LogDebug("銘柄{Symbol}の期間 {StartDate} から {EndDate} は完全にデータなし期間に含まれています (Month is fully in no-data period for symbol)",
                    symbol, normalizedDate.ToString("yyyy-MM-dd"), monthEnd.ToString("yyyy-MM-dd"));
            }
            
            return count > 0;
        }

        #region NoDataPeriod実装

        /// <summary>
        /// 指定された日をデータが存在しない期間として記録します（単一日）
        /// </summary>
        /// <param name="targetDate">データが存在しない日</param>
        public void RecordNoDataPeriod(DateTime targetDate)
        {
            // 同じ日付を開始日と終了日として設定
            RecordNoDataPeriod("ALL", targetDate, targetDate);
        }

        /// <summary>
        /// 指定された日付範囲をデータが存在しない期間として記録します
        /// </summary>
        /// <param name="startDate">データが存在しない期間の開始日</param>
        /// <param name="endDate">データが存在しない期間の終了日</param>
        public void RecordNoDataPeriod(DateTime startDate, DateTime endDate)
        {
            // ALL（すべての銘柄）に対して記録
            RecordNoDataPeriod("ALL", startDate, endDate);
        }

        /// <summary>
        /// 指定された銘柄の指定された月をデータが存在しない期間として記録します
        /// </summary>
        /// <param name="symbol">銘柄シンボル</param>
        /// <param name="monthStart">データが存在しない月の初日</param>
        public void RecordNoDataPeriod(string symbol, DateTime monthStart)
        {
            // nullチェック
            symbol = symbol ?? "ALL";
            
            // 月の初日に正規化
            var normalizedDate = new DateTime(monthStart.Year, monthStart.Month, 1);
            
            // 月の最終日を取得
            var lastDayOfMonth = DateTime.DaysInMonth(normalizedDate.Year, normalizedDate.Month);
            var monthEnd = new DateTime(normalizedDate.Year, normalizedDate.Month, lastDayOfMonth);
            
            // 日単位のメソッドを呼び出す
            RecordNoDataPeriod(symbol, normalizedDate, monthEnd);
        }

        /// <summary>
        /// 指定された日付範囲をデータが存在しない期間として記録します
        /// </summary>
        /// <param name="symbol">銘柄シンボル</param>
        /// <param name="startDate">データが存在しない期間の開始日</param>
        /// <param name="endDate">データが存在しない期間の終了日</param>
        public void RecordNoDataPeriod(string symbol, DateTime startDate, DateTime endDate)
        {
            var normalizedStartDate = startDate.Date;
            var normalizedEndDate = endDate.Date;
            
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            
            using var transaction = connection.BeginTransaction();
            
            try
            {
                using (var deleteCommand = connection.CreateCommand())
                {
                    deleteCommand.Transaction = transaction;
                    deleteCommand.CommandText = @"
                        DELETE FROM NoDataPeriods 
                        WHERE Symbol = @Symbol
                        AND (date(@StartDate) <= date(EndDate) AND date(@EndDate) >= date(StartDate))";
                    deleteCommand.Parameters.AddWithValue("@Symbol", symbol);
                    deleteCommand.Parameters.AddWithValue("@StartDate", normalizedStartDate.ToString("yyyy-MM-dd"));
                    deleteCommand.Parameters.AddWithValue("@EndDate", normalizedEndDate.ToString("yyyy-MM-dd"));
                    deleteCommand.ExecuteNonQuery();
                }
                
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO NoDataPeriods (Symbol, YearMonth, StartDate, EndDate)
                    VALUES (@Symbol, @YearMonth, @StartDate, @EndDate)";
                command.Parameters.AddWithValue("@Symbol", symbol);
                command.Parameters.AddWithValue("@YearMonth", GetYearMonthKey(normalizedStartDate)); 
                command.Parameters.AddWithValue("@StartDate", normalizedStartDate.ToString("yyyy-MM-dd"));
                command.Parameters.AddWithValue("@EndDate", normalizedEndDate.ToString("yyyy-MM-dd"));
                command.ExecuteNonQuery();
                
                transaction.Commit();
                _logger.LogDebug("銘柄{Symbol}の期間 {StartDate} から {EndDate} をデータなし期間として記録しました (Period recorded as no-data period for symbol)",
                                    symbol, normalizedStartDate.ToString("yyyy-MM-dd"), normalizedEndDate.ToString("yyyy-MM-dd"));
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "銘柄{Symbol}のデータなし期間の記録中にエラーが発生しました (Error occurred while recording no-data period for symbol)", symbol);
                throw;
            }
        }

        #endregion NoDataPeriod実装

        /// <summary>
        /// キャッシュを保存します
        /// </summary>
        public async Task SaveCacheAsync()
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// 年月キーを取得します
        /// </summary>
        /// <param name="date">日付</param>
        /// <returns>年月キー（YYYY-MM形式）</returns>
        private string GetYearMonthKey(DateTime date)
        {
            return $"{date.Year}-{date.Month:D2}";
        }

        /// <summary>
        /// キャッシュが期限切れかどうかを確認します
        /// </summary>
        /// <param name="yearMonthKey">年月キー</param>
        /// <returns>期限切れの場合はtrue</returns>
        private async Task<bool> IsCacheExpiredAsync(string yearMonthKey)
        {
            // 現在の月または最近の月は常に最新データが必要なため、キャッシュが期限切れとみなす
            var currentMonth = $"{DateTime.Now.Year}-{DateTime.Now.Month:D2}";
            var previousMonth = DateTime.Now.AddMonths(-1);
            var prevMonthKey = $"{previousMonth.Year}-{previousMonth.Month:D2}";
            
            if (yearMonthKey == currentMonth || yearMonthKey == prevMonthKey)
            {
                _logger.LogDebug("年月キー {YearMonth} は現在または先月のため、キャッシュを更新します (YearMonth is current or previous month, updating cache)",
                    yearMonthKey);
                return true;
            }
            
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT LastUpdated FROM CacheMetadata WHERE YearMonth = @YearMonth";
            command.Parameters.AddWithValue("@YearMonth", yearMonthKey);
            
            var result = await command.ExecuteScalarAsync();
            if (result == null)
            {
                return true;
            }
            
            var lastUpdated = DateTime.Parse(result.ToString() ?? string.Empty);
            return DateTime.Now - lastUpdated > _cacheDuration;
        }

        /// <summary>
        /// 月の取引日を更新します
        /// </summary>
        /// <param name="date">更新する月の日付</param>
        private async Task UpdateTradingDaysForMonthAsync(DateTime date)
        {
            var normalizedDate = new DateTime(date.Year, date.Month, 1);
            var yearMonthKey = GetYearMonthKey(normalizedDate);
            
            try
            {
                _logger.LogDebug("{Year}年{Month}月の取引日データを取得しています (Fetching trading days for {Year}-{Month})...",
                                    normalizedDate.Year, normalizedDate.Month, normalizedDate.Year, normalizedDate.Month);
                
                var tradingDays = await FetchTradingDaysFromYahooFinanceAsync(normalizedDate);
                
                if (tradingDays.Count > 0)
                {
                    await InsertTradingDaysBatchAsync(yearMonthKey, tradingDays);
                    
                    await UpdateCacheMetadataAsync(yearMonthKey);
                    
                    _logger.LogDebug("{Year}年{Month}月の取引日データを更新しました (Trading days updated for {Year}-{Month}): {Count}日",
                                        normalizedDate.Year, normalizedDate.Month, normalizedDate.Year, normalizedDate.Month, tradingDays.Count);
                }
                else
                {
                    _logger.LogWarning("{Year}年{Month}月の取引日データが見つかりませんでした (No trading days found for {Year}-{Month})",
                                    normalizedDate.Year, normalizedDate.Month, normalizedDate.Year, normalizedDate.Month);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Year}年{Month}月の取引日データの更新中にエラーが発生しました (Error updating trading days for {Year}-{Month})",
                                normalizedDate.Year, normalizedDate.Month, normalizedDate.Year, normalizedDate.Month);
                throw;
            }
        }

        /// <summary>
        /// Yahoo Financeから取引日データを取得します
        /// </summary>
        /// <param name="monthStart">取得する月の初日</param>
        /// <returns>取引日のリスト</returns>
        private async Task<List<DateTime>> FetchTradingDaysFromYahooFinanceAsync(DateTime monthStart)
        {
            var tradingDays = new List<DateTime>();
            
            var lastDayOfMonth = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
            var monthEnd = new DateTime(monthStart.Year, monthStart.Month, lastDayOfMonth);
            
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/%5EGSPC?period1={ToUnixTimestamp(monthStart)}&period2={ToUnixTimestamp(monthEnd)}&interval=1d";
            
            _logger.LogDebug("Yahoo Financeから{Year}年{Month}月の取引日データを取得しています (Fetching trading days for {Year}-{Month} from Yahoo Finance)...",
                monthStart.Year, monthStart.Month, monthStart.Year, monthStart.Month);
            
            int retryCount = 0;
            const int maxRetries = 3;
            TimeSpan retryDelay = TimeSpan.FromSeconds(2);
            
            while (retryCount < maxRetries)
            {
                try
                {
                    _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                    _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://finance.yahoo.com");

                    var response = await _httpClient.GetAsync(url);
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        retryCount++;
                        _logger.LogWarning("Yahoo Financeからのデータ取得でレート制限に達しました。{RetryCount}/{MaxRetries}回目のリトライを{Delay}秒後に行います。 (Rate limit reached. Retrying {RetryCount}/{MaxRetries} in {Delay} seconds)",
                            retryCount, maxRetries, retryDelay.TotalSeconds, retryCount, maxRetries, retryDelay.TotalSeconds);
                        
                        await Task.Delay(retryDelay);
                        retryDelay = TimeSpan.FromSeconds(retryDelay.TotalSeconds * 2);
                        continue;
                    }
                    
                    response.EnsureSuccessStatusCode();
                    
                    var content = await response.Content.ReadAsStringAsync();
                    
                    using (JsonDocument doc = JsonDocument.Parse(content))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("chart", out var chart) && 
                            chart.TryGetProperty("result", out var results) && 
                            results.GetArrayLength() > 0)
                        {
                            var result = results[0];
                            if (result.TryGetProperty("timestamp", out var timestamps))
                            {
                                _logger.LogDebug("タイムスタンプの数: {Count} (Number of timestamps)", timestamps.GetArrayLength());
                                
                                foreach (var timestamp in timestamps.EnumerateArray())
                                {
                                    var unixTime = timestamp.GetInt64();
                                    var dateTime = DateTimeOffset.FromUnixTimeSeconds(unixTime).DateTime;
                                    tradingDays.Add(dateTime.Date);
                                }
                                
                                if (tradingDays.Count > 0)
                                {
                                    _logger.LogDebug("最初のタイムスタンプ: {FirstTimestamp} ({FirstDate}), 最後のタイムスタンプ: {LastTimestamp} ({LastDate}) (First and last timestamps)",
                                        timestamps[0].GetInt64(), tradingDays.First().ToString("yyyy-MM-dd"),
                                        timestamps[timestamps.GetArrayLength() - 1].GetInt64(), tradingDays.Last().ToString("yyyy-MM-dd"));
                                }
                            }
                        }
                    }
                    
                    break;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    _logger.LogError(ex, "Yahoo Financeからの取引日データ取得中にエラーが発生しました。{RetryCount}/{MaxRetries}回目のリトライを{Delay}秒後に行います。 (Error fetching trading days. Retrying {RetryCount}/{MaxRetries} in {Delay} seconds)",
                        retryCount, maxRetries, retryDelay.TotalSeconds, retryCount, maxRetries, retryDelay.TotalSeconds);
                    
                    if (retryCount >= maxRetries)
                    {
                        throw;
                    }
                    
                    await Task.Delay(retryDelay);
                    retryDelay = TimeSpan.FromSeconds(retryDelay.TotalSeconds * 2);
                }
            }
            
            return tradingDays;
        }

        /// <summary>
        /// 複数の取引日を一度に挿入します
        /// </summary>
        /// <param name="yearMonthKey">年月キー</param>
        /// <param name="tradingDays">取引日のリスト</param>
        private async Task InsertTradingDaysBatchAsync(string yearMonthKey, List<DateTime> tradingDays)
        {
            if (tradingDays.Count == 0)
            {
                return;
            }
            
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            using var transaction = connection.BeginTransaction();
            
            try
            {
                // 既存のデータを削除する前に、挿入する日付と重複するエントリを確認
                var existingDays = new HashSet<int>();
                using (var checkCommand = connection.CreateCommand())
                {
                    checkCommand.Transaction = transaction;
                    checkCommand.CommandText = "SELECT Day FROM TradingDays WHERE YearMonth = @YearMonth";
                    checkCommand.Parameters.AddWithValue("@YearMonth", yearMonthKey);
                    
                    using var reader = await checkCommand.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        existingDays.Add(reader.GetInt32(0));
                    }
                }
                
                // 既存データの有無に基づいて処理を分岐
                if (existingDays.Count > 0)
                {
                    _logger.LogDebug("{YearMonth}には既に{Count}日の取引日データが存在します。重複しないデータのみ挿入します。 (Trading days already exist for {YearMonth}, inserting only new days)",
                        yearMonthKey, existingDays.Count, yearMonthKey, existingDays.Count);
                    
                    // 重複しない日付だけをフィルタリング
                    var newTradingDays = tradingDays.Where(d => !existingDays.Contains(d.Day)).ToList();
                    
                    if (newTradingDays.Count == 0)
                    {
                        _logger.LogDebug("{YearMonth}には新しい取引日データはありません。更新をスキップします。 (No new trading days for {YearMonth}, skipping update)",
                            yearMonthKey, yearMonthKey);
                        return;
                    }
                    
                    // 個別に挿入クエリを実行（一括挿入ではなく）
                    using var insertCommand = connection.CreateCommand();
                    insertCommand.Transaction = transaction;
                    insertCommand.CommandText = "INSERT OR IGNORE INTO TradingDays (YearMonth, Day) VALUES (@YearMonth, @Day)";
                    var yearMonthParam = insertCommand.Parameters.Add("@YearMonth", SqliteType.Text);
                    var dayParam = insertCommand.Parameters.Add("@Day", SqliteType.Integer);
                    
                    foreach (var day in newTradingDays)
                    {
                        yearMonthParam.Value = yearMonthKey;
                        dayParam.Value = day.Day;
                        await insertCommand.ExecuteNonQueryAsync();
                    }
                    
                    await transaction.CommitAsync();
                    _logger.LogDebug("{YearMonth}に{Count}件の新しい取引日データを追加しました (Added {Count} new trading days to {YearMonth})",
                        yearMonthKey, newTradingDays.Count, newTradingDays.Count, yearMonthKey);
                }
                else
                {
                    // 既存データがない場合は従来通り一括挿入
                    using (var deleteCommand = connection.CreateCommand())
                    {
                        deleteCommand.Transaction = transaction;
                        deleteCommand.CommandText = "DELETE FROM TradingDays WHERE YearMonth = @YearMonth";
                        deleteCommand.Parameters.AddWithValue("@YearMonth", yearMonthKey);
                        await deleteCommand.ExecuteNonQueryAsync();
                    }
                    
                    // 一括挿入ではなく同じINSERT OR IGNOREメソッドを使用
                    using var insertCommand = connection.CreateCommand();
                    insertCommand.Transaction = transaction;
                    insertCommand.CommandText = "INSERT OR IGNORE INTO TradingDays (YearMonth, Day) VALUES (@YearMonth, @Day)";
                    var yearMonthParam = insertCommand.Parameters.Add("@YearMonth", SqliteType.Text);
                    var dayParam = insertCommand.Parameters.Add("@Day", SqliteType.Integer);
                    
                    foreach (var day in tradingDays)
                    {
                        yearMonthParam.Value = yearMonthKey;
                        dayParam.Value = day.Day;
                        await insertCommand.ExecuteNonQueryAsync();
                    }
                    
                    await transaction.CommitAsync();
                    
                    _logger.LogDebug("{Count}件の取引日データを{YearMonth}に一括挿入しました (Bulk inserted {Count} trading days for {YearMonth})",
                        tradingDays.Count, yearMonthKey, tradingDays.Count, yearMonthKey);
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "取引日データの一括挿入中にエラーが発生しました (Error during bulk insert of trading days)");
                throw;
            }
        }

        /// <summary>
        /// キャッシュメタデータを更新します
        /// </summary>
        /// <param name="yearMonthKey">年月キー</param>
        private async Task UpdateCacheMetadataAsync(string yearMonthKey)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO CacheMetadata (YearMonth, LastUpdated)
                VALUES (@YearMonth, @LastUpdated)";
            command.Parameters.AddWithValue("@YearMonth", yearMonthKey);
            command.Parameters.AddWithValue("@LastUpdated", DateTime.Now.ToString("o"));
            
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// 日付をUnixタイムスタンプに変換します
        /// </summary>
        /// <param name="date">変換する日付</param>
        /// <returns>Unixタイムスタンプ</returns>
        private long ToUnixTimestamp(DateTime date)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(date.ToUniversalTime() - epoch).TotalSeconds;
        }
    }
}
