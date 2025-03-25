using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using System.Linq;
using System.Text.RegularExpressions;
using SBIFetcherTest.Models;

namespace SBIFetcherTest.Services
{
    /// <summary>
    /// SBI証券から米国株式の銘柄情報を取得するサービス
    /// </summary>
    public class SBIStockFetcher
    {
        private readonly ILogger<SBIStockFetcher> _logger;

        public SBIStockFetcher(ILogger<SBIStockFetcher> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// SBI証券から普通株式の一覧を取得します
        /// </summary>
        /// <returns>取得した銘柄情報のリスト</returns>
        public async Task<List<StockSymbol>> FetchStockSymbolsAsync()
        {
            var symbols = new List<StockSymbol>();
            
            try
            {
                _logger.LogInformation("SBI証券から普通株式一覧を取得します...");
                
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
                        "--disable-dev-shm-usage"
                    }
                };
                
                using var browser = await Puppeteer.LaunchAsync(launchOptions);
                using var page = await browser.NewPageAsync();
                
                // SBI証券の米国株式一覧ページにアクセス
                await page.GoToAsync("https://search.sbisec.co.jp/v2/popwin/info/stock/pop6040_usequity_list.html");
                
                // ページが完全に読み込まれるまで待機
                await page.WaitForSelectorAsync("table#DataTables_Table_0");
                
                // 「全件」表示オプションを選択するJavaScriptを実行
                _logger.LogInformation("「全件」表示オプションを選択します...");
                
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
                
                _logger.LogInformation("「全件」表示オプションを選択しました。データが読み込まれるまで待機します...");
                
                // データが読み込まれるまで待機（最大30秒）
                await Task.Delay(5000);
                
                // テーブルの行数を確認
                var rowCount = await page.EvaluateExpressionAsync<int>(@"
                    document.querySelectorAll('table#DataTables_Table_0 tbody tr').length
                ");
                
                _logger.LogInformation("テーブルの行数: {RowCount}", rowCount);
                
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
                    _logger.LogInformation("{Count}行のデータを処理します...", rows.Count);
                    
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
                
                _logger.LogInformation("SBI証券から{Count}銘柄の情報を取得しました", symbols.Count);
                
                // スクリーンショットを保存（デバッグ用）
                await page.ScreenshotAsync("sbi_screenshot.png");
                _logger.LogInformation("スクリーンショットを保存しました: sbi_screenshot.png");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SBI証券からの銘柄情報取得中にエラーが発生しました");
            }
            
            return symbols;
        }
    }
}
