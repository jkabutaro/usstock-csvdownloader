using System;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using USStockDownloader.Models;
using USStockDownloader.Services;
using Xunit;

namespace USStockDownloader.Tests.Services
{
    public class BuffettCacheServiceTests : IDisposable
    {
        private readonly Mock<ILogger<BuffettCacheService>> _loggerMock;
        private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
        private readonly HttpClient _httpClient;
        private readonly BuffettCacheService _buffettCacheService;
        private readonly string _testCacheDir;
        private readonly string _testCacheFilePath;
        private readonly TimeSpan _testCacheExpiry = TimeSpan.FromHours(1);

        public BuffettCacheServiceTests()
        {
            _loggerMock = new Mock<ILogger<BuffettCacheService>>();
            _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
            _httpClient = new HttpClient(_httpMessageHandlerMock.Object);

            // テスト用の一時ディレクトリを作成
            _testCacheDir = Path.Combine(Path.GetTempPath(), "USStockDownloader_Tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testCacheDir);
            _testCacheFilePath = Path.Combine(_testCacheDir, "buffett_test_cache.json");

            // テスト用のキャッシュ期限を短く設定
            _buffettCacheService = new BuffettCacheService(_httpClient, _loggerMock.Object, _testCacheFilePath, _testCacheExpiry);
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
        public async Task GetSymbolsAsync_WhenCacheExists_ShouldReturnFromCache()
        {
            // Arrange
            var expectedSymbols = new List<StockSymbol>
            {
                new StockSymbol { Symbol = "AAPL", Name = "Apple Inc." },
                new StockSymbol { Symbol = "BRK.B", Name = "Berkshire Hathaway Inc. Class B" }
            };

            // キャッシュファイルを作成
            var json = System.Text.Json.JsonSerializer.Serialize(expectedSymbols);
            Directory.CreateDirectory(Path.GetDirectoryName(_testCacheFilePath)!);
            await File.WriteAllTextAsync(_testCacheFilePath, json);

            // キャッシュファイルの更新日時を現在時刻に設定（有効なキャッシュにする）
            File.SetLastWriteTime(_testCacheFilePath, DateTime.Now);

            // Act
            var result = await _buffettCacheService.GetSymbolsAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedSymbols.Count, result.Count);
            Assert.Equal(expectedSymbols[0].Symbol, result[0].Symbol);
            Assert.Equal(expectedSymbols[1].Symbol, result[1].Symbol);

            // HTTPリクエストが行われていないことを確認
            _httpMessageHandlerMock.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task GetSymbolsAsync_WhenCacheExpired_ShouldFetchFromWikipedia()
        {
            // Arrange
            var expectedSymbols = new List<StockSymbol>
            {
                new StockSymbol { Symbol = "AAPL", Name = "Apple Inc." },
                new StockSymbol { Symbol = "BRK.B", Name = "Berkshire Hathaway Inc. Class B" }
            };

            // 期限切れのキャッシュファイルを作成
            var json = System.Text.Json.JsonSerializer.Serialize(expectedSymbols);
            Directory.CreateDirectory(Path.GetDirectoryName(_testCacheFilePath)!);
            await File.WriteAllTextAsync(_testCacheFilePath, json);

            // キャッシュファイルの更新日時を古く設定（期限切れにする）
            File.SetLastWriteTime(_testCacheFilePath, DateTime.Now.Subtract(_testCacheExpiry).AddMinutes(-10));

            // Wikipediaのモックレスポンスをセットアップ
            var htmlContent = @"
                <html>
                    <body>
                        <table class='wikitable sortable'>
                            <tr><th>Company</th><th>Ticker</th><th>Shares</th><th>Value</th></tr>
                            <tr><td>Apple Inc.</td><td>AAPL</td><td>1,000,000</td><td>$150,000,000</td></tr>
                            <tr><td>Berkshire Hathaway Inc.</td><td>BRK.B</td><td>500,000</td><td>$200,000,000</td></tr>
                        </table>
                    </body>
                </html>";

            _httpMessageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains("wikipedia.org")),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(htmlContent)
                });

            // Act
            var result = await _buffettCacheService.GetSymbolsAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("AAPL", result[0].Symbol);
            Assert.Equal("BRK.B", result[1].Symbol);

            // HTTPリクエストが行われたことを確認
            _httpMessageHandlerMock.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("wikipedia.org")),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task GetSymbolsAsync_WhenCacheDoesNotExist_ShouldFetchFromWikipedia()
        {
            // Arrange
            // キャッシュファイルが存在しないことを確認
            if (File.Exists(_testCacheFilePath))
            {
                File.Delete(_testCacheFilePath);
            }

            // Wikipediaのモックレスポンスをセットアップ
            var htmlContent = @"
                <html>
                    <body>
                        <table class='wikitable sortable'>
                            <tr><th>Company</th><th>Ticker</th><th>Shares</th><th>Value</th></tr>
                            <tr><td>Apple Inc.</td><td>AAPL</td><td>1,000,000</td><td>$150,000,000</td></tr>
                            <tr><td>Berkshire Hathaway Inc.</td><td>BRK.B</td><td>500,000</td><td>$200,000,000</td></tr>
                        </table>
                    </body>
                </html>";

            _httpMessageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains("wikipedia.org")),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(htmlContent)
                });

            // Act
            var result = await _buffettCacheService.GetSymbolsAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("AAPL", result[0].Symbol);
            Assert.Equal("BRK.B", result[1].Symbol);

            // HTTPリクエストが行われたことを確認
            _httpMessageHandlerMock.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("wikipedia.org")),
                ItExpr.IsAny<CancellationToken>());

            // キャッシュファイルが作成されたことを確認
            Assert.True(File.Exists(_testCacheFilePath));
        }

        [Fact]
        public async Task GetSymbolsAsync_WithForceUpdate_ShouldFetchFromWikipediaEvenIfCacheExists()
        {
            // Arrange
            var cachedSymbols = new List<StockSymbol>
            {
                new StockSymbol { Symbol = "OLD1", Name = "Old Symbol 1" },
                new StockSymbol { Symbol = "OLD2", Name = "Old Symbol 2" }
            };

            // 有効なキャッシュファイルを作成
            var json = System.Text.Json.JsonSerializer.Serialize(cachedSymbols);
            Directory.CreateDirectory(Path.GetDirectoryName(_testCacheFilePath)!);
            await File.WriteAllTextAsync(_testCacheFilePath, json);
            File.SetLastWriteTime(_testCacheFilePath, DateTime.Now);

            // Wikipediaのモックレスポンスをセットアップ
            var htmlContent = @"
                <html>
                    <body>
                        <table class='wikitable sortable'>
                            <tr><th>Company</th><th>Ticker</th><th>Shares</th><th>Value</th></tr>
                            <tr><td>Apple Inc.</td><td>AAPL</td><td>1,000,000</td><td>$150,000,000</td></tr>
                            <tr><td>Berkshire Hathaway Inc.</td><td>BRK.B</td><td>500,000</td><td>$200,000,000</td></tr>
                        </table>
                    </body>
                </html>";

            _httpMessageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains("wikipedia.org")),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(htmlContent)
                });

            // Act
            var result = await _buffettCacheService.GetSymbolsAsync(forceUpdate: true);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("AAPL", result[0].Symbol);
            Assert.Equal("BRK.B", result[1].Symbol);

            // HTTPリクエストが行われたことを確認
            _httpMessageHandlerMock.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("wikipedia.org")),
                ItExpr.IsAny<CancellationToken>());
        }

        [Fact]
        public async Task GetSymbolsAsync_WhenWikipediaRequestFails_ShouldThrowException()
        {
            // Arrange
            // キャッシュファイルが存在しないことを確認
            if (File.Exists(_testCacheFilePath))
            {
                File.Delete(_testCacheFilePath);
            }

            // Wikipediaのモックレスポンスをセットアップ（エラーレスポンス）
            _httpMessageHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains("wikipedia.org")),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                    Content = new StringContent("Internal Server Error")
                });

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() => _buffettCacheService.GetSymbolsAsync());

            // HTTPリクエストが行われたことを確認
            _httpMessageHandlerMock.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("wikipedia.org")),
                ItExpr.IsAny<CancellationToken>());
        }
    }
}
