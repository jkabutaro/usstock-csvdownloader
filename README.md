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
- SBI証券取扱いの米国株式データのダウンロード
- SBI証券取扱いの銘柄リストのCSV出力

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
dotnet run -- --sp500-f

# NYダウ銘柄リストを自動取得してダウンロード
dotnet run -- -n

# NYダウ銘柄リストを強制的に更新
dotnet run -- --nyd-f

# バフェットポートフォリオの銘柄を自動取得してダウンロード
dotnet run -- -b

# バフェットポートフォリオの銘柄リストを強制的に更新してダウンロード
dotnet run -- --buffett-f

# 主要指数リストをCSVファイルに出力
dotnet run -- --index --listcsv output/index_list.csv

# 主要指数リストを強制的に更新してCSVファイルに出力
dotnet run -- --index-f --listcsv output/index_list.csv

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
dotnet run -- --buffett-f --listcsv output/buffett_list.csv

# S&P 500銘柄リストをCSVファイルに出力
dotnet run -- --sp500 --listcsv output/sp500_list.csv

# SBI証券取扱いの米国株式データをダウンロード
dotnet run -- --sbi

# SBI証券取扱いの米国株式データを強制的に更新してダウンロード
dotnet run -- --sbi --sbi-f

# SBI証券取扱いの銘柄リストをCSVファイルに出力
dotnet run -- --sbi --listcsv output/sbi_list.csv

# SBI証券取扱いの銘柄リストを強制的に更新してCSVファイルに出力
dotnet run -- --sbi --sbi-f --listcsv output/sbi_list.csv
```

#### 利用可能なオプション
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

出力ファイル名はデフォルトで`us_stock_list.csv`となります。すべてのCSVファイルはShift-JISエンコーディングで出力されるため、日本語環境での利用に適しています。

#### SBI証券の銘柄リストをCSVファイルに出力
SBI証券の銘柄リストをCSVファイルに出力するには、`--sbi`と`--listcsv`オプションを組み合わせて使用します：

```bash
dotnet run -- --sbi --listcsv output/sbi_list.csv
```

## 免責事項と注意点

### データソースについて
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
dotnet run -- --buffett-f

# バフェットポートフォリオの銘柄リストをCSVファイルに出力
dotnet run -- --buffett --listcsv output/buffett_list.csv

# バフェットポートフォリオの銘柄リストを強制的に更新してCSVファイルに出力
dotnet run -- --buffett-f --listcsv output/buffett_list.csv
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

6. **SBI証券銘柄リストCSV**
   - 場所: `us_stock_list.csv`
   - 内容:
     - SBI証券で取り扱いのある米国株式の銘柄リスト（`--sbi --listcsv`で出力）
     - 形式: code,name,market,type
     - エンコーディング: UTF-8
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

## 使用例とヒント

#### 基本的な使い方
1. **S&P 500全銘柄のダウンロード**
   ```bash
   dotnet run -- --sp500
   ```
   - Wikipediaから最新のS&P 500銘柄リストを取得
   - 全銘柄の1年分のデータをダウンロード
   - キャッシュされた銘柄リストがある場合はそれを使用（更新が必要な場合は`--sp500-f`オプションを使用）

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
   dotnet run -- --index-f --listcsv output/index_list.csv
   ```
   - 主要指数リストを強制的に更新してCSVファイルに出力

9. **バフェットポートフォリオ銘柄リストのCSV出力**
   ```bash
   dotnet run -- --buffett --listcsv output/buffett_list.csv
   ```
   - バフェットポートフォリオの銘柄リストをCSVファイルに出力

10. **バフェットポートフォリオ銘柄リストを強制的に更新してCSVファイルに出力**
    ```bash
    dotnet run -- --buffett-f --listcsv output/buffett_list.csv
    ```
    - バフェットポートフォリオの銘柄リストを強制的に更新してCSVファイルに出力

11. **SBI証券取扱いの米国株式データをダウンロード**
    ```bash
    dotnet run -- --sbi
    ```
    - SBI証券取扱いの米国株式データをダウンロード

12. **SBI証券取扱いの米国株式データを強制的に更新してダウンロード**
    ```bash
    dotnet run -- --sbi --sbi-f
    ```
    - SBI証券取扱いの米国株式データを強制的に更新してダウンロード

13. **SBI証券取扱いの銘柄リストをCSVファイルに出力**
    ```bash
    dotnet run -- --sbi --listcsv output/sbi_list.csv
    ```
    - SBI証券取扱いの銘柄リストをCSVファイルに出力

14. **SBI証券取扱いの銘柄リストを強制的に更新してCSVファイルに出力**
    ```bash
    dotnet run -- --sbi --sbi-f --listcsv output/sbi_list.csv
    ```
    - SBI証券取扱いの銘柄リストを強制的に更新してCSVファイルに出力

#### パフォーマンスチューニング

1. **並列数の調整**
   ```bash
   dotnet run -- --sp500 --concurrent 5
   ```
   - 並列ダウンロード数を5に設定
   - デフォルトは3
   - 数値が大きいほど処理は速くなりますが、API制限に引っかかる可能性が高まります

2. **リトライ回数の調整**
   ```bash
   dotnet run -- --sp500 --retries 5
   ```
   - ダウンロード失敗時のリトライ回数を5に設定
   - デフォルトは3

3. **リトライ間隔の調整**
   ```bash
   dotnet run -- --sp500 --delay 2000
   ```
   - リトライ間隔を2000ミリ秒（2秒）に設定
   - デフォルトは1000ミリ秒（1秒）

4. **指数バックオフの無効化**
   ```bash
   dotnet run -- --sp500 --no-exponential
   ```
   - リトライ時の指数バックオフを無効化
   - デフォルトは有効

#### エラー処理とリトライ戦略

このツールは、Yahoo Finance APIからのデータ取得時に発生する可能性のあるエラーを適切に処理するために、堅牢なリトライ戦略を実装しています。

1. **指数バックオフ**

   指数バックオフは、リクエストが失敗した場合に再試行間隔を徐々に長くする戦略です。この戦略には以下のような利点があります：

   - **サーバー負荷の軽減**: 連続的な失敗が発生した場合、サーバーへの負荷を減らします
   - **一時的な問題の解決**: ネットワークの一時的な問題や、サーバーの一時的な過負荷が自然に解消されるのを待ちます
   - **API制限の回避**: レート制限のあるAPIに対して、制限が解除されるまで待機時間を延長します

   **指数バックオフの仕組み**:
   - 1回目の失敗: 基本待機時間（例: 1秒）
   - 2回目の失敗: 基本待機時間 × 2^1（例: 2秒）
   - 3回目の失敗: 基本待機時間 × 2^2（例: 4秒）
   - 4回目の失敗: 基本待機時間 × 2^3（例: 8秒）

   このツールでは、`--delay`オプションで基本待機時間を設定でき、`--no-exponential`オプションで指数バックオフを無効化できます。

2. **ジッター（Jitter）**

   ジッターは、リトライ間隔にランダムな変動を加える技術です。この戦略には以下のような利点があります：

   - **サンダーヘッド問題の回避**: 複数のクライアントが同時に失敗した場合に、全てが同じタイミングで再試行することを防ぎます
   - **負荷の分散**: サーバーへのリクエストが時間的に分散されるため、サーバーの負荷が平準化されます
   - **予測不可能性の導入**: 再試行タイミングにランダム性を導入することで、パターン化されたアクセスを避けます

   **ジッターの仕組み**:
   - 基本待機時間（またはバックオフで計算された時間）に対して、ランダムな変動（±20%程度）を追加します
   - 例: 基本待機時間が1000msの場合、実際の待機時間は800ms～1200msの範囲でランダムに決定されます
   - これにより、同時に多数のリクエストが失敗した場合でも、再試行のタイミングが分散されます

   このツールでは、Pollyライブラリの機能を利用してジッターを実装しています。

3. **HTTP 429（レート制限）の特別処理**

   Yahoo Finance APIには、短時間に多数のリクエストを送信すると発生するレート制限があります。このツールは、HTTP 429エラー（Too Many Requests）を検出すると、通常のリトライよりも長い待機時間を設定し、APIの制限が解除されるのを待ちます。

#### エラー対応

{{ ... }}
