using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using USStockDownloader.Models;
using System.Net.Http;
using HtmlAgilityPack;
using USStockDownloader.Utils;

namespace USStockDownloader.Services;

public class SP500CacheService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SP500CacheService> _logger;
    private readonly string _cacheFilePath;
    private List<StockSymbol>? _cachedSymbols;
    private const string SP500_URL = "https://en.wikipedia.org/wiki/List_of_S%26P_500_companies";
    private readonly TimeSpan _cacheExpiry;

    public SP500CacheService(HttpClient httpClient, ILogger<SP500CacheService> logger)
        : this(httpClient, logger, Path.Combine("Cache", "sp500_symbols.json"), TimeSpan.FromDays(1))
    {
    }

    public SP500CacheService(HttpClient httpClient, ILogger<SP500CacheService> logger, string cacheFilePath, TimeSpan cacheExpiry)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cacheFilePath = cacheFilePath;
        _cacheExpiry = cacheExpiry;
    }

    public async Task<List<StockSymbol>> GetSP500Symbols()
    {
        if (_cachedSymbols != null)
        {
            return _cachedSymbols;
        }

        if (File.Exists(_cacheFilePath))
        {
            var fileInfo = new FileInfo(_cacheFilePath);
            if (DateTime.Now - fileInfo.LastWriteTime < _cacheExpiry)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_cacheFilePath);
                    _cachedSymbols = JsonSerializer.Deserialize<List<StockSymbol>>(json);
                    if (_cachedSymbols != null)
                    {
                        _logger.LogInformation("Loaded {Count} S&P 500 symbols from cache", _cachedSymbols.Count);
                        return _cachedSymbols;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to load S&P 500 symbols from cache {CacheFile}: {ErrorMessage}", PathUtils.ToRelativePath(_cacheFilePath), ex.Message);
                }
            }
        }

        _cachedSymbols = await FetchSP500Symbols();
        await SaveSymbolsToCache(_cachedSymbols);
        return _cachedSymbols;
    }

    public async Task<List<string>> GetSymbolsAsync()
    {
        var symbols = await GetSP500Symbols();
        return symbols.Select(s => s.Symbol).ToList();
    }

    public async Task ForceUpdateAsync()
    {
        _logger.LogInformation("Forcing update of S&P 500 symbols");
        _cachedSymbols = await FetchSP500Symbols();
        await SaveSymbolsToCache(_cachedSymbols);
    }

    private async Task<List<StockSymbol>> FetchSP500Symbols()
    {
        try
        {
            var response = await _httpClient.GetAsync(SP500_URL);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(content);

            var table = doc.DocumentNode.SelectSingleNode("//table[@id='constituents']");
            if (table == null)
            {
                throw new Exception("Failed to find S&P 500 table");
            }

            var symbols = new List<StockSymbol>();
            var rows = table.SelectNodes(".//tr");

            if (rows == null)
            {
                throw new Exception("No rows found in S&P 500 table");
            }

            foreach (var row in rows.Skip(1)) // Skip header row
            {
                var cells = row.SelectNodes(".//td");
                if (cells != null && cells.Count >= 3) // 少なくとも3列（シンボル、名前、セクター）が必要
                {
                    var symbol = cells[0].InnerText.Trim();
                    var name = cells[1].InnerText.Trim();
                    
                    // 市場情報の判定（S&P 500はほとんどがNYSEかNASDAQ）
                    string market = DetermineMarket(symbol);
                    
                    // 種別の判定（ETFかどうか）
                    string type = DetermineType(symbol, name);
                    
                    symbols.Add(new StockSymbol { 
                        Symbol = symbol, 
                        Name = name,
                        Market = market,
                        Type = type
                    });
                }
            }

            _logger.LogInformation("Fetched {Count} S&P 500 symbols from Wikipedia", symbols.Count);
            return symbols;
        }
        catch (Exception ex)
        {
            _logger.LogError("WikipediaからS&P 500銘柄の取得に失敗しました: {ErrorMessage} (Failed to fetch S&P 500 symbols from Wikipedia)", ex.Message);
            return new List<StockSymbol>();
        }
    }

    // 銘柄の市場を判定するヘルパーメソッド
    private string DetermineMarket(string symbol)
    {
        // 一般的なルールとして、ほとんどのS&P 500銘柄はNYSEかNASDAQに上場している
        // 実際にはより複雑な判定が必要ですが、簡易的な実装として以下のルールを使用
        
        // 一部の有名なNASDAQ銘柄
        string[] nasdaqSymbols = { "AAPL", "MSFT", "AMZN", "GOOGL", "GOOG", "META", "TSLA", "NVDA", "ADBE", "NFLX", "PYPL", "INTC", "CSCO", "CMCSA", "PEP" };
        
        if (nasdaqSymbols.Contains(symbol))
        {
            return "NASDAQ";
        }
        
        // 4文字以上の銘柄コードはNASDAQの可能性が高い
        if (symbol.Length >= 4 && !symbol.Contains("."))
        {
            return "NASDAQ";
        }
        
        // それ以外はNYSEと仮定
        return "NYSE";
    }

    // 銘柄の種別（個別株/ETF）を判定するヘルパーメソッド
    private string DetermineType(string symbol, string name)
    {
        // 名前にETFが含まれる場合はETF
        if (name.Contains("ETF") || name.Contains("Fund") || name.Contains("Trust") || 
            name.Contains("iShares") || name.Contains("SPDR") || name.Contains("Vanguard"))
        {
            return "etf";
        }
        
        // 特定のETFシンボルのリスト
        string[] etfSymbols = { "SPY", "QQQ", "IWM", "DIA", "GLD", "SLV", "VTI", "VOO", "VEA", "VWO", "BND", "AGG", "LQD", "TLT", "SHY" };
        
        if (etfSymbols.Contains(symbol))
        {
            return "etf";
        }
        
        // デフォルトは個別株
        return "stock";
    }

    private async Task SaveSymbolsToCache(List<StockSymbol> symbols)
    {
        try
        {
            // キャッシュディレクトリが存在しない場合は作成
            var cacheDir = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(cacheDir) && !Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }

            var json = JsonSerializer.Serialize(symbols, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_cacheFilePath, json);
            _logger.LogInformation("Saved {Count} S&P 500 symbols to cache file {CacheFile}", symbols.Count, PathUtils.ToRelativePath(_cacheFilePath));
        }
        catch (Exception ex)
        {
            _logger.LogError("S&P 500銘柄のキャッシュファイルへの保存に失敗しました: {CacheFile} - {ErrorMessage} (Failed to save S&P 500 symbols to cache file)", PathUtils.ToRelativePath(_cacheFilePath), ex.Message);
        }
    }
}
