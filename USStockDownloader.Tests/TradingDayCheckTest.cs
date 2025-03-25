using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using USStockDownloader.Services;

namespace USStockDownloader.Tests
{
    /// <summary>
    /// 営業日チェック機能のテスト用クラス
    /// </summary>
    public class TradingDayCheckTest
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("営業日チェック機能のテストを開始します (Starting trading day check test)");
            
            // サービスプロバイダーを設定
            var serviceProvider = ConfigureServices();
            var stockDataService = serviceProvider.GetRequiredService<IStockDataService>();
            
            // テスト用の日付を設定
            var today = DateTime.Now.Date;
            var yesterday = today.AddDays(-1);
            var lastWeek = today.AddDays(-7);
            var lastMonth = today.AddMonths(-1);
            
            // テストケース
            var testCases = new[]
            {
                (yesterday, today, "昨日から今日"),
                (lastWeek, today, "先週から今日"),
                (lastMonth, today, "先月から今日"),
                (new DateTime(2025, 3, 22), new DateTime(2025, 3, 23), "週末のみ（土日）"),
                (new DateTime(2024, 12, 25), new DateTime(2024, 12, 25), "クリスマス（休日）"),
                (new DateTime(2025, 1, 1), new DateTime(2025, 1, 1), "元日（休日）")
            };
            
            // 各テストケースを実行
            foreach (var (start, end, description) in testCases)
            {
                Console.WriteLine($"\n===== テストケース: {description} =====");
                Console.WriteLine($"期間: {start:yyyy-MM-dd} から {end:yyyy-MM-dd}");
                
                try
                {
                    // 営業日チェック
                    var hasTradingDays = await stockDataService.CheckTradingDayRangeAsync(start, end);
                    
                    Console.WriteLine($"結果: {(hasTradingDays ? "営業日あり" : "営業日なし")} (Trading days: {(hasTradingDays ? "Yes" : "No")})");
                    
                    // 実際にデータを取得してみる
                    Console.WriteLine("AAPLのデータを取得してみます (Fetching AAPL data)");
                    var stockData = await stockDataService.GetStockDataAsync("AAPL", start, end);
                    Console.WriteLine($"取得データ数: {stockData.Count} 件 (Data points retrieved)");
                    
                    if (stockData.Count > 0)
                    {
                        Console.WriteLine("最初のデータポイント:");
                        var firstData = stockData[0];
                        Console.WriteLine($"  日付: {firstData.DateTime:yyyy-MM-dd}");
                        Console.WriteLine($"  始値: {firstData.Open}");
                        Console.WriteLine($"  高値: {firstData.High}");
                        Console.WriteLine($"  安値: {firstData.Low}");
                        Console.WriteLine($"  終値: {firstData.Close}");
                        Console.WriteLine($"  出来高: {firstData.Volume}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"エラー: {ex.Message}");
                }
            }
            
            Console.WriteLine("\n営業日チェック機能のテストが完了しました (Trading day check test completed)");
            Console.ReadLine();
        }
        
        private static IServiceProvider ConfigureServices()
        {
            // サービスコレクションを作成
            var services = new ServiceCollection();
            
            // ロガーを登録
            services.AddLogging(builder => 
            {
                builder.AddConsole();
            });
            
            // HTTPクライアントを登録
            services.AddHttpClient();
            
            // サービスを登録
            services.AddSingleton<IStockDataService, StockDataService>();
            
            // サービスプロバイダーを構築
            return services.BuildServiceProvider();
        }
    }
}
