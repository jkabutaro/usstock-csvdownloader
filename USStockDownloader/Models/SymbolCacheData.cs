using System;
using System.Collections.Generic;

namespace USStockDownloader.Models
{
    /// <summary>
    /// シンボルごとのキャッシュデータを保持するクラス
    /// </summary>
    public class SymbolCacheData
    {
        /// <summary>
        /// 株価データのリスト
        /// </summary>
        public List<StockData> Data { get; set; } = new List<StockData>();

        /// <summary>
        /// 最終更新日時
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}
