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

public class NYDCacheServiceTests : IDisposable
{
    private readonly Mock<ILogger<NYDCacheService>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly NYDCacheService _nydCacheService;
    private readonly string _testCacheDir;
    private readonly string _testCacheFilePath;
    private readonly TimeSpan _testCacheExpiry = TimeSpan.FromHours(1);
    private readonly List<StockSymbol> _testSymbols;

    public NYDCacheServiceTests()
    {
        _loggerMock = new Mock<ILogger<NYDCacheService>>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        
        // テスト用の一時ディレクトリを作成
        _testCacheDir = Path.Combine(Path.GetTempPath(), "USStockDownloader_Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testCacheDir);
        _testCacheFilePath = Path.Combine(_testCacheDir, "nyd_test_cache.json");
        
        // テスト用の銘柄データを準備
        _testSymbols = new List<StockSymbol>
        {
            new StockSymbol { Symbol = "AAPL", Name = "Apple Inc.", Market = "NASDAQ", Type = "stock" },
            new StockSymbol { Symbol = "MSFT", Name = "Microsoft Corporation", Market = "NASDAQ", Type = "stock" },
            new StockSymbol { Symbol = "KO", Name = "The Coca-Cola Company", Market = "NYSE", Type = "stock" },
            new StockSymbol { Symbol = "JPM", Name = "JPMorgan Chase & Co.", Market = "NYSE", Type = "stock" },
            new StockSymbol { Symbol = "AMGN", Name = "Amgen Inc.", Market = "NASDAQ", Type = "stock" }
        };

        // テスト用のキャッシュ期限を短く設定
        _nydCacheService = new NYDCacheService(_httpClient, _loggerMock.Object, _testCacheFilePath, _testCacheExpiry);
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
    public async Task GetNYDSymbols_NoCacheFile_FetchesFromWikipedia()
    {
        // Arrange - キャッシュファイルが存在しないことを確認
        if (File.Exists(_testCacheFilePath))
        {
            File.Delete(_testCacheFilePath);
        }

        // Act
        var result = await _nydCacheService.GetNYDSymbols();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Count);
        Assert.Contains(result, s => s.Symbol == "AAPL" && s.Name == "Apple Inc.");
        Assert.Contains(result, s => s.Symbol == "MSFT" && s.Name == "Microsoft Corporation");
        Assert.Contains(result, s => s.Symbol == "KO" && s.Name == "The Coca-Cola Company");
        Assert.Contains(result, s => s.Symbol == "JPM" && s.Name == "JPMorgan Chase & Co.");
        Assert.Contains(result, s => s.Symbol == "AMGN" && s.Name == "Amgen Inc.");
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
    public async Task GetNYDSymbols_WithValidCache_UsesCache()
    {
        // Arrange - 有効なキャッシュファイルを作成
        Directory.CreateDirectory(Path.GetDirectoryName(_testCacheFilePath)!);
        var json = JsonSerializer.Serialize(_testSymbols);
        await File.WriteAllTextAsync(_testCacheFilePath, json);

        // Act
        var result = await _nydCacheService.GetNYDSymbols();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_testSymbols.Count, result.Count);
        for (int i = 0; i < _testSymbols.Count; i++)
        {
            Assert.Equal(_testSymbols[i].Symbol, result[i].Symbol);
            Assert.Equal(_testSymbols[i].Name, result[i].Name);
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
    public async Task GetSymbolsAsync_ReturnsSymbolsOnly()
    {
        // Arrange - 有効なキャッシュファイルを作成
        Directory.CreateDirectory(Path.GetDirectoryName(_testCacheFilePath)!);
        var json = JsonSerializer.Serialize(_testSymbols);
        await File.WriteAllTextAsync(_testCacheFilePath, json);

        // Act
        var result = await _nydCacheService.GetSymbolsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_testSymbols.Count, result.Count);
        Assert.Contains("AAPL", result);
        Assert.Contains("MSFT", result);
        Assert.Contains("KO", result);
        Assert.Contains("JPM", result);
        Assert.Contains("AMGN", result);
    }

    [Fact]
    public async Task ForceUpdateAsync_IgnoresCache_FetchesFromWikipedia()
    {
        // Arrange - 有効なキャッシュファイルを作成
        Directory.CreateDirectory(Path.GetDirectoryName(_testCacheFilePath)!);
        var json = JsonSerializer.Serialize(_testSymbols);
        await File.WriteAllTextAsync(_testCacheFilePath, json);

        // Act
        await _nydCacheService.ForceUpdateAsync();
        var result = await _nydCacheService.GetNYDSymbols(); // キャッシュが更新されているはず

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
    public async Task GetNYDSymbols_InvalidCacheFile_FetchesFromWikipedia()
    {
        // Arrange - 無効なJSONを含むキャッシュファイルを作成
        Directory.CreateDirectory(Path.GetDirectoryName(_testCacheFilePath)!);
        await File.WriteAllTextAsync(_testCacheFilePath, "Invalid JSON");

        // Act
        var result = await _nydCacheService.GetNYDSymbols();

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

    [Fact]
    public async Task GetNYDSymbols_HttpRequestFails_ThrowsException()
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
        await Assert.ThrowsAsync<HttpRequestException>(() => _nydCacheService.GetNYDSymbols());
    }

    [Fact]
    public async Task GetNYDSymbols_InvalidHtmlResponse_ThrowsException()
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
        await Assert.ThrowsAsync<Exception>(() => _nydCacheService.GetNYDSymbols());
    }
}
