# US Stock CSV Downloader

米国株式の株価データをYahoo Financeから一括ダウンロードするツール。

## 機能

### 基本機能
- Yahoo Finance APIを使用した株価データのダウンロード
- CSVフォーマットでのデータ保存
- 並列ダウンロード（デフォルト3並列）
- S&P 500銘柄の自動取得（Wikipediaから）
- ニューヨークダウ工業株30種の銘柄の自動取得（Wikipediaから）
- S&P 500銘柄リストのCSV出力（銘柄コード、名前、市場、種別情報を含む）
- NYダウ銘柄リストのCSV出力（銘柄コード、名前（日本語名付き）、市場、種別情報を含む）
- 主要指数リストのCSV出力
- バフェットポートフォリオ銘柄リストのCSV出力（Wikipediaから）
- Yahoo Finance APIで取得できる全銘柄リストのCSV出力

### エラー処理とリトライ機能
- Pollyライブラリを使用したリトライ機構
  - 指数バックオフとジッターによる再試行間隔の制御
  - HTTP 429（レート制限）の特別処理
  - 詳細なエラーログ記録

### データ検証機能
- 価格データの妥当性チェック
  - 価格が0以上であることの確認
  - High/Lowの関係チェック
  - Open/CloseがHigh/Lowの範囲内にあることの確認
- 欠損データの検出と除外

### テスト機能
- 全銘柄取得機能のテスト（Yahoo Finance、SBI証券などの複数ソースから）
- 取得した銘柄の市場別・タイプ別の内訳表示

### エラーレポート機能
- 失敗した銘柄の詳細レポート生成
- エラータイプごとの集計
- 再試行結果の記録

## パフォーマンス指標

- S&P 500銘柄のダウンロード成功率：99.4%（503銘柄中500銘柄成功）
- 失敗した銘柄：ETR、BRK.B、BF.B
  - 主な失敗理由：ピリオドを含むシンボルの処理とデータ欠落

## リポジトリ情報

このプロジェクトは以下のGitHubリポジトリで公開されています：
- リポジトリURL: https://github.com/jkabutaro/usstock-csvdownloader
- 開発者: jkabutaro

## 使用方法

### システム要件
- Windows 10以降
- .NET 9.0 Runtime
- インターネット接続（Yahoo Finance APIへのアクセスに必要）

### インストール
```bash
git clone https://github.com/jkabutaro/usstock-csvdownloader.git
cd USStockDownloader
dotnet restore
```

### 実行オプション

#### 基本的な使い方
```bash
# S&P 500銘柄を自動取得してダウンロード
dotnet run -- --sp500

# S&P 500銘柄リストを強制的に更新してダウンロード
dotnet run -- --sp500f

# NYダウ銘柄リストを自動取得してダウンロード
dotnet run -- -n

# NYダウ銘柄リストを強制的に更新してダウンロード
dotnet run -- --nyd-f

# NYダウ銘柄リストをCSVファイルに出力
dotnet run -- --nyd --listcsv output/nyd_list.csv

# NYダウ銘柄リストを強制的に更新してCSVファイルに出力
dotnet run -- --nyd --nyd-f --listcsv output/nyd_list.csv

# NYダウ構成銘柄リストをCSVファイルに出力（Shift-JISエンコーディング）
dotnet run -- --nyd --listcsv

# バフェットポートフォリオの銘柄を自動取得してダウンロード
dotnet run -- -b

# バフェットポートフォリオの銘柄リストを強制的に更新してダウンロード
dotnet run -- --buffett-f

# バフェットポートフォリオの銘柄リストをCSVファイルに出力
dotnet run -- --buffett --listcsv output/buffett_list.csv

# バフェットポートフォリオの銘柄リストを強制的に更新してCSVファイルに出力
dotnet run -- --buffett --buffett-f --listcsv output/buffett_list.csv

# 全銘柄リストをCSVファイルに出力
dotnet run -- --all --listcsv output/all_stock_list.csv

# 全銘柄リストを強制的に更新してCSVファイルに出力
dotnet run -- --all --all-f --listcsv output/all_stock_list.csv

# 全銘柄取得機能のテスト実行
dotnet run -- --testall

# 特定の銘柄リストをダウンロード
dotnet run -- --file symbols.txt

# 個別銘柄を指定してダウンロード（カンマ区切り）
dotnet run -- --symbols AAPL,MSFT,GOOGL

# S&P 500銘柄リストをCSVファイルに出力
dotnet run -- --sp500 --listcsv output/sp500_list.csv

# 主要指数リストをCSVファイルに出力
dotnet run -- --index --listcsv output/index_list.csv

# 主要指数リストを強制的に更新してCSVファイルに出力
dotnet run -- --index --indexf --listcsv output/index_list.csv

# Yahoo Finance APIで取得できる全銘柄リストをCSVファイルに出力
dotnet run -- --all --listcsv output/all_stocks_list.csv
```

#### 利用可能なオプション
| オプション | 説明 | デフォルト値 |
|------------|------|--------------|
| `--sp500` | S&P 500銘柄を自動取得してダウンロード | - |
| `--sp500f` | S&P 500銘柄リストを強制的に更新してダウンロード | - |
| `-n, --nyd` | NYダウ銘柄リストを自動取得してダウンロード | - |
| `--nyd-f` | NYダウ銘柄リストを強制的に更新 | - |
| `-b, --buffett` | バフェットポートフォリオの銘柄を自動取得してダウンロード | - |
| `--buffett-f` | バフェットポートフォリオの銘柄リストを強制的に更新してダウンロード | - |
| `--file <path>` | 指定したファイルから銘柄リストを読み込み | - |
| `--symbols <symbols>` | カンマ区切りで個別銘柄を指定（例：AAPL,MSFT,GOOGL） | - |
| `--concurrent <num>` | 並列ダウンロード数 | 3 |
| `--max-retries <num>` | リトライ回数 | 3 |
| `--retry-delay <ms>` | 初回リトライ時の待機時間（ミリ秒） | 5000 |
| `--rate-limit-delay <ms>` | レート制限時の待機時間（ミリ秒） | 30000 |
| `--exponential-backoff` | 指数バックオフを使用（true/false） | true |
| `--jitter` | ジッターを使用（true/false） | true |
| `--output-dir <path>` | 出力ディレクトリを指定 | ./output |
| `--start-date <date>` | データ取得開始日（yyyy-MM-dd形式） | 1年前 |
| `--end-date <date>` | データ取得終了日（yyyy-MM-dd形式） | 現在 |
| `--listcsv <path>` | 銘柄リストをCSVファイルに出力（相対パスを指定） | - |
| `--index` | 主要指数を使用 | - |
| `--indexf` | 主要指数リストを強制的に更新 | - |
| `--all` | Yahoo Finance APIで取得できる全銘柄をリストアップ | - |

## データ形式

### 株価データCSVファイル

ダウンロードしたデータは、`output` ディレクトリに銘柄ごとのCSVファイルとして保存されます。

#### CSVファイル形式
```csv
Date,Open,High,Low,Close,AdjClose,Volume
20240225,180.15,182.34,179.89,181.56,181.56,75234567
```

| カラム | 説明 | 型 |
|--------|------|-----|
| Date | 取引日（yyyymmdd形式の整数値） | 整数 |
| Open | 始値 | 数値 |
| High | 高値 | 数値 |
| Low | 安値 | 数値 |
| Close | 終値 | 数値 |
| AdjClose | 調整後終値 | 数値 |
| Volume | 出来高 | 整数 |

### 銘柄リストCSVファイル（--listcsvオプション使用時）

`--listcsv`オプションを使用すると、銘柄リストがCSVファイルとして出力されます。出力されるCSVファイルの形式は以下の通りです：

```csv
code,name,market,type
A,Agilent Technologies アジレント テクノロジーズ,NYSE,stock
AA,Alcoa アルコア,NYSE,stock
AACI,Armada Acquisition Corp1 アルマダ アクイジション1,NASDAQ,stock
AADI,Aadi Biosciences Inc アーディ バイオサイエンシズ,NASDAQ,stock
```

| カラム | 説明 | 型 |
|--------|------|-----|
| code | ティッカーシンボル | 文字列 |
| name | 銘柄名（日本語名付き） | 文字列 |
| market | 市場(NYSEやNASDAQ) | 文字列 |
| type | 種類(stockやindexやetf) | 文字列 |

NYダウ銘柄リスト（`--nyd --listcsv`）の場合は、Shift-JISエンコーディングで出力され、企業名に日本語名が付加されます。出力ファイル名はデフォルトで`us_stock_list.csv`となります。その他のリスト（S&P 500、バフェットポートフォリオなど）はUTF-8エンコーディングで出力されます。

## 免責事項と注意点

### データソースについて
本アプリケーションは以下のソースから銘柄情報を取得しています：
1. **S&P 500銘柄リスト**
   - 取得元: Wikipediaの「List of S&P 500 companies」ページ
   - URL: https://en.wikipedia.org/wiki/List_of_S%26P_500_companies
   - 取得方法: HTMLテーブルのスクレイピング
   - 更新頻度: 24時間キャッシュ（`--sp500f`オプションで強制更新可能）

2. **NYダウ銘柄リスト**
   - 取得元: Wikipediaの「Dow Jones Industrial Average」ページ
   - URL: https://en.wikipedia.org/wiki/Dow_Jones_Industrial_Average
   - 取得方法: HTMLテーブルのスクレイピング
   - 更新頻度: 24時間キャッシュ（`--nyd-f`オプションで強制更新可能）
   - 出力ファイル: `us_stock_list.csv`（`--nyd --listcsv`オプション使用時）
   - 出力形式: 銘柄コード、企業名（日本語名付き）、マーケット情報、タイプ情報
   - エンコーディング: Shift-JIS（日本語環境での利用を考慮）

3. **バフェットポートフォリオ銘柄リスト**
   - 取得元: Wikipediaの「List of assets owned by Berkshire Hathaway」ページ
   - URL: https://en.wikipedia.org/wiki/List_of_assets_owned_by_Berkshire_Hathaway
   - 取得方法: HTMLテーブルのスクレイピング
   - 更新頻度: 24時間キャッシュ（`--buffett-f`オプションで強制更新可能）

4. **主要指数リスト**
   - 取得元: Yahoo Financeの世界指数ページ（https://finance.yahoo.com/world-indices）
   - 取得方法: HTMLテーブルのスクレイピング
   - フォールバック: スクレイピングに失敗した場合は内部定義のデフォルトリストを使用
   - 更新頻度: 24時間キャッシュ（`--indexf`オプションで強制更新可能）

5. **全銘柄リスト**
   - 取得元: Yahoo Finance API
   - 取得方法: APIリクエスト
   - 更新頻度: リクエストごとに最新情報を取得

これらの情報は以下の点に注意してください：
- Wikipediaの記事内容はいつでも変更される可能性があり、その結果として正確な情報が取得できない場合があります
- Wikipediaのページ構造が変更されると、データ抽出ロジックが機能しなくなる可能性があります
- 最新の情報を確実に反映しているという保証はありません
- 重要な投資判断には、公式の情報源からデータを確認することを強く推奨します

これらの制限を軽減するため、本アプリケーションでは以下の対策を講じています：
- データ取得に失敗した場合のフォールバックメカニズム
- キャッシュの強制更新オプション（`--sp500-f`、`--nyd-f`、`--buffett-f`）
- 詳細なエラーログとレポート機能

## バフェットポートフォリオ銘柄について

バフェットポートフォリオ銘柄（`-b`または`--buffett`オプションで指定）は、バークシャー・ハサウェイ社が保有する株式銘柄を指します。これらの銘柄は、投資家のウォーレン・バフェットが選定したものとして知られています。

### データソース

バフェットポートフォリオの銘柄リストは、以下の方法で取得しています：
1. Wikipediaの「List of assets owned by Berkshire Hathaway」ページからスクレイピングして取得
2. スクレイピングに失敗した場合、主要な保有銘柄のフォールバックリストを使用

### 注意事項

- バフェットポートフォリオの内容は定期的に変更される可能性があります
- 最新の情報を取得するには、`--buffett-f`オプションを使用して強制更新してください
- データは24時間キャッシュされます（特に指定がない場合）

### 使用例

```bash
# バフェットポートフォリオの銘柄を使用してダウンロード
dotnet run -- -b

# バフェットポートフォリオの銘柄リストを強制的に更新してダウンロード
dotnet run -- -b --buffett-f

# バフェットポートフォリオの銘柄リストをCSVファイルに出力
dotnet run -- --buffett --listcsv output/buffett_list.csv

# バフェットポートフォリオの銘柄リストを強制的に更新してCSVファイルに出力
dotnet run -- --buffett --buffett-f --listcsv output/buffett_list.csv
```

## データ形式
ダウンロードしたデータは、`output` ディレクトリに銘柄ごとのCSVファイルとして保存されます。

#### CSVファイル形式
```csv
Date,Open,High,Low,Close,AdjClose,Volume
20240225,180.15,182.34,179.89,181.56,181.56,75234567
```

| カラム | 説明 | 型 |
|--------|------|-----|
| Date | 取引日（yyyymmdd形式の整数値） | 整数 |
| Open | 始値 | 数値 |
| High | 高値 | 数値 |
| Low | 安値 | 数値 |
| Close | 終値 | 数値 |
| AdjClose | 調整後終値 | 数値 |
| Volume | 出来高 | 整数 |

## エラー処理
- HTTP 429（レート制限）
  - デフォルトで30秒待機後に再試行
  - `--rate-limit-delay` で待機時間を調整可能
- その他のエラー
  - 設定された間隔（`--retry-delay`）で再試行
  - 指数バックオフ有効時は待機時間が2倍ずつ増加
  - ジッター有効時は待機時間に±20%のランダム変動を追加

## 出力ファイル
1. **株価データ**
   - 場所: `output/<symbol>.csv`
   - 形式: 上記CSVファイル形式
   - 例: `output/AAPL.csv`

2. **エラーレポート**
   - 場所: `output/failed_symbols_report.txt`
   - 内容:
     - 失敗した銘柄のリスト
     - エラータイプごとの集計
     - 再試行結果の詳細

3. **銘柄リストCSV**
   - 場所: `output/us_stock_list.csv`
   - 内容:
     - S&P 500銘柄のリスト（`--sp500 --listcsv`で出力）
     - 形式: code,name,market,type
     - エンコーディング: Shift-JIS
   - 例:
     ```csv
     code,name,market,type
     AAPL,Apple,NASDAQ,stock
     MSFT,Microsoft,NASDAQ,stock
     ```

4. **主要指数リストCSV**
   - 場所: `output/us_index_list.csv`
   - 内容:
     - 主要指数のリスト（`--index --listcsv`で出力）
     - 形式: code,name,market,type
     - エンコーディング: Shift-JIS
   - 例:
     ```csv
     code,name,market,type
     ^DJI,NYダウ（Dow Jones Industrial Average DJIA）,,index
     ^GSPC,S&P 500（Standard & Poor's 500）,,index
     ^IXIC,ナスダック総合指数（NASDAQ Composite）,,index
     ^RUT,ラッセル2000指数（Russell 2000 Index）,,index
     ```

5. **バフェットポートフォリオ銘柄リストCSV**
   - 場所: `output/us_stock_list.csv`
   - 内容:
     - バフェットポートフォリオの銘柄リスト（`--buffett --listcsv`で出力）
     - 形式: code,name,market,type
     - エンコーディング: Shift-JIS
   - 例:
     ```csv
     code,name,market,type
     AAPL,Apple アップル,NASDAQ,stock
     AMZN,Amazon アマゾン,NASDAQ,stock
     ```

6. **全銘柄リストCSV**
   - 場所: `output/all_stocks_list.csv`
   - 内容:
     - Yahoo Finance APIで取得できる全銘柄のリスト（`--all --listcsv`で出力）
     - 形式: code,name,market,type
     - エンコーディング: Shift-JIS
   - 例:
     ```csv
     code,name,market,type
     AAPL,Apple Inc.,NASDAQ,stock
     MSFT,Microsoft Corporation,NASDAQ,stock
     ```

7. **ダウンロードログ**
   - 場所: `logs/download_<timestamp>.log`
   - 内容:
     - 詳細な実行ログ
     - エラーメッセージ
     - パフォーマンス情報

## 使用例とヒント

#### 基本的な使い方
1. **S&P 500全銘柄のダウンロード**
   ```bash
   dotnet run -- --sp500
   ```
   - Wikipediaから最新のS&P 500銘柄リストを取得
   - 全銘柄の1年分のデータをダウンロード
   - キャッシュされた銘柄リストがある場合はそれを使用（更新が必要な場合は`--sp500f`オプションを使用）

2. **NYダウ銘柄リストのダウンロード**
   ```bash
   dotnet run -- --nyd
   ```
   - Wikipediaから最新のNYダウ銘柄リストを取得
   - 全銘柄の1年分のデータをダウンロード
   - S&P 500に比べて銘柄数が少ないため短時間で完了

3. **カスタム銘柄リストの使用**
   ```bash
   dotnet run -- --file my_symbols.txt
   ```
   - `my_symbols.txt`の形式: 1行1銘柄
   ```
   AAPL
   MSFT
   GOOGL
   ```

4. **特定銘柄のダウンロード**
   ```bash
   dotnet run -- --symbols AAPL,MSFT,GOOGL
   ```
   - カンマ区切りで複数銘柄を指定可能

5. **日付範囲を指定してダウンロード**
   ```bash
   dotnet run -- --sp500 --start-date 2023-01-01 --end-date 2024-12-31
   ```
   - 特定期間のデータのみを取得

6. **S&P 500銘柄リストのCSV出力**
   ```bash
   dotnet run -- --sp500 --listcsv output/sp500_list.csv
   ```
   - S&P 500銘柄のリストをCSVファイルに出力

7. **主要指数リストのCSV出力**
   ```bash
   dotnet run -- --index --listcsv output/index_list.csv
   ```
   - 主要指数のリストをCSVファイルに出力

8. **主要指数リストを強制的に更新してCSVファイルに出力**
   ```bash
   dotnet run -- --index --indexf --listcsv output/index_list.csv
   ```
   - 主要指数リストを強制的に更新してCSVファイルに出力

9. **バフェットポートフォリオ銘柄リストのCSV出力**
   ```bash
   dotnet run -- --buffett --listcsv output/buffett_list.csv
   ```
   - バフェットポートフォリオの銘柄リストをCSVファイルに出力

10. **バフェットポートフォリオ銘柄リストを強制的に更新してCSVファイルに出力**
    ```bash
    dotnet run -- --buffett --buffett-f --listcsv output/buffett_list.csv
    ```
    - バフェットポートフォリオの銘柄リストを強制的に更新してCSVファイルに出力

11. **全銘柄リストのCSV出力**
    ```bash
    dotnet run -- --all --listcsv output/all_stocks_list.csv
    ```
    - Yahoo Finance APIで取得できる全銘柄のリストをCSVファイルに出力

#### パフォーマンスチューニング

1. **並列数の調整**
   ```bash
   dotnet run -- --sp500 --concurrent 5
   ```
   - 並列数を増やすとダウンロードが高速化
   - ただしレート制限に注意

2. **リトライ設定の最適化**
   ```bash
   dotnet run -- --sp500 --max-retries 5 --retry-delay 10000
   ```
   - エラー時のリトライ回数と間隔を調整
   - レート制限が頻発する場合は間隔を長く

3. **レート制限対策**
   ```bash
   dotnet run -- --sp500 --rate-limit-delay 60000 --concurrent 2
   ```
   - レート制限が頻発する場合の推奨設定
   - 待機時間を長く、並列数を少なく

#### トラブルシューティング

1. **レート制限エラーが頻発する場合**
   - `--rate-limit-delay`を60000（60秒）に増やす
   - `--concurrent`を2に減らす
   - `--jitter`をtrueに設定して同時リクエストを分散

2. **特定の銘柄でエラーが発生する場合**
   - エラーレポート（`failed_symbols_report.txt`）を確認
   - `--max-retries`を増やして再試行
   - 問題が解決しない場合はその銘柄をスキップ

3. **メモリ使用量が高い場合**
   - `--concurrent`を減らして並列数を制限
   - 処理する銘柄数を分割して実行

## パフォーマンス指標

### 成功率
- S&P 500銘柄のダウンロード成功率：99.4%（最新のテストで503銘柄中500銘柄成功）
- 主な改善点：
  - ピリオドを含むシンボル（BRK.B、BF.B）の処理を改善
  - レート制限への対応を強化
  - データ検証機能の追加

### 実行時間
- 3並列実行時の平均処理時間：約10分（S&P 500全銘柄）
- 1銘柄あたりの平均処理時間：約1秒

## 実装の詳細

### コンポーネント構成
1. **StockDataService**
   - Yahoo Finance APIからのデータ取得
   - Pollyを使用したリトライロジック
   - エラー処理とログ記録

2. **StockDownloadManager**
   - 並列ダウンロードの制御
   - セマフォを使用した同時実行数の制限
   - 失敗した銘柄の追跡と再試行

3. **SP500CacheService**
   - WikipediaからのS&P 500銘柄の取得
   - HTMLパース処理

4. **StockDataCache**
   - ダウンロードした株価データのキャッシュ管理
   - 不要なAPI呼び出しの防止
   - 市場時間に基づく更新判断ロジック

### キャッシュ機能の詳細
#### 概要
- アプリケーションはダウンロードした株価データをローカルにキャッシュし、不要なAPI呼び出しの防止します
- キャッシュは銘柄ごとに管理され、各銘柄の最終更新時刻とデータの日付範囲を記録します
- キャッシュの状態に基づいて、データを再取得するかどうかを判断します

#### キャッシュの保存場所
- ファイル: `%LocalAppData%\USStockDownloader\stock_data_cache.json`
- 形式: JSON形式でシンボルごとのキャッシュ情報を保存

#### キャッシュ情報の構造
```json
{
  "AAPL": {
    "Symbol": "AAPL",
    "LastUpdate": "2025-02-26T12:00:00",
    "StartDate": "2024-02-26T00:00:00",
    "EndDate": "2025-02-26T00:00:00"
  },
  "MSFT": {
    // 他の銘柄情報...
  }
}
```

#### キャッシュの更新条件
以下の条件を満たす場合に、データが再取得されます：
1. **市場取引時間内の場合**
   - 米国東部時間の取引時間（9:30-16:00）内は常に最新データを取得
   - 土日および米国の祝日は市場が閉まっているためキャッシュを使用

2. **キャッシュの鮮度**
   - 市場取引時間外の場合、最後の更新から1時間以上経過していれば更新
   - 頻繁に実行しても不要なAPI呼び出しが発生しない設計

3. **日付範囲の拡張**
   - 要求された日付範囲がキャッシュの範囲外の場合（より古いデータや新しいデータが必要な場合）
   - 例: キャッシュが2024-01-01から2025-02-01までのデータを持ち、2023-01-01から2025-02-26までのデータが要求された場合

#### 市場時間の判定ロジック
- 米国東部時間（東部標準時/東部夏時間）への変換
- 夏時間の自動判定
- 米国の祝日（元日、MLKデー、プレジデントデー、メモリアルデー、独立記念日、レイバーデー、サンクスギビング、クリスマス）の判定
- タイムゾーン取得に失敗した場合のフォールバックメカニズム

#### キャッシュ機能の利点
1. **パフォーマンスの向上**
   - 不要なAPI呼び出しの削減
   - ダウンロード時間の短縮
   - レート制限の回避

2. **API利用の最適化**
   - Yahoo Finance APIの利用を必要最小限に抑制
   - レート制限エラーの発生頻度を低減

3. **オフライン対応**
   - 一度ダウンロードしたデータは、市場閉場時にインターネット接続なしでも利用可能

4. **使用例**
   ```bash
   # 初回実行時: すべてのデータをダウンロード
   dotnet run -- --symbols AAPL,MSFT,GOOGL
   
   # 同日の再実行時（市場閉場時）: キャッシュを使用
   dotnet run -- --symbols AAPL,MSFT,GOOGL
   
   # 市場取引時間内の実行: 最新データを取得
   dotnet run -- --symbols AAPL,MSFT,GOOGL
   ```

### 依存関係
- Polly: リトライ処理
- CsvHelper: CSV操作
- HtmlAgilityPack: HTML解析

## 最近のアップデート

### コマンドライン引数処理の改善（2025-02-26）
アプリケーションのユーザビリティを向上させるため、コマンドライン引数処理を改善しました：
1. **ヘルプメッセージの強化**
   - 引数なしで実行した場合に自動的にヘルプを表示
   - `--help`または`-h`オプションでヘルプを表示
   - 詳細なオプション説明と使用例の提供

2. **エラーハンドリングの強化**
   - 無効な引数の検出と適切なエラーメッセージの表示
   - 必須引数（シンボルソース）が指定されていない場合の具体的なガイダンス
   - エラーメッセージ表示後にヘルプ情報を提供

3. **使用例**
   ```bash
   # ヘルプの表示
   dotnet run -- --help
   
   # 無効な引数を指定した場合（エラーメッセージとヘルプが表示されます）
   dotnet run -- --invalid-option
   
   # 引数なしで実行した場合（ヘルプが表示されます）
   dotnet run
   ```

## 現在の課題

### 改善が必要な項目
1. **パフォーマンスモニタリング**
   - ダウンロード速度の測定機能の追加
   - メモリ使用量の監視
   - エラー率の追跡
   - パフォーマンス統計レポートの生成

2. **レポート機能**
   - 詳細な進捗レポートの実装
   - パフォーマンス統計の追加
   - エラー分析の強化
   - ダウンロード履歴の管理

3. **データ品質**
   - より厳密なデータ検証ルールの追加
   - 異常値の検出アルゴリズムの改善
   - 欠損データの補完方法の実装
   - データ整合性チェックの強化

4. **運用性**
   - 設定のカスタマイズ機能
   - バッチ処理のスケジュール機能
   - エラー通知システムの実装
   - 自動リカバリー機能の追加

### 将来の拡張計画
1. **機能拡張**
   - 複数の取引所対応
   - リアルタイムデータの取得
   - テクニカル指標の計算
   - データ分析機能の追加

2. **インフラ**
   - クラウドでの実行対応
   - データベース統合
   - APIサービス化
   - スケーラビリティの向上

3. **ユーザビリティ**
   - WebUIの実装
   - バッチ処理の設定UI
   - データ可視化機能
   - レポートのカスタマイズ

## ライセンス
MIT License

## 貢献
プルリクエストやイシューの報告を歓迎します.
