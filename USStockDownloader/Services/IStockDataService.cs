using USStockDownloader.Models;

namespace USStockDownloader.Services;

public interface IStockDataService
{
    Task<List<StockData>> GetStockDataAsync(string symbol, DateTime startDate, DateTime endDate);
    
    /// <summary>
    /// 指定された日付範囲に営業日があるかどうかを確認します
    /// </summary>
    /// <param name="startDate">開始日</param>
    /// <param name="endDate">終了日</param>
    /// <returns>営業日がある場合はtrue、ない場合はfalse</returns>
    Task<bool> CheckTradingDayRangeAsync(DateTime startDate, DateTime endDate);
}
