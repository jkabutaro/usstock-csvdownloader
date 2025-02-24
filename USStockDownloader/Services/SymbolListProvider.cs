using System.Reflection;
using CsvHelper;
using System.Globalization;
using USStockDownloader.Models;
using Microsoft.Extensions.Logging;

namespace USStockDownloader.Services;

public class SymbolListProvider
{
    private readonly ILogger<SymbolListProvider> _logger;
    private readonly IndexSymbolService _indexSymbolService;
    private readonly string _dataDirectory;

    public SymbolListProvider(ILogger<SymbolListProvider> logger, IndexSymbolService indexSymbolService)
    {
        _logger = logger;
        _indexSymbolService = indexSymbolService;
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        _dataDirectory = Path.Combine(Path.GetDirectoryName(assemblyLocation)!, "Data");
    }

    public async Task<List<StockSymbol>> GetSymbols(string source)
    {
        try
        {
            return source.ToLower() switch
            {
                "sp500" => await _indexSymbolService.GetSP500Symbols(),
                "nyd" => await _indexSymbolService.GetDJIASymbols(),
                _ => LoadSymbolsFromFile(source)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get symbols from {Source}", source);
            throw;
        }
    }

    private List<StockSymbol> LoadSymbolsFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Symbol file not found: {filePath}");
        }

        try
        {
            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            var symbols = csv.GetRecords<StockSymbol>().ToList();
            _logger.LogInformation("Loaded {Count} symbols from file {FilePath}", symbols.Count, filePath);
            return symbols;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load symbols from file {FilePath}", filePath);
            throw;
        }
    }
}
