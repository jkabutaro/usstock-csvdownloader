using System.Text.Json.Serialization;

namespace USStockDownloader.Models;

public class YahooFinanceResponse
{
    [JsonPropertyName("chart")]
    public Chart? Chart { get; set; }
}

public class Chart
{
    [JsonPropertyName("result")]
    public List<ChartResult>? Result { get; set; }

    [JsonPropertyName("error")]
    public object? Error { get; set; }
}

public class ChartResult
{
    [JsonPropertyName("meta")]
    public Meta? Meta { get; set; }

    [JsonPropertyName("timestamp")]
    public List<long?>? Timestamp { get; set; }

    [JsonPropertyName("indicators")]
    public Indicators? Indicators { get; set; }
}

public class Meta
{
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("exchangeName")]
    public string? ExchangeName { get; set; }

    [JsonPropertyName("instrumentType")]
    public string? InstrumentType { get; set; }

    [JsonPropertyName("firstTradeDate")]
    public long? FirstTradeDate { get; set; }

    [JsonPropertyName("regularMarketTime")]
    public long? RegularMarketTime { get; set; }

    [JsonPropertyName("gmtoffset")]
    public int? Gmtoffset { get; set; }

    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }

    [JsonPropertyName("exchangeTimezoneName")]
    public string? ExchangeTimezoneName { get; set; }

    [JsonPropertyName("regularMarketPrice")]
    public decimal? RegularMarketPrice { get; set; }

    [JsonPropertyName("chartPreviousClose")]
    public decimal? ChartPreviousClose { get; set; }

    [JsonPropertyName("priceHint")]
    public int? PriceHint { get; set; }

    [JsonPropertyName("dataGranularity")]
    public string? DataGranularity { get; set; }

    [JsonPropertyName("range")]
    public string? Range { get; set; }
}

public class Indicators
{
    [JsonPropertyName("quote")]
    public List<Quote>? Quote { get; set; }
}

public class Quote
{
    [JsonPropertyName("high")]
    public List<decimal?>? High { get; set; }

    [JsonPropertyName("open")]
    public List<decimal?>? Open { get; set; }

    [JsonPropertyName("low")]
    public List<decimal?>? Low { get; set; }

    [JsonPropertyName("close")]
    public List<decimal?>? Close { get; set; }

    [JsonPropertyName("volume")]
    public List<long?>? Volume { get; set; }
}
