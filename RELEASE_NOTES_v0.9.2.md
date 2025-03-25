# 米国株式CSVダウンローダー v0.9.2 リリースノート (US Stock CSV Downloader v0.9.2 Release Notes)

## 概要 (Overview)

このリリースでは、キャッシュ機能の改善とドキュメントの更新を行いました。
(This release includes improvements to the cache functionality and documentation updates.)

## 改善内容 (Improvements)

### キャッシュ機能の改善 (Cache Function Improvement)

- **キャッシュ有効性判定の最適化** (Optimization of cache validity determination)
  - リクエスト終了日が現在日付で、キャッシュの終了日が最新取引日の場合、キャッシュを有効と判断 (When the request end date is the current date and the cache end date is the latest trading day, the cache is considered valid)
  - 不要なデータダウンロードを回避し、処理時間を短縮 (Avoids unnecessary data downloads and reduces processing time)
  - API呼び出し回数の削減によるリソース節約 (Resource saving by reducing the number of API calls)

- **詳細なログメッセージの追加** (Addition of detailed log messages)
  - キャッシュ使用状況を日本語と英語の両方で明確に表示 (Clearly displays cache usage status in both Japanese and English)
  - 「リクエスト終了日が現在日付で、キャッシュの終了日が最新取引日のため、キャッシュを使用します」などの詳細メッセージ (Detailed messages such as "Request end date is today, cache end date is the latest trading day, using cache")

### ドキュメント更新 (Documentation Updates)

- **READMEの更新** (README Updates)
  - 更新履歴セクションの追加 (Addition of update history section)
  - SBI証券銘柄リストCSVのエンコーディング情報の修正（UTF-8からShift-JISに） (Correction of SBI Securities symbol list CSV encoding information from UTF-8 to Shift-JIS)

## テスト結果 (Test Results)

- **S&P 500およびNYダウの全銘柄で正常にキャッシュが機能** (Cache functions normally for all S&P 500 and NY Dow symbols)
- **2回目以降の実行では処理が高速化** (Processing is accelerated from the second execution onwards)
  - 最新データがすでにキャッシュされている場合、Yahoo Financeへのリクエストが発生せず (No requests to Yahoo Finance if the latest data is already cached)
  - 並列処理数5での全銘柄ダウンロード時間：約2分（初回）、数秒（2回目以降、キャッシュ使用時） (Download time for all symbols with 5 parallel processes: about 2 minutes (first time), a few seconds (from the second time onwards, when using cache))

## インストール方法 (Installation)

1. [GitHubリリースページ](https://github.com/jkabutaro/usstock-csvdownloader/releases)から最新のリリース（USStockDownloader-v0.9.2.zip）をダウンロード (Download the latest release (USStockDownloader-v0.9.2.zip) from the [GitHub Releases page](https://github.com/jkabutaro/usstock-csvdownloader/releases))
2. ダウンロードしたZIPファイルを任意の場所に解凍 (Extract the downloaded ZIP file to any location)
3. `USStockDownloader.exe`をダブルクリックして実行 (Double-click `USStockDownloader.exe` to run)

## 既知の問題 (Known Issues)

- JSON処理とトリミング関連の警告がビルド時に表示されますが、アプリケーションの機能には影響しません。 (JSON processing and trimming-related warnings are displayed during build, but they do not affect the functionality of the application.)
