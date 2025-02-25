using Microsoft.Win32;
using System;
using System.Runtime.Versioning;

namespace USStockDownloader.Utils
{
    [SupportedOSPlatform("windows")]
    public static class WindowsVersionChecker
    {
        public static bool IsWindows10OrLater()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        // Windows 10/11では "CurrentMajorVersionNumber" が存在する
                        var majorVersion = key.GetValue("CurrentMajorVersionNumber");
                        if (majorVersion != null)
                        {
                            return (int)majorVersion >= 10;
                        }

                        // 古いバージョンのWindowsでは "CurrentVersion" を確認
                        var version = key.GetValue("CurrentVersion")?.ToString();
                        if (version != null)
                        {
                            var parts = version.Split('.');
                            if (parts.Length > 0 && int.TryParse(parts[0], out int major))
                            {
                                return major >= 10;
                            }
                        }
                    }
                }
                return false;
            }
            catch (Exception)
            {
                // レジストリアクセスに失敗した場合は安全のためfalseを返す
                return false;
            }
        }

        public static string GetRequiredWindowsVersionMessage()
        {
            return "このアプリケーションはWindows 10以降が必要です。\n" +
                   "Please use Windows 10 or later to run this application.";
        }
    }
}
