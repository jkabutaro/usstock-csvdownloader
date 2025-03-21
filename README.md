# US Stock Price Downloader

Yahoo Finance APIを使用して、S&P 500やNYダウなどの株価データをダウンロードするツールです。

## 機能

- S&P 500銘柄の自動取得とダウンロード
- NYダウ銘柄の自動取得とダウンロード
- バフェットのポートフォリオ銘柄の自動取得とダウンロード
- カスタム銘柄リストからのダウンロード
- 並列ダウンロードによる高速化
- エラー時の自動リトライ
- レート制限への対応
- CSVフォーマットでのデータ保存

## 使い方

```bash
# S&P 500銘柄のダウンロード
dotnet run -- --sp500

# NYダウ銘柄のダウンロード
dotnet run -- --nyd

# バフェット銘柄のダウンロード
dotnet run -- --buffett

# カスタム銘柄リストからのダウンロード
dotnet run -- --file symbols.txt

# 個別銘柄の指定（カンマ区切り）
dotnet run -- --symbols AAPL,MSFT,GOOGL

# 期間を指定してダウンロード（2024年1月1日から2024年12月31日まで）
dotnet run -- --symbols AAPL --start-date 2024-01-01 --end-date 2024-12-31
```

## オプション

- `--sp500`: S&P 500銘柄をダウンロード
- `--nyd`: NYダウ銘柄をダウンロード
- `--buffett`: バフェットのポートフォリオ銘柄をダウンロード
- `--file <path>`: 指定したファイルから銘柄リストを読み込み
- `--symbols <symbols>`: カンマ区切りで個別銘柄を指定（例：AAPL,MSFT,GOOGL）
- `--start-date <date>`: 履歴データの開始日（形式：yyyy-MM-dd、デフォルト：1年前）
- `--end-date <date>`: 履歴データの終了日（形式：yyyy-MM-dd、デフォルト：今日）
- `--max-concurrent <num>`: 並列ダウンロード数（デフォルト: 3）
- `--max-retries <num>`: リトライ回数（デフォルト: 3）
- `--retry-delay <ms>`: リトライ間隔（ミリ秒、デフォルト: 1000）
- `--exponential-backoff`: 指数バックオフを使用（デフォルト: true）

## エラー処理

- HTTP 429（レート制限）: 60秒待機後に再試行
- その他のエラー: 設定された間隔で再試行
- 指数バックオフ: リトライ毎に待機時間を2倍に
- ジッター: 同時リトライを分散させるためのランダム遅延

## データ形式

ダウンロードしたデータは `Data` ディレクトリに銘柄ごとのCSVファイルとして保存されます。

```csv
Date,Open,High,Low,Close,Volume
20240225,180.15,182.34,179.89,181.56,75234567
```

- `Date`: 日付（yyyyMMdd形式の数値）
- `Open`: 始値
- `High`: 高値
- `Low`: 安値
- `Close`: 終値
- `Volume`: 出来高

## キャッシュ

S&P 500とNYダウの銘柄リストは `Cache` ディレクトリにキャッシュされ、再利用されます。

## 注意事項

- ピリオドを含む銘柄（BRK.B、BF.B）は特別な処理が必要
- 一部の銘柄でデータが欠落する可能性あり
- Yahoo Finance APIの利用制限に注意

## 依存関係

- .NET 9.0
- Polly: リトライ処理
- CsvHelper: CSV操作
- HtmlAgilityPack: HTML解析
