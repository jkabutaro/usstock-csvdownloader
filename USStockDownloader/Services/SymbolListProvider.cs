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
            _logger.LogDebug("Loaded {Count} S&P 500 symbols", symbols.Count);
            return symbols;
        }
        
        if (useNYD)
        {
            var symbols = await _indexSymbolService.GetNYDSymbols();
            _logger.LogDebug("Loaded {Count} NY Dow symbols", symbols.Count);
            return symbols;
        }

        if (useBuffett)
        {
            var symbols = await _indexSymbolService.GetBuffettSymbols();
            _logger.LogDebug("Loaded {Count} Buffett portfolio symbols", symbols.Count);
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
                var lines = await File.ReadAllLinesAsync(symbolFile);

                // ヘッダー行かどうかを判定
                bool hasHeader = false;
                if (lines.Length > 0)
                {
                    var firstRow = lines[0].ToLowerInvariant();
                    // ヘッダーに含まれることが多い文字列をチェック
                    string[] headerKeywords = { "name", "symbol", "market", "type", "ticker", "code", "exchange" };
                    hasHeader = headerKeywords.Any(keyword => firstRow.Contains(keyword));

                    //// 数値判定もバックアップとして使用
                    //if (!hasHeader)
                    //{
                    //    var firstColumn = firstRow.Split(',')[0].Trim();
                    //    // 最初の列が純粋な数値でない場合もヘッダーと判断
                    //    hasHeader = !decimal.TryParse(firstColumn, out _);
                    //}
                }

                var symbols = lines
                    .Skip(hasHeader ? 1 : 0) // ヘッダーがある場合は最初の行をスキップ
                    .Select(line =>
                    {
                        var parts = line.Split(',');
                        return parts.Length > 0 ? parts[0].Trim() : line.Trim();
                    })
                    .Where(s => !string.IsNullOrWhiteSpace(s)) // 空の値をフィルタリング
                    .ToList();

                _logger.LogDebug("Loaded {Count} symbols from file: {File}{HeaderInfo}",
                    symbols.Count,
                    symbolFile,
                    hasHeader ? " (detected and skipped header row)" : "");

                return symbols;
            }
            catch (Exception ex)
            {
                _logger.LogError("シンボルファイルの読み込みに失敗しました {File}: {ErrorMessage} (Failed to load symbols from file)", symbolFile, ex.Message);
                return new List<string>();
            }
        }

        //if (!string.IsNullOrEmpty(symbolFile))
        //{
        //    if (!File.Exists(symbolFile))
        //    {
        //        throw new FileNotFoundException($"Symbol file not found: {symbolFile}");
        //    }

        //    try
        //    {
        //        var symbols = await File.ReadAllLinesAsync(symbolFile);
        //        _logger.LogDebug("Loaded {Count} symbols from file: {File}", symbols.Length, symbolFile);
        //        return symbols.ToList();
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError("シンボルファイルの読み込みに失敗しました {File}: {ErrorMessage} (Failed to load symbols from file)", symbolFile, ex.Message);
        //        return new List<string>();
        //    }
        //}

        throw new ArgumentException("No symbol source specified. Use --sp500, --nyd, --buffett, or --file");
    }
}
