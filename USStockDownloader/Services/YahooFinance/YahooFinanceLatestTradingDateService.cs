using System;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Globalization;
using System.IO;
using USStockDownloader.Utils;
using USStockDownloader.Services;

namespace USStockDownloader.Services.YahooFinance
{
    /// <summary>
    /// Yahoo Financeから最新の取引日を取得するサービス
    /// </summary>
    public class YahooFinanceLatestTradingDateService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<YahooFinanceLatestTradingDateService> _logger;
        private readonly StockDataCacheSqliteService _sqliteService;
        private DateTime? _cachedLatestTradingDate;
        private DateTime _cacheTime = DateTime.MinValue;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromHours(6); // キャッシュの有効期間（6時間）
        private const string LATEST_TRADING_DATE_KEY = "latest_trading_date";

        // キャッシュファイルのデータ構造
        private class LatestTradingDateCache
        {
            public DateTime LatestTradingDate { get; set; }
            public DateTime CacheTime { get; set; }
        }

        public YahooFinanceLatestTradingDateService(
            HttpClient httpClient, 
            ILogger<YahooFinanceLatestTradingDateService> logger,
            StockDataCacheSqliteService sqliteService)
        {
            _httpClient = httpClient;
            _logger = logger;
            _sqliteService = sqliteService;
            
            // キャッシュからデータを読み込む
            LoadCacheAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// キャッシュからデータを読み込む
        /// </summary>
        private async Task LoadCacheAsync()
        {
            try
            {
                var date = DateTime.Now.Date;
                var latestTradingDate = await _sqliteService.GetLatestTradingDateAsync(date);
                
                if (latestTradingDate.HasValue)
                {
                    _cachedLatestTradingDate = latestTradingDate.Value;
                    _cacheTime = DateTime.Now;
                    
                    _logger.LogDebug("キャッシュから最新取引日を読み込みました: {Date}, キャッシュ時間: {CacheTime} (Loaded latest trading date from cache)",
                        _cachedLatestTradingDate.Value.ToString("yyyy-MM-dd"), _cacheTime);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("キャッシュの読み込み中にエラーが発生しました: {ErrorMessage} (Error occurred while loading cache)", ex.Message);
                // キャッシュの読み込みに失敗した場合は、キャッシュを使用しない
                _cachedLatestTradingDate = null;
                _cacheTime = DateTime.MinValue;
            }
        }

        /// <summary>
        /// キャッシュを保存する
        /// </summary>
        private async Task SaveCacheAsync()
        {
            try
            {
                if (_cachedLatestTradingDate.HasValue)
                {
                    await _sqliteService.SaveLatestTradingDateAsync(_cachedLatestTradingDate.Value);
                    _logger.LogDebug("最新取引日をキャッシュに保存しました: {Date} (Saved latest trading date to cache)",
                        _cachedLatestTradingDate.Value.ToString("yyyy-MM-dd"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("キャッシュの保存中にエラーが発生しました: {ErrorMessage} (Error occurred while saving cache)", ex.Message);
            }
        }

        /// <summary>
        /// Yahoo Financeから最新の取引日を取得します
        /// </summary>
        /// <returns>最新の取引日</returns>
        public async Task<DateTime> GetLatestTradingDateAsync()
        {
            // キャッシュが有効であれば、キャッシュから取得
            if (_cachedLatestTradingDate.HasValue && DateTime.Now - _cacheTime < _cacheDuration)
            {
                _logger.LogDebug("キャッシュから最新取引日を返します: {Date} (Returning latest trading date from cache)",
                    _cachedLatestTradingDate.Value.ToString("yyyy-MM-dd"));
                return _cachedLatestTradingDate.Value;
            }

            _logger.LogDebug("Yahoo Financeから最新取引日を取得しています (Fetching latest trading date from Yahoo Finance)");
            
            try
            {
                DateTime latestTradingDate = await FetchLatestTradingDateFromYahooFinanceAsync();
                
                // キャッシュを更新
                _cachedLatestTradingDate = latestTradingDate;
                _cacheTime = DateTime.Now;
                
                // 非同期でキャッシュを保存（結果を待たない）
                _ = SaveCacheAsync();
                
                return latestTradingDate;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "最新取引日の取得に失敗しました (Failed to fetch latest trading date)");
                
                // キャッシュがあれば、キャッシュから取得
                if (_cachedLatestTradingDate.HasValue)
                {
                    _logger.LogWarning("取得に失敗したため、キャッシュから最新取引日を返します: {Date} (Returning latest trading date from cache due to fetch failure)",
                        _cachedLatestTradingDate.Value.ToString("yyyy-MM-dd"));
                    return _cachedLatestTradingDate.Value;
                }
                
                // キャッシュもない場合は、今日の日付を返す
                _logger.LogWarning("キャッシュもないため、今日の日付を返します: {Date} (Returning today's date due to no cache)",
                    DateTime.Now.ToString("yyyy-MM-dd"));
                return DateTime.Now;
            }
        }

        /// <summary>
        /// Yahoo Financeから最新の取引日を取得します
        /// </summary>
        /// <returns>最新の取引日</returns>
        private async Task<DateTime> FetchLatestTradingDateFromYahooFinanceAsync()
        {
            _logger.LogDebug("Yahoo Financeから最新取引日を取得しています... (Fetching latest trading date from Yahoo Finance)");

            // S&P 500指数の最新データを取得するためのAPIリクエスト
            var request = new HttpRequestMessage(HttpMethod.Get, "https://query1.finance.yahoo.com/v8/finance/chart/%5EGSPC?interval=1d&range=1d");
            
            // User-Agentヘッダーを追加（Yahoo Financeは一部のリクエストでUser-Agentを要求する）
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Mozilla", "5.0"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("(Windows NT 10.0; Win64; x64)"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AppleWebKit", "537.36"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("(KHTML, like Gecko)"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Chrome", "91.0.4472.124"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Safari", "537.36"));
            
            // リクエストを送信
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            
            // レスポンスを解析
            var jsonContent = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Yahoo Finance APIからのレスポンス: {Response} (Response from Yahoo Finance API)", 
                jsonContent.Length > 1000 ? jsonContent.Substring(0, 1000) + "..." : jsonContent);
            
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(jsonContent))
                {
                    // chart プロパティの存在を確認
                    if (!doc.RootElement.TryGetProperty("chart", out var chartElement))
                    {
                        _logger.LogError("Yahoo Finance APIのレスポンスにchartプロパティがありません: {ErrorMessage} (No chart property in Yahoo Finance API response)", "No chart property");
                        return DateTime.Now;
                    }
                    
                    // result プロパティの存在を確認
                    if (!chartElement.TryGetProperty("result", out var resultElement) || resultElement.GetArrayLength() == 0)
                    {
                        _logger.LogError("Yahoo Finance APIのレスポンスにresultプロパティがないか空です: {ErrorMessage} (No result property in Yahoo Finance API response or it's empty)", "No result property");
                        return DateTime.Now;
                    }
                    
                    // timestamp プロパティの存在を確認
                    if (!resultElement[0].TryGetProperty("timestamp", out var timestamps) || timestamps.GetArrayLength() == 0)
                    {
                        _logger.LogError("Yahoo Finance APIのレスポンスにtimestampプロパティがないか空です: {ErrorMessage} (No timestamp property in Yahoo Finance API response or it's empty)", "No timestamp property");
                        return DateTime.Now;
                    }
                    
                    // タイムスタンプ配列の内容をログに出力
                    var timestampCount = timestamps.GetArrayLength();
                    _logger.LogDebug("タイムスタンプの数: {Count} (Number of timestamps)", timestampCount);
                    
                    if (timestampCount > 0)
                    {
                        // 最初と最後のタイムスタンプをログに出力
                        var firstTimestamp = timestamps[0].GetInt64();
                        var lastTimestamp = timestamps[timestampCount - 1].GetInt64();
                        
                        var firstDate = DateTimeOffset.FromUnixTimeSeconds(firstTimestamp).DateTime;
                        var lastDate = DateTimeOffset.FromUnixTimeSeconds(lastTimestamp).DateTime;
                        
                        _logger.LogDebug("最初のタイムスタンプ: {FirstTimestamp} ({FirstDate}), 最後のタイムスタンプ: {LastTimestamp} ({LastDate}) (First and last timestamps)",
                            firstTimestamp, firstDate.ToString("yyyy-MM-dd"), lastTimestamp, lastDate.ToString("yyyy-MM-dd"));
                        
                        // 最新のタイムスタンプを取得（Unix時間、秒単位）
                        long latestTimestamp = lastTimestamp;
                        
                        // Unix時間をDateTimeに変換
                        DateTime latestTradingDate = lastDate;
                        _logger.LogDebug("取得した最新取引日（変換前）: {Date} (Retrieved latest trading date before correction)", 
                            latestTradingDate.ToString("yyyy-MM-dd"));
                        
                        // 取得した日付が現在の年と異なる場合は修正
                        var currentYear = DateTime.Now.Year;
                        if (latestTradingDate.Year != currentYear)
                        {
                            _logger.LogWarning("取得した日付の年が現在と異なります。修正します: {OriginalDate} → {CorrectedDate} (Fixing year in retrieved date)",
                                latestTradingDate.ToString("yyyy-MM-dd"), 
                                latestTradingDate.AddYears(currentYear - latestTradingDate.Year).ToString("yyyy-MM-dd"));
                            
                            latestTradingDate = latestTradingDate.AddYears(currentYear - latestTradingDate.Year);
                        }
                        
                        // 土日チェック
                        if (latestTradingDate.DayOfWeek == DayOfWeek.Saturday)
                        {
                            // 土曜日の場合は金曜日を使用
                            _logger.LogWarning("取得した日付が土曜日です。前営業日を使用します: {OriginalDate} → {CorrectedDate} (Using previous business day as date is Saturday)",
                                latestTradingDate.ToString("yyyy-MM-dd"), 
                                latestTradingDate.AddDays(-1).ToString("yyyy-MM-dd"));
                            
                            latestTradingDate = latestTradingDate.AddDays(-1);
                        }
                        else if (latestTradingDate.DayOfWeek == DayOfWeek.Sunday)
                        {
                            // 日曜日の場合は金曜日を使用
                            _logger.LogWarning("取得した日付が日曜日です。前営業日を使用します: {OriginalDate} → {CorrectedDate} (Using previous business day as date is Sunday)",
                                latestTradingDate.ToString("yyyy-MM-dd"), 
                                latestTradingDate.AddDays(-2).ToString("yyyy-MM-dd"));
                            
                            latestTradingDate = latestTradingDate.AddDays(-2);
                        }
                        
                        // 時刻情報を削除して日付のみにする
                        latestTradingDate = latestTradingDate.Date;
                        
                        _logger.LogDebug("Yahoo Financeから最新取引日を取得しました: {Date} (Retrieved latest trading date from Yahoo Finance)", 
                            latestTradingDate.ToString("yyyy-MM-dd"));
                        
                        return latestTradingDate;
                    }
                    else
                    {
                        _logger.LogWarning("タイムスタンプ配列が空です (Timestamp array is empty)");
                        return DateTime.Now;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("レスポンスの解析中にエラーが発生しました: {ErrorMessage} (Error occurred while parsing response)", ex.Message);
                return DateTime.Now;
            }
        }
    }
}
