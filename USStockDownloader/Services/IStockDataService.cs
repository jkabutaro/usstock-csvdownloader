using USStockDownloader.Models;

namespace USStockDownloader.Services;

public interface IStockDataService
{
    Task<List<StockData>> GetStockDataAsync(string symbol, DateTime startDate, DateTime endDate);
    Task SaveToCsvAsync(string symbol, List<StockData> data, string outputPath);
}
