# リリースノート (Release Notes)

## バージョン 1.0.0 (2025-03-25)

### 新機能 (New Features)
- **シングルファイル配布対応**: トリミングされたビルドでのシリアライゼーションエラーを解決 (Single-file distribution support: Resolved serialization errors in trimmed builds)
  - System.Text.Jsonのソースジェネレーターを実装 (Implemented System.Text.Json source generators)
  - リフレクションベースのシリアライゼーションからの移行 (Migrated from reflection-based serialization)
  - トリミングされたビルドでの互換性向上 (Improved compatibility with trimmed builds)

- **ログ出力の改善**: セキュリティ向上のためのパス表示の最適化 (Improved logging: Optimized path display for enhanced security)
  - 絶対パスの代わりに相対パスを使用 (Used relative paths instead of absolute paths)
  - 機密情報の露出リスクを低減 (Reduced risk of exposing sensitive information)

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

### 成果 (Achievements)
- **S&P 500銘柄のダウンロード成功率**: 100%（503銘柄中503銘柄成功） (S&P 500 symbol download success rate: 100% (503 out of 503 symbols successful))
- **以前問題のあった銘柄も正常にダウンロード可能**: ETR、BRK.B、BF.B (Previously problematic symbols now downloadable normally: ETR, BRK.B, BF.B)
- **並列処理数5での全銘柄ダウンロード時間**: 約2分 (Download time for all symbols with 5 parallel processes: About 2 minutes)

### バグ修正 (Bug Fixes)
- シングルファイルビルド時のシリアライゼーションエラーを修正 (Fixed serialization errors during single-file builds)
- キャッシュ読み込み時のエラーハンドリングを改善 (Improved error handling when loading caches)
- ログメッセージの一部を改善 (Improved some log messages)

### 既知の問題 (Known Issues)
- 一部のサードパーティライブラリでトリミング警告が発生しますが、通常の使用には影響しません (Some third-party libraries generate trimming warnings, but they do not affect normal use)
