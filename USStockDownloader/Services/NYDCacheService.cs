using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using USStockDownloader.Models;
using System.Net.Http;
using HtmlAgilityPack;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using USStockDownloader.Utils;

namespace USStockDownloader.Services
{
    public class NYDCacheService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<NYDCacheService> _logger;
        private readonly string _cacheFilePath;
        private readonly TimeSpan _cacheExpiry;
        private List<StockSymbol>? _cachedSymbols;
        private const string WikipediaUrl = "https://en.wikipedia.org/wiki/Dow_Jones_Industrial_Average";
        private const string CacheFileName = "nyd_symbols.json";

        public NYDCacheService(HttpClient httpClient, ILogger<NYDCacheService> logger)
            : this(httpClient, logger, CacheManager.GetCacheFilePath(CacheFileName), TimeSpan.FromHours(24))
        {
        }

        public NYDCacheService(HttpClient httpClient, ILogger<NYDCacheService> logger, string cacheFilePath, TimeSpan cacheExpiry)
        {
            _httpClient = httpClient;
            _logger = logger;
            _cacheFilePath = cacheFilePath;
            _cacheExpiry = cacheExpiry;
        }

        public async Task<List<StockSymbol>> GetNYDSymbols()
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
                        var cachedSymbols = JsonSerializer.Deserialize<List<StockSymbol>>(json);
                        if (cachedSymbols != null && cachedSymbols.Count > 0)
                        {
                            _cachedSymbols = cachedSymbols;
                            _logger.LogInformation("Loaded {Count} NY Dow symbols from cache", _cachedSymbols.Count);
                            return _cachedSymbols;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to load NY Dow symbols from cache {CacheFile}: {ErrorMessage}", PathUtils.ToRelativePath(_cacheFilePath), ex.Message);
                    }
                }
            }

            _cachedSymbols = await FetchNYDSymbols();
            await SaveSymbolsToCache(_cachedSymbols);
            return _cachedSymbols;
        }

        public async Task<List<string>> GetSymbolsAsync()
        {
            var symbols = await GetNYDSymbols();
            return symbols.Select(s => s.Symbol).ToList();
        }

        public async Task ForceUpdateAsync()
        {
            _logger.LogInformation("Forcing update of NY Dow symbols");
            _cachedSymbols = await FetchNYDSymbols();
            await SaveSymbolsToCache(_cachedSymbols);
        }

        private async Task<List<StockSymbol>> FetchNYDSymbols()
        {
            try
            {
                var response = await _httpClient.GetAsync(WikipediaUrl);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();

                var doc = new HtmlDocument();
                doc.LoadHtml(content);

                var table = doc.DocumentNode.SelectSingleNode("//table[contains(@class, 'wikitable') and contains(@class, 'sortable')]");
                if (table == null)
                {
                    throw new Exception("Failed to find NY Dow table");
                }

                var symbols = new List<StockSymbol>();
                var rows = table.SelectNodes(".//tr");

                if (rows == null)
                {
                    throw new Exception("No rows found in NY Dow table");
                }

                foreach (var row in rows.Skip(1)) // Skip header row
                {
                    var cells = row.SelectNodes(".//td");
                    if (cells != null && cells.Count >= 2) // 少なくとも2列（会社名、シンボル）が必要
                    {
                        var nameCell = cells[0];
                        var symbolCell = cells[1];
                        
                        var name = nameCell.InnerText.Trim();
                        var symbol = symbolCell.InnerText.Trim();
                        
                        // 市場情報の判定（NY DowはほとんどがNYSE）
                        string market = "NYSE";
                        
                        // 種別の判定（NY Dowは全て個別株）
                        string type = "stock";
                        
                        symbols.Add(new StockSymbol { 
                            Symbol = symbol, 
                            Name = name,
                            Market = market,
                            Type = type
                        });
                    }
                }

                _logger.LogInformation("Fetched {Count} NY Dow symbols from Wikipedia", symbols.Count);
                return symbols;
            }
            catch (Exception ex)
            {
                _logger.LogError("WikipediaからNYダウ銘柄の取得に失敗しました: {ErrorMessage} (Failed to fetch NY Dow symbols from Wikipedia)", ex.Message);
                throw;
            }
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
                _logger.LogInformation("Saved {Count} NY Dow symbols to cache file {CacheFile}", symbols.Count, PathUtils.ToRelativePath(_cacheFilePath));
            }
            catch (Exception ex)
            {
                _logger.LogError("NYダウ銘柄のキャッシュファイルへの保存に失敗しました: {CacheFile} - {ErrorMessage} (Failed to save NY Dow symbols to cache file)", PathUtils.ToRelativePath(_cacheFilePath), ex.Message);
            }
        }
    }
}
