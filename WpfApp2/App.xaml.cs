using System.Configuration;
using System.Data;
using System.IO;
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
        private const string UpdateFlagFile = "update_completed.flag";

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

            // 업데이트 완료 플래그 확인
            CheckUpdateCompletedFlag();

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

            // 업데이트 적용 이벤트 구독 (업데이트 다운로드 및 실행 직전)
            AutoUpdater.ApplicationExitEvent += AutoUpdater_ApplicationExitEvent;

            // 시작 시간 기록
            updateCheckStartTime = DateTime.Now;

            // GitHub Releases를 사용한 자동 업데이트
            string githubUser = "Jh98JC";
            string githubRepo = "WpfApp1";
            string updateUrl = $"https://raw.githubusercontent.com/{githubUser}/{githubRepo}/main/updates/update.xml";

            AutoUpdater.Start(updateUrl);
        }

        private void CheckUpdateCompletedFlag()
        {
            try
            {
                string flagPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UpdateFlagFile);

                if (File.Exists(flagPath))
                {
                    // 플래그 파일 읽기
                    string[] lines = File.ReadAllLines(flagPath);
                    if (lines.Length >= 3)
                    {
                        string version = lines[0];
                        string changelog = lines[1];
                        string changelogUrl = lines[2];

                        // 플래그 파일 삭제
                        File.Delete(flagPath);

                        // 메인 윈도우가 로드된 후 변경 로그 표시
                        this.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            var changelogWindow = new ChangelogWindow(version, changelog, changelogUrl);
                            changelogWindow.Show();
                        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    }
                }
            }
            catch
            {
                // 오류 무시
            }
        }

        private void AutoUpdater_ApplicationExitEvent()
        {
            // 업데이트 시작 전에 플래그 파일 생성
            try
            {
                string flagPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UpdateFlagFile);

                // 버전 정보 저장 (업데이트 정보는 AutoUpdater에서 가져올 수 없으므로 기본 값 사용)
                File.WriteAllLines(flagPath, new string[]
                {
                    "업데이트 버전",
                    "• 새로운 기능 및 개선 사항이 포함되어 있습니다.\n• 버그 수정 및 성능 개선\n• 중복 실행 방지 기능 추가",
                    "https://github.com/Jh98JC/WpfApp1/releases"
                });
            }
            catch
            {
                // 오류 무시
            }
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
