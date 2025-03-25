using System;
using System.IO;

namespace USStockDownloader.Utils
{
    /// <summary>
    /// アプリケーション全体でキャッシュディレクトリを管理するユーティリティクラス
    /// (Utility class to manage cache directories across the application)
    /// </summary>
    public static class CacheManager
    {
        /// <summary>
        /// アプリケーションのキャッシュディレクトリのパス
        /// (Path to the application's cache directory)
        /// </summary>
        public static readonly string CacheDirectory = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Cache");

        /// <summary>
        /// 指定されたファイル名に対するキャッシュファイルの完全パスを取得します
        /// (Gets the full path for a cache file with the specified filename)
        /// </summary>
        /// <param name="fileName">キャッシュファイル名 (Cache file name)</param>
        /// <returns>キャッシュファイルの完全パス (Full path to the cache file)</returns>
        public static string GetCacheFilePath(string fileName)
        {
            EnsureCacheDirectoryExists();
            return Path.Combine(CacheDirectory, fileName);
        }

        /// <summary>
        /// キャッシュディレクトリが存在することを確認し、存在しない場合は作成します
        /// (Ensures the cache directory exists, creating it if it doesn't)
        /// </summary>
        public static void EnsureCacheDirectoryExists()
        {
            if (!Directory.Exists(CacheDirectory))
            {
                Directory.CreateDirectory(CacheDirectory);
            }
        }
        
        /// <summary>
        /// すべてのキャッシュファイルを削除します
        /// (Clears all cache files)
        /// </summary>
        /// <returns>削除されたファイルの数 (Number of files deleted)</returns>
        public static int ClearAllCaches()
        {
            int deletedCount = 0;
            
            if (!Directory.Exists(CacheDirectory))
            {
                return 0;
            }
            
            try
            {
                foreach (var file in Directory.GetFiles(CacheDirectory))
                {
                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"キャッシュファイル '{Path.GetFileName(file)}' の削除中にエラーが発生しました: {ex.Message} (Error occurred while deleting cache file)");
                    }
                }
                
                return deletedCount;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"キャッシュのクリア中にエラーが発生しました: {ex.Message} (Error occurred while clearing cache)");
                return deletedCount;
            }
        }
    }
}
