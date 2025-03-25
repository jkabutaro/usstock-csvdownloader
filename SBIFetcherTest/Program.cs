// See https://aka.ms/new-console-template for more information
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SBIFetcherTest.Services;
using System.Text;
using CsvHelper;
using System.Globalization;
using SBIFetcherTest.Models;

// サービスコレクションを設定
var serviceCollection = new ServiceCollection();

// ロギングを追加
serviceCollection.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// SBIStockFetcherを登録
serviceCollection.AddTransient<SBIStockFetcher>();

// サービスプロバイダーを構築
var serviceProvider = serviceCollection.BuildServiceProvider();

// ロガーを取得
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
logger.LogInformation("SBI証券からの銘柄取得テストを開始します...");

// SBIStockFetcherを取得
var fetcher = serviceProvider.GetRequiredService<SBIStockFetcher>();

// 銘柄情報を取得
Console.WriteLine("SBI証券から銘柄情報を取得しています...");
var symbols = await fetcher.FetchStockSymbolsAsync();

// 結果を表示
logger.LogInformation("取得した銘柄数: {Count}", symbols.Count);

// 最初の10件を表示
logger.LogInformation("最初の10件:");
for (int i = 0; i < Math.Min(10, symbols.Count); i++)
{
    var symbol = symbols[i];
    logger.LogInformation("  {Index}: {Symbol} - {Name} ({Market})", 
        i + 1, symbol.Symbol, symbol.Name, symbol.Market);
}

// 結果をCSVファイルに保存
if (symbols.Count > 0)
{
    var outputDir = "output";
    Directory.CreateDirectory(outputDir);
    var outputPath = Path.Combine(outputDir, "sbi_stocks.csv");
    
    using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
    using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
    {
        csv.WriteRecords(symbols);
    }
    
    logger.LogInformation("結果をCSVファイルに保存しました: {Path}", Path.GetFullPath(outputPath));
}
