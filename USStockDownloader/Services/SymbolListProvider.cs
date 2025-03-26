using Microsoft.Extensions.Logging;
using USStockDownloader.Models;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace USStockDownloader.Services;

public class SymbolListProvider
{
    private readonly IndexSymbolService _indexSymbolService;
    private readonly ILogger<SymbolListProvider> _logger;

    public SymbolListProvider(
        IndexSymbolService indexSymbolService,
        ILogger<SymbolListProvider> logger)
    {
        _indexSymbolService = indexSymbolService;
        _logger = logger;
    }

    public async Task<List<string>> GetSymbolsAsync(bool useSP500, bool useNYD, bool useBuffett, string? symbolFile)
    {
        if (useSP500)
        {
            var symbols = await _indexSymbolService.GetSP500Symbols();
            _logger.LogInformation("Loaded {Count} S&P 500 symbols", symbols.Count);
            return symbols;
        }
        
        if (useNYD)
        {
            var symbols = await _indexSymbolService.GetNYDSymbols();
            _logger.LogInformation("Loaded {Count} NY Dow symbols", symbols.Count);
            return symbols;
        }

        if (useBuffett)
        {
            var symbols = await _indexSymbolService.GetBuffettSymbols();
            _logger.LogInformation("Loaded {Count} Buffett portfolio symbols", symbols.Count);
            return symbols;
        }

        if (!string.IsNullOrEmpty(symbolFile))
        {
            if (!File.Exists(symbolFile))
            {
                throw new FileNotFoundException($"Symbol file not found: {symbolFile}");
            }

            try
            {
                var symbols = await File.ReadAllLinesAsync(symbolFile);
                _logger.LogInformation("Loaded {Count} symbols from file: {File}", symbols.Length, symbolFile);
                return symbols.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError("シンボルファイルの読み込みに失敗しました {File}: {ErrorMessage} (Failed to load symbols from file)", symbolFile, ex.Message);
                return new List<string>();
            }
        }

        throw new ArgumentException("No symbol source specified. Use --sp500, --nyd, --buffett, or --file");
    }
}
