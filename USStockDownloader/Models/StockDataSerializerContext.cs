using System.Text.Json;
using System.Text.Json.Serialization;

namespace USStockDownloader.Models;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(List<StockData>))]
public partial class StockDataSerializerContext : JsonSerializerContext
{
}
