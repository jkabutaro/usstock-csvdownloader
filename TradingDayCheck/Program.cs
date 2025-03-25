using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using USStockDownloader.Services;
using USStockDownloader.Models;

namespace TradingDayCheck
{
    /// <summary>
    /// 営業日チェック機能のテスト用プログラム
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("営業日チェック機能とリトライなしでの株価データ取得のテストを開始します (Starting trading day check and stock data fetch test without retry)");
            
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
                (yesterday, today, "昨日から今日 (Yesterday to today)"),
                (lastWeek, today, "先週から今日 (Last week to today)"),
                (lastMonth, today, "先月から今日 (Last month to today)"),
                (new DateTime(2025, 3, 22), new DateTime(2025, 3, 23), "週末のみ（土日） (Weekend only - Sat/Sun)"),
                (new DateTime(2024, 12, 25), new DateTime(2024, 12, 25), "クリスマス（休日） (Christmas - Holiday)"),
                (new DateTime(2025, 1, 1), new DateTime(2025, 1, 1), "元日（休日） (New Year's Day - Holiday)")
            };
            
            // 各テストケースを実行
            foreach (var (start, end, description) in testCases)
            {
                Console.WriteLine($"\n===== テストケース: {description} =====");
                Console.WriteLine($"期間: {start:yyyy-MM-dd} から {end:yyyy-MM-dd} (Period)");
                
                try
                {
                    // 営業日チェック
                    Console.WriteLine("営業日チェックを実行中... (Checking for trading days...)");
                    var hasTradingDays = await stockDataService.CheckTradingDayRangeAsync(start, end);
                    
                    Console.WriteLine($"結果: {(hasTradingDays ? "営業日あり" : "営業日なし")} (Trading days: {(hasTradingDays ? "Yes" : "No")})");
                    
                    // 実際にデータを取得してみる
                    Console.WriteLine("AAPLのデータを取得してみます (Fetching AAPL data)");
                    var stockData = await stockDataService.GetStockDataAsync("AAPL", start, end);
                    Console.WriteLine($"取得データ数: {stockData.Count} 件 (Data points retrieved)");
                    
                    if (stockData.Count > 0)
                    {
                        Console.WriteLine("最初のデータポイント (First data point):");
                        var firstData = stockData[0];
                        Console.WriteLine($"  日付: {firstData.DateTime:yyyy-MM-dd} (Date)");
                        Console.WriteLine($"  始値: {firstData.Open} (Open)");
                        Console.WriteLine($"  高値: {firstData.High} (High)");
                        Console.WriteLine($"  安値: {firstData.Low} (Low)");
                        Console.WriteLine($"  終値: {firstData.Close} (Close)");
                        Console.WriteLine($"  出来高: {firstData.Volume} (Volume)");
                        
                        if (stockData.Count > 1)
                        {
                            Console.WriteLine("最後のデータポイント (Last data point):");
                            var lastData = stockData[stockData.Count - 1];
                            Console.WriteLine($"  日付: {lastData.DateTime:yyyy-MM-dd} (Date)");
                            Console.WriteLine($"  始値: {lastData.Open} (Open)");
                            Console.WriteLine($"  高値: {lastData.High} (High)");
                            Console.WriteLine($"  安値: {lastData.Low} (Low)");
                            Console.WriteLine($"  終値: {lastData.Close} (Close)");
                            Console.WriteLine($"  出来高: {lastData.Volume} (Volume)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"エラー: {ex.Message} (Error)");
                    Console.WriteLine($"スタックトレース: {ex.StackTrace} (Stack trace)");
                }
            }
            
            Console.WriteLine("\n営業日チェック機能とリトライなしでの株価データ取得のテストが完了しました (Trading day check and stock data fetch test without retry completed)");
            Console.WriteLine("Enterキーを押して終了してください (Press Enter to exit)");
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
