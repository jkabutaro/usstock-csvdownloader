using System;
using System.IO;

namespace USStockDownloader.Utils
{
    /// <summary>
    /// パス操作のためのユーティリティクラス
    /// </summary>
    public static class PathUtils
    {
        /// <summary>
        /// 絶対パスを相対パスに変換します
        /// </summary>
        /// <param name="absolutePath">変換する絶対パス</param>
        /// <returns>アプリケーションのベースディレクトリからの相対パス、または元のパスがベースディレクトリ外の場合は元のパス</returns>
        public static string ToRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return absolutePath;

            try
            {
                // アプリケーションのベースディレクトリを取得
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                
                // 絶対パスを正規化
                string normalizedAbsolutePath = Path.GetFullPath(absolutePath);
                string normalizedBaseDirectory = Path.GetFullPath(baseDirectory);
                
                // ベースディレクトリで始まるかチェック
                if (normalizedAbsolutePath.StartsWith(normalizedBaseDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    // ベースディレクトリからの相対パスを計算
                    string relativePath = normalizedAbsolutePath.Substring(normalizedBaseDirectory.Length);
                    
                    // 先頭のパス区切り文字を削除
                    if (relativePath.StartsWith(Path.DirectorySeparatorChar.ToString()) || 
                        relativePath.StartsWith(Path.AltDirectorySeparatorChar.ToString()))
                    {
                        relativePath = relativePath.Substring(1);
                    }
                    
                    return relativePath;
                }
                
                // 現在の作業ディレクトリからの相対パスを試みる
                string currentDirectory = Directory.GetCurrentDirectory();
                string normalizedCurrentDirectory = Path.GetFullPath(currentDirectory);
                
                if (normalizedAbsolutePath.StartsWith(normalizedCurrentDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath = normalizedAbsolutePath.Substring(normalizedCurrentDirectory.Length);
                    
                    if (relativePath.StartsWith(Path.DirectorySeparatorChar.ToString()) || 
                        relativePath.StartsWith(Path.AltDirectorySeparatorChar.ToString()))
                    {
                        relativePath = relativePath.Substring(1);
                    }
                    
                    return relativePath;
                }
                
                // ベースディレクトリ外のパスの場合、ファイル名のみを返す
                return Path.GetFileName(absolutePath);
            }
            catch (Exception)
            {
                // エラーが発生した場合は元のパスを返す
                return absolutePath;
            }
        }
    }
}
