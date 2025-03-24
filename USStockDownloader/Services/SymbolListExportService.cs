using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using USStockDownloader.Models;

namespace USStockDownloader.Services;

public class SymbolListExportService
{
    private readonly ILogger<SymbolListExportService> _logger;
    private readonly SP500CacheService _sp500CacheService;
    private readonly NYDCacheService _nydCacheService;
    private readonly IndexListService _indexListService;
    private readonly BuffettCacheService _buffettCacheService;

    static SymbolListExportService()
    {
        // エンコーディングプロバイダーを登録
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public SymbolListExportService(
        ILogger<SymbolListExportService> logger,
        SP500CacheService sp500CacheService,
        NYDCacheService nydCacheService,
        IndexListService indexListService,
        BuffettCacheService buffettCacheService)
    {
        _logger = logger;
        _sp500CacheService = sp500CacheService;
        _nydCacheService = nydCacheService;
        _indexListService = indexListService;
        _buffettCacheService = buffettCacheService;
    }

    public async Task ExportSymbolListToCsv(string csvPath)
    {
        try
        {
            _logger.LogInformation("Exporting symbol list to CSV...");
            
            // S&P 500銘柄を取得
            var symbols = await _sp500CacheService.GetSP500Symbols();
            
            // 出力ディレクトリが存在しない場合は作成
            var outputDirectory = Path.GetDirectoryName(csvPath);
            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
            
            _logger.LogInformation("Writing {Count} symbols to {FilePath}", symbols.Count, csvPath);
            
            // Shift-JISエンコーディングでCSVファイルを作成
            using (var writer = new StreamWriter(csvPath, false, Encoding.GetEncoding(932)))
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                };
                
                using (var csv = new CsvWriter(writer, config))
                {
                    // ヘッダーを手動で書き込み
                    csv.WriteField("code");
                    csv.WriteField("name");
                    csv.WriteField("market");
                    csv.WriteField("type");
                    csv.NextRecord();
                    
                    // データを書き込み
                    foreach (var symbol in symbols)
                    {
                        csv.WriteField(symbol.Symbol);
                        csv.WriteField(symbol.Name);
                        csv.WriteField(symbol.Market);
                        csv.WriteField(symbol.Type);
                        csv.NextRecord();
                    }
                }
            }
            
            _logger.LogInformation("Successfully exported symbol list to {FilePath}", csvPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export symbol list to CSV");
            throw;
        }
    }

    public async Task ExportNYDListToCsvAsync(string csvPath, bool forceUpdate = false)
    {
        try
        {
            _logger.LogInformation("Exporting NY Dow symbol list to CSV...");
            
            // NYダウ銘柄を取得
            List<StockSymbol> nydSymbols;
            if (forceUpdate)
            {
                await _nydCacheService.ForceUpdateAsync();
            }
            
            var symbols = await _nydCacheService.GetSymbolsAsync();
            nydSymbols = symbols.Select(s => new StockSymbol { Symbol = s }).ToList();
            
            // 出力ディレクトリが存在しない場合は作成
            var outputDirectory = Path.GetDirectoryName(csvPath);
            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
            
            _logger.LogInformation("Writing {Count} NY Dow symbols to {FilePath}", nydSymbols.Count, csvPath);
            
            // 銘柄コードと企業名のマッピング
            var companyNames = new Dictionary<string, string>
            {
                { "MMM", "3M" },
                { "AXP", "American Express" },
                { "AMGN", "Amgen" },
                { "AMZN", "Amazon" },
                { "AAPL", "Apple" },
                { "BA", "Boeing" },
                { "CAT", "Caterpillar" },
                { "CVX", "Chevron" },
                { "CSCO", "Cisco" },
                { "KO", "Coca-Cola" },
                { "DIS", "Walt Disney" },
                { "GS", "Goldman Sachs" },
                { "HD", "Home Depot" },
                { "HON", "Honeywell" },
                { "IBM", "IBM" },
                { "JNJ", "Johnson & Johnson" },
                { "JPM", "JPMorgan Chase" },
                { "MCD", "McDonald's" },
                { "MRK", "Merck" },
                { "MSFT", "Microsoft" },
                { "NKE", "Nike" },
                { "NVDA", "Nvidia" },
                { "PG", "Procter & Gamble" },
                { "CRM", "Salesforce" },
                { "SHW", "Sherwin-Williams" },
                { "TRV", "Travelers" },
                { "UNH", "UnitedHealth" },
                { "VZ", "Verizon" },
                { "V", "Visa" },
                { "WMT", "Walmart" }
            };
            
            // 企業名と日本語名のマッピング
            var japaneseNames = new Dictionary<string, string>
            {
                { "3M", "スリーエム" },
                { "American Express", "アメリカン・エキスプレス" },
                { "Amgen", "アムジェン" },
                { "Amazon", "アマゾン" },
                { "Apple", "アップル" },
                { "Boeing", "ボーイング" },
                { "Caterpillar", "キャタピラー" },
                { "Chevron", "シェブロン" },
                { "Cisco", "シスコシステムズ" },
                { "Coca-Cola", "コカ・コーラ" },
                { "Walt Disney", "ウォルト・ディズニー" },
                { "Goldman Sachs", "ゴールドマン・サックス" },
                { "Home Depot", "ホーム・デポ" },
                { "Honeywell", "ハネウェル" },
                { "IBM", "アイビーエム" },
                { "Johnson & Johnson", "ジョンソン・エンド・ジョンソン" },
                { "JPMorgan Chase", "JPモルガン・チェース" },
                { "McDonald's", "マクドナルド" },
                { "Merck", "メルク" },
                { "Microsoft", "マイクロソフト" },
                { "Nike", "ナイキ" },
                { "Nvidia", "エヌビディア" },
                { "Procter & Gamble", "プロクター・アンド・ギャンブル" },
                { "Salesforce", "セールスフォース" },
                { "Sherwin-Williams", "シャーウィン・ウィリアムズ" },
                { "Travelers", "トラベラーズ" },
                { "UnitedHealth", "ユナイテッドヘルス" },
                { "Verizon", "ベライゾン" },
                { "Visa", "ビザ" },
                { "Walmart", "ウォルマート" }
            };
            
            // 銘柄コードとマーケットのマッピング
            var marketMapping = new Dictionary<string, string>
            {
                { "AAPL", "NASDAQ" },
                { "MSFT", "NASDAQ" },
                { "CSCO", "NASDAQ" },
                { "AMGN", "NASDAQ" },
                { "AMZN", "NASDAQ" },
                { "NVDA", "NASDAQ" },
                { "HON", "NASDAQ" }
            };
            
            // Shift-JISエンコーディングでCSVファイルを作成
            using (var writer = new StreamWriter(csvPath, false, Encoding.GetEncoding(932)))
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                };
                
                using (var csv = new CsvWriter(writer, config))
                {
                    // ヘッダーを手動で書き込み
                    csv.WriteField("code");
                    csv.WriteField("name");
                    csv.WriteField("market");
                    csv.WriteField("type");
                    csv.NextRecord();
                    
                    // データを書き込み
                    foreach (var symbol in nydSymbols)
                    {
                        // 銘柄コード
                        string code = symbol.Symbol;
                        csv.WriteField(code);
                        
                        // 企業名と日本語名
                        string englishName = string.Empty;
                        
                        // Wikipediaから取得した名前が不十分な場合、マッピングから取得
                        if (!companyNames.TryGetValue(code, out englishName!))
                        {
                            englishName = symbol.Name;
                            if (string.IsNullOrWhiteSpace(englishName))
                            {
                                englishName = code;
                            }
                        }
                        
                        // 日本語名を追加
                        string japaneseName = string.Empty;
                        foreach (var kvp in japaneseNames)
                        {
                            if (englishName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                japaneseName = kvp.Value;
                                break;
                            }
                        }
                        
                        // 最終的な表示名を構築
                        string fullName;
                        if (!string.IsNullOrEmpty(japaneseName))
                        {
                            fullName = $"{englishName} {japaneseName}";
                        }
                        else
                        {
                            fullName = englishName;
                        }
                        
                        csv.WriteField(fullName);
                        
                        // マーケット情報
                        string market = "NYSE"; // デフォルトはNYSE
                        if (marketMapping.TryGetValue(code, out var mappedMarket))
                        {
                            market = mappedMarket;
                        }
                        csv.WriteField(market);
                        
                        // タイプ（NYダウ構成銘柄は全て個別株）
                        csv.WriteField("stock");
                        csv.NextRecord();
                    }
                }
            }
            
            _logger.LogInformation("Successfully exported NY Dow symbol list to {FilePath}", csvPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export NY Dow symbol list to CSV");
            throw;
        }
    }

    public async Task ExportIndexListToCsvAsync(string csvPath, bool forceUpdate = false)
    {
        try
        {
            _logger.LogInformation("Exporting index list to CSV...");
            
            // 指標リストを取得
            List<StockSymbol> indices;
            if (forceUpdate)
            {
                indices = await _indexListService.ForceUpdateMajorIndicesAsync();
            }
            else
            {
                indices = await _indexListService.GetMajorIndicesAsync();
            }
            
            // 出力ディレクトリが存在しない場合は作成
            var outputDirectory = Path.GetDirectoryName(csvPath);
            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
            
            _logger.LogInformation("Writing {Count} indices to {FilePath}", indices.Count, csvPath);
            
            // Shift-JISエンコーディングでCSVファイルを作成
            using (var writer = new StreamWriter(csvPath, false, Encoding.GetEncoding(932)))
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                };
                
                using (var csv = new CsvWriter(writer, config))
                {
                    // ヘッダーを手動で書き込み
                    csv.WriteField("code");
                    csv.WriteField("name");
                    csv.WriteField("market");
                    csv.WriteField("type");
                    csv.NextRecord();
                    
                    // データを書き込み
                    foreach (var index in indices)
                    {
                        csv.WriteField(index.Symbol);
                        csv.WriteField(index.Name);
                        csv.WriteField(index.Market);
                        csv.WriteField(index.Type);
                        csv.NextRecord();
                    }
                }
            }
            
            _logger.LogInformation("Successfully exported index list to {FilePath}", csvPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export index list to CSV");
            throw;
        }
    }
    
    // 同期版メソッド（互換性のため残す）
    public void ExportIndexListToCsv(string csvPath)
    {
        ExportIndexListToCsvAsync(csvPath).GetAwaiter().GetResult();
    }

    public async Task ExportBuffettListToCsvAsync(string csvPath, bool forceUpdate = false)
    {
        try
        {
            _logger.LogInformation("Exporting Buffett portfolio list to CSV...");
            
            // バフェット銘柄を取得
            List<StockSymbol> buffettSymbols;
            if (forceUpdate)
            {
                await _buffettCacheService.ForceUpdateAsync();
            }
            buffettSymbols = await _buffettCacheService.GetSymbolsAsync();
            
            // 出力ディレクトリが存在しない場合は作成
            var outputDirectory = Path.GetDirectoryName(csvPath);
            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
            
            _logger.LogInformation("Writing {Count} Buffett portfolio symbols to {FilePath}", buffettSymbols.Count, csvPath);
            
            // 銘柄コードと企業名のマッピング
            var companyNames = new Dictionary<string, string>
            {
                { "AAPL", "Apple" },
                { "BAC", "Bank of America" },
                { "KO", "Coca-Cola" },
                { "AXP", "American Express" },
                { "BK", "Bank of New York Mellon" },
                { "CHTR", "Charter Communications" },
                { "CVX", "Chevron" },
                { "KHC", "Kraft Heinz" },
                { "MCO", "Moody's" },
                { "OXY", "Occidental Petroleum" },
                { "VZ", "Verizon" },
                { "AMZN", "Amazon" },
                { "SIRI", "Sirius XM" },
                { "HPQ", "HP" },
                { "PG", "Procter & Gamble" },
                { "JNJ", "Johnson & Johnson" },
                { "USB", "U.S. Bancorp" },
                { "GM", "General Motors" },
                { "DVA", "DaVita" },
                { "MCK", "McKesson" },
                { "PFE", "Pfizer" },
                { "ABBV", "AbbVie" },
                { "SNOW", "Snowflake" },
                { "PARA", "Paramount Global" },
                { "CE", "Celanese" },
                { "MA", "Mastercard" },
                { "V", "Visa" },
                { "TMUS", "T-Mobile" },
                { "ALLY", "Ally Financial" },
                { "C", "Citigroup" }
            };
            
            // 企業名と日本語名のマッピング
            var japaneseNames = new Dictionary<string, string>
            {
                { "Apple", "アップル" },
                { "Bank of America", "バンク・オブ・アメリカ" },
                { "Coca-Cola", "コカ・コーラ" },
                { "American Express", "アメリカン・エキスプレス" },
                { "Bank of New York Mellon", "バンク・オブ・ニューヨーク・メロン" },
                { "Charter Communications", "チャーター・コミュニケーションズ" },
                { "Chevron", "シェブロン" },
                { "Kraft Heinz", "クラフト・ハインツ" },
                { "Moody's", "ムーディーズ" },
                { "Occidental Petroleum", "オキシデンタル・ペトロリアム" },
                { "Verizon", "ベライゾン" },
                { "Amazon", "アマゾン" },
                { "Sirius XM", "シリウスXM" },
                { "HP", "ヒューレット・パッカード" },
                { "Procter & Gamble", "プロクター・アンド・ギャンブル" },
                { "Johnson & Johnson", "ジョンソン・エンド・ジョンソン" },
                { "U.S. Bancorp", "USバンコープ" },
                { "General Motors", "ゼネラル・モーターズ" },
                { "DaVita", "ダヴィータ" },
                { "McKesson", "マッケソン" },
                { "Pfizer", "ファイザー" },
                { "AbbVie", "アッヴィ" },
                { "Snowflake", "スノーフレイク" },
                { "Paramount Global", "パラマウント・グローバル" },
                { "Celanese", "セラニーズ" },
                { "Mastercard", "マスターカード" },
                { "Visa", "ビザ" },
                { "T-Mobile", "Tモバイル" },
                { "Ally Financial", "アライ・フィナンシャル" },
                { "Citigroup", "シティグループ" }
            };
            
            // 銘柄コードとマーケットのマッピング
            var marketMapping = new Dictionary<string, string>
            {
                { "AAPL", "NASDAQ" },
                { "AMZN", "NASDAQ" },
                { "CHTR", "NASDAQ" },
                { "SIRI", "NASDAQ" },
                { "PEP", "NASDAQ" },
                { "TMUS", "NASDAQ" },
                { "SNOW", "NYSE" },
                { "PARA", "NASDAQ" }
            };
            
            // 銘柄コードとタイプのマッピング (デフォルトはstock)
            var typeMapping = new Dictionary<string, string>
            {
                // ETFがある場合はここに追加
            };
            
            // Shift-JISエンコーディングでCSVファイルを作成
            using (var writer = new StreamWriter(csvPath, false, Encoding.GetEncoding(932)))
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                };
                
                using (var csv = new CsvWriter(writer, config))
                {
                    // ヘッダーを手動で書き込み
                    csv.WriteField("code");
                    csv.WriteField("name");
                    csv.WriteField("market");
                    csv.WriteField("type");
                    csv.NextRecord();
                    
                    // データを書き込み
                    foreach (var symbol in buffettSymbols)
                    {
                        // 銘柄コード
                        string code = symbol.Symbol;
                        csv.WriteField(code);
                        
                        // 企業名と日本語名
                        string englishName = string.Empty;
                        
                        // Wikipediaから取得した名前が不十分な場合、マッピングから取得
                        if (!companyNames.TryGetValue(code, out englishName!))
                        {
                            englishName = symbol.Name;
                            if (string.IsNullOrWhiteSpace(englishName))
                            {
                                englishName = code;
                            }
                        }
                        
                        // 日本語名を追加
                        string japaneseName = string.Empty;
                        foreach (var kvp in japaneseNames)
                        {
                            if (englishName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                japaneseName = kvp.Value;
                                break;
                            }
                        }
                        
                        // 最終的な表示名を構築
                        string fullName;
                        if (!string.IsNullOrEmpty(japaneseName))
                        {
                            fullName = $"{englishName} {japaneseName}";
                        }
                        else
                        {
                            fullName = englishName;
                        }
                        
                        csv.WriteField(fullName);
                        
                        // マーケット情報
                        string market = "NYSE"; // デフォルトはNYSE
                        if (marketMapping.TryGetValue(code, out var mappedMarket))
                        {
                            market = mappedMarket;
                        }
                        csv.WriteField(market);
                        
                        // タイプ情報（デフォルトはstock）
                        string type = "stock";
                        if (typeMapping.TryGetValue(code, out var mappedType))
                        {
                            type = mappedType;
                        }
                        csv.WriteField(type);
                        csv.NextRecord();
                    }
                }
            }
            
            _logger.LogInformation("Successfully exported Buffett portfolio list to {FilePath}", csvPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export Buffett portfolio list to CSV");
            throw;
        }
    }
}
