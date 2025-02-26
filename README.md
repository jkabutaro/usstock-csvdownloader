# US Stock CSV Downloader

米国株式の株価データをYahoo Financeから一括ダウンロードするツール。

## 機能

### 基本機能
- Yahoo Finance APIを使用した株価データのダウンロード
- CSVフォーマットでのデータ保存
- 並列ダウンロード（デフォルト3並列）
- S&P 500銘柄の自動取得（Wikipediaから）

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

### エラーレポート機能
- 失敗した銘柄の詳細レポート生成
- エラータイプごとの集計
- 再試行結果の記録

## 使用方法

### システム要件
- Windows 10以降
- .NET 9.0 Runtime
- インターネット接続（Yahoo Finance APIへのアクセスに必要）

### インストール
```bash
git clone [repository-url]
cd USStockDownloader
dotnet restore
```

### 実行オプション

#### 基本的な使い方
```bash
# S&P 500銘柄を自動取得してダウンロード
dotnet run -- --sp500

# 特定の銘柄リストをダウンロード
dotnet run -- --file symbols.txt

# 個別銘柄を指定してダウンロード（カンマ区切り）
dotnet run -- --symbols AAPL,MSFT,GOOGL

# カスタム設定でダウンロード
dotnet run -- --file symbols.txt --max-retries 5 --retry-delay 5000 --concurrent 3
```

#### 利用可能なオプション
| オプション | 説明 | デフォルト値 |
|------------|------|--------------|
| `--sp500` | S&P 500銘柄を自動取得してダウンロード | - |
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

### データ形式
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

### エラー処理
- HTTP 429（レート制限）
  - デフォルトで30秒待機後に再試行
  - `--rate-limit-delay` で待機時間を調整可能
- その他のエラー
  - 設定された間隔（`--retry-delay`）で再試行
  - 指数バックオフ有効時は待機時間が2倍ずつ増加
  - ジッター有効時は待機時間に±20%のランダム変動を追加

### 出力ファイル
1. **株価データ**
   - 場所: `output/<symbol>.csv`
   - 形式: 上記CSVファイル形式
   - 例: `output/AAPL.csv`

2. **エラーレポート**
   - 場所: `output/failed_symbols_report.txt`
   - 内容:
     - 失敗した銘柄のリスト
     - エラータイプごとの集計
     - リトライ結果の詳細

3. **ダウンロードログ**
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

2. **カスタム銘柄リストの使用**
   ```bash
   dotnet run -- --file my_symbols.txt
   ```
   - `my_symbols.txt`の形式: 1行1銘柄
   ```
   AAPL
   MSFT
   GOOGL
   ```

3. **特定銘柄のダウンロード**
   ```bash
   dotnet run -- --symbols AAPL,MSFT,GOOGL
   ```
   - カンマ区切りで複数銘柄を指定可能

4. **日付範囲を指定してダウンロード**
   ```bash
   dotnet run -- --sp500 --start-date 2023-01-01 --end-date 2024-12-31
   ```
   - 特定期間のデータのみを取得

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
- S&P 500銘柄のダウンロード成功率：99.8%（最新のテストで503銘柄中502銘柄成功）
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

### 依存関係
- Polly: リトライ処理
- CsvHelper: CSV操作
- HtmlAgilityPack: HTML解析

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
プルリクエストやイシューの報告を歓迎します。
