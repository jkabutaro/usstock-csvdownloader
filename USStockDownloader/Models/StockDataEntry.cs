using System;
using SQLite;

namespace USStockDownloader.Models
{
    /// <summary>
    /// SQLiteデータベースに保存する株価データのエントリ
    /// </summary>
    [Table("stock_data")]
    public class StockDataEntry
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        
        [Indexed]
        public required string Symbol { get; set; }
        
        [Indexed]
        public int Date { get; set; }
        
        public double Open { get; set; }
        
        public double High { get; set; }
        
        public double Low { get; set; }
        
        public double Close { get; set; }
        
        public double AdjClose { get; set; }
        
        public long Volume { get; set; }
    }
    
    /// <summary>
    /// キャッシュ情報を格納するモデル
    /// </summary>
    [Table("cache_info")]
    public class CacheInfo
    {
        [PrimaryKey]
        public required string Symbol { get; set; }
        
        public int StartDate { get; set; }
        
        public int EndDate { get; set; }
        
        public DateTime LastUpdate { get; set; }
        
        public DateTime? LastTradingDate { get; set; }
    }
    
    /// <summary>
    /// ランタイムチェック情報を格納するモデル
    /// </summary>
    [Table("runtime_checks")]
    public class RuntimeCheck
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        
        [Indexed]
        public required string CheckType { get; set; }
        
        public int Date { get; set; }
        
        public DateTime LastUpdate { get; set; }
    }
}
