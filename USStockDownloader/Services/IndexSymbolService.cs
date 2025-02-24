using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using USStockDownloader.Models;

namespace USStockDownloader.Services;

public class IndexSymbolService
{
    private readonly ILogger<IndexSymbolService> _logger;
    private readonly HttpClient _httpClient;
    private const string SP500_URL = "https://en.wikipedia.org/wiki/List_of_S%26P_500_companies";
    private const string DJIA_URL = "https://en.wikipedia.org/wiki/Dow_Jones_Industrial_Average";

    public IndexSymbolService(ILogger<IndexSymbolService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<List<StockSymbol>> GetSP500Symbols()
    {
        try
        {
            var doc = await LoadHtmlDocument(SP500_URL);
            var table = doc.DocumentNode.SelectSingleNode("//table[@id='constituents']");
            if (table == null)
            {
                throw new Exception("S&P 500 constituents table not found");
            }

            var symbols = new List<StockSymbol>();
            var rows = table.SelectNodes(".//tr");
            
            // Skip header row
            for (int i = 1; i < rows.Count; i++)
            {
                var cells = rows[i].SelectNodes(".//td");
                if (cells?.Count >= 2)
                {
                    var symbol = cells[0].InnerText.Trim();
                    var name = cells[1].InnerText.Trim();
                    if (!string.IsNullOrEmpty(symbol))
                    {
                        symbols.Add(new StockSymbol { Symbol = symbol, CompanyName = name });
                    }
                }
            }

            _logger.LogInformation("Retrieved {Count} S&P 500 symbols", symbols.Count);
            return symbols;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve S&P 500 symbols");
            throw;
        }
    }

    public async Task<List<StockSymbol>> GetDJIASymbols()
    {
        try
        {
            var doc = await LoadHtmlDocument(DJIA_URL);
            var table = doc.DocumentNode.SelectSingleNode("//table[contains(@class, 'wikitable') and contains(., 'Symbol')]");
            if (table == null)
            {
                throw new Exception("DJIA constituents table not found");
            }

            var symbols = new List<StockSymbol>();
            var rows = table.SelectNodes(".//tr");
            
            // Skip header row
            for (int i = 1; i < rows.Count; i++)
            {
                var cells = rows[i].SelectNodes(".//td");
                if (cells?.Count >= 2)
                {
                    // Symbol is usually in the second column
                    var symbol = cells[1].InnerText.Trim();
                    var name = cells[0].InnerText.Trim();
                    if (!string.IsNullOrEmpty(symbol))
                    {
                        symbols.Add(new StockSymbol { Symbol = symbol, CompanyName = name });
                    }
                }
            }

            _logger.LogInformation("Retrieved {Count} DJIA symbols", symbols.Count);
            return symbols;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve DJIA symbols");
            throw;
        }
    }

    private async Task<HtmlDocument> LoadHtmlDocument(string url)
    {
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc;
    }
}
