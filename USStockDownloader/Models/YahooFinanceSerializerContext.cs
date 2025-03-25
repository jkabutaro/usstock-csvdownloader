using System.Text.Json;
using System.Text.Json.Serialization;

namespace USStockDownloader.Models;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(YahooFinanceResponse))]
public partial class YahooFinanceSerializerContext : JsonSerializerContext
{
}
