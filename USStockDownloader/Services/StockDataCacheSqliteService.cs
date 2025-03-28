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
            //        _logger.LogDebug("既存のデータベースファイルを削除しました: {DbPath} (Deleted existing database file)", _dbPath);
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
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                
                // 各年のデータテーブルを作成するための準備
                // （実際のテーブルは年別に動的に作成される）
                var years = Enumerable.Range(2000, DateTime.Now.Year - 1999).ToList();
                
                using (var transaction = connection.BeginTransaction())
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    
                    // 銘柄リストテーブルの作成
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS StockSymbols (
                            SymbolId INTEGER PRIMARY KEY AUTOINCREMENT,
                            Symbol TEXT NOT NULL UNIQUE
                        );";
                    command.ExecuteNonQuery();
                    
                    // テーブル情報を保持するメタデータテーブルの作成
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS TableMeta (
                            TableName TEXT PRIMARY KEY,
                            LastUpdated TEXT NOT NULL
                        );";
                    command.ExecuteNonQuery();
                    
                    // 「データが存在しない期間」を記録するテーブル
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS StockDataNoDataPeriods (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            SymbolId INTEGER NOT NULL,
                            StartDate INTEGER NOT NULL,
                            EndDate INTEGER NOT NULL,
                            UNIQUE(SymbolId, StartDate, EndDate),
                            FOREIGN KEY (SymbolId) REFERENCES StockSymbols(SymbolId)
                        );";
                    command.ExecuteNonQuery();
                    
                    // StockDataCacheInfoテーブルの作成（stock_data_cache.jsonの代替）
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS StockDataCacheInfo (
                            Symbol TEXT PRIMARY KEY,
                            LastUpdate TEXT NOT NULL,
                            StartDate TEXT NOT NULL,
                            EndDate TEXT NOT NULL,
                            LastTradingDate TEXT NOT NULL
                        );";
                    command.ExecuteNonQuery();
                    
                    // LatestTradingDateテーブルの作成（latest_trading_date_cache.jsonの代替）
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS LatestTradingDates (
                            Date TEXT PRIMARY KEY,
                            LastChecked TEXT NOT NULL
                        );";
                    command.ExecuteNonQuery();
                    
                    // RuntimeCheckテーブルの作成（runtime_check.jsonの代替）
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS RuntimeChecks (
                            Key TEXT PRIMARY KEY,
                            Value TEXT NOT NULL,
                            LastUpdated TEXT NOT NULL
                        );";
                    command.ExecuteNonQuery();
                    
                    // インデックスの作成
                    command.CommandText = @"
                        CREATE INDEX IF NOT EXISTS idx_symbol ON StockSymbols(Symbol);
                        CREATE INDEX IF NOT EXISTS idx_nodata_symbolid ON StockDataNoDataPeriods(SymbolId);
                        CREATE INDEX IF NOT EXISTS idx_nodata_dates ON StockDataNoDataPeriods(StartDate, EndDate);
                    ";
                    command.ExecuteNonQuery();
                    
                    transaction.Commit();
                }
                
                _logger.LogDebug("データベースの初期化が完了しました: {DbPath} (Database initialized)", _dbPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "データベースの初期化に失敗しました (Database initialization failed)");
                throw;
            }
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
                
                _logger.LogDebug("SQLiteの設定を最適化しました: WALモード={Result}, busy_timeout=30000ms (Optimized SQLite settings: WAL mode={Result}, busy_timeout=30000ms)",
                    result, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SQLite設定の最適化中にエラーが発生しました (Error optimizing SQLite settings)");
            }
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
                    
                    _logger.LogDebug("{Year}年のデータテーブルを作成しました (Created data table for year {Year})",
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
                    
                    _logger.LogDebug("{Count}年分のデータテーブルを事前作成しました (Pre-created data tables for {Count} years)",
                        years.Count(), years.Count());
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    _logger.LogError(ex, "年テーブルの事前作成中にエラーが発生しました (Error pre-creating year tables)");
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
        private async Task<int> GetOrCreateSymbolIdAsync(string symbol, SqliteConnection connection, SqliteTransaction? transaction = null)
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
                    return Convert.ToInt32(result); // Unboxingを修正
                }
                
                // シンボルが存在しない場合は作成
                command.Parameters.Clear();
                command.CommandText = "INSERT INTO StockSymbols (Symbol) VALUES (@Symbol); SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("@Symbol", symbol);
                
                result = await command.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    return Convert.ToInt32(result); // Unboxingを修正
                }
                
                throw new InvalidOperationException($"シンボル {symbol} のIDの作成に失敗しました");
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
                _logger.LogDebug("{Symbol}の保存対象データがありません (No data to save for {Symbol})",
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
                        _logger.LogDebug("{Symbol}の株価データを{Count}件保存しました (Saved {Count} data points for {Symbol})",
                            symbol, dataPoints.Count, dataPoints.Count, symbol);
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        _logger.LogError(ex, "株価データの保存中にエラーが発生しました (Error saving stock data)");
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
                        _logger.LogDebug("{Symbol}のデータなし期間を記録しました: {StartDate}～{EndDate} (Recorded no-data period for {Symbol}: {StartDate} to {EndDate})",
                            symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"),
                            symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        _logger.LogError(ex, "データなし期間の記録中にエラーが発生しました (Error recording no-data period)");
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
                
                int symbolId = Convert.ToInt32(symbolIdResult); // Unboxingを修正
                int dateInt = DateToInt(date);
                
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT COUNT(*)
                    FROM StockDataNoDataPeriods
                    WHERE SymbolId = @SymbolId AND @Date BETWEEN StartDate AND EndDate
                ";
                
                command.Parameters.AddWithValue("@SymbolId", symbolId);
                command.Parameters.AddWithValue("@Date", dateInt);
                
                var countResult = await command.ExecuteScalarAsync();
                if (countResult != null && countResult != DBNull.Value)
                {
                    var count = Convert.ToInt64(countResult); // Unboxingを修正
                    return count > 0;
                }
                return false;
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
                
                int symbolId = Convert.ToInt32(symbolIdResult); // Unboxingを修正
                
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
                    
                    var countResult = await dataCommand.ExecuteScalarAsync();
                    if (countResult != null && countResult != DBNull.Value)
                    {
                        var count = Convert.ToInt64(countResult); // Unboxingを修正
                        if (count > 0)
                        {
                            return true;
                        }
                    }
                }
                
                return false;
            });
        }

        /// <summary>
        /// データポイントをデータベースから取得します
        /// </summary>
        private async Task<int> GetDataPointCountForYearAsync(int year, int symbolId, SqliteConnection connection)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT COUNT(*) FROM StockData_{year} WHERE SymbolId = @SymbolId";
                command.Parameters.AddWithValue("@SymbolId", symbolId);
                
                var result = await command.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    return Convert.ToInt32(result); // Unboxingを修正
                }
                
                return 0;
            }
            catch (SqliteException ex) when (ex.Message.Contains("no such table"))
            {
                // テーブルが存在しない場合は0件と見なす
                return 0;
            }
        }
        
        /// <summary>
        /// テーブルが空かどうかを確認し、必要に応じて削除します
        /// </summary>
        private async Task CleanupEmptyTableAsync(string tableName)
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await _dbSemaphore.WaitAsync();
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();
                    
                    using var countCommand = connection.CreateCommand();
                    countCommand.CommandText = $"SELECT COUNT(*) FROM {tableName}";
                    
                    var countResult = await countCommand.ExecuteScalarAsync();
                    if (countResult != null && countResult != DBNull.Value)
                    {
                        var count = Convert.ToInt32(countResult); // Unboxingを修正
                        
                        if (count == 0)
                        {
                            // テーブルが空なら削除
                            using var dropCommand = connection.CreateCommand();
                            dropCommand.CommandText = $"DROP TABLE {tableName}";
                            await dropCommand.ExecuteNonQueryAsync();
                            
                            _logger.LogDebug("空のテーブルを削除しました: {TableName} (Dropped empty table)", tableName);
                        }
                    }
                }
                finally
                {
                    _dbSemaphore.Release();
                }
            });
        }

        #region StockDataCacheInfo Methods

        /// <summary>
        /// StockDataCacheInfoをデータベースから取得します
        /// </summary>
        public async Task<Dictionary<string, StockDataCacheInfo>> GetStockDataCacheInfoAsync()
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                await _dbSemaphore.WaitAsync();
                try
                {
                    var result = new Dictionary<string, StockDataCacheInfo>();
                    
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();
                    
                    using var command = connection.CreateCommand();
                    command.CommandText = @"
                        SELECT Symbol, LastUpdate, StartDate, EndDate, LastTradingDate
                        FROM StockDataCacheInfo";
                    
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var symbol = reader.GetString(0);
                        var info = new StockDataCacheInfo
                        {
                            Symbol = symbol,
                            LastUpdate = DateTime.Parse(reader.GetString(1)),
                            StartDate = DateTime.Parse(reader.GetString(2)),
                            EndDate = DateTime.Parse(reader.GetString(3)),
                            LastTradingDate = DateTime.Parse(reader.GetString(4))
                        };
                        
                        result[symbol] = info;
                    }
                    
                    return result;
                }
                finally
                {
                    _dbSemaphore.Release();
                }
            });
        }
        
        /// <summary>
        /// StockDataCacheInfoをデータベースに保存または更新します
        /// </summary>
        public async Task SaveStockDataCacheInfoAsync(Dictionary<string, StockDataCacheInfo> cacheInfo)
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await _dbSemaphore.WaitAsync();
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();
                    
                    using var transaction = connection.BeginTransaction();
                    
                    // 既存データをクリア
                    using (var clearCommand = connection.CreateCommand())
                    {
                        clearCommand.Transaction = transaction;
                        clearCommand.CommandText = "DELETE FROM StockDataCacheInfo";
                        await clearCommand.ExecuteNonQueryAsync();
                    }
                    
                    // 新しいデータを挿入
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = @"
                            INSERT INTO StockDataCacheInfo (Symbol, LastUpdate, StartDate, EndDate, LastTradingDate)
                            VALUES (@Symbol, @LastUpdate, @StartDate, @EndDate, @LastTradingDate)
                        ";
                        
                        var symbolParam = command.CreateParameter();
                        symbolParam.ParameterName = "@Symbol";
                        command.Parameters.Add(symbolParam);
                        
                        var lastUpdateParam = command.CreateParameter();
                        lastUpdateParam.ParameterName = "@LastUpdate";
                        command.Parameters.Add(lastUpdateParam);
                        
                        var startDateParam = command.CreateParameter();
                        startDateParam.ParameterName = "@StartDate";
                        command.Parameters.Add(startDateParam);
                        
                        var endDateParam = command.CreateParameter();
                        endDateParam.ParameterName = "@EndDate";
                        command.Parameters.Add(endDateParam);
                        
                        var lastTradingDateParam = command.CreateParameter();
                        lastTradingDateParam.ParameterName = "@LastTradingDate";
                        command.Parameters.Add(lastTradingDateParam);
                        
                        foreach (var entry in cacheInfo)
                        {
                            symbolParam.Value = entry.Key;
                            lastUpdateParam.Value = entry.Value.LastUpdate.ToString("o");
                            startDateParam.Value = entry.Value.StartDate.ToString("o");
                            endDateParam.Value = entry.Value.EndDate.ToString("o");
                            lastTradingDateParam.Value = entry.Value.LastTradingDate.ToString("o");
                            
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                    
                    transaction.Commit();
                }
                finally
                {
                    _dbSemaphore.Release();
                }
            });
        }
        
        /// <summary>
        /// 特定の銘柄のStockDataCacheInfoを更新します
        /// </summary>
        public async Task UpdateStockDataCacheInfoAsync(string symbol, StockDataCacheInfo info)
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await _dbSemaphore.WaitAsync();
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();
                    
                    using var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT OR REPLACE INTO StockDataCacheInfo (Symbol, LastUpdate, StartDate, EndDate, LastTradingDate)
                        VALUES (@Symbol, @LastUpdate, @StartDate, @EndDate, @LastTradingDate)
                    ";
                    
                    command.Parameters.AddWithValue("@Symbol", symbol);
                    command.Parameters.AddWithValue("@LastUpdate", info.LastUpdate.ToString("o"));
                    command.Parameters.AddWithValue("@StartDate", info.StartDate.ToString("o"));
                    command.Parameters.AddWithValue("@EndDate", info.EndDate.ToString("o"));
                    command.Parameters.AddWithValue("@LastTradingDate", info.LastTradingDate.ToString("o"));
                    
                    await command.ExecuteNonQueryAsync();
                }
                finally
                {
                    _dbSemaphore.Release();
                }
            });
        }
        
        /// <summary>
        /// 特定の銘柄のStockDataCacheInfoを削除します
        /// </summary>
        public async Task RemoveStockDataCacheInfoAsync(string symbol)
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await _dbSemaphore.WaitAsync();
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();
                    
                    using var command = connection.CreateCommand();
                    command.CommandText = "DELETE FROM StockDataCacheInfo WHERE Symbol = @Symbol";
                    command.Parameters.AddWithValue("@Symbol", symbol);
                    
                    await command.ExecuteNonQueryAsync();
                }
                finally
                {
                    _dbSemaphore.Release();
                }
            });
        }
        
        #endregion
        
        #region LatestTradingDate Methods
        
        /// <summary>
        /// 最新の取引日情報を取得します
        /// </summary>
        public async Task<DateTime?> GetLatestTradingDateAsync(DateTime date)
        {
            DateTime? result = null;
            
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await _dbSemaphore.WaitAsync();
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();
                    
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT Date FROM LatestTradingDates WHERE Date = @Date";
                    command.Parameters.AddWithValue("@Date", date.Date.ToString("yyyy-MM-dd"));
                    
                    var queryResult = await command.ExecuteScalarAsync();
                    
                    if (queryResult != null && queryResult != DBNull.Value)
                    {
                        var dateStr = queryResult.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(dateStr))
                        {
                            result = DateTime.Parse(dateStr);
                        }
                    }
                }
                finally
                {
                    _dbSemaphore.Release();
                }
            });
            
            return result;
        }
        
        /// <summary>
        /// 最新の取引日情報を保存します
        /// </summary>
        public async Task SaveLatestTradingDateAsync(DateTime date)
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await _dbSemaphore.WaitAsync();
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();
                    
                    using var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT OR REPLACE INTO LatestTradingDates (Date, LastChecked)
                        VALUES (@Date, @LastChecked)";
                    
                    command.Parameters.AddWithValue("@Date", date.Date.ToString("yyyy-MM-dd"));
                    command.Parameters.AddWithValue("@LastChecked", DateTime.Now.ToString("o"));
                    
                    await command.ExecuteNonQueryAsync();
                }
                finally
                {
                    _dbSemaphore.Release();
                }
            });
        }
        
        #endregion
        
        #region RuntimeCheck Methods
        
        /// <summary>
        /// ランタイムチェック情報を取得します
        /// </summary>
        public async Task<Dictionary<string, string>> GetRuntimeChecksAsync()
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                await _dbSemaphore.WaitAsync();
                try
                {
                    var result = new Dictionary<string, string>();
                    
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();
                    
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT Key, Value FROM RuntimeChecks";
                    
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var key = reader.GetString(0);
                        var value = reader.GetString(1);
                        result[key] = value;
                    }
                    
                    return result;
                }
                finally
                {
                    _dbSemaphore.Release();
                }
            });
        }
        
        /// <summary>
        /// ランタイムチェック情報を保存します
        /// </summary>
        public async Task SaveRuntimeCheckAsync(string key, string value)
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await _dbSemaphore.WaitAsync();
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();
                    
                    using var command = connection.CreateCommand();
                    command.CommandText = @"
                        INSERT OR REPLACE INTO RuntimeChecks (Key, Value, LastUpdated)
                        VALUES (@Key, @Value, @LastUpdated)";
                    
                    command.Parameters.AddWithValue("@Key", key);
                    command.Parameters.AddWithValue("@Value", value);
                    command.Parameters.AddWithValue("@LastUpdated", DateTime.Now.ToString("o"));
                    
                    await command.ExecuteNonQueryAsync();
                }
                finally
                {
                    _dbSemaphore.Release();
                }
            });
        }
        
        /// <summary>
        /// ランタイムチェック情報を削除します
        /// </summary>
        public async Task RemoveRuntimeCheckAsync(string key)
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await _dbSemaphore.WaitAsync();
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();
                    
                    using var command = connection.CreateCommand();
                    command.CommandText = "DELETE FROM RuntimeChecks WHERE Key = @Key";
                    command.Parameters.AddWithValue("@Key", key);
                    
                    await command.ExecuteNonQueryAsync();
                }
                finally
                {
                    _dbSemaphore.Release();
                }
            });
        }
        
        #endregion
    }
}
