using Microsoft.Extensions.Logging;
using Moq;
using USStockDownloader.Models;
using USStockDownloader.Services;
using Xunit;

namespace USStockDownloader.Tests.Services;

public class IndexListServiceTests
{
    private readonly Mock<ILogger<IndexListService>> _loggerMock;
    private readonly Mock<IndexCacheService> _indexCacheServiceMock;
    private readonly IndexListService _indexListService;
    private readonly List<StockSymbol> _testIndices;

    public IndexListServiceTests()
    {
        _loggerMock = new Mock<ILogger<IndexListService>>();
        _indexCacheServiceMock = new Mock<IndexCacheService>(MockBehavior.Loose, null, null);
        _indexListService = new IndexListService(_loggerMock.Object, _indexCacheServiceMock.Object);

        // テスト用の指数データを準備
        _testIndices = new List<StockSymbol>
        {
            new StockSymbol { Symbol = "^DJI", Name = "Dow Jones Industrial Average", Market = "NYSE", Type = "index" },
            new StockSymbol { Symbol = "^GSPC", Name = "S&P 500", Market = "NYSE", Type = "index" },
            new StockSymbol { Symbol = "^IXIC", Name = "NASDAQ Composite", Market = "NASDAQ", Type = "index" },
            new StockSymbol { Symbol = "^N225", Name = "Nikkei 225", Market = "TSE", Type = "index" },
            new StockSymbol { Symbol = "^HSI", Name = "Hang Seng Index", Market = "HKEX", Type = "index" }
        };
    }

    [Fact]
    public async Task GetMajorIndicesAsync_ReturnsIndicesList()
    {
        // Arrange
        _indexCacheServiceMock
            .Setup(s => s.GetIndicesAsync())
            .ReturnsAsync(_testIndices);

        // Act
        var result = await _indexListService.GetMajorIndicesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_testIndices.Count, result.Count);
        Assert.Equal(_testIndices[0].Symbol, result[0].Symbol);
        Assert.Equal(_testIndices[1].Symbol, result[1].Symbol);
        _indexCacheServiceMock.Verify(s => s.GetIndicesAsync(), Times.Once);
    }

    [Fact]
    public async Task ForceUpdateMajorIndicesAsync_CallsForceUpdateAndReturnsIndicesList()
    {
        // Arrange
        _indexCacheServiceMock
            .Setup(s => s.ForceUpdateAsync())
            .ReturnsAsync(_testIndices);

        // Act
        var result = await _indexListService.ForceUpdateMajorIndicesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_testIndices.Count, result.Count);
        Assert.Equal(_testIndices[0].Symbol, result[0].Symbol);
        Assert.Equal(_testIndices[1].Symbol, result[1].Symbol);
        _indexCacheServiceMock.Verify(s => s.ForceUpdateAsync(), Times.Once);
    }

    [Fact]
    public void GetMajorIndices_CallsAsyncMethodAndReturnsIndicesList()
    {
        // Arrange
        _indexCacheServiceMock
            .Setup(s => s.GetIndicesAsync())
            .ReturnsAsync(_testIndices);

        // Act
        var result = _indexListService.GetMajorIndices();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_testIndices.Count, result.Count);
        Assert.Equal(_testIndices[0].Symbol, result[0].Symbol);
        Assert.Equal(_testIndices[1].Symbol, result[1].Symbol);
        _indexCacheServiceMock.Verify(s => s.GetIndicesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetMajorIndicesAsync_EmptyResult_ReturnsEmptyList()
    {
        // Arrange
        _indexCacheServiceMock
            .Setup(s => s.GetIndicesAsync())
            .ReturnsAsync(new List<StockSymbol>());

        // Act
        var result = await _indexListService.GetMajorIndicesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
        _indexCacheServiceMock.Verify(s => s.GetIndicesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetMajorIndicesAsync_ExceptionInCacheService_PropagatesException()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Test exception");
        _indexCacheServiceMock
            .Setup(s => s.GetIndicesAsync())
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _indexListService.GetMajorIndicesAsync());
        
        Assert.Equal(expectedException.Message, exception.Message);
        _indexCacheServiceMock.Verify(s => s.GetIndicesAsync(), Times.Once);
    }
}
