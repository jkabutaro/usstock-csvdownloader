using System.ComponentModel.DataAnnotations;
using CsvHelper.Configuration.Attributes;

namespace USStockDownloader.Models;

public class StockData
{
    [Name("Symbol")]
    public string Symbol { get; set; } = string.Empty;
    
    [Name("Date")]
    public DateTime Date { get; set; }
    
    [Name("Open")]
    public decimal Open { get; set; }
    
    [Name("High")]
    public decimal High { get; set; }
    
    [Name("Low")]
    public decimal Low { get; set; }
    
    [Name("Close")]
    public decimal Close { get; set; }
    
    [Name("Volume")]
    public long Volume { get; set; }
}
