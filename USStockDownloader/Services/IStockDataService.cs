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

    /// <summary>
    /// リトライなしで株価データを取得するメソッド（営業日判断用）
    /// </summary>
    /// <param name="symbol">取得する銘柄のシンボル</param>
    /// <param name="startDate">開始日</param>
    /// <param name="endDate">終了日</param>
    /// <returns>株価データのリスト</returns>
    Task<List<StockData>> GetStockDataWithoutRetryAsync(string symbol, DateTime startDate, DateTime endDate);

    /// <summary>
    /// 指定されたシンボルが上場廃止されているかどうかを確認します
    /// </summary>
    /// <param name="symbol">確認するシンボル</param>
    /// <returns>上場廃止されている場合はtrue、そうでない場合はfalse</returns>
    bool IsSymbolDelisted(string symbol);
}
