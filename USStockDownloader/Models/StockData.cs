using System.ComponentModel.DataAnnotations;
using CsvHelper.Configuration.Attributes;

namespace USStockDownloader.Models;

public class StockData
{
    [Name("Symbol")]
    public string Symbol { get; set; } = string.Empty;
    
    [Name("Date")]
    public int DateNumber { get; set; }
    
    [Ignore]
    public DateTime Date 
    { 
        get
        {
            var dateStr = DateNumber.ToString("D8");
            var year = int.Parse(dateStr.Substring(0, 4));
            var month = int.Parse(dateStr.Substring(4, 2));
            var day = int.Parse(dateStr.Substring(6, 2));
            return new DateTime(year, month, day);
        }
        set
        {
            DateNumber = int.Parse(value.ToString("yyyyMMdd"));
        }
    }
    
    [Name("Open")]
    public double Open { get; set; }
    
    [Name("High")]
    public double High { get; set; }
    
    [Name("Low")]
    public double Low { get; set; }
    
    [Name("Close")]
    public double Close { get; set; }
    
    [Name("Volume")]
    public long Volume { get; set; }
}
