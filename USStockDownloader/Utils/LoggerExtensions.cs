using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;
using System.IO;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

// ILoggerとILoggerProviderの衝突を避けるために明示的なimport
using MsLogger = Microsoft.Extensions.Logging.ILogger;
using MsLoggerFactory = Microsoft.Extensions.Logging.ILoggerFactory;
using MsLoggerProvider = Microsoft.Extensions.Logging.ILoggerProvider;

namespace USStockDownloader.Utils
{
    /// <summary>
    /// アプリケーション全体で使用する共通のILoggerFactoryを提供するクラス
    /// </summary>
    public static class AppLoggerFactory
    {
        private static MsLoggerFactory? _loggerFactory;
        private static readonly object _lock = new object();

        /// <summary>
        /// 共通のILoggerFactoryインスタンスを取得または作成します
        /// </summary>
        public static MsLoggerFactory GetLoggerFactory()
        {
            if (_loggerFactory == null)
            {
                lock (_lock)
                {
                    if (_loggerFactory == null)
                    {
                        // ログディレクトリを初期化
                        try 
                        {
                            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                            var logDir = Path.Combine(baseDir, "USStockDownloader_logs");
                            if (!Directory.Exists(logDir))
                            {
                                Directory.CreateDirectory(logDir);
                                Console.WriteLine($"AppLoggerFactory: ログディレクトリを作成しました: {logDir}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"AppLoggerFactory: ログディレクトリの作成に失敗しました: {ex.Message}");
                        }

                        // Serilogの設定
                        var appBaseDir = AppDomain.CurrentDomain.BaseDirectory;
                        var appLogDir = Path.Combine(appBaseDir, "USStockDownloader_logs");
                        
                        // 絶対パスを取得して表示（デバッグ用）
                        var absoluteLogPath = Path.GetFullPath(appLogDir);
                        Console.WriteLine($"AppLoggerFactory: ログディレクトリの絶対パス: {absoluteLogPath}");
                        
                        var logConfig = new LoggerConfiguration()
                            .MinimumLevel.Debug() // Debugレベルからすべて記録
                            .WriteTo.File(
                                Path.Combine(appLogDir, "USStockDownloader_debug_.log"),
                                rollingInterval: RollingInterval.Day,
                                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

                        // Serilogロガーの作成
                        var logger = logConfig.CreateLogger();

                        // LoggerFactoryの作成
                        _loggerFactory = LoggerFactory.Create(builder =>
                        {
                            // コンソール出力は行わない
                            
                            // デフォルトの最小ログレベルをDebugに設定（ファイルには全レベル出力）
                            builder.SetMinimumLevel(LogLevel.Debug);
                            
                            // Serilogを追加
                            builder.AddSerilog(logger, dispose: true);
                        });
                    }
                }
            }
            return _loggerFactory;
        }

        /// <summary>
        /// 指定された型のロガーを取得します
        /// </summary>
        public static Microsoft.Extensions.Logging.ILogger<T> CreateLogger<T>()
        {
            return GetLoggerFactory().CreateLogger<T>();
        }

        /// <summary>
        /// 指定されたカテゴリ名のロガーを取得します
        /// </summary>
        public static MsLogger CreateLogger(string categoryName)
        {
            return GetLoggerFactory().CreateLogger(categoryName);
        }
    }

    /// <summary>
    /// ILoggerのための拡張メソッド
    /// </summary>
    public static class LoggerExtensions
    {
        /// <summary>
        /// 指定された型のロガーを作成します
        /// </summary>
        /// <typeparam name="TSource">元のロガーの型</typeparam>
        /// <typeparam name="TTarget">ターゲットのロガーの型</typeparam>
        /// <param name="logger">元のロガー</param>
        /// <returns>ターゲット型のロガー</returns>
        public static Microsoft.Extensions.Logging.ILogger<TTarget> CreateLoggerForType<TSource, TTarget>(this Microsoft.Extensions.Logging.ILogger<TSource> logger)
        {
            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            // ILoggerFactoryを取得できないため、シンプルなラッパーを作成
            return new TypedLoggerWrapper<TSource, TTarget>(logger);
        }

        /// <summary>
        /// 型を変換するロガーラッパー
        /// </summary>
        private class TypedLoggerWrapper<TSource, TTarget> : Microsoft.Extensions.Logging.ILogger<TTarget>
        {
            private readonly Microsoft.Extensions.Logging.ILogger<TSource> _innerLogger;

            public TypedLoggerWrapper(Microsoft.Extensions.Logging.ILogger<TSource> innerLogger)
            {
                _innerLogger = innerLogger ?? throw new ArgumentNullException(nameof(innerLogger));
            }

            public IDisposable BeginScope<TState>(TState state) => _innerLogger.BeginScope(state);

            public bool IsEnabled(LogLevel logLevel) => _innerLogger.IsEnabled(logLevel);

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _innerLogger.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }

    /// <summary>
    /// 既存のILoggerFactoryをILoggerProviderとして利用するためのアダプター
    /// </summary>
    public class LoggerFactoryProvider : MsLoggerProvider
    {
        private readonly MsLoggerFactory _loggerFactory;

        public LoggerFactoryProvider(MsLoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        public MsLogger CreateLogger(string categoryName)
        {
            return _loggerFactory.CreateLogger(categoryName);
        }

        public void Dispose()
        {
            // LoggerFactoryの所有権はこのクラスにないので破棄しない
        }
    }
}
