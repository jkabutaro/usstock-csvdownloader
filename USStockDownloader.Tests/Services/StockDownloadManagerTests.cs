using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Moq;
using USStockDownloader.Exceptions;
using USStockDownloader.Models;
using USStockDownloader.Services;
using Xunit;

namespace USStockDownloader.Tests.Services;

public class StockDownloadManagerTests
{
    private readonly Mock<IStockDataService> _stockDataServiceMock;
    private readonly Mock<ILogger<StockDownloadManager>> _loggerMock;
    private readonly StockDownloadManager _stockDownloadManager;
    private readonly string _testOutputDir;

    public StockDownloadManagerTests()
    {
        _stockDataServiceMock = new Mock<IStockDataService>();
        _loggerMock = new Mock<ILogger<StockDownloadManager>>();
        _stockDownloadManager = new StockDownloadManager(_stockDataServiceMock.Object, _loggerMock.Object);
        
        // テスト用の一時ディレクトリを作成
        _testOutputDir = Path.Combine(Path.GetTempPath(), "USStockDownloaderTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testOutputDir);
    }

    [Fact]
    public async Task DownloadStockDataAsync_SuccessfulDownload_CreatesCSVFiles()
    {
        // Arrange
        var symbols = new List<string> { "AAPL", "MSFT", "GOOGL" };
        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 1, 31);

        foreach (var symbol in symbols)
        {
            var stockDataList = CreateTestStockData(symbol, 5);
            _stockDataServiceMock
                .Setup(s => s.GetStockDataAsync(symbol, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(stockDataList);
        }

        // Act
        await _stockDownloadManager.DownloadStockDataAsync(symbols, _testOutputDir, startDate, endDate);

        // Assert
        foreach (var symbol in symbols)
        {
            var csvFilePath = Path.Combine(_testOutputDir, $"{symbol}.csv");
            Assert.True(File.Exists(csvFilePath), $"CSV file for {symbol} should exist");
            
            // CSVファイルの内容を確認
            var fileContent = await File.ReadAllTextAsync(csvFilePath);
            Assert.Contains("Date,Symbol,Open,High,Low,Close,Volume", fileContent);
            Assert.Contains(symbol, fileContent);
        }
    }

    [Fact]
    public async Task DownloadStockDataAsync_PartialFailure_CreatesFailureReport()
    {
        // Arrange
        var successSymbols = new List<string> { "AAPL", "MSFT" };
        var failedSymbols = new List<string> { "BRK.B", "ETR" };
        var allSymbols = successSymbols.Concat(failedSymbols).ToList();
        
        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 1, 31);

        // 成功するシンボルの設定
        foreach (var symbol in successSymbols)
        {
            var stockDataList = CreateTestStockData(symbol, 5);
            _stockDataServiceMock
                .Setup(s => s.GetStockDataAsync(symbol, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(stockDataList);
        }

        // 失敗するシンボルの設定
        foreach (var symbol in failedSymbols)
        {
            _stockDataServiceMock
                .Setup(s => s.GetStockDataAsync(symbol, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ThrowsAsync(new DataParsingException($"Failed to parse data for {symbol}"));
        }

        // Act
        await _stockDownloadManager.DownloadStockDataAsync(allSymbols, _testOutputDir, startDate, endDate);

        // Assert
        // 成功したシンボルのCSVファイルが存在することを確認
        foreach (var symbol in successSymbols)
        {
            var csvFilePath = Path.Combine(_testOutputDir, $"{symbol}.csv");
            Assert.True(File.Exists(csvFilePath), $"CSV file for {symbol} should exist");
        }

        // 失敗したシンボルのCSVファイルが存在しないことを確認
        foreach (var symbol in failedSymbols)
        {
            var csvFilePath = Path.Combine(_testOutputDir, $"{symbol}.csv");
            Assert.False(File.Exists(csvFilePath), $"CSV file for {symbol} should not exist");
        }

        // 失敗レポートが生成されていることを確認
        var reportPath = Path.Combine(_testOutputDir, "failed_symbols_report.txt");
        Assert.True(File.Exists(reportPath), "Failure report should exist");
        
        // レポートの内容を確認
        var reportContent = await File.ReadAllTextAsync(reportPath);
        Assert.Contains("Stock Download Failure Report", reportContent);
        Assert.Contains("DataParsingException", reportContent);
        foreach (var symbol in failedSymbols)
        {
            Assert.Contains(symbol, reportContent);
        }
    }

    [Fact]
    public async Task DownloadStockDataAsync_RateLimitException_RetrySucceeds()
    {
        // Arrange
        var symbol = "AAPL";
        var symbols = new List<string> { symbol };
        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 1, 31);
        var stockDataList = CreateTestStockData(symbol, 5);

        // 最初の呼び出しでレート制限例外をスロー、2回目の呼び出しで成功
        _stockDataServiceMock
            .SetupSequence(s => s.GetStockDataAsync(symbol, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ThrowsAsync(new RateLimitException($"Rate limit exceeded for {symbol}"))
            .ReturnsAsync(stockDataList);

        // Act
        await _stockDownloadManager.DownloadStockDataAsync(symbols, _testOutputDir, startDate, endDate);

        // Assert
        var csvFilePath = Path.Combine(_testOutputDir, $"{symbol}.csv");
        Assert.True(File.Exists(csvFilePath), $"CSV file for {symbol} should exist after retry");
        
        // 2回呼び出されたことを確認
        _stockDataServiceMock.Verify(
            s => s.GetStockDataAsync(symbol, It.IsAny<DateTime>(), It.IsAny<DateTime>()), 
            Times.Exactly(2));
    }

    [Fact]
    public async Task DownloadStockDataAsync_EmptyData_ReportsFailure()
    {
        // Arrange
        var symbol = "EMPTY";
        var symbols = new List<string> { symbol };
        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 1, 31);

        // 空のデータリストを返す
        _stockDataServiceMock
            .Setup(s => s.GetStockDataAsync(symbol, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<StockData>());

        // Act
        await _stockDownloadManager.DownloadStockDataAsync(symbols, _testOutputDir, startDate, endDate);

        // Assert
        var csvFilePath = Path.Combine(_testOutputDir, $"{symbol}.csv");
        Assert.False(File.Exists(csvFilePath), $"CSV file for {symbol} should not exist");
        
        // 失敗レポートが生成されていることを確認
        var reportPath = Path.Combine(_testOutputDir, "failed_symbols_report.txt");
        Assert.True(File.Exists(reportPath), "Failure report should exist");
    }

    [Fact]
    public async Task DownloadStockDataAsync_MultipleSymbols_RespectsConcurrencyLimit()
    {
        // Arrange
        var symbols = new List<string>();
        for (int i = 1; i <= 10; i++)
        {
            symbols.Add($"SYMBOL{i}");
        }

        var startDate = new DateTime(2023, 1, 1);
        var endDate = new DateTime(2023, 1, 31);
        
        // 各シンボルの処理に時間がかかるように設定
        foreach (var symbol in symbols)
        {
            var stockDataList = CreateTestStockData(symbol, 5);
            _stockDataServiceMock
                .Setup(s => s.GetStockDataAsync(symbol, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .Returns<string, DateTime, DateTime>(async (sym, _, __) => 
                {
                    // 処理時間をシミュレート
                    await Task.Delay(100);
                    return stockDataList;
                });
        }

        // 同時実行数をカウントするための変数
        var concurrentExecutions = 0;
        var maxConcurrentExecutions = 0;
        var executionLock = new object();

        // 同時実行数を追跡するためのモック設定を上書き
        _stockDataServiceMock
            .Setup(s => s.GetStockDataAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .Returns<string, DateTime, DateTime>(async (symbol, start, end) => 
            {
                lock (executionLock)
                {
                    concurrentExecutions++;
                    maxConcurrentExecutions = Math.Max(maxConcurrentExecutions, concurrentExecutions);
                }

                try
                {
                    // 処理時間をシミュレート
                    await Task.Delay(100);
                    return CreateTestStockData(symbol, 5);
                }
                finally
                {
                    lock (executionLock)
                    {
                        concurrentExecutions--;
                    }
                }
            });

        // Act
        await _stockDownloadManager.DownloadStockDataAsync(symbols, _testOutputDir, startDate, endDate);

        // Assert
        // 最大同時実行数が3（MAX_CONCURRENT_DOWNLOADS）以下であることを確認
        Assert.True(maxConcurrentExecutions <= 3, $"Max concurrent executions should be <= 3, but was {maxConcurrentExecutions}");
        
        // すべてのCSVファイルが生成されていることを確認
        foreach (var symbol in symbols)
        {
            var csvFilePath = Path.Combine(_testOutputDir, $"{symbol}.csv");
            Assert.True(File.Exists(csvFilePath), $"CSV file for {symbol} should exist");
        }
    }

    private List<StockData> CreateTestStockData(string symbol, int count)
    {
        var result = new List<StockData>();
        var baseDate = new DateTime(2023, 1, 1);
        
        for (int i = 0; i < count; i++)
        {
            var date = baseDate.AddDays(i);
            result.Add(new StockData
            {
                Symbol = symbol,
                DateTime = date,
                Date = date.Year * 10000 + date.Month * 100 + date.Day,
                Open = 100 + i,
                High = 105 + i,
                Low = 95 + i,
                Close = 102 + i,
                Volume = 1000000 + i * 10000
            });
        }
        
        return result;
    }
}
