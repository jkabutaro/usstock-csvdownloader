# 開発記録

## プロジェクト概要

米国株価のヒストリカルデータをCSVでダウンロードするWindowsアプリケーション

### 主な機能

- Yahoo Finance APIを使用して株価データを取得
- 複数銘柄の並列ダウンロード
- CSVファイルへの保存（日付は数値形式yyyyMMdd）

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

### 今後の課題

1. 機能拡張
   - [ ] データ取得期間の指定機能
   - [ ] 株価調整値（Adjusted Close）の追加
   - [ ] 異常値の検出と除外

2. 改善点
   - [ ] ユニットテストの追加
   - [ ] API呼び出しの最適化
   - [ ] エラー時のリカバリー機能強化

3. ドキュメント
   - [ ] APIドキュメントの作成
   - [ ] エラーコードの体系化
   - [ ] 設定値のリファレンス作成

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
