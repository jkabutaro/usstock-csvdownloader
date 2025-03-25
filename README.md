# 米国株式CSVダウンローダー (US Stock CSV Downloader)

米国株式の株価データをYahoo Financeから一括ダウンロードするツール。
(A tool for batch downloading US stock price data from Yahoo Finance.)

## 機能 (Features)

### 基本機能 (Basic Features)
- Yahoo Finance APIを使用した株価データのダウンロード (Download stock price data using Yahoo Finance API)
- CSVフォーマットでのデータ保存 (Save data in CSV format)
- 並列ダウンロード（デフォルト3並列） (Parallel download, default 3 concurrent)
- S&P 500銘柄の自動取得（Wikipediaから） (Automatic retrieval of S&P 500 symbols from Wikipedia)
- ニューヨークダウ工業株30種の銘柄の自動取得（Wikipediaから） (Automatic retrieval of Dow Jones Industrial Average 30 symbols from Wikipedia)
- S&P 500銘柄リストのCSV出力（銘柄コード、名前、市場、種別情報を含む） (CSV output of S&P 500 symbol list including ticker code, name, market, and type information)
- NYダウ銘柄リストのCSV出力（銘柄コード、名前（日本語名付き）、市場、種別情報を含む） (CSV output of NY Dow symbol list including ticker code, name (with Japanese name), market, and type information)
- 主要指数リストのCSV出力 (CSV output of major indices list)
- バフェットポートフォリオ銘柄リストのCSV出力（Wikipediaから） (CSV output of Buffett portfolio symbol list from Wikipedia)
- SBI証券取扱いの米国株式データのダウンロード (Download US stock data handled by SBI Securities)
- SBI証券取扱いの銘柄リストのCSV出力 (CSV output of symbol list handled by SBI Securities)

### エラー処理とリトライ機能 (Error Handling and Retry Features)
- Pollyライブラリを使用したリトライ機構 (Retry mechanism using Polly library)
  - 指数バックオフとジッターによる再試行間隔の制御 (Control retry intervals with exponential backoff and jitter)
  - HTTP 429（レート制限）の特別処理 (Special handling for HTTP 429 rate limit)
  - 詳細なエラーログ記録 (Detailed error logging)

### データ検証機能 (Data Validation Features)
- 価格データの妥当性チェック (Price data validity check)
  - 価格が0以上であることの確認 (Verify prices are greater than or equal to 0)
  - High/Lowの関係チェック (Check relationship between High and Low)
  - Open/CloseがHigh/Lowの範囲内にあることの確認 (Verify Open/Close are within High/Low range)
- 欠損データの検出と除外 (Detection and exclusion of missing data)

### テスト機能 (Test Features)
- 全銘柄取得機能のテスト（Yahoo Finance、SBI証券などの複数ソースから） (Test all symbol acquisition functions from multiple sources including Yahoo Finance and SBI Securities)
- 取得した銘柄の市場別・タイプ別の内訳表示 (Display breakdown of acquired symbols by market and type)

### エラーレポート機能 (Error Reporting Features)
- 失敗した銘柄の詳細レポート生成 (Generate detailed reports for failed symbols)
- エラータイプごとの集計 (Aggregation by error type)
- 再試行結果の記録 (Record retry results)

## パフォーマンス指標 (Performance Metrics)

- S&P 500銘柄のダウンロード成功率：100%（503銘柄中503銘柄成功） (Download success rate for S&P 500 symbols: 100% (503 out of 503 symbols successful))
- 全銘柄のダウンロードに成功（2025-03-25 テスト実施） (Successfully downloaded all symbols (tested on 2025-03-25))

## リポジトリ情報 (Repository Information)

このプロジェクトは以下のGitHubリポジトリで公開されています：
(This project is published in the following GitHub repository:)
- リポジトリURL (Repository URL): https://github.com/jkabutaro/usstock-csvdownloader
- 開発者 (Developer): jkabutaro

## ダウンロードとインストール (Download and Installation)

### 方法1: ビルド済み実行可能ファイル（推奨） (Method 1: Pre-built Executable (Recommended))
最も簡単な方法は、ビルド済みの実行可能ファイルをダウンロードすることです：
(The easiest way is to download the pre-built executable:)

1. [GitHubリリースページ](https://github.com/jkabutaro/usstock-csvdownloader/releases)から最新のリリース（USStockDownloader-v0.9.0.zip）をダウンロード (Download the latest release (USStockDownloader-v0.9.0.zip) from the [GitHub Releases page](https://github.com/jkabutaro/usstock-csvdownloader/releases))
2. ダウンロードしたZIPファイルを任意の場所に解凍 (Extract the downloaded ZIP file to any location)
3. `USStockDownloader.exe`をダブルクリックして実行 (Double-click `USStockDownloader.exe` to run)

※ 初回実行時に自動システム要件チェックが行われます。Windows 10以上および.NET 9.0 Runtimeが必要です。
(Note: An automatic system requirements check will be performed on first run. Windows 10 or higher and .NET 9.0 Runtime are required.)

### 方法2: ソースコードからビルド (Method 2: Build from Source Code)
```bash
git clone https://github.com/jkabutaro/usstock-csvdownloader.git
cd usstock-csvdownloader
dotnet build
```

## 使用方法 (Usage)

### システム要件 (System Requirements)
- Windows 10以降 (Windows 10 or later)
- .NET 9.0 Runtime
- インターネット接続（Yahoo Finance APIへのアクセスに必要） (Internet connection (required for accessing Yahoo Finance API))

#### 自動システム要件チェック機能 (Automatic System Requirements Check Feature)
アプリケーションは起動時に以下の自動チェックを行います：
(The application performs the following automatic checks at startup:)
- **Windows 10以降のチェック**: Windows 10以降でない場合、メッセージを表示して終了します (Windows 10 or later check: If not Windows 10 or later, displays a message and exits)
- **.NET 9.0 Runtimeのチェック**: 必要なランタイムがインストールされていない場合、ダウンロードページを自動的に開き、インストール手順を案内します (.NET 9.0 Runtime check: If the required runtime is not installed, automatically opens the download page and guides you through the installation procedure)
- **チェック結果のキャッシュ**: システム要件のチェック結果はキャッシュされ、次回起動時の処理を高速化します (Cache check results: System requirements check results are cached to speed up processing on next startup)

### 実行オプション (Execution Options)

#### 基本的な使い方 (Basic Usage)
```bash
USStockDownloader --sp500 -o ./data
```
- S&P 500銘柄のデータをダウンロード (Download S&P 500 symbol data)
- デフォルトでクイックモードが有効：最新のデータを持つ銘柄はスキップされます (Quick mode is enabled by default: Symbols with up-to-date data are skipped)

#### 強制更新モード (Force Update Mode)
```bash
USStockDownloader --sp500 --force -o ./data
```
- S&P 500銘柄のデータを強制的に全て更新 (Force update all S&P 500 symbol data)
- キャッシュの状態に関わらず全銘柄をダウンロードします (Download all symbols regardless of cache status)

#### 利用可能なオプション (Available Options)
| オプション | 説明 | デフォルト値 |
|------------|------|--------------|
| `--sp500` | S&P 500銘柄を自動取得してダウンロード | - |
| `--sp500-f` | S&P 500銘柄リストを強制的に更新してダウンロード | - |
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
| `--index-f` | 主要指数リストを強制的に更新 | - |
| `--sbi` | SBI証券取扱いの米国株式データをダウンロード | - |
| `--sbi-f` | SBI証券取扱いの銘柄リストを強制的に更新してダウンロード | - |
| `--sbi --listcsv` | SBI証券取扱いの銘柄リストをCSVファイルに出力 | - |

## データ形式 (Data Format)

### 株価データCSVファイル (Stock Price CSV File)

ダウンロードしたデータは、`output` ディレクトリに銘柄ごとのCSVファイルとして保存されます。

#### CSVファイル形式 (CSV File Format)
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

### 銘柄リストCSVファイル（--listcsvオプション使用時） (Symbol List CSV File (--listcsv Option))

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

出力ファイル名はデフォルトで`us_stock_list.csv`となります。すべてのCSVファイルはShift-JISエンコーディングで出力されるため、日本語環境での利用に適しています。

#### SBI証券の銘柄リストをCSVファイルに出力 (SBI Securities Symbol List CSV Output)
SBI証券の銘柄リストをCSVファイルに出力するには、`--sbi`と`--listcsv`オプションを組み合わせて使用します：

```bash
USStockDownloader --sbi --listcsv output/sbi_list.csv
```

## 免責事項と注意点 (Disclaimer and Notes)

### データソースについて (About Data Sources)
本アプリケーションは以下のソースから銘柄情報を取得しています：
1. **S&P 500銘柄リスト**
   - 取得元: Wikipediaの「List of S&P 500 companies」ページ
   - URL: https://en.wikipedia.org/wiki/List_of_S%26P_500_companies
   - 取得方法: HTMLテーブルのスクレイピング
   - 更新頻度: 24時間キャッシュ（`--sp500-f`オプションで強制更新可能）

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
   - 更新頻度: 24時間キャッシュ（`--index-f`オプションで強制更新可能）

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

## バフェットポートフォリオ銘柄について (About Buffett Portfolio Symbols)
バフェットポートフォリオ銘柄（`-b`または`--buffett`オプションで指定）は、バークシャー・ハサウェイ社が保有する株式銘柄を指します。これらの銘柄は、投資家のウォーレン・バフェットが選定したものとして知られています。

### データソース (Data Source)
バフェットポートフォリオの銘柄リストは、以下の方法で取得しています：
1. Wikipediaの「List of assets owned by Berkshire Hathaway」ページからスクレイピングして取得
2. スクレイピングに失敗した場合、主要な保有銘柄のフォールバックリストを使用

### 注意事項 (Notes)
- バフェットポートフォリオの内容は定期的に変更される可能性があります
- 最新の情報を取得するには、`--buffett-f`オプションを使用して強制更新してください
- データは24時間キャッシュされます（特に指定がない場合）

### 使用例 (Usage Examples)
```bash
# バフェットポートフォリオの銘柄を使用してダウンロード
USStockDownloader -b

# バフェットポートフォリオの銘柄リストを強制的に更新してダウンロード
USStockDownloader --buffett-f

# バフェットポートフォリオの銘柄リストをCSVファイルに出力
USStockDownloader --buffett --listcsv output/buffett_list.csv

# バフェットポートフォリオの銘柄リストを強制的に更新してCSVファイルに出力
USStockDownloader --buffett-f --listcsv output/buffett_list.csv
```

## データ形式 (Data Format)
ダウンロードしたデータは、`output` ディレクトリに銘柄ごとのCSVファイルとして保存されます。

#### CSVファイル形式 (CSV File Format)
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

## エラー処理 (Error Handling)
- HTTP 429（レート制限）
  - デフォルトで30秒待機後に再試行
  - `--rate-limit-delay` で待機時間を調整可能
- その他のエラー
  - 設定された間隔（`--retry-delay`）で再試行
  - 指数バックオフ有効時は待機時間が2倍ずつ増加
  - ジッター有効時は待機時間に±20%のランダム変動を追加

## 出力ファイル (Output Files)
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

6. **SBI証券銘柄リストCSV**
   - 場所: `us_stock_list.csv`
   - 内容:
     - SBI証券で取り扱いのある米国株式の銘柄リスト（`--sbi --listcsv`で出力）
     - 形式: code,name,market,type
     - エンコーディング: Shift-JIS
   - 例:
     ```csv
     code,name,market,type
     AAPL,Apple Inc.,NASDAQ,stock
     MSFT,Microsoft Corporation,NASDAQ,stock
     ```
   - 注意事項:
     - SBI証券のウェブサイトからデータを取得するため、ネットワーク環境によっては
       タイムアウトエラーが発生する場合があります
     - 取得に失敗した場合は明確なエラーメッセージが表示され、代替データは提供されません

7. **ダウンロードログ**
   - 場所: `logs/download_<timestamp>.log`
   - 内容:
     - 詳細な実行ログ
     - エラーメッセージ
     - パフォーマンス情報

## 使用例とヒント (Usage Examples and Tips)

#### 基本的な使い方 (Basic Usage)
1. **S&P 500全銘柄のダウンロード**
   ```bash
   USStockDownloader --sp500
   ```
   - Wikipediaから最新のS&P 500銘柄リストを取得
   - 全銘柄の1年分のデータをダウンロード
   - キャッシュされた銘柄リストがある場合はそれを使用（更新が必要な場合は`--sp500-f`オプションを使用）

2. **NYダウ銘柄リストのダウンロード**
   ```bash
   USStockDownloader --nyd
   ```
   - Wikipediaから最新のNYダウ銘柄リストを取得
   - 全銘柄の1年分のデータをダウンロード
   - S&P 500に比べて銘柄数が少ないため短時間で完了

3. **カスタム銘柄リストの使用**
   ```bash
   USStockDownloader --file my_symbols.txt
   ```
   - `my_symbols.txt`の形式: 1行1銘柄
   ```
   AAPL
   MSFT
   GOOGL
   ```

4. **特定銘柄のダウンロード**
   ```bash
   USStockDownloader --symbols AAPL,MSFT,GOOGL
   ```
   - カンマ区切りで複数銘柄を指定可能

5. **日付範囲を指定してダウンロード**
   ```bash
   USStockDownloader --sp500 --start-date 2023-01-01 --end-date 2024-12-31
   ```
   - 特定期間のデータのみを取得

6. **S&P 500銘柄リストのCSV出力**
   ```bash
   USStockDownloader --sp500 --listcsv output/sp500_list.csv
   ```
   - S&P 500銘柄のリストをCSVファイルに出力

7. **主要指数リストのCSV出力**
   ```bash
   USStockDownloader --index --listcsv output/index_list.csv
   ```
   - 主要指数のリストをCSVファイルに出力

8. **主要指数リストを強制的に更新してCSVファイルに出力**
   ```bash
   USStockDownloader --index-f --listcsv output/index_list.csv
   ```
   - 主要指数リストを強制的に更新してCSVファイルに出力

9. **バフェットポートフォリオ銘柄リストのCSV出力**
   ```bash
   USStockDownloader --buffett --listcsv output/buffett_list.csv
   ```
   - バフェットポートフォリオの銘柄リストをCSVファイルに出力

10. **バフェットポートフォリオ銘柄リストを強制的に更新してCSVファイルに出力**
    ```bash
    USStockDownloader --buffett-f --listcsv output/buffett_list.csv
    ```
    - バフェットポートフォリオの銘柄リストを強制的に更新してCSVファイルに出力

11. **SBI証券取扱いの米国株式データをダウンロード**
    ```bash
    USStockDownloader --sbi
    ```
    - SBI証券取扱いの米国株式データをダウンロード

12. **SBI証券取扱いの米国株式データを強制的に更新してダウンロード**
    ```bash
    USStockDownloader --sbi --sbi-f
    ```
    - SBI証券取扱いの米国株式データを強制的に更新してダウンロード

13. **SBI証券取扱いの銘柄リストをCSVファイルに出力**
    ```bash
    USStockDownloader --sbi --listcsv output/sbi_list.csv
    ```
    - SBI証券取扱いの銘柄リストをCSVファイルに出力

14. **SBI証券取扱いの銘柄リストを強制的に更新してCSVファイルに出力**
    ```bash
    USStockDownloader --sbi --sbi-f --listcsv output/sbi_list.csv
    ```
    - SBI証券取扱いの銘柄リストを強制的に更新してCSVファイルに出力

#### パフォーマンスチューニング (Performance Tuning)
1. **並列数の調整**
   ```bash
   USStockDownloader --sp500 --concurrent 5
   ```
   - 並列ダウンロード数を5に設定
   - デフォルトは3
   - 数値が大きいほど処理は速くなりますが、API制限に引っかかる可能性が高まります

2. **リトライ回数の調整**
   ```bash
   USStockDownloader --sp500 --retries 5
   ```
   - ダウンロード失敗時のリトライ回数を5に設定
   - デフォルトは3

3. **リトライ間隔の調整**
   ```bash
   USStockDownloader --sp500 --delay 2000
   ```
   - リトライ間隔を2000ミリ秒（2秒）に設定
   - デフォルトは1000ミリ秒（1秒）

4. **指数バックオフの無効化**
   ```bash
   USStockDownloader --sp500 --no-exponential
   ```
   - 指数バックオフを無効化
   - デフォルトは有効

5. **ジッターの無効化**
   ```bash
   USStockDownloader --sp500 --no-jitter
   ```
   - ジッターを無効化
   - デフォルトは有効

## 最新の改善点（2025-03-25） (Latest Improvements (2025-03-25))

### シンボル処理の改善 (Symbol Processing Improvements)
- **ピリオドを含むシンボル対応**：BRK.B、BF.Bなどのピリオドを含むシンボルの処理を改善 (Improved handling of symbols containing periods, such as BRK.B and BF.B)
  - Yahoo FinanceではBRK.BがBRK-Bとして扱われることを発見 (Discovered that BRK.B is treated as BRK-B in Yahoo Finance)
  - APIリクエスト時にピリオド(.)をハイフン(-)に変換 (Convert periods to hyphens when making API requests)
  - ファイル名保存時にピリオドをアンダースコアに置換（例：BRK.B → BRK_B.csv） (Replace periods with underscores when saving filenames (e.g., BRK.B → BRK_B.csv))
  - **テスト結果**: BRK.B、BF.Bなどのピリオドを含むシンボルのダウンロードに成功 (Test result: Successfully downloaded symbols containing periods, such as BRK.B and BF.B)

### 特定シンボルの特別処理 (Special Handling for Specific Symbols)
- **ETRシンボルの特別処理**：Yahoo Finance APIでのETRシンボルの取得問題を解決 (Resolved issues with retrieving ETR symbol from Yahoo Finance API)
  - ETR (Entergy Corporation) はYahoo Financeでも「ETR」として正しく表示されることを確認 (Confirmed that ETR (Entergy Corporation) is correctly displayed as "ETR" in Yahoo Finance)
  - 追加の待機時間を設定してレート制限を回避 (Set additional wait time to avoid rate limits)
  - 詳細なログ記録による問題の診断と対応 (Diagnosed and addressed issues through detailed logging)
  - **テスト結果**: ETRシンボルのダウンロードに成功 (Test result: Successfully downloaded ETR symbol)

### エラー処理の強化 (Enhanced Error Handling)
- **詳細なエラーレポート機能**：失敗した銘柄の詳細情報をCSVファイルに出力 (Detailed error reporting: Output detailed information about failed symbols to CSV file)
  - シンボルとエラーメッセージの記録 (Record symbols and error messages)
  - 問題の診断と分析を容易に (Facilitate diagnosis and analysis of problems)
  - **テスト結果**: S&P 500全銘柄のテストで失敗レポートが生成されず（全銘柄成功） (Test result: No failure reports generated in S&P 500 full symbol test (all symbols successful))

- **特別リトライメカニズム**：問題のあるシンボルに対する特別なリトライ処理 (Special retry mechanism: Special retry processing for problematic symbols)
  - 通常のリトライよりも長い待機時間 (Longer wait times than normal retries)
  - 最大5回の追加リトライ (Up to 5 additional retries)
  - 指数バックオフとジッターの強化 (Enhanced exponential backoff and jitter)
  - **テスト結果**: リトライ機構が効果的に機能し、全銘柄のダウンロードに成功 (Test result: Retry mechanism functioned effectively, successfully downloading all symbols)

### 日付処理の改善（2025-03-25） (Date Processing Improvements (2025-03-25))
- **将来日付の自動調整機能**：まだ米国株のデータが存在しない日付を指定されても問題なく動作 (Automatic adjustment of future dates: Works properly even when dates for which US stock data does not yet exist are specified)
  - 将来の日付を自動的に最新の取引日に調整 (Automatically adjusts future dates to the latest trading day)
  - 日本時間と米国市場時間の差を考慮した処理 (Processing that takes into account the time difference between Japan time and US market time)
  - 市場が開いている時間帯でも適切に前営業日を判定 (Properly determines the previous business day even during market hours)
  - 調整が行われた場合はログに通知 (Notifies in the log when adjustment has been made)
  - **テスト結果**: 日本時間2025年3月25日に実行した場合、自動的に2025年3月24日（最新の取引日）に調整 (Test result: When executed on March 25, 2025 Japan time, automatically adjusted to March 24, 2025 (latest trading day))

- **クイックモードとの連携強化**：キャッシュ判定の精度向上 (Enhanced integration with quick mode: Improved cache determination accuracy)
  - 日付自動調整によりキャッシュの有効性判断が正確に (Date auto-adjustment makes cache validity determination more accurate)
  - 「Requested date range is outside cache range」エラーの発生を抑制 (Suppresses the occurrence of "Requested date range is outside cache range" errors)
  - **テスト結果**: クイックモードで全銘柄が「Using cache, no update required」と判定され、処理時間が大幅に短縮 (Test result: All symbols were determined to be "Using cache, no update required" in quick mode, significantly reducing processing time)

### 操作性の改善（2025-03-25） (Usability Improvements (2025-03-25))
- **デフォルトでクイックモード有効**：日常的な使用を想定した最適化 (Quick mode enabled by default: Optimized for daily use)
  - デフォルトで最新の銘柄はスキップされるように変更 (Changed to skip up-to-date symbols by default)
  - 処理時間の大幅な短縮とAPI呼び出し数の削減 (Significantly reduced processing time and API calls)
  - `--force`オプションで従来の全銘柄強制更新モードを選択可能 (Option to select the conventional force update mode with the `--force` option)
  - **テスト結果**: S&P 500の処理時間が数分から数秒に短縮 (Test result: Processing time for S&P 500 reduced from minutes to seconds)

### 総合テスト結果（2025-03-25） (Comprehensive Test Results (2025-03-25))
- **S&P 500全銘柄テスト**: 503銘柄すべてのダウンロードに成功 (S&P 500 full symbol test: Successfully downloaded all 503 symbols)
- **並列処理数**: 5（テスト時の設定） (Number of parallel processes: 5 (test setting))
- **ダウンロード時間**: 約2分（並列処理数5の場合） (Download time: Approximately 2 minutes (with 5 parallel processes))
- **成功率**: 100% (Success rate: 100%)

## 更新履歴 (Update History)

### 2025-03-25 キャッシュ機能の改善 (Cache Function Improvement)

- **キャッシュ有効性判定の最適化** (Optimization of cache validity determination)
  - リクエスト終了日が現在日付で、キャッシュの終了日が最新取引日の場合、キャッシュを有効と判断 (When the request end date is the current date and the cache end date is the latest trading day, the cache is considered valid)
  - 不要なデータダウンロードを回避し、処理時間を短縮 (Avoids unnecessary data downloads and reduces processing time)
  - API呼び出し回数の削減によるリソース節約 (Resource saving by reducing the number of API calls)

- **詳細なログメッセージの追加** (Addition of detailed log messages)
  - キャッシュ使用状況を日本語と英語の両方で明確に表示 (Clearly displays cache usage status in both Japanese and English)
  - 「リクエスト終了日が現在日付で、キャッシュの終了日が最新取引日のため、キャッシュを使用します」などの詳細メッセージ (Detailed messages such as "Request end date is today, cache end date is the latest trading day, using cache")

- **テスト結果** (Test Results)
  - S&P 500およびNYダウの全銘柄で正常にキャッシュが機能 (Cache functions normally for all S&P 500 and NY Dow symbols)
  - 2回目以降の実行では、最新データがすでにキャッシュされている場合、Yahoo Financeへのリクエストが発生せず処理が高速化 (From the second execution onwards, if the latest data is already cached, processing is accelerated without requests to Yahoo Finance)
  - 並列処理数5での全銘柄ダウンロード時間：約2分（初回）、数秒（2回目以降、キャッシュ使用時） (Download time for all symbols with 5 parallel processes: about 2 minutes (first time), a few seconds (from the second time onwards, when using cache))
