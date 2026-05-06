using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace WpfApp2.Updater
{
    public partial class UpdaterWindow : Window
    {
        private readonly string _installerPath;
        private readonly int _waitPid;
        private readonly string _silentArgs;

        public UpdaterWindow(string installerPath, int waitPid, string silentArgs, string version)
        {
            InitializeComponent();
            _installerPath = installerPath;
            _waitPid = waitPid;
            _silentArgs = silentArgs;
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
                var psi = new ProcessStartInfo(_installerPath)
                {
                    Arguments = _silentArgs,
                    UseShellExecute = true
                };
                var installer = Process.Start(psi);
                await Task.Run(() => installer?.WaitForExit());
            }
            catch (Exception ex)
            {
                StatusText.Text = $"설치 오류: {ex.Message}";
                await Task.Delay(4000);
                Application.Current.Shutdown();
                return;
            }

            // Done
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            StatusText.Text = "설치 완료! 잠시 후 창이 닫힙니다.";
            await Task.Delay(2000);
            Application.Current.Shutdown();
        }
    }
}
