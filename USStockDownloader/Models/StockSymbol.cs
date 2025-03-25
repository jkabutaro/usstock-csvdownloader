namespace USStockDownloader.Models;

using CsvHelper.Configuration.Attributes;

public class StockSymbol
{
    [Name("code")]
    public string Symbol { get; set; } = string.Empty;
    
    [Name("name")]
    public string Name { get; set; } = string.Empty;
    
    [Name("market")]
    public string Market { get; set; } = string.Empty;
    
    [Name("type")]
    public string Type { get; set; } = "stock"; // デフォルトは個別株
}
