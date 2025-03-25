using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using USStockDownloader.Models;
using USStockDownloader.Services;

namespace USStockDownloader.Tests.Services;

public class IndexCacheServiceTests : IDisposable
{
    private readonly Mock<ILogger<IndexCacheService>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly IndexCacheService _indexCacheService;
    private readonly string _testCacheDir;
    private readonly string _testCacheFilePath;
    private readonly TimeSpan _testCacheExpiry = TimeSpan.FromHours(1);

    public IndexCacheServiceTests()
    {
        _loggerMock = new Mock<ILogger<IndexCacheService>>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        
        // テスト用の一時ディレクトリを作成
        _testCacheDir = Path.Combine(Path.GetTempPath(), "USStockDownloader_Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testCacheDir);
        _testCacheFilePath = Path.Combine(_testCacheDir, "index_test_cache.json");
        
        // テスト用のキャッシュ期限を短く設定
        _indexCacheService = new IndexCacheService(_loggerMock.Object, _httpClient, _testCacheFilePath, _testCacheExpiry);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        
        // テスト後に一時ディレクトリを削除
        if (Directory.Exists(_testCacheDir))
        {
            try
            {
                Directory.Delete(_testCacheDir, true);
            }
            catch (Exception)
            {
                // 削除に失敗しても続行
            }
        }
    }

    [Fact]
    public async Task GetIndicesAsync_WhenCacheExists_ShouldReturnFromCache()
    {
        // Arrange
        var expectedIndices = new List<StockSymbol>
        {
            new StockSymbol { Symbol = "^DJI", Name = "Dow Jones Industrial Average" },
            new StockSymbol { Symbol = "^GSPC", Name = "S&P 500" }
        };
        
        // キャッシュファイルを作成
        var json = System.Text.Json.JsonSerializer.Serialize(expectedIndices);
        Directory.CreateDirectory(Path.GetDirectoryName(_testCacheFilePath)!);
        await File.WriteAllTextAsync(_testCacheFilePath, json);
        
        // キャッシュファイルの更新日時を現在時刻に設定（有効なキャッシュにする）
        File.SetLastWriteTime(_testCacheFilePath, DateTime.Now);

        // Act
        var result = await _indexCacheService.GetIndicesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedIndices.Count, result.Count);
        Assert.Equal(expectedIndices[0].Symbol, result[0].Symbol);
        Assert.Equal(expectedIndices[1].Symbol, result[1].Symbol);
        
        // HTTPリクエストが行われていないことを確認
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetIndicesAsync_WhenCacheExpired_ShouldFetchFromYahooFinance()
    {
        // Arrange
        var expectedIndices = new List<StockSymbol>
        {
            new StockSymbol { Symbol = "^DJI", Name = "Dow Jones Industrial Average" },
            new StockSymbol { Symbol = "^GSPC", Name = "S&P 500" }
        };
        
        // 期限切れのキャッシュファイルを作成
        var json = System.Text.Json.JsonSerializer.Serialize(expectedIndices);
        Directory.CreateDirectory(Path.GetDirectoryName(_testCacheFilePath)!);
        await File.WriteAllTextAsync(_testCacheFilePath, json);
        
        // キャッシュファイルの更新日時を古く設定（期限切れにする）
        File.SetLastWriteTime(_testCacheFilePath, DateTime.Now.Subtract(_testCacheExpiry).AddMinutes(-10));
        
        // Yahoo Financeのモックレスポンスをセットアップ
        var htmlContent = @"
            <html>
                <body>
                    <div>
                        <span>^DJI - Dow Jones Industrial Average</span>
                        <span>^GSPC - S&P 500</span>
                    </div>
                </body>
            </html>";
        
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(htmlContent)
            });

        // Act
        var result = await _indexCacheService.GetIndicesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count > 0);
        
        // HTTPリクエストが行われたことを確認
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.AtLeastOnce(),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetIndicesAsync_WhenCacheDoesNotExist_ShouldFetchFromYahooFinance()
    {
        // Arrange
        // キャッシュファイルが存在しないことを確認
        if (File.Exists(_testCacheFilePath))
        {
            File.Delete(_testCacheFilePath);
        }
        
        // Yahoo Financeのモックレスポンスをセットアップ
        var htmlContent = @"
            <html>
                <body>
                    <div>
                        <span>^DJI - Dow Jones Industrial Average</span>
                        <span>^GSPC - S&P 500</span>
                    </div>
                </body>
            </html>";
        
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(htmlContent)
            });

        // Act
        var result = await _indexCacheService.GetIndicesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count > 0);
        
        // HTTPリクエストが行われたことを確認
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.AtLeastOnce(),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get),
            ItExpr.IsAny<CancellationToken>());
        
        // キャッシュファイルが作成されたことを確認
        Assert.True(File.Exists(_testCacheFilePath));
    }

    [Fact]
    public async Task ForceUpdateAsync_ShouldFetchFromYahooFinanceEvenIfCacheExists()
    {
        // Arrange
        var cachedIndices = new List<StockSymbol>
        {
            new StockSymbol { Symbol = "OLD1", Name = "Old Index 1" },
            new StockSymbol { Symbol = "OLD2", Name = "Old Index 2" }
        };
        
        // 有効なキャッシュファイルを作成
        var json = System.Text.Json.JsonSerializer.Serialize(cachedIndices);
        Directory.CreateDirectory(Path.GetDirectoryName(_testCacheFilePath)!);
        await File.WriteAllTextAsync(_testCacheFilePath, json);
        File.SetLastWriteTime(_testCacheFilePath, DateTime.Now);
        
        // Yahoo Financeのモックレスポンスをセットアップ
        var htmlContent = @"
            <html>
                <body>
                    <div>
                        <span>^DJI - Dow Jones Industrial Average</span>
                        <span>^GSPC - S&P 500</span>
                    </div>
                </body>
            </html>";
        
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(htmlContent)
            });

        // Act
        await _indexCacheService.ForceUpdateAsync();
        var result = await _indexCacheService.GetIndicesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count > 0);
        Assert.DoesNotContain(result, i => i.Symbol == "OLD1");
        Assert.DoesNotContain(result, i => i.Symbol == "OLD2");
        
        // HTTPリクエストが行われたことを確認
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.AtLeastOnce(),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetIndicesAsync_WhenYahooFinanceRequestFails_ShouldThrowException()
    {
        // Arrange
        // キャッシュファイルが存在しないことを確認
        if (File.Exists(_testCacheFilePath))
        {
            File.Delete(_testCacheFilePath);
        }
        
        // Yahoo Financeのモックレスポンスをセットアップ（エラーレスポンス）
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("Internal Server Error")
            });

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => _indexCacheService.GetIndicesAsync());
        
        // HTTPリクエストが行われたことを確認
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.AtLeastOnce(),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get),
            ItExpr.IsAny<CancellationToken>());
    }
}
