using System.Configuration;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AutoUpdaterDotNET;

namespace WpfApp2
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private DateTime updateCheckStartTime;
        private SplashWindow? splashWindow;
        private static Mutex? mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 중복 실행 방지
            const string mutexName = "WpfApp2_SingleInstance_Mutex";
            mutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                System.Windows.MessageBox.Show("이미 프로그램이 실행중입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // 스플래시 창 표시
            splashWindow = new SplashWindow();
            splashWindow.Show();

            // 자동 업데이트 설정
            AutoUpdater.ShowSkipButton = false;
            AutoUpdater.ShowRemindLaterButton = false;
            AutoUpdater.Mandatory = true;
            AutoUpdater.UpdateMode = Mode.Forced;

            // 업데이트 확인 이벤트 구독
            AutoUpdater.CheckForUpdateEvent += AutoUpdater_CheckForUpdateEvent;

            // 시작 시간 기록
            updateCheckStartTime = DateTime.Now;

            // GitHub Releases를 사용한 자동 업데이트
            string githubUser = "Jh98JC";
            string githubRepo = "WpfApp1";
            string updateUrl = $"https://raw.githubusercontent.com/{githubUser}/{githubRepo}/main/updates/update.xml";

            AutoUpdater.Start(updateUrl);
        }

        private async void AutoUpdater_CheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            // 최소 1초 대기 (업데이트 확인 중임을 사용자에게 표시)
            var elapsed = DateTime.Now - updateCheckStartTime;
            var minDisplayTime = TimeSpan.FromSeconds(1);

            if (elapsed < minDisplayTime)
            {
                await Task.Delay(minDisplayTime - elapsed);
            }

            // UI 스레드에서 실행
            Dispatcher.Invoke(() =>
            {
                if (splashWindow != null)
                {
                    if (args != null)
                    {
                        if (args.IsUpdateAvailable)
                        {
                            splashWindow.UpdateStatus($"업데이트 발견! v{args.CurrentVersion}");
                        }
                        else if (args.Error != null)
                        {
                            splashWindow.UpdateStatus($"오류: {args.Error.Message}");
                        }
                        else
                        {
                            splashWindow.UpdateStatus($"최신 버전입니다. (v{args.InstalledVersion})");
                        }
                    }
                    else
                    {
                        splashWindow.UpdateStatus("업데이트 정보를 가져올 수 없습니다.");
                    }
                }
            });

            // 추가로 0.5초 대기 (상태 메시지 표시)
            await Task.Delay(500);

            // 스플래시 창 닫기
            Dispatcher.Invoke(() =>
            {
                splashWindow?.Close();
                splashWindow = null;
            });

            // 이벤트 핸들러 해제
            AutoUpdater.CheckForUpdateEvent -= AutoUpdater_CheckForUpdateEvent;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            mutex?.ReleaseMutex();
            mutex?.Dispose();
            base.OnExit(e);
        }
    }

}
