@echo off
echo ===================================================
echo S^&P 500 Symbols Download Test
echo ===================================================

rem Create test output directory
if not exist ".\sp500_test_output" mkdir ".\sp500_test_output"

echo Date and Time: %date% %time%
echo.

echo Starting S^&P 500 symbols download...
echo Parallel downloads: 5
echo Output directory: .\sp500_test_output
echo ---------------------------------------------------

dotnet run --project USStockDownloader/USStockDownloader.csproj -- --sp500 -p 5 -o ./sp500_test_output

echo.
echo Test completed. Checking results...
echo ---------------------------------------------------

rem Count result files
set /a total_files=0
for %%F in (".\sp500_test_output\*.csv") do set /a total_files+=1

rem Check for failure report
if exist ".\sp500_test_output\failed_symbols_report.csv" (
    echo Failed symbols report found: .\sp500_test_output\failed_symbols_report.csv
) else (
    echo No failed symbols report found. All symbols were downloaded successfully.
)

echo.
echo Result Summary:
echo - Total CSV files downloaded: %total_files%
echo - Check sp500_test_output directory for detailed results
echo ===================================================
