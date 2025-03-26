using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using USStockDownloader.Models;
using System.Net.Http;
using HtmlAgilityPack;
using System.IO;
using System.Text.RegularExpressions;
using USStockDownloader.Utils;

namespace USStockDownloader.Services
{
    public class BuffettCacheService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<BuffettCacheService> _logger;
        private readonly string _cacheFilePath;
        private readonly TimeSpan _cacheExpiry;
        private const string WikipediaUrl = "https://en.wikipedia.org/wiki/List_of_assets_owned_by_Berkshire_Hathaway";
        private const string CacheFileName = "buffett_symbols.json";

        public BuffettCacheService(HttpClient httpClient, ILogger<BuffettCacheService> logger)
            : this(httpClient, logger, CacheManager.GetCacheFilePath(CacheFileName), TimeSpan.FromHours(24))
        {
        }

        public BuffettCacheService(HttpClient httpClient, ILogger<BuffettCacheService> logger, string cacheFilePath, TimeSpan cacheExpiry)
        {
            _httpClient = httpClient;
            _logger = logger;
            _cacheFilePath = cacheFilePath;
            _cacheExpiry = cacheExpiry;
        }

        public async Task<List<StockSymbol>> GetSymbolsAsync(bool forceUpdate = false)
        {
            var cacheExists = File.Exists(_cacheFilePath);
            var cacheExpired = false;

            if (cacheExists)
            {
                var fileInfo = new FileInfo(_cacheFilePath);
                cacheExpired = (DateTime.Now - fileInfo.LastWriteTime) > _cacheExpiry;
            }

            if (forceUpdate || !cacheExists || cacheExpired)
            {
                _logger.LogInformation("Fetching Buffett portfolio symbols from Wikipedia");
                var fetchedSymbols = await FetchFromWikipediaAsync();
                await SaveToCacheAsync(fetchedSymbols, _cacheFilePath);
                return ApplySymbolMappings(fetchedSymbols);
            }

            _logger.LogInformation("Loading Buffett portfolio symbols from cache {CacheFile}", PathUtils.ToRelativePath(_cacheFilePath));
            var cachedSymbols = await LoadFromCacheAsync(_cacheFilePath);
            _logger.LogInformation("Loaded {Count} Buffett portfolio symbols", cachedSymbols.Count);
            return ApplySymbolMappings(cachedSymbols);
        }

        public async Task ForceUpdateAsync()
        {
            _logger.LogInformation("Forcing update of Buffett portfolio symbols...");
            var fetchedSymbols = await FetchFromWikipediaAsync();
            await SaveToCacheAsync(fetchedSymbols, _cacheFilePath);
        }

        private async Task<List<StockSymbol>> FetchFromWikipediaAsync()
        {
            var html = await _httpClient.GetStringAsync(WikipediaUrl);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var symbols = new List<StockSymbol>();

            // まずは直接ティッカーシンボルリンクを探す（最も信頼性が高い）
            var stockLinks = doc.DocumentNode.SelectNodes("//a[contains(@href, 'market-activity/stocks/') or contains(@href, 'quote/XNYS:')]");

            if (stockLinks != null && stockLinks.Count > 0)
            {
                // 見つかったリンクからティッカーシンボルを抽出
                foreach (var link in stockLinks)
                {
                    var href = link.GetAttributeValue("href", "");
                    var symbol = ExtractSymbolFromUrl(href);

                    if (!string.IsNullOrEmpty(symbol) && !symbols.Any(s => s.Symbol == symbol))
                    {
                        symbols.Add(new StockSymbol { Symbol = symbol });
                    }
                }
            }
            else
            {
                // 代替手段：U.S.-listed public company and ETF holdingsというセクションを探す
                var headers = doc.DocumentNode.SelectNodes("//h2");
                var stockSection = headers?.FirstOrDefault(h => h.InnerText.Contains("U.S.-listed public company"));

                if (stockSection == null)
                {
                    _logger.LogWarning("Could not find portfolio section on Wikipedia page");
                    return AddHardcodedSymbols();  // 最終手段としてハードコードされたシンボルを使用
                }

                // このセクションの直後にある株式銘柄のリンクを取得
                var section = stockSection.ParentNode;
                var sectionLinks = section.SelectNodes(".//a[contains(@href, 'market-activity/stocks') or contains(@href, 'quote/XNYS')]");

                if (sectionLinks != null && sectionLinks.Count > 0)
                {
                    foreach (var link in sectionLinks)
                    {
                        var href = link.GetAttributeValue("href", "");
                        var symbol = ExtractSymbolFromUrl(href);

                        if (!string.IsNullOrEmpty(symbol) && !symbols.Any(s => s.Symbol == symbol))
                        {
                            symbols.Add(new StockSymbol { Symbol = symbol });
                        }
                    }
                }
            }

            if (symbols.Count == 0)
            {
                _logger.LogWarning("No symbols found. Using hardcoded fallback list.");
                return AddHardcodedSymbols();
            }

            _logger.LogInformation("Found {Count} Buffett portfolio symbols", symbols.Count);
            return symbols;
        }

        private string ExtractSymbolFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;

            // NASDAQのURLパターン: https://www.nasdaq.com/market-activity/stocks/aapl
            if (url.Contains("nasdaq.com/market-activity/stocks/"))
            {
                var parts = url.Split('/');
                return parts[parts.Length - 1].ToUpper();
            }

            // NYSEのURLパターン: https://www.nyse.com/quote/XNYS:AAPL
            else if (url.Contains("nyse.com/quote/"))
            {
                var parts = url.Split(':');
                if (parts.Length > 1)
                {
                    return parts[parts.Length - 1].ToUpper();
                }
            }

            return string.Empty;
        }

        private List<StockSymbol> AddHardcodedSymbols()
        {
            // バフェットポートフォリオの主要な銘柄をハードコードしておく（フォールバック用）
            var hardcoded = new List<string>
            {
                "AAPL", // アップル
                "BAC",  // バンク・オブ・アメリカ
                "KO",   // コカ・コーラ
                "AXP",  // アメリカン・エキスプレス
                "BK",   // バンク・オブ・ニューヨーク・メロン
                "CHTR", // チャーター・コミュニケーションズ
                "CVX",  // シェブロン
                "KHC",  // クラフト・ハインツ
                "MCO",  // ムーディーズ
                "OXY",  // オキシデンタル・ペトロリアム
                "VZ"    // ベライゾン
            };

            _logger.LogInformation("Using {Count} hardcoded Buffett portfolio symbols", hardcoded.Count);
            return hardcoded.Select(s => new StockSymbol { Symbol = s }).ToList();
        }

        private Dictionary<string, string> GetSymbolMappings()
        {
            // 特定のシンボルがYahoo Financeでは異なる形式で参照される場合のマッピング
            return new Dictionary<string, string>
            {
                // Liberty SiriusXMは企業再編によりSIRIに変更
                { "LSXMA", "SIRI" },
                { "LSXMK", "SIRI" },
            };
        }

        private List<StockSymbol> ApplySymbolMappings(List<StockSymbol> originalSymbols)
        {
            var mappings = GetSymbolMappings();
            var result = new List<StockSymbol>();

            _logger.LogInformation("Applying symbol mappings to {Count} symbols", originalSymbols.Count);

            foreach (var symbol in originalSymbols)
            {
                if (mappings.TryGetValue(symbol.Symbol, out var mappedSymbol))
                {
                    _logger.LogInformation("Mapping symbol {Original} to {Mapped}", symbol.Symbol, mappedSymbol);
                    // マッピングされたシンボルを追加（元のは追加しない）
                    result.Add(new StockSymbol { Symbol = mappedSymbol });
                }
                else
                {
                    result.Add(symbol);
                }
            }

            _logger.LogInformation("After mapping: {Count} symbols", result.Count);
            return result;
        }

        private async Task<List<StockSymbol>> LoadFromCacheAsync(string cachePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(cachePath);
                var symbols = JsonSerializer.Deserialize<List<StockSymbol>>(json);
                return symbols ?? new List<StockSymbol>();
            }
            catch (Exception ex)
            {
                _logger.LogError("バフェットポートフォリオ銘柄のキャッシュファイルからの読み込みに失敗しました: {CacheFile} - {ErrorMessage} (Failed to load Buffett portfolio symbols from cache file)", PathUtils.ToRelativePath(cachePath), ex.Message);
                return new List<StockSymbol>();
            }
        }

        private async Task SaveToCacheAsync(List<StockSymbol> symbols, string cachePath)
        {
            try
            {
                // キャッシュディレクトリが存在しない場合は作成
                var cacheDir = Path.GetDirectoryName(cachePath);
                if (!string.IsNullOrEmpty(cacheDir) && !Directory.Exists(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                }

                var json = JsonSerializer.Serialize(symbols, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(cachePath, json);
                _logger.LogInformation("Saved {Count} Buffett portfolio symbols to cache file {CacheFile}", symbols.Count, PathUtils.ToRelativePath(cachePath));
            }
            catch (Exception ex)
            {
                _logger.LogError("バフェットポートフォリオ銘柄のキャッシュファイルへの保存に失敗しました: {CacheFile} - {ErrorMessage} (Failed to save Buffett portfolio symbols to cache file)", PathUtils.ToRelativePath(cachePath), ex.Message);
            }
        }
    }
}
