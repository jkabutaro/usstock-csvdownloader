using Microsoft.Extensions.Logging;
using System;

namespace USStockDownloader.Utils
{
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
        public static ILogger<TTarget> CreateLoggerForType<TSource, TTarget>(this ILogger<TSource> logger)
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
        private class TypedLoggerWrapper<TSource, TTarget> : ILogger<TTarget>
        {
            private readonly ILogger<TSource> _innerLogger;

            public TypedLoggerWrapper(ILogger<TSource> innerLogger)
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
}
