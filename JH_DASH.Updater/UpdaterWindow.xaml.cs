using System;
using System.Diagnostics;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Windows;

namespace WpfApp2.Updater
{
    public partial class UpdaterWindow : Window
    {
        private readonly string _installerPath;
        private readonly int _waitPid;
        private readonly string _silentArgs;
        private readonly string _appPath;

        public UpdaterWindow(string installerPath, int waitPid, string silentArgs, string version, string appPath)
        {
            InitializeComponent();
            _installerPath = installerPath;
            _waitPid = waitPid;
            _silentArgs = silentArgs;
            _appPath = appPath;
            Loaded += async (_, _) => await RunUpdateAsync();
        }

        private async Task RunUpdateAsync()
        {
            // Wait for the main app process to exit
            try
            {
                if (_waitPid > 0)
                {
                    var proc = Process.GetProcessById(_waitPid);
                    StatusText.Text = "앱 종료 대기 중...";
                    await Task.Run(() => proc.WaitForExit());
                }
            }
            catch
            {
                // Process already gone — continue
            }

            await Task.Delay(500);

            // Run the installer silently
            StatusText.Text = "설치 중...";
            try
            {
                string ext = System.IO.Path.GetExtension(_installerPath).ToLowerInvariant();
                if (ext == ".zip")
                {
                    string installDir = !string.IsNullOrEmpty(_appPath)
                        ? System.IO.Path.GetDirectoryName(_appPath)!
                        : AppDomain.CurrentDomain.BaseDirectory;
                    await Task.Run(() =>
                    {
                        using var zip = System.IO.Compression.ZipFile.OpenRead(_installerPath);
                        foreach (var entry in zip.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;
                            string destPath = System.IO.Path.Combine(installDir, entry.FullName);
                            string? destDir = System.IO.Path.GetDirectoryName(destPath);
                            if (!string.IsNullOrEmpty(destDir))
                                System.IO.Directory.CreateDirectory(destDir);
                            try { entry.ExtractToFile(destPath, overwrite: true); }
                            catch { /* 실행 중인 파일은 건너뜀 */ }
                        }
                    });
                }
                else
                {
                    var psi = new ProcessStartInfo(_installerPath)
                    {
                        Arguments = _silentArgs,
                        UseShellExecute = true
                    };
                    var installer = Process.Start(psi);
                    await Task.Run(() => installer?.WaitForExit());
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"설치 오류: {ex.Message}";
                await Task.Delay(4000);
                Application.Current.Shutdown();
                return;
            }

            // Done — 앱 재시작 후 종료
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            StatusText.Text = "설치 완료! 앱을 시작합니다...";
            await Task.Delay(1500);

            // appPath가 비어있으면 같은 폴더의 WpfApp2.exe로 폴백
            string appToStart = _appPath;
            if (string.IsNullOrEmpty(appToStart) || !System.IO.File.Exists(appToStart))
                appToStart = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "대시보드.exe");

            if (System.IO.File.Exists(appToStart))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(appToStart) { UseShellExecute = true });
                }
                catch { }
            }

            Application.Current.Shutdown();
        }
    }
}
