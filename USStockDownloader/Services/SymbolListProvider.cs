using System.Reflection;
using CsvHelper;
using System.Globalization;
using USStockDownloader.Models;
using Microsoft.Extensions.Logging;

namespace USStockDownloader.Services;

public class SymbolListProvider
{
    private readonly ILogger<SymbolListProvider> _logger;
    private readonly string _dataDirectory;

    public SymbolListProvider(ILogger<SymbolListProvider> logger)
    {
        _logger = logger;
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        _dataDirectory = Path.Combine(Path.GetDirectoryName(assemblyLocation)!, "Data");
    }

    public List<StockSymbol> GetSymbols(string source)
    {
        var filePath = source.ToLower() switch
        {
            "sp500" => Path.Combine(_dataDirectory, "sp500.csv"),
            "nyd" => Path.Combine(_dataDirectory, "nyd.csv"),
            _ => source
        };

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Symbol file not found: {filePath}");
        }

        try
        {
            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            var symbols = csv.GetRecords<StockSymbol>().ToList();
            _logger.LogInformation("Loaded {Count} symbols from {Source}", symbols.Count, source);
            return symbols;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load symbols from {Source}", source);
            throw;
        }
    }
}
