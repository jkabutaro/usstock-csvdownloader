using System.Globalization;
using System.Text;
using CsvHelper;
using Microsoft.Extensions.Logging;
using Moq;
using USStockDownloader.Models;
using USStockDownloader.Services;
using Xunit;

namespace USStockDownloader.Tests.Services;

public class SymbolListExportServiceTests
{
    private readonly Mock<ILogger<SymbolListExportService>> _loggerMock;
    private readonly Mock<SP500CacheService> _sp500CacheServiceMock;
    private readonly Mock<NYDCacheService> _nydCacheServiceMock;
    private readonly Mock<IndexListService> _indexListServiceMock;
    private readonly Mock<BuffettCacheService> _buffettCacheServiceMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<SBIStockFetcher> _sbiStockFetcherMock;
    private readonly SymbolListExportService _symbolListExportService;
    private readonly string _testOutputDir;

    public SymbolListExportServiceTests()
    {
        _loggerMock = new Mock<ILogger<SymbolListExportService>>();
        _sp500CacheServiceMock = new Mock<SP500CacheService>(MockBehavior.Loose, null, null);
        _nydCacheServiceMock = new Mock<NYDCacheService>(MockBehavior.Loose, null, null);
        _indexListServiceMock = new Mock<IndexListService>(MockBehavior.Loose, null, null);
        _buffettCacheServiceMock = new Mock<BuffettCacheService>(MockBehavior.Loose, null, null);
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _sbiStockFetcherMock = new Mock<SBIStockFetcher>(MockBehavior.Loose, null, null);

        _symbolListExportService = new SymbolListExportService(
            _loggerMock.Object,
            _sp500CacheServiceMock.Object,
            _nydCacheServiceMock.Object,
            _indexListServiceMock.Object,
            _buffettCacheServiceMock.Object,
            _httpClientFactoryMock.Object,
            _loggerFactoryMock.Object,
            _sbiStockFetcherMock.Object
        );
        
        // テスト用の一時ディレクトリを作成
        _testOutputDir = Path.Combine(Path.GetTempPath(), "USStockDownloaderTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testOutputDir);
        
        // エンコーディングプロバイダーを登録（Shift-JIS対応）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public async Task ExportSymbolListToCsv_ValidSymbols_CreatesCSVFile()
    {
        // Arrange
        var testSymbols = new List<StockSymbol>
        {
            new StockSymbol { Symbol = "AAPL", Name = "Apple Inc.", Market = "NASDAQ", Type = "stock" },
            new StockSymbol { Symbol = "MSFT", Name = "Microsoft Corporation", Market = "NASDAQ", Type = "stock" },
            new StockSymbol { Symbol = "GOOGL", Name = "Alphabet Inc.", Market = "NASDAQ", Type = "stock" }
        };
        
        _sp500CacheServiceMock
            .Setup(s => s.GetSP500Symbols())
            .ReturnsAsync(testSymbols);
        
        var csvPath = Path.Combine(_testOutputDir, "test_sp500.csv");
        
        // Act
        await _symbolListExportService.ExportSymbolListToCsv(csvPath);
        
        // Assert
        Assert.True(File.Exists(csvPath), "CSV file should exist");
        
        // CSVファイルの内容を確認
        using (var reader = new StreamReader(csvPath, Encoding.GetEncoding(932)))
        {
            var content = await reader.ReadToEndAsync();
            Assert.Contains("code,name,market,type", content);
            Assert.Contains("AAPL,Apple Inc.,NASDAQ,stock", content);
            Assert.Contains("MSFT,Microsoft Corporation,NASDAQ,stock", content);
            Assert.Contains("GOOGL,Alphabet Inc.,NASDAQ,stock", content);
        }
    }

    [Fact]
    public async Task ExportNYDListToCsvAsync_ValidSymbols_CreatesCSVFileWithJapaneseNames()
    {
        // Arrange
        var testSymbols = new List<string>
        {
            "AAPL",
            "MSFT",
            "KO"
        };
        
        _nydCacheServiceMock
            .Setup(s => s.GetSymbolsAsync())
            .Returns(Task.FromResult(testSymbols));
        
        var csvPath = Path.Combine(_testOutputDir, "test_nyd.csv");
        
        // Act
        await _symbolListExportService.ExportNYDListToCsvAsync(csvPath);
        
        // Assert
        Assert.True(File.Exists(csvPath), "CSV file should exist");
        
        // CSVファイルの内容を確認
        using (var reader = new StreamReader(csvPath, Encoding.GetEncoding(932)))
        {
            var content = await reader.ReadToEndAsync();
            Assert.Contains("code,name,market,type", content);
            Assert.Contains("AAPL", content);
            Assert.Contains("アップル", content); // 日本語名が含まれていることを確認
            Assert.Contains("MSFT", content);
            Assert.Contains("マイクロソフト", content); // 日本語名が含まれていることを確認
            Assert.Contains("KO", content);
            Assert.Contains("コカ・コーラ", content); // 日本語名が含まれていることを確認
            Assert.Contains("NASDAQ", content); // マーケット情報が含まれていることを確認
            Assert.Contains("NYSE", content); // マーケット情報が含まれていることを確認
            Assert.Contains("stock", content); // タイプ情報が含まれていることを確認
        }
    }

    [Fact]
    public async Task ExportNYDListToCsvAsync_ForceUpdate_CallsForceUpdateAsync()
    {
        // Arrange
        var testSymbols = new List<string> { "AAPL", "MSFT" };
        
        _nydCacheServiceMock
            .Setup(s => s.ForceUpdateAsync())
            .Returns(Task.CompletedTask);
        
        _nydCacheServiceMock
            .Setup(s => s.GetSymbolsAsync())
            .Returns(Task.FromResult(testSymbols));
        
        var csvPath = Path.Combine(_testOutputDir, "test_nyd_force.csv");
        
        // Act
        await _symbolListExportService.ExportNYDListToCsvAsync(csvPath, true);
        
        // Assert
        _nydCacheServiceMock.Verify(s => s.ForceUpdateAsync(), Times.Once);
        Assert.True(File.Exists(csvPath), "CSV file should exist");
    }

    [Fact]
    public async Task ExportIndexListToCsvAsync_ValidIndices_CreatesCSVFile()
    {
        // Arrange
        var testIndices = new List<StockSymbol>
        {
            new StockSymbol { Symbol = "^DJI", Name = "Dow Jones Industrial Average", Market = "NYSE", Type = "index" },
            new StockSymbol { Symbol = "^GSPC", Name = "S&P 500", Market = "NYSE", Type = "index" },
            new StockSymbol { Symbol = "^IXIC", Name = "NASDAQ Composite", Market = "NASDAQ", Type = "index" }
        };
        
        _indexListServiceMock
            .Setup(s => s.GetMajorIndicesAsync())
            .Returns(Task.FromResult(testIndices));
        
        var csvPath = Path.Combine(_testOutputDir, "test_indices.csv");
        
        // Act
        await _symbolListExportService.ExportIndexListToCsvAsync(csvPath);
        
        // Assert
        Assert.True(File.Exists(csvPath), "CSV file should exist");
        
        // CSVファイルの内容を確認
        using (var reader = new StreamReader(csvPath, Encoding.GetEncoding(932)))
        {
            var content = await reader.ReadToEndAsync();
            Assert.Contains("code,name,market,type", content);
            Assert.Contains("^DJI,Dow Jones Industrial Average,NYSE,index", content);
            Assert.Contains("^GSPC,S&P 500,NYSE,index", content);
            Assert.Contains("^IXIC,NASDAQ Composite,NASDAQ,index", content);
        }
    }

    [Fact]
    public async Task ExportBuffettListToCsvAsync_ValidSymbols_CreatesCSVFile()
    {
        // Arrange
        var testSymbols = new List<StockSymbol>
        {
            new StockSymbol { Symbol = "AAPL", Name = "Apple Inc.", Market = "NASDAQ", Type = "stock" },
            new StockSymbol { Symbol = "KO", Name = "The Coca-Cola Company", Market = "NYSE", Type = "stock" },
            new StockSymbol { Symbol = "BAC", Name = "Bank of America Corp", Market = "NYSE", Type = "stock" }
        };
        
        _buffettCacheServiceMock
            .Setup(s => s.GetSymbolsAsync())
            .Returns(Task.FromResult(testSymbols));
        
        var csvPath = Path.Combine(_testOutputDir, "test_buffett.csv");
        
        // Act
        await _symbolListExportService.ExportBuffettListToCsvAsync(csvPath);
        
        // Assert
        Assert.True(File.Exists(csvPath), "CSV file should exist");
        
        // CSVファイルの内容を確認
        using (var reader = new StreamReader(csvPath, Encoding.GetEncoding(932)))
        {
            var content = await reader.ReadToEndAsync();
            Assert.Contains("code,name,market,type", content);
            Assert.Contains("AAPL", content);
            Assert.Contains("KO", content);
            Assert.Contains("BAC", content);
        }
    }

    [Fact]
    public async Task ExportBuffettListToCsvAsync_ForceUpdate_CallsForceUpdateAsync()
    {
        // Arrange
        var testSymbols = new List<StockSymbol> { new StockSymbol { Symbol = "AAPL" } };
        
        _buffettCacheServiceMock
            .Setup(s => s.ForceUpdateAsync())
            .Returns(Task.CompletedTask);
        
        _buffettCacheServiceMock
            .Setup(s => s.GetSymbolsAsync())
            .Returns(Task.FromResult(testSymbols));
        
        var csvPath = Path.Combine(_testOutputDir, "test_buffett_force.csv");
        
        // Act
        await _symbolListExportService.ExportBuffettListToCsvAsync(csvPath, true);
        
        // Assert
        _buffettCacheServiceMock.Verify(s => s.ForceUpdateAsync(), Times.Once);
        Assert.True(File.Exists(csvPath), "CSV file should exist");
    }

    [Fact]
    public async Task ExportSBIListToCsvAsync_ValidSymbols_CreatesCSVFile()
    {
        // Arrange
        var testSymbols = new List<StockSymbol>
        {
            new StockSymbol { Symbol = "AAPL", Name = "Apple Inc.", Market = "NASDAQ", Type = "stock" },
            new StockSymbol { Symbol = "MSFT", Name = "Microsoft Corporation", Market = "NASDAQ", Type = "stock" }
        };
        
        _sbiStockFetcherMock
            .Setup(s => s.FetchStockSymbolsAsync())
            .Returns(Task.FromResult(testSymbols));
        
        var csvPath = Path.Combine(_testOutputDir, "test_sbi.csv");
        
        // Act
        await _symbolListExportService.ExportSBIListToCsvAsync(csvPath);
        
        // Assert
        Assert.True(File.Exists(csvPath), "CSV file should exist");
        
        // CSVファイルの内容を確認
        using (var reader = new StreamReader(csvPath, Encoding.GetEncoding(932)))
        {
            var content = await reader.ReadToEndAsync();
            Assert.Contains("code,name,market,type", content);
            Assert.Contains("AAPL,Apple Inc.,NASDAQ,stock", content);
            Assert.Contains("MSFT,Microsoft Corporation,NASDAQ,stock", content);
        }
    }

    [Fact]
    public async Task ExportSBIListToCsvAsync_CallsFetchStockSymbolsAsync()
    {
        // Arrange
        var testSymbols = new List<StockSymbol> { new StockSymbol { Symbol = "AAPL" } };
        
        _sbiStockFetcherMock
            .Setup(s => s.FetchStockSymbolsAsync())
            .Returns(Task.FromResult(testSymbols));
        
        var csvPath = Path.Combine(_testOutputDir, "test_sbi_force.csv");
        
        // Act
        await _symbolListExportService.ExportSBIListToCsvAsync(csvPath);
        
        // Assert
        _sbiStockFetcherMock.Verify(s => s.FetchStockSymbolsAsync(), Times.Once);
        Assert.True(File.Exists(csvPath), "CSV file should exist");
    }

    [Fact]
    public async Task ExportSymbolListToCsv_InvalidPath_ThrowsException()
    {
        // Arrange
        var testSymbols = new List<StockSymbol> { new StockSymbol { Symbol = "AAPL" } };
        
        _sp500CacheServiceMock
            .Setup(s => s.GetSP500Symbols())
            .Returns(Task.FromResult(testSymbols));
        
        var invalidPath = Path.Combine("Z:", "invalid", "path", "test.csv");
        
        // Act & Assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => 
            _symbolListExportService.ExportSymbolListToCsv(invalidPath));
    }
}
