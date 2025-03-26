using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using USStockDownloader.Models;
using USStockDownloader.Utils;

namespace USStockDownloader.Services
{
    /// <summary>
    /// 株価データをSQLiteデータベースでキャッシュするサービス
    /// </summary>
    public class StockDataCacheSqliteService
    {
        private readonly string _connectionString;
        private readonly ILogger<StockDataCacheSqliteService> _logger;
        private readonly string _dbPath;
        
        // データベースアクセスを同期化するためのセマフォ
        private static readonly SemaphoreSlim _dbSemaphore = new SemaphoreSlim(1, 1);
        
        // 年ごとのテーブルアクセスを同期化するためのセマフォ
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _yearSemaphores = 
            new ConcurrentDictionary<int, SemaphoreSlim>();
        
        // SQLiteデータベースロック時のリトライポリシー
        private readonly AsyncRetryPolicy _retryPolicy;

        public StockDataCacheSqliteService(string cacheDirectory, ILogger<StockDataCacheSqliteService> logger)
        {
            _logger = logger;
            _dbPath = Path.Combine(cacheDirectory, "StockData.db");

            //// スキーマ変更時のトラブルを防ぐため、ファイルが存在する場合は削除する（開発中のみ）
            //if (File.Exists(_dbPath))
            //{
            //    try
            //    {
            //        File.Delete(_dbPath);
            //        _logger.LogInformation("既存のデータベースファイルを削除しました: {DbPath} (Deleted existing database file)", _dbPath);
            //    }
            //    catch (Exception ex)
            //    {
            //        _logger.LogWarning("データベースファイルの削除に失敗しました: {Error} (Failed to delete database file)", ex.Message);
            //    }
            //}

            // BusyTimeoutは接続文字列ではなく、PRAGMAで設定
            _connectionString = $"Data Source={_dbPath};";
            
            // リトライポリシーの設定
            _retryPolicy = Policy
                .Handle<SqliteException>(ex => ex.SqliteErrorCode == 5) // SQLite Error 5: database is locked
                .WaitAndRetryAsync(
                    retryCount: 5,
                    sleepDurationProvider: retryAttempt => 
                        TimeSpan.FromMilliseconds(100 * Math.Pow(2, retryAttempt)) // 指数バックオフ
                        + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100)), // ジッター
                    onRetry: (ex, timeSpan, retryCount, context) => 
                        _logger.LogWarning("データベースロックを検出しました。リトライを実行します。(Database lock detected. Retrying)")
                );
            
            InitializeDatabase();
            EnableWalMode();
        }
        
        /// <summary>
        /// WALモードを有効化します
        /// </summary>
        private void EnableWalMode()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                
                using var command = connection.CreateCommand();
                
                // busy_timeoutを設定（30秒）
                command.CommandText = "PRAGMA busy_timeout = 30000;";
                command.ExecuteNonQuery();
                
                // WALモードを有効化
                command.CommandText = "PRAGMA journal_mode=WAL;";
                var result = command.ExecuteScalar();
                
                // 同期設定を最適化
                command.CommandText = "PRAGMA synchronous=NORMAL;";
                command.ExecuteNonQuery();
                
                _logger.LogInformation("SQLiteの設定を最適化しました: WALモード={Result}, busy_timeout=30000ms (Optimized SQLite settings: WAL mode={Result}, busy_timeout=30000ms)",
                    result, result);
            }
            catch (Exception ex)
            {
                ExceptionLogger.LogException(_logger, ex, "SQLite設定の最適化中にエラーが発生しました");
            }
        }
        
        /// <summary>
        /// 年ごとのセマフォを取得します
        /// </summary>
        private SemaphoreSlim GetYearSemaphore(int year)
        {
            return _yearSemaphores.GetOrAdd(year, _ => new SemaphoreSlim(1, 1));
        }

        /// <summary>
        /// データベースを初期化します
        /// </summary>
        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            
            using var command = connection.CreateCommand();
            command.CommandText = @"
                -- 銘柄マスターテーブル
                CREATE TABLE IF NOT EXISTS StockSymbols (
                    SymbolId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Symbol TEXT UNIQUE NOT NULL
                );
                
                -- データが存在しない期間のテーブル
                CREATE TABLE IF NOT EXISTS StockDataNoDataPeriods (
                    SymbolId INTEGER NOT NULL,
                    StartDate INTEGER NOT NULL,
                    EndDate INTEGER NOT NULL,
                    PRIMARY KEY (SymbolId, StartDate, EndDate),
                    FOREIGN KEY (SymbolId) REFERENCES StockSymbols(SymbolId)
                );
                
                -- インデックス
                CREATE INDEX IF NOT EXISTS idx_symbol ON StockSymbols(Symbol);
                CREATE INDEX IF NOT EXISTS idx_nodata_symbolid ON StockDataNoDataPeriods(SymbolId);
                CREATE INDEX IF NOT EXISTS idx_nodata_dates ON StockDataNoDataPeriods(StartDate, EndDate);
            ";
            command.ExecuteNonQuery();
            
            _logger.LogInformation("株価データキャッシュデータベースを初期化しました: {DbPath} (Initialized stock data cache database)",
                _dbPath);
        }
        
        /// <summary>
        /// 年ごとのテーブルを作成します
        /// </summary>
        private async Task EnsureYearTableExistsAsync(int year)
        {
            // 年ごとのセマフォを取得
            var yearSemaphore = GetYearSemaphore(year);
            
            await yearSemaphore.WaitAsync();
            try
            {
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();
                    
                    // テーブルが既に存在するか確認
                    using var checkCommand = connection.CreateCommand();
                    checkCommand.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='StockData_{year}'";
                    var tableName = await checkCommand.ExecuteScalarAsync();
                    
                    if (tableName != null && tableName != DBNull.Value)
                    {
                        return; // テーブルが既に存在する場合は何もしない
                    }
                    
                    using var command = connection.CreateCommand();
                    command.CommandText = $@"
                        CREATE TABLE IF NOT EXISTS StockData_{year} (
                            SymbolId INTEGER NOT NULL,
                            Date INTEGER NOT NULL,
                            Open REAL,
                            High REAL,
                            Low REAL,
                            Close REAL,
                            AdjClose REAL,
                            Volume INTEGER,
                            PRIMARY KEY (SymbolId, Date),
                            FOREIGN KEY (SymbolId) REFERENCES StockSymbols(SymbolId)
                        );
                        
                        CREATE INDEX IF NOT EXISTS idx_{year}_symbolid ON StockData_{year}(SymbolId);
                        CREATE INDEX IF NOT EXISTS idx_{year}_date ON StockData_{year}(Date);
                    ";
                    
                    await command.ExecuteNonQueryAsync();
                    
                    _logger.LogInformation("{Year}年のデータテーブルを作成しました (Created data table for year {Year})",
                        year, year);
                });
            }
            finally
            {
                yearSemaphore.Release();
            }
        }
        
        /// <summary>
        /// 複数の年テーブルを事前に作成します
        /// </summary>
        public async Task EnsureYearTablesExistAsync(IEnumerable<int> years)
        {
            await _dbSemaphore.WaitAsync();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                using var transaction = connection.BeginTransaction();
                
                try
                {
                    foreach (var year in years)
                    {
                        using var command = connection.CreateCommand();
                        command.Transaction = transaction;
                        command.CommandText = $@"
                            CREATE TABLE IF NOT EXISTS StockData_{year} (
                                SymbolId INTEGER NOT NULL,
                                Date INTEGER NOT NULL,
                                Open REAL,
                                High REAL,
                                Low REAL,
                                Close REAL,
                                AdjClose REAL,
                                Volume INTEGER,
                                PRIMARY KEY (SymbolId, Date),
                                FOREIGN KEY (SymbolId) REFERENCES StockSymbols(SymbolId)
                            );
                            
                            CREATE INDEX IF NOT EXISTS idx_{year}_symbolid ON StockData_{year}(SymbolId);
                            CREATE INDEX IF NOT EXISTS idx_{year}_date ON StockData_{year}(Date);
                        ";
                        await command.ExecuteNonQueryAsync();
                    }
                    
                    transaction.Commit();
                    
                    _logger.LogInformation("{Count}年分のデータテーブルを事前作成しました (Pre-created data tables for {Count} years)",
                        years.Count(), years.Count());
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    ExceptionLogger.LogException(_logger, ex, "年テーブルの事前作成中にエラーが発生しました");
                    throw;
                }
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }
        
        /// <summary>
        /// シンボルIDを取得または作成します
        /// </summary>
        private async Task<int> GetOrCreateSymbolIdAsync(string symbol, SqliteConnection connection, SqliteTransaction transaction = null)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                using var command = connection.CreateCommand();
                if (transaction != null)
                {
                    command.Transaction = transaction;
                }
                
                // シンボルIDを取得
                command.CommandText = "SELECT SymbolId FROM StockSymbols WHERE Symbol = @Symbol";
                command.Parameters.AddWithValue("@Symbol", symbol);
                
                var result = await command.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    return Convert.ToInt32(result);
                }
                
                // シンボルが存在しない場合は作成
                command.Parameters.Clear();
                command.CommandText = "INSERT INTO StockSymbols (Symbol) VALUES (@Symbol); SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("@Symbol", symbol);
                
                result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            });
        }
        
        /// <summary>
        /// 日付を整数形式に変換します (yyyyMMdd)
        /// </summary>
        private int DateToInt(DateTime date)
        {
            return date.Year * 10000 + date.Month * 100 + date.Day;
        }
        
        /// <summary>
        /// 整数形式の日付をDateTimeに変換します
        /// </summary>
        private DateTime IntToDate(int dateInt)
        {
            int year = dateInt / 10000;
            int month = (dateInt / 100) % 100;
            int day = dateInt % 100;
            return new DateTime(year, month, day);
        }

        /// <summary>
        /// 株価データをデータベースに保存します
        /// </summary>
        public async Task SaveStockDataAsync(string symbol, List<StockDataPoint> dataPoints)
        {
            if (dataPoints == null || dataPoints.Count == 0)
            {
                _logger.LogWarning("{Symbol}の保存対象データがありません (No data to save for {Symbol})",
                    symbol, symbol);
                return;
            }
            
            // データポイントを年ごとにグループ化
            var dataByYear = dataPoints.GroupBy(d => d.Date.Year);
            
            // 必要な年テーブルを事前に作成
            var years = dataByYear.Select(g => g.Key).ToList();
            foreach (var year in years)
            {
                await EnsureYearTableExistsAsync(year);
            }
            
            await _dbSemaphore.WaitAsync();
            try
            {
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();
                    
                    using var transaction = connection.BeginTransaction();
                    
                    try
                    {
                        // シンボルIDを取得または作成
                        int symbolId = await GetOrCreateSymbolIdAsync(symbol, connection, transaction);
                        
                        foreach (var yearGroup in dataByYear)
                        {
                            int year = yearGroup.Key;
                            
                            // データを挿入
                            using var command = connection.CreateCommand();
                            command.Transaction = transaction;
                            command.CommandText = $@"
                                INSERT OR REPLACE INTO StockData_{year} 
                                (SymbolId, Date, Open, High, Low, Close, AdjClose, Volume)
                                VALUES 
                                (@SymbolId, @Date, @Open, @High, @Low, @Close, @AdjClose, @Volume)
                            ";
                            
                            var symbolIdParam = command.CreateParameter();
                            symbolIdParam.ParameterName = "@SymbolId";
                            symbolIdParam.Value = symbolId;
                            command.Parameters.Add(symbolIdParam);
                            
                            var dateParam = command.CreateParameter();
                            dateParam.ParameterName = "@Date";
                            command.Parameters.Add(dateParam);
                            
                            var openParam = command.CreateParameter();
                            openParam.ParameterName = "@Open";
                            command.Parameters.Add(openParam);
                            
                            var highParam = command.CreateParameter();
                            highParam.ParameterName = "@High";
                            command.Parameters.Add(highParam);
                            
                            var lowParam = command.CreateParameter();
                            lowParam.ParameterName = "@Low";
                            command.Parameters.Add(lowParam);
                            
                            var closeParam = command.CreateParameter();
                            closeParam.ParameterName = "@Close";
                            command.Parameters.Add(closeParam);
                            
                            var adjCloseParam = command.CreateParameter();
                            adjCloseParam.ParameterName = "@AdjClose";
                            command.Parameters.Add(adjCloseParam);
                            
                            var volumeParam = command.CreateParameter();
                            volumeParam.ParameterName = "@Volume";
                            command.Parameters.Add(volumeParam);
                            
                            foreach (var dataPoint in yearGroup)
                            {
                                dateParam.Value = DateToInt(dataPoint.Date);
                                openParam.Value = (decimal)dataPoint.Open;
                                highParam.Value = (decimal)dataPoint.High;
                                lowParam.Value = (decimal)dataPoint.Low;
                                closeParam.Value = (decimal)dataPoint.Close;
                                adjCloseParam.Value = (decimal)dataPoint.AdjClose;
                                volumeParam.Value = dataPoint.Volume;
                                
                                await command.ExecuteNonQueryAsync();
                            }
                        }
                        
                        transaction.Commit();
                        _logger.LogInformation("{Symbol}の株価データを{Count}件保存しました (Saved {Count} data points for {Symbol})",
                            symbol, dataPoints.Count, dataPoints.Count, symbol);
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        ExceptionLogger.LogException(_logger, ex, "株価データの保存中にエラーが発生しました");
                        throw;
                    }
                });
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        /// <summary>
        /// 株価データをデータベースから取得します
        /// </summary>
        public async Task<List<StockDataPoint>> GetStockDataAsync(string symbol, DateTime startDate, DateTime endDate)
        {
            var result = new List<StockDataPoint>();
            
            // 対象となる年を特定
            int startYear = startDate.Year;
            int endYear = endDate.Year;
            int startDateInt = DateToInt(startDate);
            int endDateInt = DateToInt(endDate);
            
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // シンボルIDを取得
                var symbolId = await GetOrCreateSymbolIdAsync(symbol, connection);
                
                for (int year = startYear; year <= endYear; year++)
                {
                    // テーブルが存在するか確認
                    using var checkCommand = connection.CreateCommand();
                    checkCommand.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='StockData_{year}'";
                    var tableName = await checkCommand.ExecuteScalarAsync();
                    
                    if (tableName == null || tableName == DBNull.Value)
                    {
                        continue; // テーブルが存在しない場合はスキップ
                    }
                    
                    // 年ごとのデータを取得
                    using var command = connection.CreateCommand();
                    
                    // 年の範囲に応じてクエリを調整
                    if (year == startYear && year == endYear)
                    {
                        // 開始年と終了年が同じ場合
                        command.CommandText = $@"
                            SELECT Date, Open, High, Low, Close, AdjClose, Volume
                            FROM StockData_{year}
                            WHERE SymbolId = @SymbolId AND Date BETWEEN @StartDate AND @EndDate
                            ORDER BY Date
                        ";
                        command.Parameters.AddWithValue("@StartDate", startDateInt);
                        command.Parameters.AddWithValue("@EndDate", endDateInt);
                    }
                    else if (year == startYear)
                    {
                        // 開始年の場合
                        command.CommandText = $@"
                            SELECT Date, Open, High, Low, Close, AdjClose, Volume
                            FROM StockData_{year}
                            WHERE SymbolId = @SymbolId AND Date >= @StartDate
                            ORDER BY Date
                        ";
                        command.Parameters.AddWithValue("@StartDate", startDateInt);
                    }
                    else if (year == endYear)
                    {
                        // 終了年の場合
                        command.CommandText = $@"
                            SELECT Date, Open, High, Low, Close, AdjClose, Volume
                            FROM StockData_{year}
                            WHERE SymbolId = @SymbolId AND Date <= @EndDate
                            ORDER BY Date
                        ";
                        command.Parameters.AddWithValue("@EndDate", endDateInt);
                    }
                    else
                    {
                        // 中間の年の場合
                        command.CommandText = $@"
                            SELECT Date, Open, High, Low, Close, AdjClose, Volume
                            FROM StockData_{year}
                            WHERE SymbolId = @SymbolId
                            ORDER BY Date
                        ";
                    }
                    
                    command.Parameters.AddWithValue("@SymbolId", symbolId);
                    
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var dateInt = reader.GetInt32(0);
                        var dataPoint = new StockDataPoint
                        {
                            Date = IntToDate(dateInt),
                            Open = (decimal)reader.GetDouble(1),
                            High = (decimal)reader.GetDouble(2),
                            Low = (decimal)reader.GetDouble(3),
                            Close = (decimal)reader.GetDouble(4),
                            AdjClose = (decimal)reader.GetDouble(5),
                            Volume = reader.GetInt64(6)
                        };
                        
                        result.Add(dataPoint);
                    }
                }
                
                return result;
            });
        }

        /// <summary>
        /// データが存在しない期間を記録します
        /// </summary>
        public async Task RecordNoDataPeriodAsync(string symbol, DateTime startDate, DateTime endDate)
        {
            await _dbSemaphore.WaitAsync();
            try
            {
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();
                    
                    using var transaction = connection.BeginTransaction();
                    
                    try
                    {
                        // シンボルIDを取得または作成
                        int symbolId = await GetOrCreateSymbolIdAsync(symbol, connection, transaction);
                        
                        using var command = connection.CreateCommand();
                        command.Transaction = transaction;
                        command.CommandText = @"
                            INSERT OR REPLACE INTO StockDataNoDataPeriods (SymbolId, StartDate, EndDate)
                            VALUES (@SymbolId, @StartDate, @EndDate)
                        ";
                        
                        command.Parameters.AddWithValue("@SymbolId", symbolId);
                        command.Parameters.AddWithValue("@StartDate", DateToInt(startDate));
                        command.Parameters.AddWithValue("@EndDate", DateToInt(endDate));
                        
                        await command.ExecuteNonQueryAsync();
                        
                        transaction.Commit();
                        _logger.LogInformation("{Symbol}のデータなし期間を記録しました: {StartDate}～{EndDate} (Recorded no-data period for {Symbol}: {StartDate} to {EndDate})",
                            symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"),
                            symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        ExceptionLogger.LogException(_logger, ex, "データなし期間の記録中にエラーが発生しました");
                        throw;
                    }
                });
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        /// <summary>
        /// 指定された期間のデータが存在しないかどうかを確認します
        /// </summary>
        public async Task<bool> IsNoDataPeriodAsync(string symbol, DateTime date)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // シンボルIDを取得
                var symbolIdCommand = connection.CreateCommand();
                symbolIdCommand.CommandText = "SELECT SymbolId FROM StockSymbols WHERE Symbol = @Symbol";
                symbolIdCommand.Parameters.AddWithValue("@Symbol", symbol);
                
                var symbolIdResult = await symbolIdCommand.ExecuteScalarAsync();
                if (symbolIdResult == null || symbolIdResult == DBNull.Value)
                {
                    return false; // シンボルが存在しない場合
                }
                
                int symbolId = Convert.ToInt32(symbolIdResult);
                int dateInt = DateToInt(date);
                
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT COUNT(*)
                    FROM StockDataNoDataPeriods
                    WHERE SymbolId = @SymbolId AND @Date BETWEEN StartDate AND EndDate
                ";
                
                command.Parameters.AddWithValue("@SymbolId", symbolId);
                command.Parameters.AddWithValue("@Date", dateInt);
                
                var count = (long)await command.ExecuteScalarAsync();
                return count > 0;
            });
        }

        /// <summary>
        /// 指定されたシンボルのデータが存在するかどうかを確認します
        /// </summary>
        public async Task<bool> HasDataForSymbolAsync(string symbol)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                
                // シンボルIDを取得
                var symbolIdCommand = connection.CreateCommand();
                symbolIdCommand.CommandText = "SELECT SymbolId FROM StockSymbols WHERE Symbol = @Symbol";
                symbolIdCommand.Parameters.AddWithValue("@Symbol", symbol);
                
                var symbolIdResult = await symbolIdCommand.ExecuteScalarAsync();
                if (symbolIdResult == null || symbolIdResult == DBNull.Value)
                {
                    return false; // シンボルが存在しない場合
                }
                
                int symbolId = Convert.ToInt32(symbolIdResult);
                
                // 年テーブルを取得
                var tableCommand = connection.CreateCommand();
                tableCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'StockData_%'";
                
                using var tableReader = await tableCommand.ExecuteReaderAsync();
                var yearTables = new List<string>();
                
                while (await tableReader.ReadAsync())
                {
                    yearTables.Add(tableReader.GetString(0));
                }
                
                // 各年テーブルでデータを確認
                foreach (var yearTable in yearTables)
                {
                    var dataCommand = connection.CreateCommand();
                    dataCommand.CommandText = $"SELECT COUNT(*) FROM {yearTable} WHERE SymbolId = @SymbolId LIMIT 1";
                    dataCommand.Parameters.AddWithValue("@SymbolId", symbolId);
                    
                    var count = (long)await dataCommand.ExecuteScalarAsync();
                    if (count > 0)
                    {
                        return true;
                    }
                }
                
                return false;
            });
        }

        ///// <summary>
        ///// 既存のJSONファイルからデータをインポートします
        ///// </summary>
        //public async Task ImportFromJsonFilesAsync(string cacheDirectory)
        //{
        //    var jsonFiles = Directory.GetFiles(Path.Combine(cacheDirectory, "output"), "*.json");
        //    _logger.LogInformation("JSONファイルからのインポートを開始します。対象ファイル数: {Count} (Starting import from JSON files. Target files: {Count})",
        //        jsonFiles.Length, jsonFiles.Length);
            
        //    foreach (var jsonFile in jsonFiles)
        //    {
        //        var symbol = Path.GetFileNameWithoutExtension(jsonFile);
                
        //        // すでにデータが存在する場合はスキップ
        //        if (await HasDataForSymbolAsync(symbol))
        //        {
        //            _logger.LogInformation("{Symbol}のデータはすでに存在します。スキップします。(Data for {Symbol} already exists. Skipping.)",
        //                symbol, symbol);
        //            continue;
        //        }
                
        //        try
        //        {
        //            var json = await File.ReadAllTextAsync(jsonFile);
        //            var stockData = System.Text.Json.JsonSerializer.Deserialize<List<StockDataPoint>>(json);
                    
        //            if (stockData != null && stockData.Count > 0)
        //            {
        //                await SaveStockDataAsync(symbol, stockData);
        //                _logger.LogInformation("{Symbol}のデータを{Count}件インポートしました (Imported {Count} data points for {Symbol})",
        //                    symbol, stockData.Count, stockData.Count, symbol);
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            ExceptionLogger.LogException(_logger, ex, "JSONファイルからのインポート中にエラーが発生しました");
        //        }
        //    }
            
        //    _logger.LogInformation("JSONファイルからのインポートが完了しました (Import from JSON files completed)");
        //}
    }
}
