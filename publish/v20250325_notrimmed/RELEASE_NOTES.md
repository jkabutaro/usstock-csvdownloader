# リリースノート (Release Notes)

## バージョン 2025.03.25 (2025-03-25)

### 新機能 (New Features)
- **上場廃止銘柄の特別処理**: 上場廃止された銘柄の処理を改善 (Special handling for delisted symbols: Improved handling of delisted symbols)
  - 「No data found, symbol may be delisted」エラーを検出して適切に処理 (Detect and properly handle "No data found, symbol may be delisted" errors)
  - 不要なリトライを回避してエラーログのみを出力 (Avoid unnecessary retries and output error logs only)
  - 処理時間の短縮とリソースの効率的な利用を実現 (Achieved shorter processing time and more efficient resource usage)

### 改善点 (Improvements)
- **ピリオドを含むシンボルの処理改善**: BRK.B、BF.Bなどのピリオドを含むシンボルの処理を改善 (Improved handling of symbols containing periods, such as BRK.B and BF.B)
  - Yahoo FinanceではBRK.BがBRK-Bとして扱われることを発見 (Discovered that BRK.B is treated as BRK-B in Yahoo Finance)
  - APIリクエスト時にピリオド(.)をハイフン(-)に変換 (Convert periods to hyphens when making API requests)
  - ファイル名保存時にピリオドをアンダースコアに置換（例：BRK.B → BRK_B.csv） (Replace periods with underscores when saving filenames (e.g., BRK.B → BRK_B.csv))

- **ETRシンボルの特別処理**: ETR (Entergy Corporation)のダウンロード成功率向上 (Special handling for ETR symbol: Improved download success rate for ETR (Entergy Corporation))
  - レート制限回避のための追加待機時間を設定 (Set additional wait time to avoid rate limits)

- **エラー処理とリトライ機構の強化**: 安定性の向上 (Enhanced error handling and retry mechanism: Improved stability)
  - 特別なリトライメカニズムの実装 (Implemented special retry mechanisms)
  - 詳細なエラーレポート機能 (Detailed error reporting functionality)

- **日付処理の改善**: 将来日付の自動調整機能を強化 (Date processing improvements: Enhanced automatic adjustment of future dates)
  - 将来の日付を自動的に最新の取引日に調整 (Automatically adjusts future dates to the latest trading day)
  - 日本時間と米国市場時間の差を考慮した処理 (Processing that takes into account the time difference between Japan time and US market time)
  - キャッシュ判定の精度向上 (Improved cache determination accuracy)

### 成果 (Achievements)
- **S&P 500銘柄のダウンロード成功率**: 100%（503銘柄中503銘柄成功） (S&P 500 symbol download success rate: 100% (503 out of 503 symbols))
- **以前問題のあった銘柄**: ETR、BRK.B、BF.Bも正常にダウンロード可能に (Previously problematic symbols: ETR, BRK.B, and BF.B can now be downloaded normally)
- **並列処理数5での全銘柄ダウンロード時間**: 約2分 (Download time for all symbols with 5 parallel processes: Approximately 2 minutes)
