## 基本動作原則

1. **指示の受信と理解**
   - ユーザーからの指示を注意深く読み取り
   - 不明点がある場合は、具体的な質問を行う
   - 技術的な制約や要件を明確に把握

2. **深い分析とプランニング**
   ```markdown
   ## タスク分析
   - 目的：株価データの効率的なダウンロードと保存
   - 技術要件：C#, Yahoo Finance API, Polly, CsvHelper
   - 実装手順：具体的なステップごとの実装
   - リスク：API制限、データ欠落、特殊シンボル処理
   - 品質基準：高いダウンロード成功率、適切なエラー処理
   ```

## 技術スタックと制約
### コア技術
- C# (.NET Core)
- Yahoo Finance API
- Polly: リトライ処理
- CsvHelper: CSV操作
- HtmlAgilityPack: HTML解析

## 品質管理プロトコル
### 1. コード品質
- C#コーディング規約の遵守
- 適切な例外処理
- コードの一貫性維持
- コンパイルエラーは恥。コード提案するときにはコンパイルエラーが出ないようにコード全体を見直してから提案する
### 2. パフォーマンス
- 並列ダウンロードの最適化
- リトライ戦略の効率化
- メモリ使用量の最適化
### 3. エラー処理
- 詳細なログ記録
- 段階的なデバッグ手法の適用
- リトライメカニズムの活用

## プロジェクト構造規約
```
usstock-csvdownloader/
├── Services/           # サービス層
│   ├── StockDataService.cs
│   ├── StockDownloadManager.cs
│   └── IndexSymbolService.cs
├── Models/            # データモデル
│   ├── StockData.cs
│   └── StockSymbol.cs
├── Options/          # 設定クラス
│   └── RetryOptions.cs
└── Interfaces/       # インターフェース定義
    └── IStockDataService.cs
```

## 重要な制約
1. **データ形式**
   - 日付形式：yyyymmdd（数値）
   - CSVフォーマット準拠
2. **並列処理**
   - デフォルト3並列実行
   - セマフォによる制御
3. **エラー処理**
   - Pollyによるリトライ
   - 指数バックオフとジッター
   - HTTP 429の特別処理

## 実装工程

- コード生成
- テストコード生成
- テスト実行
- 報告書作成

## 実装プロセス
### 1. デバッグ手順
- console.logによる段階的なデバッグ
- 処理の開始と終了の記録
- 変数状態の追跡

### 2. エラー対応
- 問題発生箇所の特定
- ログの戦略的配置
- 原因の段階的究明



## エラー対応プロトコル
1. **問題の特定**
   - エラーメッセージの解析
   - 影響範囲の特定
   - 原因の切り分け
2. **解決策の策定**
   - 複数の対応案の検討
   - リスク評価
   - 最適解の選択
3. **実装と検証**
   - 解決策の実装
   - テストによる検証
   - 副作用の確認
4. **文書化**
   - 問題と解決策の記録
   - 再発防止策の提案
   - 学習点の共有


# ローカライゼーションルール

## 出力テキスト形式
- すべてのログメッセージ、コンソール出力、およびドキュメント（README.mdなど）は、日本語を主要言語とし、英語を補助言語として使用します。
- 形式：「日本語メッセージ (English message)」
- 例：「ダウンロードを開始します (Starting download)」

## 適用範囲
- コンソール出力メッセージ
- ログファイル
- エラーメッセージ
- ドキュメント（README.mdなど）
- ユーザーインターフェースのテキスト

## 目的
- 日本語を母国語とするユーザーの利便性を向上させる
- 英語の補助テキストを提供することで国際的な利用も可能にする


## 配布用ビルドプロトコル

### 1. シングルファイル形式でのビルド（推奨：トリミング無効）
- 配布用には必ずシングルファイル形式でビルドする
- 以下のコマンドを使用：

```bash
dotnet publish USStockDownloader -c Release -o ./publish/v{バージョン}_notrimmed -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

‐ 依存DLLファイルを含めた単一の実行ファイルを生成
‐ .NET Runtimeのインストールが不要（セルフコンテインド）
‐ リフレクションベースの機能が正常に動作
‐ 実行ファイルサイズ：約33MB

### 2. 配布パッケージの構成
- USStockDownloader.exe（シングルファイル形式でビルドされた実行ファイル）
- README.md（詳細な説明）
- RELEASE_NOTES.md（更新内容）
- 使用方法ガイド.md（基本的な使い方の説明）
- output/（空のディレクトリ） 
- Cache/（空のディレクトリ）

### 3. 配布時の注意点
- 安定性を重視するので、トリミングを無効にしたビルドを使用すること
- 配布用パッケージには必ず空のoutputディレクトリとCacheディレクトリを含める
- ZIPアーカイブ名はUSStockDownloader_v{バージョン}.zipの形式で統一


## プロンプト節約ルール

### 1. コミュニケーション効率化
- 簡潔かつ明確な指示を優先
- 冗長な説明や繰り返しを避ける
- 重要なポイントのみを強調

### 2. コード関連の指示
- コードスニペットは必要最小限に留める
- 変更が必要な箇所のみを具体的に指定
- 完全なファイル内容ではなく、変更部分のみを示す

### 3. 質問と回答
- 質問は具体的かつ焦点を絞って行う
- 複数の質問は関連性のあるものをグループ化
- 回答は簡潔に、必要な情報のみを含める

### 4. ファイル参照の最適化
- ファイルパスは短縮形で参照（相対パスを優先）
- 同じファイルを繰り返し参照する場合は略称を定義
- 大きなファイルは必要な部分のみを参照

### 5. コンテキスト管理
- 既に説明済みの情報は繰り返さない
- 新しい情報や変更点のみを強調
- 複雑な背景情報は必要な場合のみ提供


## ログ出力の標準化

### 1. ExceptionLoggerの使用
- すべての例外処理において、`ExceptionLogger.LogException`メソッドを使用してログを出力します。
- 例外メッセージは一般化し、具体的なパスやスタックトレースを含めないようにします。

### 2. ログレベルの適切な設定
- エラーや警告: 重要なエラーや警告は`LogError`や`LogWarning`を使用して出力しますが、詳細な情報は含めません。
- 情報メッセージ: 通常の情報メッセージは`LogInformation`を使用し、開発環境に依存しない内容にします。

### 3. コードレビューとテスト
- 新しく追加されたログ出力コードは、コードレビューを通じて確認し、開発環境の情報が含まれていないかをチェックします。
- ユニットテストや統合テストを実施し、例外処理が正しく機能することを確認します。

### 4. ドキュメントの更新
- ログ出力の基準や使用方法をドキュメントに明記し、チーム全体で共有します。


## 大事なこと
- 想像で実装しないこと
- わからないことは聞くこと
- ファイルや関数が大きくなってきたら、分割して思考しやすくすること


以上の指示に従い、確実で質の高い実装を行います。指示された範囲内でのみ処理を行い、不要な追加実装は行いません。不明点や重要な判断が必要な場合は、必ず確認を取ります。
