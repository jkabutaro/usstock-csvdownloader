using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using USStockDownloader.Models;
using USStockDownloader.Utils;

namespace USStockDownloader.Interfaces
{
    /// <summary>
    /// 株価データのキャッシュサービスのインターフェース
    /// </summary>
    public interface IStockDataCacheService
    {
        /// <summary>
        /// キャッシュ情報を取得します
        /// </summary>
        Task<CacheStatus> GetCacheInfoAsync(string symbol, DateTime startDate, DateTime endDate);

        /// <summary>
        /// 株価データをデータベースに保存します
        /// </summary>
        Task SaveStockDataAsync(string symbol, List<StockDataPoint> dataPoints);

        /// <summary>
        /// 指定された期間のデータを取得します
        /// </summary>
        Task<List<StockDataPoint>> GetStockDataAsync(string symbol, DateTime startDate, DateTime endDate);

        /// <summary>
        /// 指定された期間のデータを削除します
        /// </summary>
        Task DeleteStockDataForDateRangeAsync(string symbol, DateTime startDate, DateTime endDate);

        /// <summary>
        /// 同期用にシンボルの最新の株価データを取得します
        /// </summary>
        Task<DateTime?> GetLastTradingDateAsync(string symbol);
    }
}
