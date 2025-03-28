using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using System.Linq;
using System.Text.RegularExpressions;
using USStockDownloader.Models;
using Polly;
using Polly.Retry;
using System.Net.Http;
using System.IO;
using System.Text.Json;

namespace USStockDownloader.Services
{
    /// <summary>
    /// SBI証券から米国株式の銘柄情報を取得するサービス
    /// </summary>
    public class SBIStockFetcher
    {
        private readonly ILogger<SBIStockFetcher> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly string _cacheFilePath;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromDays(7); // キャッシュの有効期間（7日）

        public SBIStockFetcher(ILogger<SBIStockFetcher> logger)
        {
            _logger = logger;
            
            // リトライポリシーを設定
            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    3, // 最大リトライ回数
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // 指数バックオフ
                    (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(exception, 
                            "SBI証券へのアクセス中にエラーが発生しました。{RetryCount}回目のリトライを{TimeSpan}秒後に実行します。", 
                            retryCount, timeSpan.TotalSeconds);
                    }
                );
                
            // キャッシュファイルのパスを設定
            _cacheFilePath = Utils.CacheManager.GetCacheFilePath("sbi_symbols_cache.json");
        }

        /// <summary>
        /// SBI証券から普通株式の一覧を取得します。キャッシュがある場合はキャッシュから読み込みます。
        /// </summary>
        /// <returns>取得した銘柄情報のリスト</returns>
        public async Task<List<StockSymbol>> FetchStockSymbolsAsync()
        {
            // キャッシュが有効な場合はキャッシュから読み込む
            var cachedSymbols = await LoadFromCacheAsync();
            if (cachedSymbols != null && cachedSymbols.Count > 0)
            {
                _logger.LogDebug($"キャッシュから{cachedSymbols.Count}件の銘柄情報を読み込みました。");
                return cachedSymbols;
            }
            
            // キャッシュがない場合は新たに取得
            return await FetchAndCacheSymbolsAsync();
        }
        
        /// <summary>
        /// キャッシュを無視してSBI証券から普通株式の一覧を強制的に取得します。
        /// </summary>
        /// <returns>取得した銘柄情報のリスト</returns>
        public async Task<List<StockSymbol>> ForceUpdateAsync()
        {
            _logger.LogDebug("SBI証券の銘柄情報を強制的に更新します...");
            return await FetchAndCacheSymbolsAsync();
        }
        
        /// <summary>
        /// SBI証券から普通株式の一覧を取得してキャッシュに保存します。
        /// </summary>
        /// <returns>取得した銘柄情報のリスト</returns>
        private async Task<List<StockSymbol>> FetchAndCacheSymbolsAsync()
        {
            var symbols = new List<StockSymbol>();
            
            try
            {
                // リトライポリシーを適用
                symbols = await _retryPolicy.ExecuteAsync(async () =>
                {
                    _logger.LogDebug("SBI証券から普通株式一覧を取得します...");
                    
                    // Puppeteerをインストール（既存のChromiumがあれば再利用）
                    var browserFetcher = new BrowserFetcher();
                    await browserFetcher.DownloadAsync();
                    
                    // ブラウザを起動
                    var launchOptions = new LaunchOptions
                    {
                        Headless = true,
                        Args = new[] { 
                            "--no-sandbox", 
                            "--disable-setuid-sandbox",
                            "--disable-dev-shm-usage",
                            "--disable-web-security",
                            "--disable-features=IsolateOrigins,site-per-process",
                            "--disable-extensions",
                            "--disable-gpu",
                            "--ignore-certificate-errors",
                            "--disable-infobars",
                            "--window-size=1920,1080",
                            "--proxy-server='direct://'",
                            "--proxy-bypass-list=*"
                        },
                        // IgnoreHTTPSErrors = true, // 一部のバージョンでは使用できない
                        Timeout = 120000 // ブラウザ起動のタイムアウトを2分に設定
                    };
                    
                    using var browser = await Puppeteer.LaunchAsync(launchOptions);
                    using var page = await browser.NewPageAsync();
                    
                    // タイムアウトを設定
                    // PuppeteerSharpのバージョンによっては、これらのメソッドが利用できない場合があります
                    // await page.SetDefaultNavigationTimeoutAsync(90000); // 90秒
                    // await page.SetDefaultTimeoutAsync(90000); // 90秒
                    
                    _logger.LogDebug("SBI証券の米国株式一覧ページにアクセスします...");
                    
                    // SBI証券の米国株式一覧ページにアクセス
                    string url = "https://search.sbisec.co.jp/v2/popwin/info/stock/pop6040_usequity_list.html";
                    _logger.LogDebug("SBI証券の米国株式一覧ページにアクセスします: {Url}", url);
                    
                    try
                    {
                        await page.GoToAsync(url, 
                            new NavigationOptions 
                            { 
                                Timeout = 120000, // タイムアウトを2分に延長
                                WaitUntil = new[] { WaitUntilNavigation.Load } // Networkidle0ではなくLoadに変更
                            });
                        
                        _logger.LogDebug("ページへのアクセスに成功しました。テーブルの読み込みを待機します...");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("ページへのアクセス中にエラーが発生しました: {Url} - {ErrorMessage}", url, ex.Message);
                        throw; // 上位のリトライポリシーでキャッチするために再スロー
                    }
                    
                    // ページが完全に読み込まれるまで待機
                    await page.WaitForSelectorAsync("table#DataTables_Table_0", new WaitForSelectorOptions { Timeout = 30000 });
                    
                    // 「全件」表示オプションを選択するJavaScriptを実行
                    _logger.LogDebug("「全件」表示オプションを選択します...");
                    
                    var result = await page.EvaluateExpressionAsync<bool>(@"
                        (() => {
                            try {
                                // セレクトボックスを取得
                                const select = document.querySelector('select[name=""DataTables_Table_0_length""]');
                                if (!select) {
                                    console.error('セレクトボックスが見つかりません');
                                    return false;
                                }
                                
                                // 「全件」オプションを選択
                                select.value = '-1';
                                
                                // 変更イベントを発火
                                const event = new Event('change', { bubbles: true });
                                select.dispatchEvent(event);
                                
                                return true;
                            } catch (error) {
                                console.error('エラーが発生しました:', error);
                                return false;
                            }
                        })()
                    ");
                    
                    if (!result)
                    {
                        _logger.LogWarning("「全件」表示オプションの選択に失敗しました");
                        return symbols;
                    }
                    
                    _logger.LogDebug("「全件」表示オプションを選択しました。データが読み込まれるまで待機します...");
                    
                    // データが読み込まれるまで待機（最大10秒）
                    await Task.Delay(10000);
                    
                    // テーブルの行数を確認
                    var rowCount = await page.EvaluateExpressionAsync<int>(@"
                        document.querySelectorAll('table#DataTables_Table_0 tbody tr').length
                    ");
                    
                    _logger.LogDebug("テーブルの行数: {RowCount}", rowCount);
                    
                    if (rowCount == 0)
                    {
                        _logger.LogWarning("テーブルに行がありません");
                        return symbols;
                    }
                    
                    // テーブルのHTMLを取得
                    var tableHtml = await page.EvaluateExpressionAsync<string>(@"
                        document.querySelector('table#DataTables_Table_0').outerHTML
                    ");
                    
                    // HTMLをパース
                    var htmlDoc = new HtmlDocument();
                    htmlDoc.LoadHtml(tableHtml);
                    
                    // テーブルの行を処理
                    var rows = htmlDoc.DocumentNode.SelectNodes("//tbody/tr");
                    if (rows != null)
                    {
                        _logger.LogDebug("{Count}行のデータを処理します...", rows.Count);
                        
                        foreach (var row in rows)
                        {
                            // thとtdが3つある行だけが銘柄情報のある行
                            var thElement = row.SelectSingleNode(".//th");
                            var tdElements = row.SelectNodes(".//td");
                            
                            if (thElement != null && tdElements != null && tdElements.Count >= 3)
                            {
                                // ティッカーシンボル（th）
                                var ticker = thElement.InnerText.Trim();
                                
                                // 銘柄名（td1つ目）
                                var name = tdElements[0].InnerText.Trim();
                                
                                // マーケット情報（td3つ目）
                                var marketText = tdElements[2].InnerText.Trim();
                                string market = "US"; // デフォルト値
                                
                                if (marketText.Contains("NASDAQ"))
                                {
                                    market = "NASDAQ";
                                }
                                else if (marketText.Contains("NYSE"))
                                {
                                    market = "NYSE";
                                }
                                
                                if (!string.IsNullOrEmpty(ticker))
                                {
                                    symbols.Add(new StockSymbol
                                    {
                                        Symbol = ticker,
                                        Name = name,
                                        Market = market,
                                        Type = "stock" // 普通株式
                                    });
                                }
                            }
                        }
                    }
                    
                    _logger.LogDebug("SBI証券から{Count}銘柄の情報を取得しました", symbols.Count);
                    
                    // スクリーンショットを保存（デバッグ用）
                    await page.ScreenshotAsync("sbi_screenshot.png");
                    _logger.LogDebug("スクリーンショットを保存しました: sbi_screenshot.png");
                    
                    // 取得したデータをキャッシュに保存
                    await SaveToCacheAsync(symbols);
                    
                    return symbols;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("SBI証券からの銘柄情報取得中にエラーが発生しました: {ErrorMessage}", ex.Message);
                return new List<StockSymbol>();
            }
            
            return symbols;
        }
        
        /// <summary>
        /// キャッシュから銘柄情報を読み込みます。
        /// </summary>
        /// <returns>キャッシュから読み込んだ銘柄情報のリスト。キャッシュがない場合や期限切れの場合はnull。</returns>
        private async Task<List<StockSymbol>?> LoadFromCacheAsync()
        {
            try
            {
                if (!File.Exists(_cacheFilePath))
                {
                    _logger.LogDebug("キャッシュファイルが存在しません。");
                    return null;
                }
                
                // ファイルの最終更新日時を確認
                var fileInfo = new FileInfo(_cacheFilePath);
                if (DateTime.Now - fileInfo.LastWriteTime > _cacheExpiration)
                {
                    _logger.LogDebug("キャッシュの有効期限が切れています。");
                    return null;
                }
                
                // キャッシュファイルを読み込む
                var json = await File.ReadAllTextAsync(_cacheFilePath);
                var cacheData = JsonSerializer.Deserialize<CacheData>(json);
                
                if (cacheData == null)
                {
                    _logger.LogWarning("キャッシュデータのデシリアライズに失敗しました。");
                    return null;
                }
                
                return cacheData.Symbols;
            }
            catch (Exception ex)
            {
                _logger.LogError("キャッシュからの読み込み中にエラーが発生しました: {ErrorMessage}", ex.Message);
                return new List<StockSymbol>();
            }
        }
        
        /// <summary>
        /// 銘柄情報をキャッシュに保存します。
        /// </summary>
        /// <param name="symbols">保存する銘柄情報のリスト</param>
        private async Task SaveToCacheAsync(List<StockSymbol> symbols)
        {
            try
            {
                var cacheData = new CacheData
                {
                    Timestamp = DateTime.Now,
                    Symbols = symbols
                };
                
                var json = JsonSerializer.Serialize(cacheData, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(_cacheFilePath, json);
                _logger.LogDebug($"{symbols.Count}件の銘柄情報をキャッシュに保存しました。");
            }
            catch (Exception ex)
            {
                _logger.LogError("キャッシュへの保存中にエラーが発生しました: {ErrorMessage}", ex.Message);
            }
        }
        
        /// <summary>
        /// キャッシュデータを格納するクラス
        /// </summary>
        private class CacheData
        {
            public DateTime Timestamp { get; set; }
            public List<StockSymbol> Symbols { get; set; } = new List<StockSymbol>();
        }
    }
}
