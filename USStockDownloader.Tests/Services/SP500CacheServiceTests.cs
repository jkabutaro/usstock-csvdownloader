using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using USStockDownloader.Models;
using USStockDownloader.Services;
using Xunit;

namespace USStockDownloader.Tests.Services;

public class SP500CacheServiceTests : IDisposable
{
    private readonly Mock<ILogger<SP500CacheService>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly SP500CacheService _sp500CacheService;
    private readonly string _testCacheDir;
    private readonly string _testCacheFilePath;
    private readonly TimeSpan _testCacheExpiry = TimeSpan.FromHours(1);
    private readonly List<StockSymbol> _testSymbols;

    public SP500CacheServiceTests()
    {
        _loggerMock = new Mock<ILogger<SP500CacheService>>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        
        // テスト用の一時ディレクトリを作成
        _testCacheDir = Path.Combine(Path.GetTempPath(), "USStockDownloader_Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testCacheDir);
        _testCacheFilePath = Path.Combine(_testCacheDir, "sp500_test_cache.json");
        
        // テスト用のキャッシュ期限を短く設定
        _sp500CacheService = new SP500CacheService(_httpClient, _loggerMock.Object, _testCacheFilePath, _testCacheExpiry);
        
        // テスト用の銘柄データを準備
        _testSymbols = new List<StockSymbol>
        {
            new StockSymbol { Symbol = "AAPL", Name = "Apple Inc.", Market = "NASDAQ", Type = "stock" },
            new StockSymbol { Symbol = "MSFT", Name = "Microsoft Corporation", Market = "NASDAQ", Type = "stock" },
            new StockSymbol { Symbol = "AMZN", Name = "Amazon.com Inc.", Market = "NASDAQ", Type = "stock" },
            new StockSymbol { Symbol = "GOOGL", Name = "Alphabet Inc. Class A", Market = "NASDAQ", Type = "stock" },
            new StockSymbol { Symbol = "BRK.B", Name = "Berkshire Hathaway Inc. Class B", Market = "NYSE", Type = "stock" }
        };
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
    public async Task GetSP500Symbols_NoCacheFile_FetchesFromWikipedia()
    {
        // Arrange - キャッシュファイルが存在しないことを確認
        if (File.Exists(_testCacheFilePath))
        {
            File.Delete(_testCacheFilePath);
        }

        // Wikipediaのモックレスポンスをセットアップ
        var htmlContent = @"
            <html>
                <body>
                    <table id='constituents'>
                        <tr>
                            <th>Symbol</th>
                            <th>Security</th>
                            <th>SEC filings</th>
                            <th>GICS Sector</th>
                            <th>GICS Sub-Industry</th>
                            <th>Headquarters Location</th>
                            <th>Date added</th>
                            <th>CIK</th>
                            <th>Founded</th>
                        </tr>
                        <tr>
                            <td>AAPL</td>
                            <td>Apple Inc.</td>
                            <td>reports</td>
                            <td>Information Technology</td>
                            <td>Consumer Electronics</td>
                            <td>Cupertino, California</td>
                            <td></td>
                            <td>0000320193</td>
                            <td>1976</td>
                        </tr>
                        <tr>
                            <td>MSFT</td>
                            <td>Microsoft Corporation</td>
                            <td>reports</td>
                            <td>Information Technology</td>
                            <td>Systems Software</td>
                            <td>Redmond, Washington</td>
                            <td></td>
                            <td>0000789019</td>
                            <td>1975</td>
                        </tr>
                        <tr>
                            <td>AMZN</td>
                            <td>Amazon.com Inc.</td>
                            <td>reports</td>
                            <td>Consumer Discretionary</td>
                            <td>Internet & Direct Marketing Retail</td>
                            <td>Seattle, Washington</td>
                            <td></td>
                            <td>0001018724</td>
                            <td>1994</td>
                        </tr>
                        <tr>
                            <td>GOOGL</td>
                            <td>Alphabet Inc. Class A</td>
                            <td>reports</td>
                            <td>Communication Services</td>
                            <td>Interactive Media & Services</td>
                            <td>Mountain View, California</td>
                            <td></td>
                            <td>0001652044</td>
                            <td>1998</td>
                        </tr>
                        <tr>
                            <td>BRK.B</td>
                            <td>Berkshire Hathaway Inc. Class B</td>
                            <td>reports</td>
                            <td>Financials</td>
                            <td>Multi-Sector Holdings</td>
                            <td>Omaha, Nebraska</td>
                            <td></td>
                            <td>0001067983</td>
                            <td>1839</td>
                        </tr>
                    </table>
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
                Content = new StringContent(htmlContent, Encoding.UTF8, "text/html")
            });

        // Act
        var result = await _sp500CacheService.GetSP500Symbols();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Count);
        Assert.Contains(result, s => s.Symbol == "AAPL" && s.Name == "Apple Inc." && s.Market == "NASDAQ" && s.Type == "stock");
        Assert.Contains(result, s => s.Symbol == "MSFT" && s.Name == "Microsoft Corporation" && s.Market == "NASDAQ" && s.Type == "stock");
        Assert.Contains(result, s => s.Symbol == "AMZN" && s.Name == "Amazon.com Inc." && s.Market == "NASDAQ" && s.Type == "stock");
        Assert.Contains(result, s => s.Symbol == "GOOGL" && s.Name == "Alphabet Inc. Class A" && s.Market == "NASDAQ" && s.Type == "stock");
        Assert.Contains(result, s => s.Symbol == "BRK.B" && s.Name == "Berkshire Hathaway Inc. Class B" && s.Market == "NYSE" && s.Type == "stock");
        Assert.True(File.Exists(_testCacheFilePath), "キャッシュファイルが作成されていません");

        // HttpClientが呼び出されたことを確認
        _httpMessageHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetSP500Symbols_WithValidCache_UsesCache()
    {
        // Arrange - 有効なキャッシュファイルを作成
        Directory.CreateDirectory(Path.GetDirectoryName(_testCacheFilePath)!);
        var json = JsonSerializer.Serialize(_testSymbols);
        await File.WriteAllTextAsync(_testCacheFilePath, json);
        File.SetLastWriteTime(_testCacheFilePath, DateTime.Now); // 最終更新時間を現在に設定

        // Act
        var result = await _sp500CacheService.GetSP500Symbols();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_testSymbols.Count, result.Count);
        for (int i = 0; i < _testSymbols.Count; i++)
        {
            Assert.Equal(_testSymbols[i].Symbol, result[i].Symbol);
            Assert.Equal(_testSymbols[i].Name, result[i].Name);
            Assert.Equal(_testSymbols[i].Market, result[i].Market);
            Assert.Equal(_testSymbols[i].Type, result[i].Type);
        }

        // HttpClientが呼び出されていないことを確認（キャッシュを使用）
        _httpMessageHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetSP500Symbols_WithExpiredCache_FetchesFromWikipedia()
    {
        // Arrange - 期限切れのキャッシュファイルを作成
        Directory.CreateDirectory(Path.GetDirectoryName(_testCacheFilePath)!);
        var json = JsonSerializer.Serialize(_testSymbols);
        await File.WriteAllTextAsync(_testCacheFilePath, json);
        File.SetLastWriteTime(_testCacheFilePath, DateTime.Now.AddDays(-2)); // 2日前の日付に設定

        // Wikipediaのモックレスポンスをセットアップ
        var htmlContent = @"
            <html>
                <body>
                    <table id='constituents'>
                        <tr>
                            <th>Symbol</th>
                            <th>Security</th>
                            <th>SEC filings</th>
                            <th>GICS Sector</th>
                            <th>GICS Sub-Industry</th>
                            <th>Headquarters Location</th>
                            <th>Date added</th>
                            <th>CIK</th>
                            <th>Founded</th>
                        </tr>
                        <tr>
                            <td>AAPL</td>
                            <td>Apple Inc.</td>
                            <td>reports</td>
                            <td>Information Technology</td>
                            <td>Consumer Electronics</td>
                            <td>Cupertino, California</td>
                            <td></td>
                            <td>0000320193</td>
                            <td>1976</td>
                        </tr>
                        <tr>
                            <td>MSFT</td>
                            <td>Microsoft Corporation</td>
                            <td>reports</td>
                            <td>Information Technology</td>
                            <td>Systems Software</td>
                            <td>Redmond, Washington</td>
                            <td></td>
                            <td>0000789019</td>
                            <td>1975</td>
                        </tr>
                        <tr>
                            <td>AMZN</td>
                            <td>Amazon.com Inc.</td>
                            <td>reports</td>
                            <td>Consumer Discretionary</td>
                            <td>Internet & Direct Marketing Retail</td>
                            <td>Seattle, Washington</td>
                            <td></td>
                            <td>0001018724</td>
                            <td>1994</td>
                        </tr>
                        <tr>
                            <td>GOOGL</td>
                            <td>Alphabet Inc. Class A</td>
                            <td>reports</td>
                            <td>Communication Services</td>
                            <td>Interactive Media & Services</td>
                            <td>Mountain View, California</td>
                            <td></td>
                            <td>0001652044</td>
                            <td>1998</td>
                        </tr>
                        <tr>
                            <td>BRK.B</td>
                            <td>Berkshire Hathaway Inc. Class B</td>
                            <td>reports</td>
                            <td>Financials</td>
                            <td>Multi-Sector Holdings</td>
                            <td>Omaha, Nebraska</td>
                            <td></td>
                            <td>0001067983</td>
                            <td>1839</td>
                        </tr>
                    </table>
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
                Content = new StringContent(htmlContent, Encoding.UTF8, "text/html")
            });

        // Act
        var result = await _sp500CacheService.GetSP500Symbols();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Count); // Wikipediaから取得した5銘柄

        // HttpClientが呼び出されたことを確認（キャッシュが期限切れ）
        _httpMessageHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ForceUpdateAsync_IgnoresCache_FetchesFromWikipedia()
    {
        // Arrange - 有効なキャッシュファイルを作成
        Directory.CreateDirectory(Path.GetDirectoryName(_testCacheFilePath)!);
        var json = JsonSerializer.Serialize(_testSymbols);
        await File.WriteAllTextAsync(_testCacheFilePath, json);
        File.SetLastWriteTime(_testCacheFilePath, DateTime.Now); // 最終更新時間を現在に設定

        // Wikipediaのモックレスポンスをセットアップ
        var htmlContent = @"
            <html>
                <body>
                    <table id='constituents'>
                        <tr>
                            <th>Symbol</th>
                            <th>Security</th>
                            <th>SEC filings</th>
                            <th>GICS Sector</th>
                            <th>GICS Sub-Industry</th>
                            <th>Headquarters Location</th>
                            <th>Date added</th>
                            <th>CIK</th>
                            <th>Founded</th>
                        </tr>
                        <tr>
                            <td>AAPL</td>
                            <td>Apple Inc.</td>
                            <td>reports</td>
                            <td>Information Technology</td>
                            <td>Consumer Electronics</td>
                            <td>Cupertino, California</td>
                            <td></td>
                            <td>0000320193</td>
                            <td>1976</td>
                        </tr>
                        <tr>
                            <td>MSFT</td>
                            <td>Microsoft Corporation</td>
                            <td>reports</td>
                            <td>Information Technology</td>
                            <td>Systems Software</td>
                            <td>Redmond, Washington</td>
                            <td></td>
                            <td>0000789019</td>
                            <td>1975</td>
                        </tr>
                        <tr>
                            <td>AMZN</td>
                            <td>Amazon.com Inc.</td>
                            <td>reports</td>
                            <td>Consumer Discretionary</td>
                            <td>Internet & Direct Marketing Retail</td>
                            <td>Seattle, Washington</td>
                            <td></td>
                            <td>0001018724</td>
                            <td>1994</td>
                        </tr>
                        <tr>
                            <td>GOOGL</td>
                            <td>Alphabet Inc. Class A</td>
                            <td>reports</td>
                            <td>Communication Services</td>
                            <td>Interactive Media & Services</td>
                            <td>Mountain View, California</td>
                            <td></td>
                            <td>0001652044</td>
                            <td>1998</td>
                        </tr>
                        <tr>
                            <td>BRK.B</td>
                            <td>Berkshire Hathaway Inc. Class B</td>
                            <td>reports</td>
                            <td>Financials</td>
                            <td>Multi-Sector Holdings</td>
                            <td>Omaha, Nebraska</td>
                            <td></td>
                            <td>0001067983</td>
                            <td>1839</td>
                        </tr>
                    </table>
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
                Content = new StringContent(htmlContent, Encoding.UTF8, "text/html")
            });

        // Act
        await _sp500CacheService.ForceUpdateAsync();
        var result = await _sp500CacheService.GetSP500Symbols(); // キャッシュが更新されているはず

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Count); // Wikipediaから取得した5銘柄

        // HttpClientが呼び出されたことを確認（強制更新）
        _httpMessageHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetSymbolsAsync_ReturnsSymbolsOnly()
    {
        // Arrange - 有効なキャッシュファイルを作成
        Directory.CreateDirectory(Path.GetDirectoryName(_testCacheFilePath)!);
        var json = JsonSerializer.Serialize(_testSymbols);
        await File.WriteAllTextAsync(_testCacheFilePath, json);
        File.SetLastWriteTime(_testCacheFilePath, DateTime.Now); // 最終更新時間を現在に設定

        // Act
        var result = await _sp500CacheService.GetSymbolsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_testSymbols.Count, result.Count);
        Assert.Contains("AAPL", result);
        Assert.Contains("MSFT", result);
        Assert.Contains("AMZN", result);
        Assert.Contains("GOOGL", result);
        Assert.Contains("BRK.B", result);
    }

    [Fact]
    public async Task GetSP500Symbols_HttpRequestFails_ThrowsException()
    {
        // Arrange - HttpClientがエラーを返すように設定
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("Error", Encoding.UTF8, "text/plain")
            });

        // キャッシュファイルが存在しないことを確認
        if (File.Exists(_testCacheFilePath))
        {
            File.Delete(_testCacheFilePath);
        }

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => _sp500CacheService.GetSP500Symbols());
    }

    [Fact]
    public async Task GetSP500Symbols_InvalidHtmlResponse_ThrowsException()
    {
        // Arrange - 無効なHTMLを返すように設定
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("<html><body>Invalid HTML without table</body></html>", Encoding.UTF8, "text/html")
            });

        // キャッシュファイルが存在しないことを確認
        if (File.Exists(_testCacheFilePath))
        {
            File.Delete(_testCacheFilePath);
        }

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => _sp500CacheService.GetSP500Symbols());
    }

    [Fact]
    public async Task GetSP500Symbols_InvalidCacheFile_FetchesFromWikipedia()
    {
        // Arrange - 無効なJSONを含むキャッシュファイルを作成
        Directory.CreateDirectory(Path.GetDirectoryName(_testCacheFilePath)!);
        await File.WriteAllTextAsync(_testCacheFilePath, "Invalid JSON");
        File.SetLastWriteTime(_testCacheFilePath, DateTime.Now); // 最終更新時間を現在に設定

        // Wikipediaのモックレスポンスをセットアップ
        var htmlContent = @"
            <html>
                <body>
                    <table id='constituents'>
                        <tr>
                            <th>Symbol</th>
                            <th>Security</th>
                            <th>SEC filings</th>
                            <th>GICS Sector</th>
                            <th>GICS Sub-Industry</th>
                            <th>Headquarters Location</th>
                            <th>Date added</th>
                            <th>CIK</th>
                            <th>Founded</th>
                        </tr>
                        <tr>
                            <td>AAPL</td>
                            <td>Apple Inc.</td>
                            <td>reports</td>
                            <td>Information Technology</td>
                            <td>Consumer Electronics</td>
                            <td>Cupertino, California</td>
                            <td></td>
                            <td>0000320193</td>
                            <td>1976</td>
                        </tr>
                        <tr>
                            <td>MSFT</td>
                            <td>Microsoft Corporation</td>
                            <td>reports</td>
                            <td>Information Technology</td>
                            <td>Systems Software</td>
                            <td>Redmond, Washington</td>
                            <td></td>
                            <td>0000789019</td>
                            <td>1975</td>
                        </tr>
                        <tr>
                            <td>AMZN</td>
                            <td>Amazon.com Inc.</td>
                            <td>reports</td>
                            <td>Consumer Discretionary</td>
                            <td>Internet & Direct Marketing Retail</td>
                            <td>Seattle, Washington</td>
                            <td></td>
                            <td>0001018724</td>
                            <td>1994</td>
                        </tr>
                        <tr>
                            <td>GOOGL</td>
                            <td>Alphabet Inc. Class A</td>
                            <td>reports</td>
                            <td>Communication Services</td>
                            <td>Interactive Media & Services</td>
                            <td>Mountain View, California</td>
                            <td></td>
                            <td>0001652044</td>
                            <td>1998</td>
                        </tr>
                        <tr>
                            <td>BRK.B</td>
                            <td>Berkshire Hathaway Inc. Class B</td>
                            <td>reports</td>
                            <td>Financials</td>
                            <td>Multi-Sector Holdings</td>
                            <td>Omaha, Nebraska</td>
                            <td></td>
                            <td>0001067983</td>
                            <td>1839</td>
                        </tr>
                    </table>
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
                Content = new StringContent(htmlContent, Encoding.UTF8, "text/html")
            });

        // Act
        var result = await _sp500CacheService.GetSP500Symbols();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Count); // Wikipediaから取得した5銘柄

        // HttpClientが呼び出されたことを確認（キャッシュが無効）
        _httpMessageHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>());
    }
}
