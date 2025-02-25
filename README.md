# US Stock Price Downloader

Yahoo Finance APIを使用して、S&P 500やNYダウなどの株価データをダウンロードするツールです。

## 機能

- S&P 500銘柄の自動取得とダウンロード
- NYダウ銘柄の自動取得とダウンロード
- バフェットのポートフォリオ銘柄の自動取得とダウンロード
- カスタム銘柄リストからのダウンロード
- 個別銘柄の指定によるダウンロード
- 並列ダウンロードによる高速化
- エラー時の自動リトライ
- レート制限への対応
- CSVフォーマットでのデータ保存
- 動作環境の自動チェック
  - Windows 10以降の確認
  - .NET 9.0 Runtimeの確認
- 実行時のキャッシュ管理
  - 前回の環境チェック結果の保存
  - 銘柄リストのキャッシュ
  - 取引時間に基づくデータ更新制御

## 使い方

```bash
# S&P 500銘柄のダウンロード
USStockDownloader.exe --sp500

# NYダウ銘柄のダウンロード
USStockDownloader.exe --nyd

# バフェット銘柄のダウンロード
USStockDownloader.exe --buffett

# カスタム銘柄リストからのダウンロード
USStockDownloader.exe --file symbols.txt

# 個別銘柄の指定（カンマ区切り）
USStockDownloader.exe --symbols AAPL,MSFT,GOOGL

# リトライ設定を変更してダウンロード
USStockDownloader.exe --file symbols.txt --max-retries 5 --retry-delay 5000 --rate-limit-delay 120000
```

## オプション

- `--sp500`: S&P 500銘柄をダウンロード
- `--nyd`: NYダウ銘柄をダウンロード
- `--buffett`: バフェットのポートフォリオ銘柄をダウンロード
- `--file <path>`: 指定したファイルから銘柄リストを読み込み
- `--symbols <symbols>`: カンマ区切りで個別銘柄を指定（例：AAPL,MSFT,GOOGL）
- `--max-concurrent <num>`: 並列ダウンロード数（デフォルト: 3）
- `--max-retries <num>`: リトライ回数（デフォルト: 3）
- `--retry-delay <ms>`: リトライ間隔（ミリ秒、デフォルト: 1000）
- `--rate-limit-delay <ms>`: レート制限時の待機時間（ミリ秒、デフォルト: 60000）
- `--exponential-backoff`: 指数バックオフを使用（デフォルト: true）

## システム要件

- Windows 10以降
- .NET 9.0 Runtime
- インターネット接続（Yahoo Finance APIへのアクセスに必要）

## エラー処理

- HTTP 429（レート制限）: デフォルトで60秒待機後に再試行（`--rate-limit-delay`で調整可能）
- その他のエラー: 設定された間隔で再試行
- 指数バックオフ: リトライ毎に待機時間を2倍に
- ジッター: 同時リトライを分散させるためのランダム遅延（待機時間の±20%）

## データ形式

ダウンロードしたデータは、`Data` ディレクトリに銘柄ごとのCSVファイルとして保存されます。

```csv
Date,Open,High,Low,Close,Volume
2024-02-25,180.15,182.34,179.89,181.56,75234567
```

- `Date`: 日付（yyyy-MM-dd形式）
- `Open`: 始値
- `High`: 高値
- `Low`: 安値
- `Close`: 終値
- `Volume`: 出来高

## キャッシュ

- S&P 500とNYダウの銘柄リストは `Cache` ディレクトリにキャッシュされ、再利用されます。
- 取引時間内（米国東部時間 9:30-16:00）は常に最新データを取得します。
- 取引時間外は1時間以上経過していて、かつ土日と米国の主要な祝日以外の場合は更新します。
- 土日と米国の主要な祝日は市場が閉まっているため、キャッシュを使用します。

## 注意事項

- ピリオドを含む銘柄（BRK.B、BF.B）は特別な処理が必要
- 一部の銘柄でデータが欠落する可能性あり
- Yahoo Finance APIの利用制限に注意
  - レート制限に遭遇した場合は `--rate-limit-delay` を増やしてください
  - 並列ダウンロード数 `--max-concurrent` を減らすことで制限を回避できる場合があります

## 開発ログ

### 2025-02-25
- `--symbols`オプションを復活
  - カンマ区切りで複数の銘柄を指定可能（例：`--symbols AAPL,MSFT,GOOGL`）
- リトライロジックの改善
  - レート制限時の処理を強化（`RateLimitException`と`HttpStatusCode.TooManyRequests`に対応）
  - より詳細なログ出力を追加
  - エクスポネンシャルバックオフとジッターを実装

### 次回の課題
1. Yahoo Finance APIのレート制限対策
   - リトライ間隔の最適化（現在は初回5秒、レート制限時30秒）
   - 同時ダウンロード数の調整（現在は3並列）
   - テスト時の銘柄数を制限（1-2銘柄から開始）
2. エラーハンドリングの強化
   - 失敗した銘柄のレポート機能
   - リトライ後も失敗した場合の処理改善

## 依存関係

- .NET 9.0
- Polly: リトライ処理
- CsvHelper: CSV操作
- HtmlAgilityPack: HTML解析
