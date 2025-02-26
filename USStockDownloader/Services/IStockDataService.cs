using USStockDownloader.Models;

namespace USStockDownloader.Services;

public interface IStockDataService
{
    Task<List<StockData>> GetStockDataAsync(string symbol, DateTime startDate, DateTime endDate);
}
