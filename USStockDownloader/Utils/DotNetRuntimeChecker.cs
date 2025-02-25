using Microsoft.Win32;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace USStockDownloader.Utils
{
    public static class DotNetRuntimeChecker
    {
        private const string DOTNET_DOWNLOAD_URL = "https://download.visualstudio.microsoft.com/download/pr/e216cf20-77b5-4c82-bc16-3d817e906a88/4b7e673c1e97e5f2e6f39d6c3d4910b3/dotnet-runtime-9.0.2-win-x64.exe";
        private const string DOTNET_INSTALLER_NAME = "dotnet-runtime-9.0.2-win-x64.exe";
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
                            // バージョン比較
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

        public static async Task<bool> InstallRuntimeAsync()
        {
            try
            {
                Console.WriteLine(".NET 9.0 Runtime のインストールを開始します...");
                Console.WriteLine("Starting .NET 9.0 Runtime installation...");

                // インストーラーをダウンロード
                var installerPath = Path.Combine(Path.GetTempPath(), DOTNET_INSTALLER_NAME);
                using (var client = new HttpClient())
                {
                    Console.WriteLine("インストーラーをダウンロード中...");
                    var response = await client.GetAsync(DOTNET_DOWNLOAD_URL);
                    response.EnsureSuccessStatusCode();
                    using (var fs = new FileStream(installerPath, FileMode.Create))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                Console.WriteLine("インストーラーを実行中...");
                // インストーラーを実行
                var startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/install /quiet /norestart",
                    UseShellExecute = true,
                    Verb = "runas" // 管理者権限で実行
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                        if (process.ExitCode == 0)
                        {
                            Console.WriteLine(".NET Runtime のインストールが完了しました。");
                            Console.WriteLine(".NET Runtime installation completed.");
                            return true;
                        }
                        else
                        {
                            Console.WriteLine($".NET Runtime のインストールに失敗しました。終了コード: {process.ExitCode}");
                            Console.WriteLine($"Failed to install .NET Runtime. Exit code: {process.ExitCode}");
                        }
                    }
                }

                Console.WriteLine(".NET Runtime のインストールに失敗しました。");
                Console.WriteLine("Failed to install .NET Runtime.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($".NET Runtime のインストール中にエラーが発生しました: {ex.Message}");
                Console.WriteLine($"Error during .NET Runtime installation: {ex.Message}");
                return false;
            }
            finally
            {
                // インストーラーファイルを削除
                try
                {
                    var installerPath = Path.Combine(Path.GetTempPath(), DOTNET_INSTALLER_NAME);
                    if (File.Exists(installerPath))
                    {
                        File.Delete(installerPath);
                    }
                }
                catch
                {
                    // 削除に失敗しても続行
                }
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
