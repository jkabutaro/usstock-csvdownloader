using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using USStockDownloader.Exceptions;
using USStockDownloader.Models;
using USStockDownloader.Services;
using Xunit;

namespace USStockDownloader.Tests.Services;

public class StockDataServiceTests
{
    private readonly Mock<ILogger<StockDataService>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly StockDataService _stockDataService;

    public StockDataServiceTests()
    {
        _loggerMock = new Mock<ILogger<StockDataService>>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://query1.finance.yahoo.com/")
        };
        _stockDataService = new StockDataService(_httpClient, _loggerMock.Object);
    }

    [Fact]
    public async Task GetStockDataAsync_ValidSymbol_ReturnsStockData()
    {
        // Arrange
        var symbol = "AAPL";
        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 1, 31);
        var unixStartTime = ((DateTimeOffset)startDate).ToUnixTimeSeconds();
        var unixEndTime = ((DateTimeOffset)endDate).ToUnixTimeSeconds();

        var yahooResponse = CreateValidYahooResponse();
        var jsonResponse = JsonSerializer.Serialize(yahooResponse);

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Method == HttpMethod.Get && 
                    req.RequestUri.ToString().Contains(symbol)),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse)
            });

        // Act
        var result = await _stockDataService.GetStockDataAsync(symbol, startDate, endDate);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Equal(3, result.Count);
        Assert.All(result, data => Assert.Equal(symbol, data.Symbol));
    }

    [Fact]
    public async Task GetStockDataAsync_RateLimitExceeded_ThrowsRateLimitException()
    {
        // Arrange
        var symbol = "AAPL";
        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 1, 31);

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.TooManyRequests
            });

        // Act & Assert
        await Assert.ThrowsAsync<RateLimitException>(() => 
            _stockDataService.GetStockDataAsync(symbol, startDate, endDate));
    }

    [Fact]
    public async Task GetStockDataAsync_EmptyResponse_ThrowsDataParsingException()
    {
        // Arrange
        var symbol = "INVALID";
        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 1, 31);

        var emptyResponse = new YahooFinanceResponse
        {
            Chart = new Chart
            {
                Result = new List<Result>()
            }
        };
        var jsonResponse = JsonSerializer.Serialize(emptyResponse);

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse)
            });

        // Act & Assert
        await Assert.ThrowsAsync<DataParsingException>(() => 
            _stockDataService.GetStockDataAsync(symbol, startDate, endDate));
    }

    [Fact]
    public async Task GetStockDataAsync_InvalidDataStructure_ThrowsDataParsingException()
    {
        // Arrange
        var symbol = "AAPL";
        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 1, 31);

        var invalidResponse = new YahooFinanceResponse
        {
            Chart = new Chart
            {
                Result = new List<Result>
                {
                    new Result
                    {
                        // Missing Timestamp and Indicators
                    }
                }
            }
        };
        var jsonResponse = JsonSerializer.Serialize(invalidResponse);

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse)
            });

        // Act & Assert
        await Assert.ThrowsAsync<DataParsingException>(() => 
            _stockDataService.GetStockDataAsync(symbol, startDate, endDate));
    }

    [Fact]
    public async Task GetStockDataAsync_MissingPriceData_ThrowsDataParsingException()
    {
        // Arrange
        var symbol = "AAPL";
        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 1, 31);

        var invalidResponse = new YahooFinanceResponse
        {
            Chart = new Chart
            {
                Result = new List<Result>
                {
                    new Result
                    {
                        Timestamp = new List<long?> { 1672531200, 1672617600, 1672704000 },
                        Indicators = new Indicators
                        {
                            Quote = new List<Quote>
                            {
                                new Quote
                                {
                                    // Missing price data
                                }
                            }
                        }
                    }
                }
            }
        };
        var jsonResponse = JsonSerializer.Serialize(invalidResponse);

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse)
            });

        // Act & Assert
        await Assert.ThrowsAsync<DataParsingException>(() => 
            _stockDataService.GetStockDataAsync(symbol, startDate, endDate));
    }

    [Fact]
    public async Task GetStockDataAsync_InvalidPriceData_FiltersOutInvalidData()
    {
        // Arrange
        var symbol = "AAPL";
        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 1, 31);

        var response = new YahooFinanceResponse
        {
            Chart = new Chart
            {
                Result = new List<Result>
                {
                    new Result
                    {
                        Timestamp = new List<long?> { 1672531200, 1672617600, 1672704000 },
                        Indicators = new Indicators
                        {
                            Quote = new List<Quote>
                            {
                                new Quote
                                {
                                    Open = new List<decimal?> { 130.28m, -5.0m, 131.25m },
                                    High = new List<decimal?> { 132.42m, 129.95m, 130.90m },
                                    Low = new List<decimal?> { 129.64m, 128.12m, 130.30m },
                                    Close = new List<decimal?> { 131.86m, 130.15m, 130.73m },
                                    Volume = new List<long?> { 79144000, 69742000, 70790000 }
                                }
                            }
                        }
                    }
                }
            }
        };
        var jsonResponse = JsonSerializer.Serialize(response);

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse)
            });

        // Act
        var result = await _stockDataService.GetStockDataAsync(symbol, startDate, endDate);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        // 2つ目のデータは無効なので除外される
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetStockDataAsync_AllInvalidPriceData_ThrowsDataParsingException()
    {
        // Arrange
        var symbol = "AAPL";
        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 1, 31);

        var response = new YahooFinanceResponse
        {
            Chart = new Chart
            {
                Result = new List<Result>
                {
                    new Result
                    {
                        Timestamp = new List<long?> { 1672531200, 1672617600, 1672704000 },
                        Indicators = new Indicators
                        {
                            Quote = new List<Quote>
                            {
                                new Quote
                                {
                                    Open = new List<decimal?> { -1.0m, -5.0m, -10.0m },
                                    High = new List<decimal?> { -0.5m, -4.0m, -9.0m },
                                    Low = new List<decimal?> { -2.0m, -6.0m, -11.0m },
                                    Close = new List<decimal?> { -1.5m, -5.5m, -10.5m },
                                    Volume = new List<long?> { 79144000, 69742000, 70790000 }
                                }
                            }
                        }
                    }
                }
            }
        };
        var jsonResponse = JsonSerializer.Serialize(response);

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse)
            });

        // Act & Assert
        await Assert.ThrowsAsync<DataParsingException>(() => 
            _stockDataService.GetStockDataAsync(symbol, startDate, endDate));
    }

    [Fact]
    public async Task GetStockDataAsync_HighLowInvalid_FiltersOutInvalidData()
    {
        // Arrange
        var symbol = "AAPL";
        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 1, 31);

        var response = new YahooFinanceResponse
        {
            Chart = new Chart
            {
                Result = new List<Result>
                {
                    new Result
                    {
                        Timestamp = new List<long?> { 1672531200, 1672617600, 1672704000 },
                        Indicators = new Indicators
                        {
                            Quote = new List<Quote>
                            {
                                new Quote
                                {
                                    Open = new List<decimal?> { 130.28m, 129.50m, 131.25m },
                                    High = new List<decimal?> { 132.42m, 128.00m, 130.90m }, // 2つ目はLowより低い
                                    Low = new List<decimal?> { 129.64m, 129.00m, 130.30m },
                                    Close = new List<decimal?> { 131.86m, 130.15m, 130.73m },
                                    Volume = new List<long?> { 79144000, 69742000, 70790000 }
                                }
                            }
                        }
                    }
                }
            }
        };
        var jsonResponse = JsonSerializer.Serialize(response);

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse)
            });

        // Act
        var result = await _stockDataService.GetStockDataAsync(symbol, startDate, endDate);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        // 2つ目のデータは無効なので除外される
        Assert.Equal(2, result.Count);
    }

    private YahooFinanceResponse CreateValidYahooResponse()
    {
        return new YahooFinanceResponse
        {
            Chart = new Chart
            {
                Result = new List<Result>
                {
                    new Result
                    {
                        Timestamp = new List<long?> { 1672531200, 1672617600, 1672704000 },
                        Indicators = new Indicators
                        {
                            Quote = new List<Quote>
                            {
                                new Quote
                                {
                                    Open = new List<decimal?> { 130.28m, 129.50m, 131.25m },
                                    High = new List<decimal?> { 132.42m, 129.95m, 132.90m },
                                    Low = new List<decimal?> { 129.64m, 128.12m, 130.30m },
                                    Close = new List<decimal?> { 131.86m, 129.93m, 130.73m },
                                    Volume = new List<long?> { 79144000, 69742000, 70790000 }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
