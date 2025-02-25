using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace USStockDownloader.Utils
{
    [SupportedOSPlatform("windows")]
    public static class DotNetRuntimeChecker
    {
        private const string DOTNET_DOWNLOAD_PAGE_URL = "https://dotnet.microsoft.com/download/dotnet/9.0/runtime";
        private const string REQUIRED_VERSION = "9.0.2";

        public static bool IsRequiredRuntimeInstalled()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost"))
                {
                    if (key != null)
                    {
                        var version = key.GetValue("Version")?.ToString();
                        if (version != null)
                        {
                            return new Version(version) >= new Version(REQUIRED_VERSION);
                        }
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static Task<bool> InstallRuntimeAsync()
        {
            try
            {
                Console.WriteLine($".NET {REQUIRED_VERSION} Runtime がインストールされていません。");
                Console.WriteLine($".NET {REQUIRED_VERSION} Runtime is not installed.");
                Console.WriteLine("\nダウンロードページを開きます。インストールを完了後、プログラムを再度実行してください。");
                Console.WriteLine("Opening download page. Please install the runtime and run the program again.\n");

                // ブラウザでダウンロードページを開く
                Process.Start(new ProcessStartInfo
                {
                    FileName = DOTNET_DOWNLOAD_PAGE_URL,
                    UseShellExecute = true
                });

                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"エラー: ブラウザでダウンロードページを開けませんでした。");
                Console.WriteLine($"Error: Could not open download page in browser.");
                Console.WriteLine($"URL: {DOTNET_DOWNLOAD_PAGE_URL}");
                Console.WriteLine($"Exception: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public static void RestartApplication(string[] args)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "",
                Arguments = string.Join(" ", args),
                UseShellExecute = true
            };

            Process.Start(startInfo);
            Environment.Exit(0);
        }
    }
}
