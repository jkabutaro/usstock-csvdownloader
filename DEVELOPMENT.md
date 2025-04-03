# 開発記録

## プロジェクト概要

米国株価のヒストリカルデータをCSVでダウンロードするWindowsアプリケーション

### 主な機能

- Yahoo Finance APIを使用して株価データを取得
- 複数銘柄の並列ダウンロード
- CSVファイルへの保存（日付は数値形式yyyyMMdd）

## 大事なこと

- 想像で実装しないこと
- わからないことは聞くこと
- ファイルや関数が大きくなってきたら、分割して思考しやすくすること


## 開発状況

### 2025-02-24の作業内容

1. 基本機能の実装
   - Yahoo Finance APIからのデータ取得機能
   - CSVファイルへの保存機能
   - 並列ダウンロード機能（デフォルト3並列）
   - エラーハンドリングとロギング

2. CSVフォーマットの調整
   - Symbol列を出力から除外
   - Date列をyyyyMMdd形式の数値として出力
   - 出力列順序: Date, Open, High, Low, Close, Volume

3. プロジェクト構成
   ```
   USStockDownloader/
   ├── Models/
   │   ├── StockData.cs          # 株価データモデル
   │   ├── StockDataMap.cs       # CSVマッピング定義
   │   └── StockSymbol.cs        # 銘柄情報モデル
   ├── Services/
   │   ├── IStockDataService.cs  # データ取得インターフェース
   │   ├── StockDataService.cs   # Yahoo Finance API実装
   │   └── StockDownloadManager.cs # 並列ダウンロード管理
   ├── Options/
   │   ├── DownloadOptions.cs    # ダウンロード設定
   │   └── RetryOptions.cs       # リトライ設定
   └── Exceptions/
       └── StockDataException.cs  # カスタム例外
   ```

### 2025-03-25の作業内容

1. 上場廃止銘柄の処理改善
   - 上場廃止銘柄を検出する機能を実装
   - 上場廃止銘柄用のHashSetを追加して追跡
   - 上場廃止銘柄の場合は空のCSVファイルを作成
   - エラーログを改善し、上場廃止銘柄の場合は明示的に記録

2. エラー処理の強化
   - `IsSymbolDelisted`メソッドをインターフェースに追加
   - 上場廃止銘柄の場合は不要なリトライを回避
   - Yahoo Financeからの特定のエラーレスポンスを解析して上場廃止を検出

3. READMEの整理と簡略化
   - パフォーマンス指標セクションを削除
   - ビルドと配布セクションを簡略化
   - キャッシュファイルセクションを簡潔に再構成
   - データソースセクションを簡略化
   - 更新履歴セクションを削除
   - 使用例とヒントセクションを整理

4. 実装の詳細
   ```csharp
   // 上場廃止銘柄を追跡するためのHashSetを追加
   private readonly HashSet<string> _delistedSymbols = new HashSet<string>();
   
   // 上場廃止銘柄かどうかを確認するメソッド
   public bool IsSymbolDelisted(string symbol)
   {
       return _delistedSymbols.Contains(symbol);
   }
   
   // 上場廃止銘柄の検出と処理
   if (content.Contains("\"code\":\"Not Found\"") && content.Contains("\"description\":\"No data found, symbol may be delisted\""))
   {
       _logger.LogWarning("{Symbol}のデータが見つかりません。シンボルは上場廃止されている可能性があります。リトライを中止します。 (No data found for symbol, may be delisted. Skipping retry.)", symbol);
       _delistedSymbols.Add(symbol);
       return new List<StockData>();
   }
   ```

### 2025-03-26の作業内容

1. **SQLiteキャッシュの導入**
   - 取引日キャッシュをSQLiteで管理するように変更
   - データなし期間を月単位から日単位で管理するように改善
   - RecordNoDataPeriodメソッドを追加し、任意の日付範囲を記録可能に

2. **リトライ機構の強化**
   - RetryOptionsクラスにDelayとTimeoutプロパティを追加
   - Pollyを使用したリトライ処理を実装
   - 指数バックオフとジッターを適用
   - HTTP 429エラーの特別処理を追加

3. **特殊シンボルの処理改善**
   - BRK.B、BF.B、ETRなどの特殊シンボルに対する特別処理を実装
   - APIリクエスト時にピリオド(.)をハイフン(-)に自動変換
   - ETRシンボルに対するレート制限回避のための追加待機時間を設定

4. **配布用ビルドの最適化**
   - シングルファイル形式でのビルドを実装
   - トリミングを無効にすることでリフレクションベースの機能を確保
   - SQLitePCLRaw.bundle_e_sqlite3を追加し、ネイティブライブラリの依存関係を解決

5. **エラーハンドリングの改善**
   - 詳細なエラーレポート機能を追加
   - ログメッセージの形式を「日本語メッセージ (English message)」に統一
   - エラーログの構造化とファイル出力を改善

### Yahoo Finance APIリクエストの最適化 (Yahoo Finance API Request Optimization)

### HTTPヘッダーの設定 (HTTP Header Configuration)
Yahoo Finance APIへのリクエストには、以下のヘッダーが必須です。これらを設定しないと、レート制限エラー（429 Too Many Requests）が発生する可能性があります。
(The following headers are required for Yahoo Finance API requests. Without these, you may encounter rate limit errors (429 Too Many Requests).)

```csharp
request.Headers.Add("User-Agent", "Mozilla/5.0");
request.Headers.Add("Accept", "application/json");
request.Headers.Add("Referer", "https://finance.yahoo.com/");
```

### 注意点 (Important Notes)
1. **ヘッダーサイズ (Header Size)**: ヘッダーが大きすぎると「431 Request Header Fields Too Large」エラーが発生するため、必要最小限のヘッダーを使用してください。(If headers are too large, you may encounter a "431 Request Header Fields Too Large" error. Use only the minimum necessary headers.)
2. **レート制限 (Rate Limits)**: リクエストが多すぎるとレート制限に引っかかります。Pollyなどのリトライライブラリを使用して、一時的なエラーに対処してください。(Too many requests will trigger rate limits. Use retry libraries like Polly to handle temporary errors.)
3. **シンボルのエンコード (Symbol Encoding)**: シンボルにピリオド（例: `BRK.B`）が含まれる場合は、ハイフン（例: `BRK-B`）に変換してください。(If a symbol contains a period (e.g., `BRK.B`), convert it to a hyphen (e.g., `BRK-B`).)

### リトライ処理の例 (Retry Processing Example)
```csharp
var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .OrResult<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.TooManyRequests)
    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
```

### ログ出力 (Logging)
リクエストURLとレスポンスステータスコードをログに出力し、デバッグを容易にします。
(Log the request URL and response status code to facilitate debugging.)

```csharp
_logger.LogDebug("リクエストURL: {Url} (Request URL)", url);
_logger.LogDebug("レスポンスステータスコード: {StatusCode} (Response status code)", response.StatusCode);
```

### 2025-03-26の改善内容 (Improvements as of 2025-03-26)
1. **HTTPヘッダーの最適化 (HTTP Header Optimization)**: 
   - 必要最小限のヘッダーのみを使用することで、「431 Request Header Fields Too Large」エラーを防止
   - (Using only the minimum necessary headers to prevent "431 Request Header Fields Too Large" errors)
2. **APIリクエスト処理の改善 (API Request Processing Improvement)**:
   - リクエスト実行時の余分なヘッダー設定を削除
   - (Removed unnecessary header settings during request execution)
3. **URLの簡素化 (URL Simplification)**:
   - 重複するパラメータを削除し、必要最小限のパラメータのみを使用
   - (Removed duplicate parameters and used only the minimum necessary parameters)

### 実装済み機能の詳細

1. データ取得（StockDataService）
   - Yahoo Finance APIを使用
   - 日次の株価データを取得
   - エラー時のリトライ機能

2. CSVファイル管理
   - 既存ファイルとの自動マージ
   - 重複データの除去
   - 日付降順でのソート

3. エラーハンドリング
   - API通信エラーの処理
   - ファイル入出力エラーの処理
   - カスタム例外によるエラー情報の詳細化

### 今後の課題（更新）

1. 機能拡張
   - [ ] データ取得期間の指定機能
   - [ ] 株価調整値（Adjusted Close）の追加
   - [ ] 異常値の検出と除外
   - [ ] 上場廃止銘柄のリスト管理と永続化

2. 改善点
   - [ ] ユニットテストの追加
   - [ ] API呼び出しの最適化
   - [ ] エラー時のリカバリー機能強化
   - [x] 上場廃止銘柄の処理改善

3. ドキュメント
   - [ ] APIドキュメントの作成
   - [ ] エラーコードの体系化
   - [ ] 設定値のリファレンス作成
   - [x] READMEの整理と簡略化

## 開発環境

- .NET 8.0
- C#
- Windows
- 主要パッケージ:
  - CsvHelper
  - Microsoft.Extensions.Logging
  - CommandLineParser

## 使用方法

1. 基本的な使用方法:
   ```bash
   dotnet run --project USStockDownloader -- -f symbols.csv
   ```

2. オプション:
   - `-f, --file`: シンボルファイルのパス（必須）
   - `-p, --parallel`: 並列ダウンロード数（デフォルト: 3）
   - `-r, --retries`: リトライ回数（デフォルト: 3）
   - `-d, --delay`: リトライ間隔（ミリ秒、デフォルト: 1000）
   - `-e, --exponential`: 指数バックオフの使用（デフォルト: true）
