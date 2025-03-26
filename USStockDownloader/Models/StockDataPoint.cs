using System;

namespace USStockDownloader.Models
{
    /// <summary>
    /// SQLiteデータベースに保存する株価データのポイントを表すクラス
    /// </summary>
    public class StockDataPoint
    {
        /// <summary>
        /// 日付
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// 始値
        /// </summary>
        public decimal Open { get; set; }

        /// <summary>
        /// 高値
        /// </summary>
        public decimal High { get; set; }

        /// <summary>
        /// 安値
        /// </summary>
        public decimal Low { get; set; }

        /// <summary>
        /// 終値
        /// </summary>
        public decimal Close { get; set; }

        /// <summary>
        /// 調整後終値
        /// </summary>
        public decimal AdjClose { get; set; }

        /// <summary>
        /// 出来高
        /// </summary>
        public long Volume { get; set; }
    }
}
