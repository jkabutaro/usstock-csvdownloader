@echo off
echo ===================================================
echo 問題のあるシンボルのテスト (ETR, BRK.B, BF.B)
echo ===================================================

rem テスト出力ディレクトリの作成
if not exist ".\test_output" mkdir ".\test_output"

echo 実行日時: %date% %time%
echo.

echo [1/3] ETRシンボルのテスト...
echo ---------------------------------------------------
dotnet run --project USStockDownloader/USStockDownloader.csproj -- --symbols ETR -o ./test_output
echo.

echo [2/3] BRK.Bシンボルのテスト (ピリオドをハイフンに変換)...
echo ---------------------------------------------------
dotnet run --project USStockDownloader/USStockDownloader.csproj -- --symbols BRK.B -o ./test_output
echo.

echo [3/3] BF.Bシンボルのテスト (ピリオドをハイフンに変換)...
echo ---------------------------------------------------
dotnet run --project USStockDownloader/USStockDownloader.csproj -- --symbols BF.B -o ./test_output
echo.

echo テスト完了。結果を確認しています...
echo ---------------------------------------------------
echo.

rem ファイルの存在確認
if exist ".\test_output\ETR.csv" (
    echo ETR: 成功 - ファイルが作成されました
    for %%F in (".\test_output\ETR.csv") do echo   サイズ: %%~zF バイト
) else (
    echo ETR: 失敗 - ファイルが見つかりません
)

if exist ".\test_output\BRK_B.csv" (
    echo BRK.B: 成功 - BRK_B.csvとして作成されました
    for %%F in (".\test_output\BRK_B.csv") do echo   サイズ: %%~zF バイト
) else (
    echo BRK.B: 失敗 - ファイルが見つかりません
)

if exist ".\test_output\BF_B.csv" (
    echo BF.B: 成功 - BF_B.csvとして作成されました
    for %%F in (".\test_output\BF_B.csv") do echo   サイズ: %%~zF バイト
) else (
    echo BF.B: 失敗 - ファイルが見つかりません
)

echo.
echo 詳細な結果はtest_outputディレクトリを確認してください。
echo ===================================================
