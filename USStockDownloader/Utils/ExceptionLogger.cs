using Microsoft.Extensions.Logging;
using System;

namespace USStockDownloader.Utils
{
    /// <summary>
    /// 例外のログ出力を標準化するユーティリティクラス
    /// </summary>
    public static class ExceptionLogger
    {
        /// <summary>
        /// 例外情報をログに記録します。開発環境の情報は含まれません。
        /// </summary>
        /// <param name="logger">ロガーインスタンス</param>
        /// <param name="exception">発生した例外</param>
        /// <param name="contextMessage">例外発生時のコンテキスト情報</param>
        /// <param name="logLevel">ログレベル（デフォルトはError）</param>
        public static void LogException<T>(ILogger<T> logger, Exception exception, string contextMessage, LogLevel logLevel = LogLevel.Error)
        {
            // 開発環境の情報を含まない一般化されたメッセージを生成
            string message = $"{contextMessage}: {GetSanitizedExceptionMessage(exception)} (An error occurred)";
            
            // ログレベルに応じた出力
            switch (logLevel)
            {
                case LogLevel.Critical:
                    logger.LogCritical(message);
                    break;
                case LogLevel.Error:
                    logger.LogError(message);
                    break;
                case LogLevel.Warning:
                    logger.LogWarning(message);
                    break;
                default:
                    logger.LogInformation(message);
                    break;
            }
        }
        
        /// <summary>
        /// 例外メッセージから機密情報や環境依存の情報を除去します
        /// </summary>
        private static string GetSanitizedExceptionMessage(Exception exception)
        {
            if (exception == null)
                return "不明なエラー (Unknown error)";
                
            // ファイルパスを含むメッセージの処理
            string message = exception.Message;
            
            // パス情報の削除（C:\など）
            message = System.Text.RegularExpressions.Regex.Replace(
                message, 
                @"[A-Za-z]:\\.*?\\", 
                "[パス情報]"
            );
            
            // エラーコードだけを保持し、詳細情報を省略
            if (exception is System.Data.Common.DbException)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    message, 
                    @"Error (\d+):"
                );
                if (match.Success)
                {
                    return $"データベースエラー (コード: {match.Groups[1].Value}) (Database error, code: {match.Groups[1].Value})";
                }
                return "データベースエラーが発生しました (Database error occurred)";
            }
            
            return message;
        }
    }
}
