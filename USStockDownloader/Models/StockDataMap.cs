using CsvHelper.Configuration;

namespace USStockDownloader.Models;

public sealed class StockDataMap : ClassMap<StockData>
{
    public StockDataMap()
    {
        Map(m => m.Symbol).Name("Symbol");
        Map(m => m.Date).Name("Date").TypeConverterOption.Format("yyyyMMdd");
        Map(m => m.Open).Name("Open");
        Map(m => m.High).Name("High");
        Map(m => m.Low).Name("Low");
        Map(m => m.Close).Name("Close");
        Map(m => m.Volume).Name("Volume");
    }
}
