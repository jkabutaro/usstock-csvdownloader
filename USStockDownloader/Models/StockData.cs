using System.ComponentModel.DataAnnotations;
using CsvHelper.Configuration.Attributes;

namespace USStockDownloader.Models;

public class StockData
{
    [Ignore]
    public string Symbol { get; set; } = string.Empty;
    
    [Name("Date")]
    public int Date { get; set; }

    [Ignore]
    public DateTime DateTime { get; set; }
    
    [Ignore]
    //public int DateNumber => DateTime.Year * 10000 + DateTime.Month * 100 + DateTime.Day;
    public string DateString => DateTime.ToString("yyyy-MM-dd");

    [Name("Open")]
    public decimal Open { get; set; }
    
    [Name("High")]
    public decimal High { get; set; }
    
    [Name("Low")]
    public decimal Low { get; set; }
    
    [Name("Close")]
    public decimal Close { get; set; }
    
    [Name("Adj Close")]
    public decimal AdjClose { get; set; }
    
    [Name("Volume")]
    public long Volume { get; set; }
}
