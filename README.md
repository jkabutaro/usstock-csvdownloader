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
- S&P 500銘柄リストのCSV出力（ティッカーシンボル、名前、市場、種別情報を含む） (CSV output of S&P 500 symbol list including ticker code, name, market, and type information)
- NYダウ銘柄リストのCSV出力（ティッカーシンボル、名前、市場、種別情報を含む） (CSV output of NY Dow symbol list including ticker code, name (with Japanese name), market, and type information)
- 主要指数リストのCSV出力 (CSV output of major indices list)
- バフェットポートフォリオ銘柄リストのCSV出力（Wikipediaから） (CSV output of Buffett portfolio symbol list from Wikipedia)
- SBI証券取扱いの米国株式データのダウンロード (Download US stock data handled by SBI Securities)
- SBI証券取扱いの銘柄リストのCSV出力 (CSV output of symbol list handled by SBI Securities)

### エラー処理とリトライ機能 (Error Handling and Retry Features)
- Pollyライブラリを使用したリトライ機構 (Retry mechanism using Polly library)
  - 指数バックオフとジッターによる再試行間隔の制御 (Control retry intervals with exponential backoff and jitter)
  - HTTP 429（レート制限）の特別処理 (Special handling for HTTP 429 rate limit)
  - 詳細なエラーログ記録 (Detailed error logging)
- 上場廃止銘柄の特別処理 (Special handling for delisted symbols)
  - "No data found, symbol may be delisted"エラーの検出と処理 (Detection and handling of "No data found, symbol may be delisted" errors)
  - 上場廃止された銘柄に対するリトライの回避 (Avoiding retries for delisted symbols)
  - 上場廃止銘柄の記録と空のCSVファイル作成 (Recording delisted symbols and creating empty CSV files)

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

## リポジトリ情報 (Repository Information)

このプロジェクトは以下のGitHubリポジトリで公開されています：
(This project is published in the following GitHub repository:)
- リポジトリURL (Repository URL): https://github.com/jkabutaro/usstock-csvdownloader
- 開発者 (Developer): jkabutaro

## ダウンロードとインストール (Download and Installation)

### 方法1: ビルド済み実行可能ファイル（推奨） (Method 1: Pre-built Executable (Recommended))
最も簡単な方法は、ビルド済みの実行可能ファイルをダウンロードすることです：
(The easiest way is to download the pre-built executable:)

1. [GitHubリリースページ](https://github.com/jkabutaro/usstock-csvdownloader/releases)から最新のリリースをダウンロード (Download the latest release from the [GitHub Releases page](https://github.com/jkabutaro/usstock-csvdownloader/releases))
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

## システム要件 (System Requirements)
- Windows 10以降 (Windows 10 or later)
- .NET 9.0 Runtime
- インターネット接続（Yahoo Finance APIへのアクセスに必要） (Internet connection (required for accessing Yahoo Finance API))

### 自動システム要件チェック機能 (Automatic System Requirements Check Feature)
アプリケーションは起動時に以下の自動チェックを行います：
(The application performs the following automatic checks at startup:)
- **Windows 10以降のチェック**: Windows 10以降でない場合、メッセージを表示して終了します (Windows 10 or later check: If not Windows 10 or later, displays a message and exits)
- **.NET 9.0 Runtimeのチェック**: 必要なランタイムがインストールされていない場合、ダウンロードページを自動的に開き、インストール手順を案内します (.NET 9.0 Runtime check: If the required runtime is not installed, automatically opens the download page and guides you through the installation procedure)

## 使用方法 (Usage)

### 基本的な使い方 (Basic Usage)
```bash
USStockDownloader --sp500 --output ./data
```
- S&P 500銘柄のデータをダウンロード (Download S&P 500 symbol data)
- デフォルトでクイックモードが有効：最新のデータを持つ銘柄はスキップされます (Quick mode is enabled by default: Symbols with up-to-date data are skipped)

### キャッシュクリアオプション (Cache Clear Option)
```bash
USStockDownloader --cache-clear --sp500 --output ./data
```
- キャッシュをクリアしてからS&P 500銘柄のデータをダウンロード (Clear cache before downloading S&P 500 symbol data)

### 利用可能なオプション 銘柄指定 (Available Options - Symbol Specification)
| オプション | 説明 | デフォルト値 |
|------------|------|--------------|
| `--index` | 主要指数をダウンロード 指数のラインナップは現状固定 (Fixed index lineup) | - |
| `--sp500` | S&P 500銘柄をダウンロード | - |
| `--nyd` | NYダウ銘柄リストをダウンロード | - |
| `--buffett` | バフェットポートフォリオの銘柄をダウンロード | - |
| `--sbi` | SBI証券取扱いの米国株銘柄をダウンロード | - |
| `--file <path>` | 指定したファイルから銘柄リストを読み込み | - |
| `--symbols <symbols>` | カンマ区切りで個別銘柄を指定（例：AAPL,MSFT,GOOGL） | - |


### 組み合わせて使うオプション (Available Options - Combining Options)
| オプション | 説明 | デフォルト値 |
|------------|------|--------------|
| `--listcsv <path>` | 個別CSVをダウンロードせず、受信銘柄のリストをCSVファイルに出力 | - |
| `--output <path>` | 出力ディレクトリを指定 | ./output |
| `--start-date <date>` | データ取得開始日（yyyy-MM-dd形式） | 1年前 |
| `--end-date <date>` | データ取得終了日（yyyy-MM-dd形式） | 現在 |
| `--cache-clear` | キャッシュをクリアしてから実行 | - |

### 利用可能なオプション ダウンロード設定 (Available Options - Download Settings)
| オプション | 説明 | デフォルト値 |
|------------|------|--------------|
| `--parallel <num>` | 並列ダウンロード数 | 3 |
| `--retries <num>` | リトライ回数 | 3 |
| `--delay <ms>` | リトライ時の待機時間（ミリ秒） | 5000 |
| `--exponential-backoff` | 指数バックオフを使用（true/false） | true |

## キャッシュファイル (Cache Files)

本ツールは、パフォーマンス向上のためにいくつかのデータをSQLiteデータベースとして保存します。
(This tool stores some data as SQLite database to improve performance.)

キャッシュファイルは以下の場所に保存されます：
(Cache files are stored in the following locations:)

1. **取引日データキャッシュ**: `Cache/trading_days.db`（SQLiteデータベース）

キャッシュファイルは自動的に管理されるため、通常はユーザーが直接操作する必要はありません。
(Cache files are automatically managed, so users usually do not need to manipulate them directly.)

## データ形式 (Data Format)

### 株価データCSVファイル (Stock Price CSV File)

ダウンロードしたデータは、`output` ディレクトリに銘柄ごとのCSVファイルとして保存されます。

#### CSVファイル形式 (CSV File Format)
```csv
Date,Open,High,Low,Close,Volume
20240225,180.15,182.34,179.89,181.56,75234567
```

| カラム | 説明 | 型 |
|--------|------|-----|
| Date | 取引日（yyyymmdd形式の整数値） | 整数 |
| Open | 始値 | 数値 |
| High | 高値 | 数値 |
| Low | 安値 | 数値 |
| Close | 終値 | 数値 |
| Volume | 出来高 | 整数 |

### 銘柄リストCSVファイル (Symbol List CSV File)

`--listcsv`オプションを使用すると、銘柄リストがCSVファイルとして出力されます。出力されるCSVファイルの形式は以下の通りです：

```csv
Symbol,name,market,type
A,Agilent Technologies アジレント テクノロジーズ,NYSE,stock
AA,Alcoa アルコア,NYSE,stock
```

| カラム | 説明 | 型 |
|--------|------|-----|
| Symbol | ティッカーシンボル | 文字列 |
| name | 銘柄名 | 文字列 |
| market | 市場(NYSEやNASDAQ) | 文字列 |
| type | 種類(stockやindexやetf) | 文字列 |

出力ファイル名はデフォルトで`us_stock_list.csv`となります。すべてのCSVファイルはShift-JISエンコーディングで出力されるため、日本語環境での利用に適しています。

## 免責事項と注意点 (Disclaimer and Notes)

### データソースについて (About Data Sources)
本アプリケーションは以下のソースから銘柄情報を取得しています：
- **S&P 500銘柄リスト**: Wikipediaの「List of S&P 500 companies」ページ
- **NYダウ銘柄リスト**: Wikipediaの「Dow Jones Industrial Average」ページ
- **バフェットポートフォリオ銘柄リスト**: Wikipediaの「List of assets owned by Berkshire Hathaway」ページ
- **主要指数リスト**: Yahoo Financeの世界指数ページ
- **SBI証券銘柄リスト**: SBI証券の米国株式一覧ページ

これらの情報は以下の点に注意してください：
- Wikipediaの記事内容はいつでも変更される可能性があり、その結果として正確な情報が取得できない場合があります
- Wikipediaのページ構造が変更されると、データ抽出ロジックが機能しなくなる可能性があります
- Yahoo FinanceのAPIは公式にドキュメント化されておらず、予告なく変更される可能性があります
- SBI証券のウェブサイト構造が変更されると、データ抽出ロジックが機能しなくなる可能性があります

### 免責事項 (Disclaimer)
本アプリケーションは情報提供のみを目的としており、投資判断の根拠として使用することを推奨するものではありません。提供される情報の正確性、完全性、有用性について一切の保証を行いません。本アプリケーションの使用によって生じたいかなる損害についても、開発者は責任を負いません。

### 利用規約の遵守 (Compliance with Terms of Service)
本アプリケーションの使用者は、データソース（Yahoo Finance、Wikipedia、SBI証券など）の利用規約を遵守する責任があります。過度なリクエストを送信したり、商用目的で使用したりする場合は、各サービスの利用規約を確認してください。

### 著作権表示 (Copyright Notice)
Copyright 2024 jkabutaro. All rights reserved.
