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
        private MainWindow? mainWindow;  // 메인 윈도우 인스턴스 저장
        private string? pendingUpdateVersion;  // 업데이트될 버전 저장
        private string? pendingChangelogUrl;  // 변경로그 URL 저장

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

            // 먼저 메인 윈도우 생성 (표시는 하지 않음)
            mainWindow = new MainWindow();

            // MainWindow를 애플리케이션의 메인 윈도우로 설정
            this.MainWindow = mainWindow;

            // 스플래시 창 표시
            splashWindow = new SplashWindow();
            splashWindow.Show();

            // 자동 업데이트 설정
            AutoUpdater.ShowSkipButton = false;  // Skip 버튼 숨김
            AutoUpdater.ShowRemindLaterButton = false;  // Remind Later 버튼 숨김
            AutoUpdater.Mandatory = true;  // 필수 업데이트
            AutoUpdater.UpdateMode = Mode.Normal;  // 정상 모드 - 사용자가 다운로드 버튼 클릭
            AutoUpdater.ReportErrors = false;  // 업데이트 없을 때 메시지 숨김

            // Synchronous = true: 업데이트 확인 완료까지 대기
            AutoUpdater.Synchronous = true;

            // RunUpdateAsAdmin을 false로 설정
            AutoUpdater.RunUpdateAsAdmin = false;

            // LetUserSelectRemindLater를 false로 설정
            AutoUpdater.LetUserSelectRemindLater = false;

            // 디버그 로깅 (개발 중)
            System.Diagnostics.Debug.WriteLine("=== AutoUpdater 설정 시작 ===");

            // 파싱 이벤트만 구독 (커스텀 XML 파싱용)
            AutoUpdater.ParseUpdateInfoEvent += AutoUpdater_ParseUpdateInfoEvent;

            // ApplicationExitEvent 추가 - 업데이트 다운로드 완료 후 앱 종료 전
            AutoUpdater.ApplicationExitEvent += AutoUpdater_ApplicationExitEvent;

            // 시작 시간 기록
            updateCheckStartTime = DateTime.Now;

            // GitHub Releases를 사용한 자동 업데이트
            string githubUser = "Jh98JC";
            string githubRepo = "WpfApp1";
            string updateUrl = $"https://raw.githubusercontent.com/{githubUser}/{githubRepo}/main/updates/update.xml";

            System.Diagnostics.Debug.WriteLine($"업데이트 URL: {updateUrl}");

            // AutoUpdater 시작 - 업데이트 확인 (동기)
            AutoUpdater.Start(updateUrl);

            // 스플래시 1초 후 닫고 MainWindow 표시
            Task.Run(async () =>
            {
                await Task.Delay(1000);
                Dispatcher.Invoke(() =>
                {
                    splashWindow?.Close();
                    splashWindow = null;

                    // AutoUpdater.Start()가 완료되었으므로 MainWindow 표시
                    if (mainWindow != null && !mainWindow.IsVisible)
                    {
                        mainWindow.Show();
                    }
                });
            });
        }

        private void CheckUpdateCompletedFlag()
        {
            try
            {
                string flagPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UpdateFlagFile);
                System.Diagnostics.Debug.WriteLine($"=== CheckUpdateCompletedFlag ===");
                System.Diagnostics.Debug.WriteLine($"플래그 파일 경로: {flagPath}");
                System.Diagnostics.Debug.WriteLine($"플래그 파일 존재: {File.Exists(flagPath)}");

                if (File.Exists(flagPath))
                {
                    // 플래그 파일에서 버전 정보 읽기
                    string versionInfo = File.ReadAllText(flagPath);
                    System.Diagnostics.Debug.WriteLine($"플래그 파일 내용: {versionInfo}");
                    File.Delete(flagPath);
                    System.Diagnostics.Debug.WriteLine("플래그 파일 삭제됨");

                    // GitHub Release URL 구성 (예: "1.0.9.0" -> "v1.0.9")
                    string versionTag = "v" + versionInfo.TrimEnd('0', '.');
                    string changelogUrl = $"https://github.com/Jh98JC/WpfApp1/releases/tag/{versionTag}";
                    System.Diagnostics.Debug.WriteLine($"변경로그 URL 구성: {changelogUrl}");

                    // MainWindow가 표시된 후 변경내용 창 표시
                    Task.Run(async () =>
                    {
                        await Task.Delay(1500); // MainWindow 표시 후 1.5초 대기
                        Dispatcher.Invoke(() =>
                        {
                            System.Diagnostics.Debug.WriteLine($"ChangelogWindow 표시 시도");
                            System.Diagnostics.Debug.WriteLine($"mainWindow null? {mainWindow == null}");
                            System.Diagnostics.Debug.WriteLine($"mainWindow.IsVisible? {mainWindow?.IsVisible}");

                            if (mainWindow != null && mainWindow.IsVisible)
                            {
                                System.Diagnostics.Debug.WriteLine($"ChangelogWindow 생성 - 버전: {versionInfo}, URL: {changelogUrl}");
                                var changelogWindow = new ChangelogWindow(versionInfo, null, changelogUrl)
                                {
                                    Owner = mainWindow,
                                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                                };
                                changelogWindow.ShowDialog();
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("MainWindow가 보이지 않아 ChangelogWindow 표시 안 함");
                            }
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CheckUpdateCompletedFlag 오류: {ex.Message}");
            }
        }

        private void AutoUpdater_ParseUpdateInfoEvent(ParseUpdateInfoEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine("=== ParseUpdateInfoEvent 호출됨 ===");
            System.Diagnostics.Debug.WriteLine($"RemoteData: {args.RemoteData}");

            try
            {
                var xml = System.Xml.Linq.XDocument.Parse(args.RemoteData);
                var item = xml.Element("item");

                var newVersion = item.Element("version")?.Value;
                var changelogUrl = item.Element("changelog")?.Value;

                pendingUpdateVersion = newVersion;  // 새 버전 저장
                pendingChangelogUrl = changelogUrl;  // 변경로그 URL 저장

                args.UpdateInfo = new UpdateInfoEventArgs
                {
                    CurrentVersion = newVersion,
                    DownloadURL = item.Element("url")?.Value,
                    ChangelogURL = changelogUrl,
                    Mandatory = new Mandatory
                    {
                        Value = bool.Parse(item.Element("mandatory")?.Value ?? "false")
                    }
                };

                System.Diagnostics.Debug.WriteLine($"파싱 완료 - 버전: {args.UpdateInfo.CurrentVersion}");
                System.Diagnostics.Debug.WriteLine($"다운로드 URL: {args.UpdateInfo.DownloadURL}");
                System.Diagnostics.Debug.WriteLine($"변경로그 URL: {changelogUrl}");

                // 오류 로깅을 위한 파일 기록
                try
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update_debug.log");
                    File.WriteAllText(logPath, $"ParseUpdateInfoEvent - {DateTime.Now}\n" +
                        $"Version: {args.UpdateInfo.CurrentVersion}\n" +
                        $"DownloadURL: {args.UpdateInfo.DownloadURL}\n" +
                        $"ChangelogURL: {args.UpdateInfo.ChangelogURL}\n" +
                        $"Mandatory: {args.UpdateInfo.Mandatory.Value}\n");
                }
                catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"XML 파싱 오류: {ex.Message}");

                // 오류 로깅
                try
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update_error.log");
                    File.WriteAllText(logPath, $"ParseUpdateInfoEvent Error - {DateTime.Now}\n" +
                        $"Message: {ex.Message}\n" +
                        $"StackTrace: {ex.StackTrace}\n");
                }
                catch { }
            }
        }

        private void AutoUpdater_ApplicationExitEvent()
        {
            System.Diagnostics.Debug.WriteLine("=== ApplicationExitEvent 호출됨 ===");
            System.Diagnostics.Debug.WriteLine("업데이트를 위해 애플리케이션을 종료합니다.");

            // 업데이트 완료 플래그 생성 (새 버전 정보 저장)
            try
            {
                string flagPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UpdateFlagFile);
                // 업데이트될 버전 정보를 저장 (ParseUpdateInfoEvent에서 저장한 값)
                File.WriteAllText(flagPath, pendingUpdateVersion ?? "Unknown");
                System.Diagnostics.Debug.WriteLine($"플래그 파일 생성: {flagPath}, 버전: {pendingUpdateVersion}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"플래그 파일 생성 실패: {ex.Message}");
            }

            // 업데이트를 위해 즉시 앱 종료
            Environment.Exit(0);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            mutex?.ReleaseMutex();
            mutex?.Dispose();
            base.OnExit(e);
        }
    }

}
