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
        private MainWindow? mainWindow;
        private string? pendingUpdateVersion;
        private string? pendingChangelogUrl;
        private bool _updateInProgress = false;

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

            // Azure SQL 설정 로드
            DatabaseService.LoadConfig();

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

            // 업데이트 확인 결과 이벤트 — 커스텀 UpdateWindow 표시
            AutoUpdater.CheckForUpdateEvent += AutoUpdater_CheckForUpdateEvent;

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

            // 스플래시 1초 후 닫고 로그인 창 표시
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000);
                    Dispatcher.Invoke(() =>
                    {
                        if (_updateInProgress) return;
                        splashWindow?.Close();
                        splashWindow = null;
                        ShowLoginAndProceed();
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SplashTask] {ex.Message}");
                    Dispatcher.Invoke(ShowLoginAndProceed);
                }
            });
        }

        private void ShowLoginAndProceed()
        {
            try
            {
                var loginWin = new LoginWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                if (loginWin.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }

                // 로그인 성공 → MainWindow 표시
                mainWindow?.ApplyRoleVisibility();
                if (mainWindow != null && !mainWindow.IsVisible)
                    mainWindow.Show();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"초기화 오류:\n{ex.Message}",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
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
                    // 플래그 파일에서 버전|URL 정보 읽기
                    string flagContent = File.ReadAllText(flagPath);
                    System.Diagnostics.Debug.WriteLine($"플래그 파일 내용: {flagContent}");

                    // 버전과 URL 분리
                    var parts = flagContent.Split('|');
                    string versionInfo = parts.Length > 0 ? parts[0] : "Unknown";
                    string changelogUrl = parts.Length > 1 ? parts[1] : "";

                    File.Delete(flagPath);
                    System.Diagnostics.Debug.WriteLine("플래그 파일 삭제됨");

                    // MainWindow가 표시된 후 변경내용 창 표시
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(1500);
                            Dispatcher.Invoke(() =>
                            {
                                if (mainWindow != null && mainWindow.IsVisible)
                                {
                                    var changelogWindow = new ChangelogWindow(versionInfo, null, changelogUrl)
                                    {
                                        Owner = mainWindow,
                                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                                    };
                                    changelogWindow.ShowDialog();
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ChangelogTask] {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CheckUpdateCompletedFlag 오류: {ex.Message}");
            }
        }

        private void AutoUpdater_CheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            if (args?.IsUpdateAvailable != true) return;

            _updateInProgress = true;

            Dispatcher.Invoke(() =>
            {
                // 스플래시 즉시 닫기 — 업데이트창만 표시
                splashWindow?.Close();
                splashWindow = null;

                string current      = args.InstalledVersion?.ToString() ?? "알 수 없음";
                string latest       = args.CurrentVersion  ?? "알 수 없음";
                string changelog    = pendingChangelogUrl  ?? "";
                string downloadUrl  = args.DownloadURL     ?? "";

                var win = new UpdateWindow(current, latest, changelog, downloadUrl)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                win.ShowDialog(); // 사용자가 '나중에' 누르면 반환, 설치 시 프로세스 종료

                // '나중에'로 취소한 경우 — 로그인 절차 진행
                _updateInProgress = false;
                splashWindow?.Close();
                splashWindow = null;
                ShowLoginAndProceed();
            });
        }

        internal void StartInstall(string tempFilePath)
        {
            try
            {
                string flagPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UpdateFlagFile);
                File.WriteAllText(flagPath, $"{pendingUpdateVersion ?? "Unknown"}|{pendingChangelogUrl ?? ""}");
            }
            catch { }

            try
            {
                string silentArgs = DetectSilentArgs(tempFilePath);
                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;

                string updaterPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WpfApp2.Updater.exe");
                if (File.Exists(updaterPath))
                {
                    // 별도 프로세스로 업데이터 실행 — 메인 앱 종료 후 인스톨러 실행
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(updaterPath)
                    {
                        Arguments = $"--install \"{tempFilePath}\" --pid {pid} --args \"{silentArgs}\" --version \"{pendingUpdateVersion ?? ""}\"",
                        UseShellExecute = false
                    });
                }
                else
                {
                    // 폴백: 배치 파일 방식
                    string batchPath = Path.Combine(Path.GetTempPath(), "wpfapp2_update.bat");
                    string batch =
                        "@echo off\r\n" +
                        "timeout /t 2 /nobreak >nul\r\n" +
                        ":loop\r\n" +
                        $"tasklist /fi \"pid eq {pid}\" /nh 2>nul | find /i \".exe\" >nul\r\n" +
                        "if not errorlevel 1 (timeout /t 1 /nobreak >nul & goto loop)\r\n" +
                        $"start \"\" \"{tempFilePath}\" {silentArgs}\r\n" +
                        "del \"%~f0\"\r\n";
                    File.WriteAllText(batchPath, batch);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
                    {
                        Arguments = $"/c \"{batchPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
            }
            catch { }

            Environment.Exit(0);
        }

        private static string DetectSilentArgs(string filePath)
        {
            try
            {
                byte[] buf = new byte[65536];
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                int read = fs.Read(buf, 0, buf.Length);
                string text = System.Text.Encoding.ASCII.GetString(buf, 0, read);

                if (text.Contains("Inno Setup"))
                    return "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART";

                if (text.Contains("Nullsoft"))
                    return "/S";
            }
            catch { }

            return "/S";
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

            // 업데이트 완료 플래그 생성 (버전과 변경로그 URL 저장)
            try
            {
                string flagPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UpdateFlagFile);
                // 버전|URL 형식으로 저장
                string flagContent = $"{pendingUpdateVersion ?? "Unknown"}|{pendingChangelogUrl ?? ""}";
                File.WriteAllText(flagPath, flagContent);
                System.Diagnostics.Debug.WriteLine($"플래그 파일 생성: {flagPath}");
                System.Diagnostics.Debug.WriteLine($"버전: {pendingUpdateVersion}, URL: {pendingChangelogUrl}");
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
