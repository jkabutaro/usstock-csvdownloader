using System.Net;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Polly;
using Polly.Retry;
using USStockDownloader.Models;
using USStockDownloader.Exceptions;

namespace USStockDownloader.Services;

public class StockDataService : IStockDataService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StockDataService> _logger;
    private static readonly Random _random = new Random();
    private readonly AsyncRetryPolicy<List<StockData>> _retryPolicy;

    public StockDataService(HttpClient httpClient, ILogger<StockDataService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // デフォルトのヘッダーを設定
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");

        // リトライポリシーの設定
        _retryPolicy = Policy<List<StockData>>
            .Handle<HttpRequestException>()
            .Or<DataParsingException>()
            .WaitAndRetryAsync(
                3, // 最大リトライ回数
                retryAttempt => 
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + // 指数バックオフ
                    TimeSpan.FromMilliseconds(_random.Next(0, 1000)) // ジッター
            );
    }

    public async Task<List<StockData>> GetStockDataAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        return await _retryPolicy.ExecuteAsync(async (context) =>
        {
            context["Symbol"] = symbol;

            try
            {
                _logger.LogInformation("Fetching data for symbol: {Symbol} from {StartDate} to {EndDate}", 
                    symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

                // ランダムな遅延を追加してレート制限を回避
                await Task.Delay(TimeSpan.FromMilliseconds(_random.Next(500, 1500)));

                var unixStartTime = ((DateTimeOffset)startDate).ToUnixTimeSeconds();
                var unixEndTime = ((DateTimeOffset)endDate).ToUnixTimeSeconds();

                _logger.LogDebug("Unix timestamps - Start: {StartTime}, End: {EndTime}", unixStartTime, unixEndTime);

                var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?period1={unixStartTime}&period2={unixEndTime}&interval=1d";
                _logger.LogDebug("Request URL: {Url}", url);

                var response = await _httpClient.GetAsync(url);
                _logger.LogDebug("Response status code: {StatusCode}", response.StatusCode);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("Rate limit hit for symbol {Symbol}", symbol);
                    throw new RateLimitException($"Rate limit exceeded for {symbol}");
                }

                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Response content length: {Length} bytes", content.Length);

                var yahooResponse = JsonSerializer.Deserialize<YahooFinanceResponse>(content);
                if (yahooResponse?.Chart?.Result == null || !yahooResponse.Chart.Result.Any())
                {
                    _logger.LogError("No data returned for symbol {Symbol}", symbol);
                    throw new DataParsingException($"No data returned for {symbol}");
                }

                var result = yahooResponse.Chart.Result[0];
                if (result.Timestamp == null || result.Indicators?.Quote == null || !result.Indicators.Quote.Any())
                {
                    _logger.LogError("Invalid data structure for symbol {Symbol}", symbol);
                    throw new DataParsingException($"Invalid data structure for {symbol}");
                }

                var quote = result.Indicators.Quote[0];
                if (quote.High == null || quote.Low == null || quote.Open == null || quote.Close == null || quote.Volume == null)
                {
                    _logger.LogError("Missing price data for symbol {Symbol}", symbol);
                    throw new DataParsingException($"Missing price data for {symbol}");
                }

                var stockDataList = new List<StockData>();

                for (int i = 0; i < result.Timestamp.Count; i++)
                {
                    if (quote.Open?[i] == null || quote.High?[i] == null || 
                        quote.Low?[i] == null || quote.Close?[i] == null || 
                        quote.Volume?[i] == null)
                    {
                        continue;
                    }

                    var dateTime = DateTimeOffset.FromUnixTimeSeconds(result.Timestamp[i] ?? 0).Date;
                    var stockData = new StockData
                    {
                        Symbol = symbol,
                        DateTime = dateTime,
                        Date = dateTime.Year * 10000 + dateTime.Month * 100 + dateTime.Day,
                        Open = quote.Open[i].Value,
                        High = quote.High[i].Value,
                        Low = quote.Low[i].Value,
                        Close = quote.Close[i].Value,
                        Volume = quote.Volume[i].Value
                    };

                    // データの検証
                    if (ValidateStockData(stockData))
                    {
                        stockDataList.Add(stockData);
                    }
                    else
                    {
                        _logger.LogWarning("Invalid data point for {Symbol} at {Date}", symbol, stockData.DateTime);
                    }
                }

                if (!stockDataList.Any())
                {
                    throw new DataParsingException($"No valid data points found for {symbol}");
                }

                _logger.LogInformation("Successfully fetched {Count} data points for {Symbol}", 
                    stockDataList.Count, symbol);

                return stockDataList;
            }
            catch (Exception ex) when (ex is not RateLimitException)
            {
                _logger.LogError(ex, "Error fetching data for symbol {Symbol}", symbol);
                throw;
            }
        }, new Context());
    }

    private bool ValidateStockData(StockData data)
    {
        // 基本的なデータ検証
        if (data.Open <= 0 || data.High <= 0 || data.Low <= 0 || data.Close <= 0 || data.Volume <= 0)
        {
            _logger.LogWarning("Invalid price or volume values for {Symbol} at {Date}", data.Symbol, data.DateTime);
            return false;
        }

        // High/Low の関係チェック
        if (data.High < data.Low)
        {
            _logger.LogWarning("High price is lower than low price for {Symbol} at {Date}", data.Symbol, data.DateTime);
            return false;
        }

        // Open/Close が High/Low の範囲内にあることを確認
        if (data.Open > data.High || data.Open < data.Low || 
            data.Close > data.High || data.Close < data.Low)
        {
            _logger.LogWarning("Open/Close prices are outside High/Low range for {Symbol} at {Date}", data.Symbol, data.DateTime);
            return false;
        }

        return true;
    }
}
