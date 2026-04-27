using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Core;

namespace 대진포스_쿼리
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        private WebScraper _scraper;
        private string _selectedSelector = null;
        private bool _isRecording = false;
        private List<RecordedAction> _recordedActions = new List<RecordedAction>();
        private string _recordingFolder = null;
        private int _stepCounter = 0;
        private bool _isAutoCollecting = false;
        private List<List<string>> _collectedData = new List<List<string>>();
        private List<List<string>> _allAccountsData = new List<List<string>>(); // 모든 계정 데이터 누적
        private List<string> _failedStores = new List<string>(); // 실패한 매장 로그
        private DateTime? _collectStartDate = null;
        private DateTime? _collectEndDate = null;

        // 📁 데이터 저장 경로 상수
        private readonly string DATA_OUTPUT_PATH = @"C:\Users\jc941\Desktop\VSCODE\데이터 더미";

        // 🔍 디버그 로그 경로
        private string _debugLogPath;

        public MainWindow()
        {
            InitializeComponent();
            _scraper = new WebScraper();

            // 🔍 디버그 로그 파일 초기화 (주석처리 - 필요시에만 활성화)
            // string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            // _debugLogPath = System.IO.Path.Combine(desktopPath, $"POS_Debug_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            // File.WriteAllText(_debugLogPath, $"=== 대진포스 디버그 로그 시작 ===\n시작 시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n");

            InitializeWebView();
        }

        private async void InitializeWebView()
        {
            try
            {
                // 두 계정의 쿠키/세션이 섞이지 않도록 각 WebView2에 별도 데이터 폴더 지정
                string baseDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "대진포스쿼리");

                var env1 = await CoreWebView2Environment.CreateAsync(null,
                    System.IO.Path.Combine(baseDir, "Profile_junco"));
                var env2 = await CoreWebView2Environment.CreateAsync(null,
                    System.IO.Path.Combine(baseDir, "Profile_junco3"));
                var env3 = await CoreWebView2Environment.CreateAsync(null,
                    System.IO.Path.Combine(baseDir, "Profile_junco4"));

                await WebView.EnsureCoreWebView2Async(env1);
                WebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                await WebView2.EnsureCoreWebView2Async(env2);
                await WebView3.EnsureCoreWebView2Async(env3);

                StatusText.Text = "WebView2 초기화 완료 (병렬 처리 준비됨)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2 초기화 실패: {ex.Message}\n\nMicrosoft Edge WebView2 Runtime을 설치해주세요.");
            }
        }

        private async void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string message = e.TryGetWebMessageAsString();

                // 녹화 모드 처리
                if (message.StartsWith("RECORD_CLICK|||"))
                {
                    if (!_isRecording) return;

                    var jsonData = message.Substring("RECORD_CLICK|||".Length);

                    // 간단한 JSON 파싱
                    var selector = ExtractJsonValue(jsonData, "selector");
                    var text = ExtractJsonValue(jsonData, "text");
                    var tag = ExtractJsonValue(jsonData, "tag");
                    var url = ExtractJsonValue(jsonData, "url");
                    var eventType = ExtractJsonValue(jsonData, "eventType");

                    // 중복 클릭 필터링 (같은 선택자 + 텍스트는 1초 이내 무시)
                    var lastAction = _recordedActions.LastOrDefault();
                    if (lastAction != null && 
                        (DateTime.Now - lastAction.Timestamp).TotalMilliseconds < 1000 &&
                        lastAction.ElementSelector == selector &&
                        lastAction.ElementText == text)
                    {
                        System.Diagnostics.Debug.WriteLine($"중복 클릭 무시: {selector}");
                        return;
                    }

                    _stepCounter++;

                    var action = new RecordedAction
                    {
                        Step = _stepCounter,
                        Timestamp = DateTime.Now,
                        ActionType = $"Click ({eventType})",
                        ElementSelector = selector,
                        ElementText = text,
                        ElementTag = tag,
                        Url = url
                    };

                    // 스크린샷 저장 (비동기로 처리하되 기다리지 않음)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(300); // 클릭 후 화면 변화를 기다림

                            string screenshotPath = System.IO.Path.Combine(_recordingFolder, "screenshots", $"step{_stepCounter:D3}.png");

                            await Dispatcher.InvokeAsync(async () =>
                            {
                                using (var stream = File.Create(screenshotPath))
                                {
                                    await WebView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                                }
                                action.ScreenshotPath = screenshotPath;
                            });
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"스크린샷 저장 실패: {ex.Message}");
                        }
                    });

                    // HTML 스냅샷 저장
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(200);

                            string html = null;
                            await Dispatcher.InvokeAsync(async () =>
                            {
                                var htmlScript = await WebView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
                                html = System.Text.RegularExpressions.Regex.Unescape(htmlScript).Trim('"');
                            });

                            if (!string.IsNullOrEmpty(html))
                            {
                                string htmlPath = System.IO.Path.Combine(_recordingFolder, "html", $"step{_stepCounter:D3}.html");
                                File.WriteAllText(htmlPath, html, Encoding.UTF8);
                                action.HtmlSnapshotPath = htmlPath;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"HTML 저장 실패: {ex.Message}");
                        }
                    });

                    _recordedActions.Add(action);

                    // 상태 업데이트 (UI 스레드에서)
                    await Dispatcher.InvokeAsync(() =>
                    {
                        StatusText.Text = $"🔴 녹화 중... ({_stepCounter}개 액션 기록됨)";

                        // 최근 5개 액션만 표시 (.NET Framework 4.8 호환)
                        var recentActions = _recordedActions.Skip(Math.Max(0, _recordedActions.Count - 5)).Reverse();
                        DetailText.Text = string.Join("\n", recentActions.Select(a => 
                            $"[{a.Step}] {a.ElementTag}: {a.ElementText.Substring(0, Math.Min(30, a.ElementText.Length))}..."));
                    });

                    return;
                }

                // 기존 요소 선택 처리
                var parts = message.Split(new[] { "|||" }, StringSplitOptions.None);

                if (parts.Length >= 2)
                {
                    _selectedSelector = parts[0];
                    string previewData = parts[1];

                    DetailText.Text = $"선택자: {_selectedSelector}\n미리보기: {previewData.Substring(0, Math.Min(100, previewData.Length))}...";

                    StatusText.Text = "요소 선택됨";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"메시지 처리 오류: {ex.Message}");
            }
        }

        private string ExtractJsonValue(string json, string key)
        {
            try
            {
                var searchKey = $"\"{key}\":\"";
                var startIndex = json.IndexOf(searchKey);
                if (startIndex == -1) return "";

                startIndex += searchKey.Length;
                var endIndex = json.IndexOf("\"", startIndex);
                if (endIndex == -1) return "";

                return json.Substring(startIndex, endIndex - startIndex);
            }
            catch
            {
                return "";
            }
        }

        // 🔍 디버그 로깅 헬퍼 메서드
        private void LogDebug(string message)
        {
            try
            {
                // 디버그 로그 경로가 설정되어 있을 때만 파일에 기록
                if (!string.IsNullOrEmpty(_debugLogPath))
                {
                    string logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
                    File.AppendAllText(_debugLogPath, logEntry);
                }
                System.Diagnostics.Debug.WriteLine(message);
            }
            catch { }
        }


        // 🔑 자동 로그인만 수행 (날짜 설정 등을 위해)
        private async void LoginOnlyButton_Click(object sender, RoutedEventArgs e)
        {
            if (WebView.CoreWebView2 == null)
            {
                MessageBox.Show("WebView2가 초기화되지 않았습니다.");
                return;
            }

            // 커스텀 계정 선택 다이얼로그
            var dialog = new AccountSelectionDialog();
            bool? dialogResult = dialog.ShowDialog();

            if (dialogResult != true)
                return;

            string userId = dialog.SelectedAccount;
            string password = "dines9293!!";

            try
            {
                StatusText.Text = $"🔐 {userId} 계정으로 로그인 중...";

                // 로그인 페이지로 이동
                WebView.CoreWebView2.Navigate("https://topint.co.kr/member/login");

                // 페이지 로드 대기
                await Task.Delay(3000);

                // 자동 로그인 스크립트 실행 (실제 버튼 클릭 방식)
                string loginScript = $@"
                    (function() {{
                        try {{
                            // 로그인 폼 찾기
                            var userInput = document.querySelector('input[name=""mb_id""]') || 
                                           document.querySelector('input[id=""mb_id""]') || 
                                           document.querySelectorAll('input[type=""text""]')[0];
                            var passInput = document.querySelector('input[name=""mb_password""]') || 
                                           document.querySelector('input[id=""mb_password""]') || 
                                           document.querySelector('input[type=""password""]');

                            if (!userInput || !passInput) {{
                                return 'fail:inputs_not_found';
                            }}

                            // 값 설정 및 이벤트 발생
                            userInput.value = '{userId}';
                            passInput.value = '{password}';

                            // 필수 이벤트들 트리거
                            ['input', 'change', 'blur'].forEach(function(eventType) {{
                                userInput.dispatchEvent(new Event(eventType, {{ bubbles: true }}));
                                passInput.dispatchEvent(new Event(eventType, {{ bubbles: true }}));
                            }});

                            // 정확한 로그인 버튼 찾기
                            var submitBtn = document.querySelector('button.btn-submit') || 
                                          document.querySelector('button.btn.squ.big.full.btn-submit') ||
                                          document.querySelector('button[type=""button""].btn-submit');

                            if (!submitBtn) {{
                                // 대체 방법: 텍스트로 찾기
                                var buttons = document.querySelectorAll('button');
                                for (var i = 0; i < buttons.length; i++) {{
                                    if (buttons[i].textContent.includes('로그인')) {{
                                        submitBtn = buttons[i];
                                        break;
                                    }}
                                }}
                            }}

                            // 버튼 클릭 (마우스 이벤트 포함)
                            if (submitBtn) {{
                                // 마우스 이벤트 생성
                                var mouseEvents = ['mousedown', 'mouseup', 'click'];
                                mouseEvents.forEach(function(eventType) {{
                                    var event = new MouseEvent(eventType, {{
                                        bubbles: true,
                                        cancelable: true,
                                        view: window
                                    }});
                                    submitBtn.dispatchEvent(event);
                                }});
                                return 'success';
                            }}

                            return 'fail:button_not_found';
                        }} catch(ex) {{
                            return 'error:' + ex.message;
                        }}
                    }})()
                ";

                string loginResult = await WebView.CoreWebView2.ExecuteScriptAsync(loginScript);
                loginResult = loginResult.Trim('"');

                if (loginResult.StartsWith("success"))
                {
                    StatusText.Text = $"⏳ 로그인 처리 중...";

                    // 로그인 완료 대기 (페이지 전환 확인)
                    bool loginCompleted = false;
                    string currentUrl = "";

                    for (int i = 0; i < 30; i++) // 최대 15초 대기
                    {
                        await Task.Delay(500);
                        currentUrl = WebView.CoreWebView2.Source;

                        // 로그인 페이지를 벗어났는지 확인
                        if (!currentUrl.Contains("/member/login"))
                        {
                            loginCompleted = true;
                            break;
                        }
                    }

                    if (loginCompleted)
                    {
                        StatusText.Text = $"✅ {userId} 계정 로그인 완료 (날짜 설정 후 수동으로 진행하세요)";
                    }
                    else
                    {
                        StatusText.Text = "⚠️ 로그인 확인 실패";
                        MessageBox.Show(
                            "로그인 버튼을 클릭했지만 페이지 전환이 확인되지 않았습니다.\n수동으로 로그인 상태를 확인해주세요.",
                            "로그인 확인 필요",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                    }
                }
                else
                {
                    StatusText.Text = "❌ 로그인 실패";
                    MessageBox.Show(
                        $"로그인 실패: {loginResult}",
                        "오류",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "❌ 오류 발생";
                MessageBox.Show($"오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 🚀 날짜 설정 후 자동 수집 (통합 버튼)
        private async void AutoLoginAndCollectButton_Click(object sender, RoutedEventArgs e)
        {
            if (WebView.CoreWebView2 == null)
            {
                MessageBox.Show("WebView2가 초기화되지 않았습니다.");
                return;
            }

            try
            {
                // 1단계: 날짜 입력 (하루만 사용)
                var dateDialog = new Window
                {
                    Title = "날짜 설정",
                    Width = 380,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    SizeToContent = SizeToContent.Height
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
                grid.Margin = new Thickness(20, 15, 20, 15);

                var titleText = new TextBlock
                {
                    Text = "🚀 자동 수집할 날짜를 선택하세요",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 15)
                };
                Grid.SetRow(titleText, 0);
                grid.Children.Add(titleText);

                var datePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
                datePanel.Children.Add(new TextBlock { Text = "날짜:", Width = 60, VerticalAlignment = VerticalAlignment.Center });
                var yesterday = DateTime.Today.AddDays(-1);
                var startDatePicker = new DatePicker { Width = 200, SelectedDate = yesterday, DisplayDateEnd = yesterday };
                datePanel.Children.Add(startDatePicker);
                Grid.SetRow(datePanel, 1);
                grid.Children.Add(datePanel);

                var infoText = new TextBlock
                {
                    Text = "⚡ 확인을 누르면 자동으로:\n   junco + junco3 + junco4 동시 수집 → 통합 파일 생성",
                    FontSize = 11,
                    Foreground = System.Windows.Media.Brushes.DarkBlue,
                    Margin = new Thickness(0, 10, 0, 10)
                };
                Grid.SetRow(infoText, 2);
                grid.Children.Add(infoText);

                var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
                var okButton = new Button { Content = "확인", Width = 90, Height = 35, Margin = new Thickness(0, 0, 10, 0), FontSize = 13, FontWeight = FontWeights.Bold };
                var cancelButton = new Button { Content = "취소", Width = 90, Height = 35, FontSize = 13 };

                bool dialogOk = false;
                okButton.Click += (s, ev) => { dialogOk = true; dateDialog.Close(); };
                cancelButton.Click += (s, ev) => { dateDialog.Close(); };

                buttonPanel.Children.Add(okButton);
                buttonPanel.Children.Add(cancelButton);
                Grid.SetRow(buttonPanel, 3);
                grid.Children.Add(buttonPanel);

                dateDialog.Content = grid;
                dateDialog.ShowDialog();

                if (!dialogOk || !startDatePicker.SelectedDate.HasValue)
                {
                    StatusText.Text = "작업 취소됨";
                    return;
                }

                DateTime currentDate = startDatePicker.SelectedDate.Value;
                string currentDateStr = currentDate.ToString("yyyyMMdd");
                _collectStartDate = currentDate;
                _collectEndDate = currentDate;

                // 세 계정 정보
                var accounts = new[]
                {
                    new { UserId = "junco",  Password = "dines9293!!" },
                    new { UserId = "junco3", Password = "dines9293!!" },
                    new { UserId = "junco4", Password = "dines9293!!" }
                };

                // 진행바 초기화
                ProgressBar.Value = 0;
                ProgressText.Text = "0%";
                DetailText.Text = $"{currentDate:yyyy-MM-dd} | 3개 계정 병렬 수집 시작...";

                StatusText.Text = $"⚡ {currentDate:yyyy-MM-dd} - 3개 계정 병렬 수집 시작...";

                // 해당 날짜의 모든 계정 데이터 초기화
                _allAccountsData.Clear();
                _failedStores.Clear();
                _isAutoCollecting = true;

                // 세 계정 전용 로컬 리스트
                var list1 = new List<List<string>>();
                var list2 = new List<List<string>>();
                var list3 = new List<List<string>>();

                // 세 계정을 병렬 로그인 → 수집
                // acctIndex: 0=junco(파란), 1=junco3(초록), 2=junco4(주황)
                async Task CollectAccount(Microsoft.Web.WebView2.Wpf.WebView2 wv, string userId, string password, List<List<string>> outList, int acctIndex)
                {
                    var acctStatus   = acctIndex == 0 ? Account1StatusText   : acctIndex == 1 ? Account2StatusText   : Account3StatusText;
                    var acctDetail   = acctIndex == 0 ? Account1DetailText   : acctIndex == 1 ? Account2DetailText   : Account3DetailText;
                    var acctProgress = acctIndex == 0 ? Account1ProgressBar  : acctIndex == 1 ? Account2ProgressBar  : Account3ProgressBar;
                    var acctProgTxt  = acctIndex == 0 ? Account1ProgressText : acctIndex == 1 ? Account2ProgressText : Account3ProgressText;

                    acctStatus.Text    = "시작 중...";
                    acctDetail.Text    = "";
                    acctProgress.Value = 0;
                    acctProgTxt.Text   = "0%";

                    try
                    {
                        await LoginWithAccount(wv, userId, password,
                            onStatus: s => acctStatus.Text = s);
                        await AutoNavigateSetDateAndCollect(wv, currentDateStr, currentDateStr, userId, false, currentDate, outList,
                            onStatus:       s => acctStatus.Text = s,
                            onDetail:       d => acctDetail.Text = d,
                            onProgress:     p => { acctProgress.Value = p; acctProgTxt.Text = $"{p:F1}%"; },
                            onProgressText: t => acctProgTxt.Text = t);
                        acctStatus.Text = $"✅ 완료 ({outList.Count}개 매장)";
                    }
                    catch (Exception ex)
                    {
                        acctStatus.Text = $"❌ 오류: {ex.Message.Substring(0, Math.Min(40, ex.Message.Length))}";
                        StatusText.Text = $"❌ {userId} 오류";
                        System.Diagnostics.Debug.WriteLine($"{userId} 오류: {ex.Message}");
                    }
                }

                await Task.WhenAll(
                    CollectAccount(WebView,  accounts[0].UserId, accounts[0].Password, list1, 0),
                    CollectAccount(WebView2, accounts[1].UserId, accounts[1].Password, list2, 1),
                    CollectAccount(WebView3, accounts[2].UserId, accounts[2].Password, list3, 2)
                );

                // 세 결과를 _allAccountsData에 병합
                _allAccountsData.AddRange(list1);
                _allAccountsData.AddRange(list2);
                _allAccountsData.AddRange(list3);

                DetailText.Text = $"{currentDate:yyyy-MM-dd} | junco:{list1.Count} + junco3:{list2.Count} + junco4:{list3.Count}개 매장 수집 완료";

                // 파일 저장
                SaveAllAccountsDataWithDate(currentDateStr, currentDate);

                ProgressBar.Value = 100;
                ProgressText.Text = "100%";

                // 진행바 완료
                ProgressBar.Value = 100;
                ProgressText.Text = "100%";
                DetailText.Text = "모든 작업 완료!";

                StatusText.Text = "✅ 모든 계정 처리 완료!";
                ResetAutoCollectUI();
            }
            catch (Exception ex)
            {
                StatusText.Text = "❌ 오류 발생";
                ResetAutoCollectUI();
                MessageBox.Show($"오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

                // 로그인 헬퍼 함수
                private async Task LoginWithAccount(Microsoft.Web.WebView2.Wpf.WebView2 webView, string userId, string password, Action<string> onStatus = null)
                {
                    void SetS(string t) { StatusText.Text = t; onStatus?.Invoke(t); }
                    SetS($"🔐 {userId} 계정으로 로그인 중...");
                    webView.CoreWebView2.Navigate("https://topint.co.kr/member/login");
            await Task.Delay(3000);

            string loginScript = $@"
                (function() {{
                    try {{
                        var userInput = document.querySelector('input[name=""mb_id""]') || 
                                       document.querySelector('input[id=""mb_id""]') || 
                                       document.querySelectorAll('input[type=""text""]')[0];
                        var passInput = document.querySelector('input[name=""mb_password""]') || 
                                       document.querySelector('input[id=""mb_password""]') || 
                                       document.querySelector('input[type=""password""]');

                        if (!userInput || !passInput) {{
                            return 'fail:inputs_not_found';
                        }}

                        userInput.value = '{userId}';
                        passInput.value = '{password}';

                        ['input', 'change', 'blur'].forEach(function(eventType) {{
                            userInput.dispatchEvent(new Event(eventType, {{ bubbles: true }}));
                            passInput.dispatchEvent(new Event(eventType, {{ bubbles: true }}));
                        }});

                        var submitBtn = document.querySelector('button.btn-submit') || 
                                      document.querySelector('button.btn.squ.big.full.btn-submit') ||
                                      document.querySelector('button[type=""button""].btn-submit');

                        if (!submitBtn) {{
                            var buttons = document.querySelectorAll('button');
                            for (var i = 0; i < buttons.length; i++) {{
                                if (buttons[i].textContent.includes('로그인')) {{
                                    submitBtn = buttons[i];
                                    break;
                                }}
                            }}
                        }}

                        if (submitBtn) {{
                            var mouseEvents = ['mousedown', 'mouseup', 'click'];
                            mouseEvents.forEach(function(eventType) {{
                                var event = new MouseEvent(eventType, {{
                                    bubbles: true,
                                    cancelable: true,
                                    view: window
                                }});
                                submitBtn.dispatchEvent(event);
                            }});
                            return 'success';
                        }}

                        return 'fail:button_not_found';
                    }} catch(ex) {{
                        return 'error:' + ex.message;
                    }}
                }})()
            ";

            string loginResult = await webView.CoreWebView2.ExecuteScriptAsync(loginScript);
            loginResult = loginResult.Trim('"');

            if (!loginResult.StartsWith("success"))
            {
                SetS("❌ 로그인 실패");
                throw new Exception($"로그인 실패: {loginResult}");
            }

            // 로그인 완료 대기
            SetS("⏳ 로그인 처리 중...");
            bool loginCompleted = false;
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(500);
                string currentUrl = webView.CoreWebView2.Source;
                if (!currentUrl.Contains("/member/login"))
                {
                    loginCompleted = true;
                    break;
                }
            }

            if (!loginCompleted)
            {
                SetS("⚠️ 로그인 확인 실패");
                throw new Exception("로그인 확인 실패");
            }

            SetS($"✅ {userId} 로그인 완료");
            await Task.Delay(1000);
        }

        // 로그아웃 헬퍼 함수
        private async Task LogoutAccount()
        {
            try
            {
                string logoutScript = @"
                    (function() {
                        var logoutLink = document.querySelector('a[href*=""logout""]') ||
                                       document.querySelector('a[href*=""Logout""]');
                        if (logoutLink) {
                            logoutLink.click();
                            return 'success';
                        }
                        return 'not_found';
                    })()
                ";

                await WebView.CoreWebView2.ExecuteScriptAsync(logoutScript);
                await Task.Delay(2000);
            }
            catch
            {
                // 로그아웃 실패 시 그냥 로그인 페이지로 이동
                WebView.CoreWebView2.Navigate("https://topint.co.kr/member/login");
                await Task.Delay(2000);
            }
        }

        // 메뉴 이동 + 날짜 설정 + 자동 수집
        private async Task AutoNavigateSetDateAndCollect(Microsoft.Web.WebView2.Wpf.WebView2 webView, string startDateStr, string endDateStr, string accountId = null, bool showMessage = true, DateTime? targetDate = null, List<List<string>> localList = null, Action<string> onStatus = null, Action<string> onDetail = null, Action<double> onProgress = null, Action<string> onProgressText = null)
        {
            void SetS(string t) { StatusText.Text = t; onStatus?.Invoke(t); }
            try
            {
                SetS("📋 메뉴별 매출현황으로 이동 중...");
                await Task.Delay(2000);

                // 메뉴 클릭
                string clickMenuScript = @"
                    (function() {
                        var links = document.querySelectorAll('a');
                        for (var i = 0; i < links.length; i++) {
                            if (links[i].textContent.includes('메뉴별 매출현황')) {
                                links[i].click();
                                return 'success';
                            }
                        }
                        return 'not_found';
                    })()
                ";

                string menuResult = await webView.CoreWebView2.ExecuteScriptAsync(clickMenuScript);
                menuResult = menuResult.Trim('"');

                if (menuResult != "success")
                {
                    SetS("❌ 메뉴를 찾을 수 없습니다");
                    MessageBox.Show("'메뉴별 매출현황' 메뉴를 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                await Task.Delay(3000);

                // 날짜 설정
                SetS($"📆 날짜 설정 중... ({startDateStr} ~ {endDateStr})");

                string setDateScript = $@"
                    (function() {{
                        var iframes = document.querySelectorAll('iframe');

                        for (var i = 0; i < iframes.length; i++) {{
                            try {{
                                var iframe = iframes[i];
                                var rect = iframe.getBoundingClientRect();

                                if (rect.width > 0 && rect.height > 0) {{
                                    var iframeDoc = iframe.contentDocument || iframe.contentWindow.document;
                                    if (iframeDoc && iframeDoc.body) {{

                                        var startInput = iframeDoc.getElementById('opendate_s') || 
                                                       iframeDoc.querySelector('input[name=""opendate_s""]');
                                        if (startInput) {{
                                            startInput.value = '{startDateStr}';
                                            ['input', 'change', 'blur'].forEach(function(eventType) {{
                                                var event = new Event(eventType, {{ bubbles: true, cancelable: true }});
                                                startInput.dispatchEvent(event);
                                            }});
                                        }}

                                        var endInput = iframeDoc.getElementById('opendate_e') || 
                                                     iframeDoc.querySelector('input[name=""opendate_e""]');
                                        if (endInput) {{
                                            endInput.value = '{endDateStr}';
                                            ['input', 'change', 'blur'].forEach(function(eventType) {{
                                                var event = new Event(eventType, {{ bubbles: true, cancelable: true }});
                                                endInput.dispatchEvent(event);
                                            }});
                                        }}

                                        if (startInput && endInput) {{
                                            return JSON.stringify({{
                                                success: true,
                                                startDate: startInput.value,
                                                endDate: endInput.value
                                            }});
                                        }}
                                    }}
                                }}
                            }} catch (e) {{
                                console.log('iframe 접근 오류:', e);
                            }}
                        }}

                        return JSON.stringify({{ success: false }});
                    }})()
                ";

                string dateResult = await webView.CoreWebView2.ExecuteScriptAsync(setDateScript);
                dateResult = System.Text.RegularExpressions.Regex.Unescape(dateResult);
                dateResult = dateResult.Trim('"');

                if (!dateResult.Contains("\"success\":true"))
                {
                    SetS("❌ 날짜 설정 실패");
                    MessageBox.Show("날짜 입력 필드를 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                await Task.Delay(500);

                // 검색 버튼 클릭
                SetS("🔍 검색 중...");

                string clickSearchScript = @"
                    (function() {
                        var iframes = document.querySelectorAll('iframe');

                        for (var i = 0; i < iframes.length; i++) {
                            try {
                                var iframe = iframes[i];
                                var rect = iframe.getBoundingClientRect();

                                if (rect.width > 0 && rect.height > 0) {
                                    var iframeDoc = iframe.contentDocument || iframe.contentWindow.document;
                                    if (iframeDoc && iframeDoc.body) {
                                        var searchBtn = iframeDoc.getElementById('btnSearch') ||
                                                      iframeDoc.querySelector('button#btnSearch');

                                        if (searchBtn) {
                                            searchBtn.click();
                                            return 'clicked';
                                        }
                                    }
                                }
                            } catch (e) {}
                        }

                        return 'not_found';
                    })()
                ";

                string searchResult = await webView.CoreWebView2.ExecuteScriptAsync(clickSearchScript);
                searchResult = searchResult.Trim('"');

                if (searchResult != "clicked")
                {
                    SetS("❌ 검색 버튼을 찾을 수 없습니다");
                    MessageBox.Show("검색 버튼을 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 검색 후 즉시 데이터 로드 추적 시작 (고정 대기 없이 바로 추적)
                SetS("⏳ 데이터 로드 추적 시작...");
                LogDebug("\n========== 초기 데이터 로드 추적 시작 ==========");
                await Task.Delay(300); // 검색 요청이 전송되도록 최소 대기 (500ms → 300ms)

                bool dataLoaded = false;
                int maxWaitAttempts = 120; // 60초 대기 (120 × 500ms) - 충분한 시간 확보
                int stableCount = 0; // 데이터가 안정적으로 유지된 횟수
                int requiredStableCount = 2; // 연속 2회 확인으로 단축 (3회 → 2회)

                for (int attempt = 0; attempt < maxWaitAttempts; attempt++)
                {
                    await Task.Delay(500);

                    string checkDataScript = @"
                        (function() {
                            var iframes = document.querySelectorAll('iframe');
                            for (var i = 0; i < iframes.length; i++) {
                                try {
                                    var iframe = iframes[i];
                                    var rect = iframe.getBoundingClientRect();
                                    if (rect.width > 0 && rect.height > 0) {
                                        var iframeDoc = iframe.contentDocument || iframe.contentWindow.document;
                                        if (iframeDoc && iframeDoc.body) {
                                            var grid01 = iframeDoc.getElementById('jqGrid01');
                                            var grid02 = iframeDoc.getElementById('jqGrid02');

                                            // 상단 테이블 (매장 목록) 확인
                                            var topRows = 0;
                                            var topHasData = false;
                                            if (grid01) {
                                                var topRowElements = grid01.querySelectorAll('tbody tr[role=""row""]');
                                                topRows = topRowElements.length;

                                                // 실제 데이터가 있는지 확인 (첫 번째 셀에 텍스트가 있는지)
                                                if (topRows > 0) {
                                                    for (var j = 0; j < topRowElements.length; j++) {
                                                        var firstCell = topRowElements[j].querySelector('td');
                                                        if (firstCell && firstCell.textContent.trim().length > 0) {
                                                            topHasData = true;
                                                            break;
                                                        }
                                                    }
                                                }
                                            }

                                            // 하단 테이블 (메뉴 데이터) 확인
                                            var bottomRows = 0;
                                            var bottomHasData = false;
                                            if (grid02) {
                                                var bottomRowElements = grid02.querySelectorAll('tbody tr[role=""row""]');
                                                bottomRows = bottomRowElements.length;

                                                // 실제 데이터가 있는지 확인
                                                if (bottomRows > 0) {
                                                    for (var k = 0; k < bottomRowElements.length; k++) {
                                                        var cells = bottomRowElements[k].querySelectorAll('td');
                                                        var hasContent = false;
                                                        for (var m = 0; m < cells.length; m++) {
                                                            if (cells[m].textContent.trim().length > 0) {
                                                                hasContent = true;
                                                                break;
                                                            }
                                                        }
                                                        if (hasContent) {
                                                            bottomHasData = true;
                                                            break;
                                                        }
                                                    }
                                                }
                                            }

                                            var topLoaded = topRows >= 1 && topHasData;
                                            var bottomLoaded = bottomRows >= 1 && bottomHasData;

                                            return JSON.stringify({
                                                topRows: topRows,
                                                bottomRows: bottomRows,
                                                topHasData: topHasData,
                                                bottomHasData: bottomHasData,
                                                topLoaded: topLoaded,
                                                bottomLoaded: bottomLoaded,
                                                allLoaded: topLoaded && bottomLoaded
                                            });
                                        }
                                    }
                                } catch (e) {
                                    console.log('데이터 체크 오류:', e);
                                }
                            }
                            return JSON.stringify({topRows: 0, bottomRows: 0, topHasData: false, bottomHasData: false, topLoaded: false, bottomLoaded: false, allLoaded: false});
                        })()
                    ";

                    string dataCheckResult = await webView.CoreWebView2.ExecuteScriptAsync(checkDataScript);
                    dataCheckResult = System.Text.RegularExpressions.Regex.Unescape(dataCheckResult);
                    dataCheckResult = dataCheckResult.Trim('"');

                    LogDebug($"시도 #{attempt + 1}: {dataCheckResult}");

                    // 디버깅: 현재 상태 파싱
                    bool hasTopData = dataCheckResult.Contains("\"topHasData\":true");
                    bool hasBottomData = dataCheckResult.Contains("\"bottomHasData\":true");
                    bool allLoaded = dataCheckResult.Contains("\"allLoaded\":true");

                    if (allLoaded)
                    {
                        stableCount++;
                        LogDebug($"✅ 데이터 감지! 안정화 카운트: {stableCount}/{requiredStableCount}");
                        if (stableCount >= requiredStableCount)
                        {
                            dataLoaded = true;
                            SetS($"✅ 데이터 로드 완료 (안정화 확인 {stableCount}회)");
                            LogDebug($"========== 초기 데이터 로드 완료 ==========\n");
                            break;
                        }
                        else
                        {
                            SetS($"⏳ 데이터 안정화 확인 중... ({stableCount}/{requiredStableCount})");
                        }
                    }
                    else
                    {
                        // 데이터가 없어지면 카운터 리셋
                        if (stableCount > 0)
                        {
                            LogDebug($"⚠️ 데이터 불안정 - 카운터 리셋");
                        }
                        stableCount = 0;
                    }

                    // 진행 상황을 더 자주 표시 (디버깅 정보 포함)
                    if ((attempt + 1) % 2 == 0 && stableCount == 0)
                    {
                        string debugInfo = $"상단:{(hasTopData ? "✓" : "✗")} 하단:{(hasBottomData ? "✓" : "✗")}";
                        SetS($"⏳ 로딩 중... ({attempt + 1}/{maxWaitAttempts}) {debugInfo}");
                    }
                }

                if (!dataLoaded)
                {
                    SetS("⚠️ 데이터 로드 시간 초과");
                    MessageBox.Show("데이터 로드 시간이 초과되었습니다.\n수동으로 확인해주세요.", "경고", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 데이터가 안정적으로 로드됨 - 바로 자동 수집 시작
                SetS("✅ 데이터 준비 완료");
                await Task.Delay(500);

                // 자동 수집 시작
                SetS("🤖 자동 수집 시작...");
                await StartAutoCollect(webView, 0, false, accountId, showMessage, targetDate, localList, onStatus, onDetail, onProgress, onProgressText);
            }
            catch (Exception ex)
            {
                SetS("❌ 오류 발생");
                MessageBox.Show($"오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task AutoNavigateAndCollect()
        {
            try
            {
                StatusText.Text = "📋 메뉴별 매출현황으로 이동 중...";
                await Task.Delay(2000);

                // "메뉴별 매출현황" 링크 클릭
                string clickMenuScript = @"
                    (function() {
                        var links = document.querySelectorAll('a');
                        for (var i = 0; i < links.length; i++) {
                            if (links[i].textContent.includes('메뉴별 매출현황')) {
                                links[i].click();
                                return 'success';
                            }
                        }
                        return 'not_found';
                    })()
                ";

                string menuResult = await WebView.CoreWebView2.ExecuteScriptAsync(clickMenuScript);

                if (!menuResult.Contains("success"))
                {
                    MessageBox.Show("메뉴별 매출현황 링크를 찾을 수 없습니다.");
                    return;
                }

                StatusText.Text = "⏳ 페이지 로딩 중...";
                await Task.Delay(3000);

                // iframe 내부의 검색 버튼 클릭
                StatusText.Text = "🔍 검색 버튼 클릭 중...";
                string clickSearchScript = @"
                    (function() {
                        var iframes = document.querySelectorAll('iframe');
                        for (var i = 0; i < iframes.length; i++) {
                            try {
                                var iframe = iframes[i];
                                var rect = iframe.getBoundingClientRect();
                                if (rect.width > 0 && rect.height > 0) {
                                    var iframeDoc = iframe.contentDocument || iframe.contentWindow.document;
                                    if (iframeDoc && iframeDoc.body) {
                                        // 검색 버튼 찾기 (오른쪽 위)
                                        var searchBtn = iframeDoc.querySelector('button[onclick*=""fn_Search""]') ||
                                                       iframeDoc.querySelector('input[type=""button""][value*=""검색""]') ||
                                                       iframeDoc.querySelector('button[value*=""검색""]') ||
                                                       iframeDoc.querySelector('img[alt*=""검색""]') ||
                                                       iframeDoc.querySelector('a[onclick*=""fn_Search""]');

                                        if (!searchBtn) {
                                            // 모든 버튼/이미지 중 검색 관련 찾기
                                            var allButtons = iframeDoc.querySelectorAll('button, input[type=""button""], img, a');
                                            for (var j = 0; j < allButtons.length; j++) {
                                                var elem = allButtons[j];
                                                var text = elem.textContent || elem.value || elem.alt || elem.title || '';
                                                var onclick = elem.getAttribute('onclick') || '';
                                                if (text.includes('검색') || onclick.includes('Search') || onclick.includes('search')) {
                                                    searchBtn = elem;
                                                    break;
                                                }
                                            }
                                        }

                                        if (searchBtn) {
                                            searchBtn.click();
                                            return 'search_clicked';
                                        }
                                    }
                                }
                            } catch (e) {
                                console.log('iframe 접근 오류:', e);
                            }
                        }
                        return 'search_button_not_found';
                    })()
                ";

                string searchResult = await WebView.CoreWebView2.ExecuteScriptAsync(clickSearchScript);

                if (!searchResult.Contains("search_clicked"))
                {
                    MessageBox.Show("검색 버튼을 찾을 수 없습니다.\n수동으로 검색 버튼을 눌러주세요.");
                    return;
                }

                StatusText.Text = "⏳ 데이터 로딩 대기 중... (위/아래 표 로드 확인)";

                // 데이터 완전 로드 대기 (위 표와 아래 표 모두 데이터가 있을 때까지)
                bool dataLoaded = false;
                int maxWaitAttempts = 60; // 최대 30초 대기 (0.5초 × 60)

                for (int attempt = 0; attempt < maxWaitAttempts; attempt++)
                {
                    await Task.Delay(500);

                    string checkDataScript = @"
                        (function() {
                            var iframes = document.querySelectorAll('iframe');
                            for (var i = 0; i < iframes.length; i++) {
                                try {
                                    var iframe = iframes[i];
                                    var rect = iframe.getBoundingClientRect();
                                    if (rect.width > 0 && rect.height > 0) {
                                        var iframeDoc = iframe.contentDocument || iframe.contentWindow.document;
                                        if (iframeDoc && iframeDoc.body) {
                                            // 위 표 (jqGrid01) - 매장 목록
                                            var topGrid = iframeDoc.getElementById('jqGrid01');
                                            var topRows = topGrid ? topGrid.querySelectorAll('tbody tr[role=""row""]') : [];

                                            // 아래 표 (jqGrid02) - 메뉴 데이터
                                            var bottomGrid = iframeDoc.getElementById('jqGrid02');
                                            var bottomRows = bottomGrid ? bottomGrid.querySelectorAll('tbody tr[role=""row""]') : [];

                                            // 위 표는 최소 2개 이상, 아래 표는 최소 1개 이상의 데이터 행이 있어야 함
                                            var topLoaded = topRows.length >= 2;
                                            var bottomLoaded = bottomRows.length >= 1;

                                            return JSON.stringify({
                                                topRows: topRows.length,
                                                bottomRows: bottomRows.length,
                                                topLoaded: topLoaded,
                                                bottomLoaded: bottomLoaded,
                                                allLoaded: topLoaded && bottomLoaded
                                            });
                                        }
                                    }
                                } catch (e) {
                                    console.log('데이터 확인 오류:', e);
                                }
                            }
                            return JSON.stringify({topRows: 0, bottomRows: 0, topLoaded: false, bottomLoaded: false, allLoaded: false});
                        })()
                    ";

                    string dataCheckResult = await WebView.CoreWebView2.ExecuteScriptAsync(checkDataScript);
                    dataCheckResult = System.Text.RegularExpressions.Regex.Unescape(dataCheckResult);
                    dataCheckResult = dataCheckResult.Trim('"');

                    // allLoaded 확인
                    if (dataCheckResult.Contains("\"allLoaded\":true"))
                    {
                        dataLoaded = true;

                        // 행 개수 추출하여 표시
                        var topRowsMatch = System.Text.RegularExpressions.Regex.Match(dataCheckResult, @"""topRows"":(\d+)");
                        var bottomRowsMatch = System.Text.RegularExpressions.Regex.Match(dataCheckResult, @"""bottomRows"":(\d+)");

                        string topCount = topRowsMatch.Success ? topRowsMatch.Groups[1].Value : "?";
                        string bottomCount = bottomRowsMatch.Success ? bottomRowsMatch.Groups[1].Value : "?";

                        StatusText.Text = $"✅ 데이터 로드 완료! (위:{topCount}개, 아래:{bottomCount}개)";
                        break;
                    }

                    // 진행 상황 표시
                    StatusText.Text = $"⏳ 데이터 로딩 중... ({attempt + 1}/{maxWaitAttempts}) - {dataCheckResult.Substring(0, Math.Min(50, dataCheckResult.Length))}";
                }

                if (!dataLoaded)
                {
                    MessageBox.Show(
                        "데이터 로딩이 완료되지 않았습니다.\n\n더 긴 기간을 조회하는 경우 시간이 더 걸릴 수 있습니다.\n수동으로 데이터가 로드되었는지 확인한 후 자동 수집을 시작해주세요.",
                        "데이터 로딩 대기 시간 초과",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                // 자동 수집 시작 (5개만)
                StatusText.Text = "🤖 자동 수집 시작 (5개)...";
                MessageBox.Show(
                    "로그인 및 페이지 이동 완료!\n\n이제 5개 매장 데이터를 자동 수집합니다.",
                    "자동화 진행",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                // 자동 수집 버튼 시뮬레이션 (5개 제한)
                await StartAutoCollect(WebView, 5, false);
            }
            catch (Exception ex)
            {
                StatusText.Text = "❌ 자동화 실패";
                MessageBox.Show($"자동화 중 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StartAutoCollect(Microsoft.Web.WebView2.Wpf.WebView2 webView, int maxCount, bool isTestMode, string accountId = null, bool showMessage = true, DateTime? targetDate = null, List<List<string>> localList = null, Action<string> onStatus = null, Action<string> onDetail = null, Action<double> onProgress = null, Action<string> onProgressText = null)
        {
            // localList가 제공되면 그것을 사용 (병렬 실행 시), 아니면 공유 _collectedData 사용
            var activeData = localList ?? _collectedData;
            void SetS(string t) { StatusText.Text = t; onStatus?.Invoke(t); }
            void SetD(string t) { DetailText.Text = t; onDetail?.Invoke(t); }
            void SetP(double v) { ProgressBar.Value = v; onProgress?.Invoke(v); }
            void SetPT(string t) { ProgressText.Text = t; onProgressText?.Invoke(t); }

            try
            {
                LogDebug($"\n========== StartAutoCollect 시작 (maxCount={maxCount}, isTestMode={isTestMode}, targetDate={targetDate}) ==========");

                if (webView.CoreWebView2 == null)
                {
                    LogDebug("❌ WebView2가 초기화되지 않음");
                    MessageBox.Show("WebView2가 초기화되지 않았습니다.");
                    return;
                }

                _isAutoCollecting = true;
                activeData.Clear();
                LogDebug($"✅ 데이터 목록 초기화 완료");

                // UI 업데이트
                StopAutoCollectButton.IsEnabled = true;
                SetS(isTestMode ? $"🧪 테스트 수집 중... (최대 {maxCount}개)" : "🤖 자동 수집 중...");

                // 진행바 초기화
                SetP(0);
                SetPT("0%");

                // iframe 내부의 "위 표" (매장 목록) 행들 찾기
                string findRowsScript = @"
                    (function() {
                        var result = {totalIframes: 0, rows: [], storeTableId: '', detailTableId: ''};
                        var iframes = document.querySelectorAll('iframe');
                        result.totalIframes = iframes.length;

                        if (iframes.length === 0) {
                            return JSON.stringify(result);
                        }

                        // 첫 번째 보이는 iframe 찾기
                        for (var i = 0; i < iframes.length; i++) {
                            try {
                                var iframe = iframes[i];
                                var rect = iframe.getBoundingClientRect();

                                if (rect.width > 0 && rect.height > 0) {
                                    var iframeDoc = iframe.contentDocument || iframe.contentWindow.document;
                                    if (iframeDoc && iframeDoc.body) {
                                        // 모든 테이블 찾기
                                        var allTables = iframeDoc.querySelectorAll('table[id*=jqGrid]');

                                        // 첫 번째 테이블 = 위 표 (매장 목록)
                                        // 두 번째 테이블 = 아래 표 (판매 내역)
                                        if (allTables.length >= 2) {
                                            result.storeTableId = allTables[0].id || 'table_0';
                                            result.detailTableId = allTables[1].id || 'table_1';

                                            // 위 표의 행들 찾기
                                            var rows = allTables[0].querySelectorAll('tbody tr[role=row]');

                                            for (var j = 0; j < rows.length; j++) {
                                                var row = rows[j];
                                                var rowId = row.id || ('row_' + j);
                                                var firstCell = row.querySelector('td');
                                                var text = firstCell ? firstCell.textContent.trim() : '';

                                                result.rows.push({
                                                    index: j,
                                                    id: rowId,
                                                    text: text,
                                                    iframeIndex: i
                                                });
                                            }
                                        } else if (allTables.length === 1) {
                                            // 테이블이 1개인 경우 (다른 구조일 수 있음)
                                            var rows = allTables[0].querySelectorAll('tbody tr[role=row]');
                                            result.storeTableId = allTables[0].id || 'table_0';

                                            for (var j = 0; j < rows.length; j++) {
                                                var row = rows[j];
                                                var rowId = row.id || ('row_' + j);
                                                var firstCell = row.querySelector('td');
                                                var text = firstCell ? firstCell.textContent.trim() : '';

                                                result.rows.push({
                                                    index: j,
                                                    id: rowId,
                                                    text: text,
                                                    iframeIndex: i
                                                });
                                            }
                                        }

                                        break; // 첫 번째 iframe만 처리
                                    }
                                }
                            } catch (e) {
                                console.log('iframe 접근 오류:', e);
                            }
                        }

                        return JSON.stringify(result);
                    })();
                ";

                string findResult = await webView.CoreWebView2.ExecuteScriptAsync(findRowsScript);
                findResult = System.Text.RegularExpressions.Regex.Unescape(findResult);
                findResult = findResult.Trim('"');

                LogDebug($"매장 목록 검색 결과: {findResult.Substring(0, Math.Min(200, findResult.Length))}...");

                // 간단한 JSON 파싱
                int totalIframes = 0;
                var rowsText = "";

                if (findResult.Contains("\"totalIframes\":"))
                {
                    var totalMatch = System.Text.RegularExpressions.Regex.Match(findResult, @"""totalIframes""\s*:\s*(\d+)");
                    if (totalMatch.Success)
                    {
                        int.TryParse(totalMatch.Groups[1].Value, out totalIframes);
                    }

                    var rowsMatch = System.Text.RegularExpressions.Regex.Match(findResult, @"""rows""\s*:\s*\[(.*)\]");
                    if (rowsMatch.Success)
                    {
                        rowsText = rowsMatch.Groups[1].Value;
                    }
                }

                LogDebug($"totalIframes={totalIframes}, rowsText.Length={rowsText.Length}");

                if (totalIframes == 0)
                {
                    LogDebug("❌ iframe을 찾을 수 없음");
                    MessageBox.Show("iframe을 찾을 수 없습니다.\n\niframe이 있는 페이지로 이동해주세요.");
                    ResetAutoCollectUI();
                    return;
                }

                // 행 개수 파싱
                var rowMatches = System.Text.RegularExpressions.Regex.Matches(rowsText, @"\{[^}]+\}");
                int rowCount = rowMatches.Count;

                LogDebug($"매장 행 개수: {rowCount}");

                if (rowCount == 0)
                {
                    LogDebug("❌ 매장 목록(위 표)을 찾을 수 없음");
                    MessageBox.Show("iframe 내부에 매장 목록(위 표)을 찾을 수 없습니다.\n\n매장 목록이 있는 페이지로 이동해주세요.");
                    ResetAutoCollectUI();
                    return;
                }

                // 실제 수집할 개수 결정 (테스트 모드면 제한)
                // maxCount가 0이면 전체 수집, 아니면 제한된 개수만 수집
                int actualCount = (maxCount == 0 || maxCount > rowCount) ? rowCount : maxCount;

                LogDebug($"수집 시작: actualCount={actualCount}, rowCount={rowCount}, maxCount={maxCount}");

                if (isTestMode && actualCount < rowCount)
                {
                    SetS($"🧪 테스트 수집 중... (전체 {rowCount}개 중 {actualCount}개만 수집)");
                }
                else
                {
                    SetS($"🤖 자동 수집 중... (총 {rowCount}개 매장 발견)");
                }

                // 각 매장 행을 순차적으로 클릭하고 판매 내역 수집
                // 첫 번째 행(인덱스 0)은 헤더이므로 1번부터 시작
                LogDebug($"매장 반복문 시작: i=1 to {actualCount}");
                for (int i = 1; i <= actualCount; i++)
                {
                    if (!_isAutoCollecting)
                    {
                        MessageBox.Show($"자동 수집이 중단되었습니다.\n\n수집된 매장: {activeData.Count}개");
                        break;
                    }

                    // 매장명 가져오기 (첫 번째 셀)
                    string getStoreInfoScript = $@"
                        (function() {{
                            var iframes = document.querySelectorAll('iframe');
                            for (var i = 0; i < iframes.length; i++) {{
                                try {{
                                    var iframe = iframes[i];
                                    var rect = iframe.getBoundingClientRect();
                                    if (rect.width > 0 && rect.height > 0) {{
                                        var iframeDoc = iframe.contentDocument || iframe.contentWindow.document;
                                        if (iframeDoc && iframeDoc.body) {{
                                            var allTables = iframeDoc.querySelectorAll('table[id*=jqGrid]');
                                            if (allTables.length > 0) {{
                                                var rows = allTables[0].querySelectorAll('tbody tr[role=row]');
                                                if (rows[{i}]) {{
                                                     var cells = rows[{i}].querySelectorAll('td');
                                                    var cellTexts = [];
                                                    for (var j = 0; j < cells.length; j++) {{
                                                        cellTexts.push(cells[j].textContent.trim());
                                                    }}
                                                    return JSON.stringify({{
                                                        storeName: cells[2] ? cells[2].textContent.trim() : '매장_{i + 1}',
                                                        allCells: cellTexts
                                                    }});
                                                }}
                                            }}
                                        }}
                                    }}
                                }} catch (e) {{
                                    console.log('매장 정보 가져오기 오류:', e);
                                }}
                            }}
                            return JSON.stringify({{storeName: '매장_{i + 1}', allCells: []}});
                        }})();
                    ";

                    string storeInfoJson = await webView.CoreWebView2.ExecuteScriptAsync(getStoreInfoScript);
                    storeInfoJson = System.Text.RegularExpressions.Regex.Unescape(storeInfoJson);
                    storeInfoJson = storeInfoJson.Trim('"');

                    // JSON 파싱
                    string storeName = ExtractJsonValue(storeInfoJson, "storeName");

                    // 클릭한 행의 모든 셀 정보 추출 (구분을 위해)
                    string clickedRowInfo = "";
                    var allCellsMatch = System.Text.RegularExpressions.Regex.Match(storeInfoJson, @"""allCells""\s*:\s*\[(.*?)\]");
                    if (allCellsMatch.Success)
                    {
                        var cellsText = allCellsMatch.Groups[1].Value;
                        // JSON 배열에서 값 추출
                        var cellMatches = System.Text.RegularExpressions.Regex.Matches(cellsText, @"""([^""]*?)""");
                        if (cellMatches.Count > 0)
                        {
                            var cellValues = new List<string>();
                            foreach (System.Text.RegularExpressions.Match m in cellMatches)
                            {
                                cellValues.Add(m.Groups[1].Value);
                            }
                            clickedRowInfo = string.Join("\t", cellValues);
                        }
                    }

                    string progressText = isTestMode
                        ? $"🧪 테스트 수집 중... ({i}/{actualCount}) - {storeName}"
                        : $"🤖 자동 수집 중... ({i}/{actualCount}) - {storeName}";
                    SetS(progressText);

                    // 진행바 업데이트
                    double progress = ((double)(i + 1) / actualCount) * 100;
                    SetP(progress);
                    SetPT($"{progress:F1}%");
                    SetD($"매장: {storeName} | {i + 1}/{actualCount}");

                    // 위 표의 행 클릭 (매장 선택)
                    string clickScript = $@"
                        (function() {{
                            var iframes = document.querySelectorAll('iframe');
                            for (var i = 0; i < iframes.length; i++) {{
                                try {{
                                    var iframe = iframes[i];
                                    var rect = iframe.getBoundingClientRect();
                                    if (rect.width > 0 && rect.height > 0) {{
                                        var iframeDoc = iframe.contentDocument || iframe.contentWindow.document;
                                        if (iframeDoc && iframeDoc.body) {{
                                            var allTables = iframeDoc.querySelectorAll('table[id*=jqGrid]');
                                            if (allTables.length > 0) {{
                                                var rows = allTables[0].querySelectorAll('tbody tr[role=row]');
                                                if (rows[{i}]) {{
                                                    rows[{i}].click();
                                                    return 'clicked';
                                                }}
                                            }}
                                        }}
                                    }}
                                }} catch (e) {{
                                    console.log('클릭 오류:', e);
                                }}
                            }}
                            return 'failed';
                        }})();
                    ";

                    string clickResult = await webView.CoreWebView2.ExecuteScriptAsync(clickScript);

                    if (clickResult.Contains("failed"))
                    {
                        continue; // 실패한 행은 건너뛰기
                    }

                    // 아래 표가 업데이트될 때까지 대기 (최적화된 폴링 방식 + 재시도 로직)
                    SetS($"⏳ 매장 {i} ({storeName}) 데이터 로딩 대기 중...");
                    LogDebug($"\n---------- 매장 #{i} ({storeName}) 데이터 검증 시작 ----------");

                    bool storeDataLoaded = false;
                    int storeRowCount = 0;
                    int maxRetries = 3; // 최대 재시도 횟수

                    for (int retryCount = 0; retryCount < maxRetries && !storeDataLoaded; retryCount++)
                    {
                        if (retryCount > 0)
                        {
                            LogDebug($"🔄 재시도 #{retryCount}: 매장 #{i} ({storeName}) 다시 클릭");
                            SetS($"🔄 매장 {i} ({storeName}) 재시도 중... ({retryCount}/{maxRetries})");

                            // 재클릭
                            await webView.CoreWebView2.ExecuteScriptAsync(clickScript);
                            await Task.Delay(500);
                        }
                        else
                        {
                            // 첫 클릭 후 최소 대기 시간
                            await Task.Delay(300);
                        }

                        // 데이터 로드를 폴링으로 감지
                        int maxAttempts = 40; // 재시도마다 대기 시간 단축 (100회 → 40회, 약 12초)
                        int stableCheckCount = 0;
                        int requiredStableChecks = 1;

                        for (int attempt = 0; attempt < maxAttempts; attempt++)
                        {
                            await Task.Delay(300);

                            string checkDataScript = @"
                                (function() {
                                    var iframes = document.querySelectorAll('iframe');
                                    for (var i = 0; i < iframes.length; i++) {
                                        try {
                                            var iframe = iframes[i];
                                            var rect = iframe.getBoundingClientRect();
                                            if (rect.width > 0 && rect.height > 0) {
                                                var iframeDoc = iframe.contentDocument || iframe.contentWindow.document;
                                                if (iframeDoc && iframeDoc.body) {
                                                    var grid = iframeDoc.getElementById('jqGrid02');
                                                    if (grid) {
                                                        var rows = grid.querySelectorAll('tbody tr[role=""row""]');
                                                        var dataRowCount = 0;

                                                        for (var j = 0; j < rows.length; j++) {
                                                            var cells = rows[j].querySelectorAll('td');
                                                            for (var k = 0; k < cells.length; k++) {
                                                                if (cells[k].textContent.trim().length > 0) {
                                                                    dataRowCount++;
                                                                    break;
                                                                }
                                                            }
                                                        }

                                                        return dataRowCount;
                                                    }
                                                }
                                            }
                                        } catch (e) {}
                                    }
                                    return 0;
                                })();
                            ";

                            string rowCountStr = await webView.CoreWebView2.ExecuteScriptAsync(checkDataScript);
                            int currentRowCount = 0;
                            int.TryParse(rowCountStr.Trim('"'), out currentRowCount);

                            LogDebug($"재시도 {retryCount + 1}, 시도 #{attempt + 1}: {currentRowCount}행 감지");

                            if (currentRowCount > 1)
                            {
                                stableCheckCount++;
                                storeRowCount = currentRowCount;

                                if (stableCheckCount >= requiredStableChecks)
                                {
                                    storeDataLoaded = true;
                                    LogDebug($"✅ 매장 #{i} 데이터 로드 성공! ({storeRowCount}행, 재시도 {retryCount}회)");
                                    break;
                                }
                            }
                            else
                            {
                                if (stableCheckCount > 0)
                                {
                                    LogDebug($"⚠️ 데이터 불안정 - 카운터 리셋");
                                }
                                stableCheckCount = 0;
                            }

                            // 진행 상황 표시
                            if ((attempt + 1) % 4 == 0)
                            {
                                SetS($"⏳ 매장 {i} ({storeName}) 로딩 중... (재시도 {retryCount + 1}/{maxRetries}, {attempt + 1}/{maxAttempts})");
                            }
                        }

                        if (storeDataLoaded)
                        {
                            break; // 성공 시 재시도 루프 종료
                        }
                    }

                    if (storeDataLoaded)
                    {
                        LogDebug($"✅ 매장 #{i} 데이터 실시간 감지 성공! ({storeRowCount}행)");
                        SetS($"✅ 매장 {i} ({storeName}) 데이터 로드 완료 ({storeRowCount}행)");
                    }

                    // 데이터 로드 실패 시 실패 로그에 기록
                    if (!storeDataLoaded)
                    {
                        string failureLog = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | 날짜: {targetDate?.ToString("yyyy-MM-dd") ?? "미지정"} | 매장 #{i} ({storeName}) | 계정: {accountId ?? "미지정"} | 재시도 {maxRetries}회 후 실패";
                        _failedStores.Add(failureLog);

                        LogDebug($"❌ 매장 #{i} ({storeName}): 데이터 로드 실패 (재시도 {maxRetries}회)");
                        System.Diagnostics.Debug.WriteLine($"⚠️ 매장 {i} ({storeName}): 데이터 로드 실패 - 건너뜀");
                        SetS($"❌ 매장 {i} ({storeName}) 데이터 로드 실패 - 건너뜀");
                        await Task.Delay(500);
                        continue;
                    }

                    // 아래 표 (판매 내역) 데이터 추출
                    try
                    {
                        // 먼저 테이블 구조 파악 (개선된 디버깅)
                        string debugTableScript = @"
                            (function() {
                                var iframes = document.querySelectorAll('iframe');
                                for (var i = 0; i < iframes.length; i++) {
                                    try {
                                        var iframe = iframes[i];
                                        var rect = iframe.getBoundingClientRect();
                                        if (rect.width > 0 && rect.height > 0) {
                                            var iframeDoc = iframe.contentDocument || iframe.contentWindow.document;
                                            if (iframeDoc && iframeDoc.body) {
                                                var info = {
                                                    jqGridTables: [],
                                                    allTables: [],
                                                    textContent: []
                                                };

                                                // jqGrid 테이블 정보
                                                var jqGridTables = iframeDoc.querySelectorAll('table[id*=jqGrid]');
                                                for (var j = 0; j < jqGridTables.length; j++) {
                                                    var table = jqGridTables[j];

                                                    // 테이블 위의 텍스트 찾기 (제목)
                                                    var title = '';
                                                    var parent = table.parentElement;
                                                    while (parent && !title) {
                                                        var prevSibling = parent.previousElementSibling;
                                                        if (prevSibling) {
                                                            title = prevSibling.textContent.trim().substring(0, 50);
                                                            if (title) break;
                                                        }
                                                        parent = parent.parentElement;
                                                    }

                                                    // 테이블 첫 행의 텍스트
                                                    var firstRowText = '';
                                                    var firstRow = table.querySelector('tbody tr');
                                                    if (firstRow) {
                                                        firstRowText = firstRow.textContent.trim().substring(0, 50);
                                                    }

                                                    info.jqGridTables.push({
                                                        index: j,
                                                        id: table.id || 'no-id',
                                                        rowCount: table.querySelectorAll('tbody tr').length,
                                                        title: title,
                                                        firstRowText: firstRowText
                                                    });
                                                }

                                                // iframe 내부의 주요 텍스트 (매장별, 매뉴별 등 제목 찾기)
                                                var textElements = iframeDoc.querySelectorAll('div, span, td, th');
                                                for (var k = 0; k < Math.min(textElements.length, 20); k++) {
                                                    var text = textElements[k].textContent.trim();
                                                    if (text.length > 3 && text.length < 100 && 
                                                        (text.indexOf('매장') !== -1 || text.indexOf('매뉴') !== -1 || 
                                                         text.indexOf('메뉴') !== -1 || text.indexOf('현황') !== -1)) {
                                                        info.textContent.push(text);
                                                    }
                                                }

                                                return JSON.stringify(info);
                                            }
                                        }
                                    } catch (e) {}
                                }
                                return '{}';
                            })();
                        ";

                        string debugInfo = await webView.CoreWebView2.ExecuteScriptAsync(debugTableScript);
                        debugInfo = System.Text.RegularExpressions.Regex.Unescape(debugInfo);
                        debugInfo = debugInfo.Trim('"');
                        System.Diagnostics.Debug.WriteLine($"═══ 테이블 구조 상세 정보 ═══");
                        System.Diagnostics.Debug.WriteLine(debugInfo);
                        System.Diagnostics.Debug.WriteLine($"═══════════════════════════");

                        // 아래 표 HTML 추출 - jqGrid API로 직접 데이터 가져오기
                        string detailTableScript = @"
                            (function() {
                                var iframes = document.querySelectorAll('iframe');
                                for (var i = 0; i < iframes.length; i++) {
                                    try {
                                        var iframe = iframes[i];
                                        var rect = iframe.getBoundingClientRect();
                                        if (rect.width > 0 && rect.height > 0) {
                                            var iframeDoc = iframe.contentDocument || iframe.contentWindow.document;
                                            if (iframeDoc && iframeDoc.body) {

                                                console.log('iframe 내부 jqGrid 데이터 추출 시작...');

                                                // jqGrid02 찾기
                                                var gridId = 'jqGrid02';
                                                var grid = iframeDoc.getElementById(gridId);

                                                if (!grid) {
                                                    var tables = iframeDoc.querySelectorAll('table[aria-labelledby*=""gbox_jqGrid02""]');
                                                    if (tables.length > 0) {
                                                        grid = tables[0];
                                                        gridId = grid.id;
                                                    }
                                                }

                                                if (!grid) {
                                                    // 두 번째 jqGrid 테이블 사용
                                                    var allGrids = iframeDoc.querySelectorAll('table[id*=jqGrid]');
                                                    if (allGrids.length >= 2) {
                                                        grid = allGrids[1];
                                                        gridId = grid.id;
                                                    } else if (allGrids.length === 1) {
                                                        grid = allGrids[0];
                                                        gridId = grid.id;
                                                    }
                                                }

                                                if (grid && gridId) {
                                                    console.log('✅ jqGrid 발견:', gridId);

                                                    // jqGrid jQuery 인스턴스
                                                    var $grid = iframeDoc.defaultView.jQuery ? iframeDoc.defaultView.jQuery('#' + gridId) : null;

                                                    if ($grid && typeof $grid.jqGrid === 'function') {
                                                        // jqGrid API로 데이터 가져오기
                                                        console.log('✅ jqGrid API 사용');
                                                        try {
                                                            var rowIds = $grid.jqGrid('getDataIDs');
                                                            var rows = [];

                                                            for (var j = 0; j < rowIds.length; j++) {
                                                                var rowData = $grid.jqGrid('getRowData', rowIds[j]);
                                                                rows.push(rowData);
                                                            }

                                                            // 합계 행
                                                            var footerData = null;
                                                            try {
                                                                footerData = $grid.jqGrid('footerData', 'get');
                                                            } catch (e) {
                                                                console.log('합계 행 없음');
                                                            }

                                                            return JSON.stringify({
                                                                success: true,
                                                                method: 'jqGrid-API',
                                                                rows: rows,
                                                                footer: footerData || {}
                                                            });
                                                        } catch (apiError) {
                                                            console.log('jqGrid API 호출 실패:', apiError);
                                                        }
                                                    }

                                                    // jqGrid API 실패 시 HTML 반환
                                                    console.log('⚠️ jqGrid API 사용 불가, HTML 반환');
                                                    return JSON.stringify({
                                                        success: true,
                                                        method: 'HTML',
                                                        html: grid.outerHTML
                                                    });
                                                }

                                                console.log('❌ jqGrid를 찾을 수 없음');
                                                return JSON.stringify({success: false, method: 'none', html: ''});
                                            }
                                        }
                                    } catch (e) {
                                        console.log('❌ iframe 접근 오류:', e);
                                    }
                                }
                                return JSON.stringify({success: false, method: 'none', html: ''});
                            })();
                        ";

                        string detailDataJson = await webView.CoreWebView2.ExecuteScriptAsync(detailTableScript);
                        detailDataJson = System.Text.RegularExpressions.Regex.Unescape(detailDataJson);
                        detailDataJson = detailDataJson.Trim('"');

                        // 디버깅: JSON 파일 생성 비활성화
                        // try
                        // {
                        //     Directory.CreateDirectory(DATA_OUTPUT_PATH);
                        //     string debugPath = System.IO.Path.Combine(DATA_OUTPUT_PATH, $"debug_json_{i + 1}.txt");
                        //     System.IO.File.WriteAllText(debugPath, string.IsNullOrEmpty(detailDataJson) ? "[비어있음]" : detailDataJson, Encoding.UTF8);
                        //     System.Diagnostics.Debug.WriteLine($"📁 Debug JSON 저장: {debugPath}");
                        // }
                        // catch (Exception debugEx)
                        // {
                        //     System.Diagnostics.Debug.WriteLine($"Debug 파일 저장 실패: {debugEx.Message}");
                        // }
                        System.Diagnostics.Debug.WriteLine($"📁 Debug JSON 준비 완료 (파일 저장하지 않음)");

                        // 추가 디버깅: 테이블 셀 구조 분석
                        try
                        {
                            string analyzeTableScript = @"
                                (function() {
                                    var iframes = document.querySelectorAll('iframe');
                                    for (var i = 0; i < iframes.length; i++) {
                                        try {
                                            var iframe = iframes[i];
                                            var rect = iframe.getBoundingClientRect();
                                            if (rect.width > 0 && rect.height > 0) {
                                                var iframeDoc = iframe.contentDocument || iframe.contentWindow.document;
                                                if (iframeDoc && iframeDoc.body) {
                                                    var grid = iframeDoc.getElementById('jqGrid02');
                                                    if (!grid) {
                                                        var grids = iframeDoc.querySelectorAll('table[id*=jqGrid]');
                                                        grid = grids.length >= 2 ? grids[1] : (grids.length > 0 ? grids[0] : null);
                                                    }

                                                    if (grid) {
                                                        var analysis = {
                                                            tableId: grid.id,
                                                            sampleRows: []
                                                        };

                                                        var rows = grid.querySelectorAll('tbody tr[role=""row""]');
                                                        for (var j = 0; j < Math.min(rows.length, 3); j++) {
                                                            var cells = rows[j].querySelectorAll('td');
                                                            var rowData = {
                                                                rowClass: rows[j].className,
                                                                cellCount: cells.length,
                                                                cells: []
                                                            };

                                                            for (var k = 0; k < cells.length; k++) {
                                                                rowData.cells.push({
                                                                    index: k,
                                                                    ariaDescribedby: cells[k].getAttribute('aria-describedby') || '',
                                                                    title: cells[k].title || '',
                                                                    innerText: cells[k].innerText.substring(0, 50)
                                                                });
                                                            }

                                                            analysis.sampleRows.push(rowData);
                                                        }

                                                        return JSON.stringify(analysis, null, 2);
                                                    }
                                                }
                                            }
                                        } catch (e) {}
                                    }
                                    return '테이블을 찾을 수 없음';
                                })();
                            ";

                            string tableAnalysis = await WebView.CoreWebView2.ExecuteScriptAsync(analyzeTableScript);
                            tableAnalysis = System.Text.RegularExpressions.Regex.Unescape(tableAnalysis);
                            tableAnalysis = tableAnalysis.Trim('"');

                            // 디버그 파일 생성 비활성화
                            // Directory.CreateDirectory(DATA_OUTPUT_PATH);
                            // string analysisPath = System.IO.Path.Combine(DATA_OUTPUT_PATH, $"debug_table_analysis_{i}.txt");
                            // System.IO.File.WriteAllText(analysisPath, tableAnalysis, Encoding.UTF8);
                            System.Diagnostics.Debug.WriteLine($"📊 테이블 분석 완료 (파일 저장하지 않음)");
                        }
                        catch (Exception analysisEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"테이블 분석 실패: {analysisEx.Message}");
                        }

                        System.Diagnostics.Debug.WriteLine($"═══ jqGrid 데이터 추출 결과 ═══");
                        System.Diagnostics.Debug.WriteLine(detailDataJson.Substring(0, Math.Min(500, detailDataJson.Length)));

                        List<string> data = null;

                        if (!string.IsNullOrEmpty(detailDataJson) && detailDataJson.Contains("\"success\":true"))
                        {
                            // 디버그 JSON 파일 생성 비활성화
                            // Directory.CreateDirectory(DATA_OUTPUT_PATH);
                            // string debugPath = System.IO.Path.Combine(DATA_OUTPUT_PATH, $"debug_json_{i}.txt");
                            // System.IO.File.WriteAllText(debugPath, detailDataJson, Encoding.UTF8);
                            System.Diagnostics.Debug.WriteLine($"📁 Debug JSON 파싱 완료 (파일 저장하지 않음)");

                            if (detailDataJson.Contains("\"method\":\"jqGrid-API\""))
                            {
                                // jqGrid API로 가져온 데이터 파싱
                                System.Diagnostics.Debug.WriteLine("✅ jqGrid API 데이터 사용");
                                data = ParseJqGridJsonData(detailDataJson, storeName);
                            }
                            else
                            {
                                // HTML 파싱 방식
                                System.Diagnostics.Debug.WriteLine("⚠️ HTML 파싱 방식 사용");
                                var htmlMatch = System.Text.RegularExpressions.Regex.Match(detailDataJson, @"""html""\s*:\s*""([\s\S]+?)""(?=,""|\}$)");
                                if (htmlMatch.Success)
                                {
                                    string html = htmlMatch.Groups[1].Value;

                                    // 디버그 HTML 파일 생성 비활성화
                                    // Directory.CreateDirectory(DATA_OUTPUT_PATH);
                                    // string debugHtmlPath = System.IO.Path.Combine(DATA_OUTPUT_PATH, $"debug_html_{i}.html");
                                    // System.IO.File.WriteAllText(debugHtmlPath, html, Encoding.UTF8);
                                    System.Diagnostics.Debug.WriteLine($"📁 Debug HTML 파싱 완료 (파일 저장하지 않음)");

                                    data = _scraper.ParseData(html, storeName);
                                }
                            }
                        }

                        if (data != null && data.Count > 0)
                        {
                            activeData.Add(data);
                            System.Diagnostics.Debug.WriteLine($"✅ 매장 {i} ({storeName}): {data.Count}개 행 추출 성공");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ 매장 {i} ({storeName}): 데이터 파싱 실패 (0개 행)");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"매장 {i + 1} ({storeName}) 데이터 추출 실패: {ex.Message}");
                        SetS($"⚠️ 매장 {i + 1} ({storeName}) 추출 실패");
                    }

                    // 다음 클릭 전 짧은 대기
                    await Task.Delay(300);
                }

                // 수집 완료
                if (activeData.Count > 0)
                {
                    if (showMessage)
                    {
                        // 단일 계정 수집 시: 즉시 저장하고 메시지 표시
                        SaveCollectedData(isTestMode, accountId);
                    }
                    else if (localList == null)
                    {
                        // 기존 다중 계정 순차 방식: _allAccountsData에 누적
                        _allAccountsData.AddRange(activeData);
                        LogDebug($"✅ 계정 {accountId} 데이터 누적 완료: {activeData.Count}개 매장");
                    }
                    // localList != null 인 경우(병렬): 호출자가 직접 머지함 — 여기서 AddRange 안 함
                    LogDebug($"\n========== 자동 수집 완료: {activeData.Count}개 매장 ==========");
                }
                else
                {
                    LogDebug($"\n========== 자동 수집 실패: 데이터 없음 ==========");
                    if (showMessage)
                    {
                        MessageBox.Show($"수집된 데이터가 없습니다.\n\n아래 표(판매 내역)가 제대로 표시되는지 확인해주세요.\n\n디버그 로그:\n{_debugLogPath}", "수집 실패");
                    }
                }

                // 병렬 모드(localList != null)에서는 ResetAutoCollectUI 호출 안 함
                // — 먼저 끝난 task가 _isAutoCollecting=false로 바꾸면 나머지 task가 중단됨
                if (localList == null)
                    ResetAutoCollectUI();
            }
            catch (Exception ex)
            {
                LogDebug($"\n========== 예외 발생 ==========\n{ex.ToString()}");
                MessageBox.Show($"자동 수집 실패:\n{ex.Message}\n\n디버그 로그:\n{_debugLogPath}");
                if (localList == null)
                    ResetAutoCollectUI();
            }
        }

        private void StopAutoCollectButton_Click(object sender, RoutedEventArgs e)
        {
            _isAutoCollecting = false;
            StatusText.Text = "자동 수집 중지됨";

            if (_collectedData.Count > 0)
            {
                var result = MessageBox.Show(
                    $"현재까지 {_collectedData.Count}개 매장의 데이터가 수집되었습니다.\n\n수집된 데이터를 저장하시겠습니까?",
                    "데이터 저장",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    SaveCollectedData(false);
                }
            }

            ResetAutoCollectUI();
        }

        private void ResetAutoCollectUI()
        {
            _isAutoCollecting = false;
            // AutoCollectButton.IsEnabled = true;
            // AutoCollectTestButton.IsEnabled = true;
            StopAutoCollectButton.IsEnabled = false;
            StatusText.Text = "준비";
        }

        private void SaveCollectedData(bool isTestMode = false, string accountId = null)
        {
            try
            {
                // 지정된 경로에 저장 (폴더가 없으면 자동 생성)
                Directory.CreateDirectory(DATA_OUTPUT_PATH);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string accountSuffix = string.IsNullOrEmpty(accountId) ? "" : $"_{accountId}";
                string fileName = $"통합데이터{accountSuffix}_{timestamp}.csv";
                string filePath = System.IO.Path.Combine(DATA_OUTPUT_PATH, fileName);

                // 통합 CSV (모든 매장 데이터를 하나의 시트로, 헤더는 맨 위에 한 번만)
                var allCsvData = new List<string>();

                // 헤더를 맨 위에 한 번만 추가
                allCsvData.Add("매장명,중분류,메뉴명,메뉴코드,판매수량,서비스수량,총수량,총매출액,총할인액,평균매출액,매출비율");

                for (int i = 0; i < _collectedData.Count; i++)
                {
                    if (_collectedData[i].Count > 0)
                    {
                        foreach (var line in _collectedData[i])
                        {
                            // 빈 줄은 건너뛰기
                            if (string.IsNullOrWhiteSpace(line))
                            {
                                continue;
                            }

                            // [소 계] 행은 건너뛰기
                            if (line.Contains("[소 계]"))
                            {
                                continue;
                            }

                            // 데이터 행: 메뉴코드에 apostrophe 추가
                            var columns = line.Split('\t');
                            for (int col = 0; col < columns.Length; col++)
                            {
                                // 메뉴코드 컬럼 (매장명이 있으면 4번째(index 3))
                                if (col == 3 && columns.Length > 3)
                                {
                                    // 메뉴코드로 추정되는 컬럼: 숫자만 있으면 앞에 apostrophe 추가
                                    if (!string.IsNullOrWhiteSpace(columns[col]) && 
                                        columns[col].All(c => char.IsDigit(c) || c == '-'))
                                    {
                                        columns[col] = $"'{columns[col]}";
                                    }
                                }
                            }
                            allCsvData.Add(string.Join(",", columns));
                        }
                    }
                }

                File.WriteAllLines(filePath, allCsvData, Encoding.UTF8);

                // DB 저장
                int dbRows = 0;
                string dbMessage = "";
                try
                {
                    dbRows = DbSaver.Save(_collectedData, _collectStartDate ?? DateTime.Today.AddDays(-1));
                    dbMessage = $"\n💾 DB 저장: {dbRows}행 저장됨";
                }
                catch (Exception dbEx)
                {
                    dbMessage = $"\n⚠️ DB 저장 실패: {dbEx.Message}";
                }

                string modeTitle = isTestMode ? "테스트 수집 완료" : "자동 수집 완료";
                string modeEmoji = isTestMode ? "🧪" : "✅";

                MessageBox.Show(
                    $"{modeEmoji} 매장별 판매내역 {(isTestMode ? "테스트 " : "")}수집 완료!\n\n" +
                    $"총 {_collectedData.Count}개 매장 수집\n" +
                    $"총 데이터 행: {allCsvData.Count - 1}개 (헤더 제외)\n\n" +
                    $"📊 저장 파일:\n{filePath}" +
                    dbMessage,
                    modeTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                DetailText.Text = $"✅ {_collectedData.Count}개 매장 저장 완료";
                StatusText.Text = $"✅ 데이터 저장 완료";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"데이터 저장 실패:\n{ex.Message}");
            }
        }

        /// <summary>
        /// 모든 계정에서 수집한 데이터를 하나의 통합 CSV 파일로 저장
        /// </summary>
        private void SaveAllAccountsData(string startDateStr, string endDateStr)
        {
            try
            {
                if (_allAccountsData.Count == 0)
                {
                    MessageBox.Show("수집된 데이터가 없습니다.", "데이터 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 저장 경로 생성
                Directory.CreateDirectory(DATA_OUTPUT_PATH);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"통합데이터_{startDateStr}to{endDateStr}_{timestamp}.csv";
                string filePath = System.IO.Path.Combine(DATA_OUTPUT_PATH, fileName);

                // CSV 데이터 생성
                var allCsvData = new List<string>();

                // 헤더 추가
                allCsvData.Add("매장명,중분류,메뉴명,메뉴코드,판매수량,서비스수량,총수량,총매출액,총할인액,평균매출액,매출비율");

                int totalRows = 0;

                // 모든 계정의 데이터 처리
                foreach (var storeData in _allAccountsData)
                {
                    foreach (var line in storeData)
                    {
                        // 빈 행이나 [소 계] 행 건너뛰기
                        if (string.IsNullOrWhiteSpace(line) || line.Contains("[소 계]"))
                        {
                            continue;
                        }

                        // 데이터 행: 메뉴코드에 apostrophe 추가
                        var columns = line.Split('\t');
                        for (int col = 0; col < columns.Length; col++)
                        {
                            // 메뉴코드 컬럼 (매장명이 있으면 4번째(index 3))
                            if (col == 3 && columns.Length > 3)
                            {
                                // 메뉴코드로 추정되는 컬럼: 숫자만 있으면 앞에 apostrophe 추가
                                if (!string.IsNullOrWhiteSpace(columns[col]) && 
                                    columns[col].All(c => char.IsDigit(c) || c == '-'))
                                {
                                    columns[col] = $"'{columns[col]}";
                                }
                            }
                        }
                        allCsvData.Add(string.Join(",", columns));
                        totalRows++;
                    }
                }

                // 파일 저장
                File.WriteAllLines(filePath, allCsvData, Encoding.UTF8);

                // DB 저장
                int dbRows = 0;
                string dbMessage = "";
                try
                {
                    dbRows = DbSaver.Save(_allAccountsData, _collectStartDate ?? DateTime.Today.AddDays(-1));
                    dbMessage = $"\n💾 DB 저장: {dbRows}행 저장됨";
                }
                catch (Exception dbEx)
                {
                    dbMessage = $"\n⚠️ DB 저장 실패: {dbEx.Message}";
                }

                // 완료 메시지
                MessageBox.Show(
                    $"✅ 모든 계정 데이터 수집 완료!\n\n" +
                    $"수집 기간: {startDateStr} ~ {endDateStr}\n" +
                    $"총 매장 수: {_allAccountsData.Count}개\n" +
                    $"총 데이터 행: {totalRows}개\n\n" +
                    $"📊 저장 파일:\n{filePath}" +
                    dbMessage,
                    "수집 완료",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                DetailText.Text = $"✅ 통합 데이터 저장 완료";
                StatusText.Text = "✅ 모든 계정 수집 완료";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"통합 데이터 저장 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 특정 날짜의 모든 계정 데이터를 CSV로 저장 (날짜 열 포함)
        /// </summary>
        private void SaveAllAccountsDataWithDate(string dateStr, DateTime targetDate)
        {
            try
            {
                if (_allAccountsData.Count == 0)
                {
                    MessageBox.Show($"{targetDate:yyyy-MM-dd} 데이터가 수집되지 않았습니다.", "데이터 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 저장 경로 생성
                Directory.CreateDirectory(DATA_OUTPUT_PATH);

                string timestamp = DateTime.Now.ToString("HHmmss");
                string fileName = $"판매데이터_{dateStr}_{timestamp}.csv";
                string filePath = System.IO.Path.Combine(DATA_OUTPUT_PATH, fileName);

                // CSV 데이터 생성
                var allCsvData = new List<string>();

                // 헤더 추가 (날짜 열을 맨 앞에 추가)
                allCsvData.Add("날짜,매장명,중분류,메뉴명,메뉴코드,판매수량,서비스수량,총수량,총매출액,총할인액,평균매출액,매출비율");

                int totalRows = 0;
                string dateColumn = targetDate.ToString("yyyy-MM-dd");

                // 모든 계정의 데이터 처리
                foreach (var storeData in _allAccountsData)
                {
                    foreach (var line in storeData)
                    {
                        // 빈 행이나 [소 계] 행 건너뛰기
                        if (string.IsNullOrWhiteSpace(line) || line.Contains("[소 계]"))
                        {
                            continue;
                        }

                        // 데이터 행: 메뉴코드에 apostrophe 추가
                        var columns = line.Split('\t');

                        // 날짜 열을 맨 앞에 추가
                        var newColumns = new List<string> { dateColumn };

                        for (int col = 0; col < columns.Length; col++)
                        {
                            // 메뉴코드 컬럼 (매장명이 있으면 4번째(index 3))
                            if (col == 3 && columns.Length > 3)
                            {
                                // 메뉴코드로 추정되는 컬럼: 숫자만 있으면 앞에 apostrophe 추가
                                if (!string.IsNullOrWhiteSpace(columns[col]) && 
                                    columns[col].All(c => char.IsDigit(c) || c == '-'))
                                {
                                    newColumns.Add($"'{columns[col]}");
                                }
                                else
                                {
                                    newColumns.Add(columns[col]);
                                }
                            }
                            else
                            {
                                newColumns.Add(columns[col]);
                            }
                        }

                        allCsvData.Add(string.Join(",", newColumns));
                        totalRows++;
                    }
                }

                // 파일 저장
                File.WriteAllLines(filePath, allCsvData, Encoding.UTF8);

                // DB 저장
                int dbRows = 0;
                string dbMessage = "";
                try
                {
                    dbRows = DbSaver.Save(_allAccountsData, targetDate);
                    dbMessage = $" | 💾 DB: {dbRows}행";
                }
                catch (Exception dbEx)
                {
                    dbMessage = $" | ⚠️ DB 실패: {dbEx.Message}";
                    LogDebug($"DB 저장 실패: {dbEx}");
                }

                LogDebug($"✅ {targetDate:yyyy-MM-dd} 데이터 저장 완료: {filePath}");
                StatusText.Text = $"✅ {targetDate:yyyy-MM-dd} 데이터 저장 완료";
                DetailText.Text = $"파일: {fileName} | {totalRows}행{dbMessage}";

                // 실패 로그가 있으면 파일로 저장
                SaveFailureLog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{targetDate:yyyy-MM-dd} 데이터 저장 실패:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 실패한 매장 정보를 텍스트 파일로 저장
        /// </summary>
        private void SaveFailureLog()
        {
            try
            {
                if (_failedStores.Count == 0)
                {
                    return; // 실패한 매장이 없으면 파일 생성 안 함
                }

                Directory.CreateDirectory(DATA_OUTPUT_PATH);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"실패로그_{timestamp}.txt";
                string filePath = System.IO.Path.Combine(DATA_OUTPUT_PATH, fileName);

                var logContent = new List<string>
                {
                    "========================================",
                    "데이터 수집 실패 로그",
                    $"생성 시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    $"총 실패 건수: {_failedStores.Count}개",
                    "========================================",
                    ""
                };

                logContent.AddRange(_failedStores);

                logContent.Add("");
                logContent.Add("========================================");
                logContent.Add("※ 실패 원인:");
                logContent.Add("  - 네트워크 지연");
                logContent.Add("  - 서버 응답 지연");
                logContent.Add("  - 해당 매장에 데이터 없음");
                logContent.Add("========================================");

                File.WriteAllLines(filePath, logContent, Encoding.UTF8);

                LogDebug($"📝 실패 로그 저장 완료: {filePath}");
            }
            catch (Exception ex)
            {
                LogDebug($"⚠️ 실패 로그 저장 실패: {ex.Message}");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _scraper?.Dispose();
        }

        /// <summary>
        /// CSV 변환 시 메뉴코드를 큰따옴표로 감싸서 텍스트로 유지
        /// </summary>
        private List<string> ConvertToCsvWithQuotedMenuCode(List<string> data)
        {
            var csvData = new List<string>();
            foreach (var line in data)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("===") || 
                    line.StartsWith("▼") || line.StartsWith("▲") || 
                    line.StartsWith("[클릭한 행") || line.Contains("매장명"))
                {
                    // 구분선, 특수 행, 헤더는 그대로
                    csvData.Add(line.Replace("\t", ","));
                }
                else
                {
                    // 데이터 행: 메뉴코드를 큰따옴표로 감싸기
                    var columns = line.Split('\t');
                    for (int col = 0; col < columns.Length; col++)
                    {
                        // 메뉴코드 컬럼 위치 판단
                        // 매장명, 중분류, 메뉴명, [메뉴코드], ... 순서이므로
                        // 매장명이 있으면 3번째(index 3), 없으면 2번째(index 2) 또는 그 근처
                        bool isMenuCodeColumn = false;

                        if (columns.Length >= 10) // 매장명 포함 (11개 컬럼)
                        {
                            isMenuCodeColumn = (col == 3);
                        }
                        else if (columns.Length >= 9) // 매장명 제외 (10개 컬럼)
                        {
                            isMenuCodeColumn = (col == 2);
                        }

                        if (isMenuCodeColumn && !string.IsNullOrWhiteSpace(columns[col]))
                        {
                            // 숫자만 있는 경우 따옴표로 감싸기 (0으로 시작하는 코드 보호)
                            if (columns[col].All(c => char.IsDigit(c)))
                            {
                                columns[col] = $"\"{columns[col]}\"";
                            }
                        }
                    }
                    csvData.Add(string.Join(",", columns));
                }
            }
            return csvData;
        }

        /// <summary>
        /// jqGrid API로 가져온 JSON 데이터 파싱
        /// </summary>
        private List<string> ParseJqGridJsonData(string json, string storeName)
        {
            var result = new List<string>();

            try
            {
                // 통합 CSV를 위해 헤더와 구분선은 추가하지 않음 (SaveCollectedData에서 한 번만 추가)

                // rows 배열 추출
                var rowsMatch = System.Text.RegularExpressions.Regex.Match(json, @"""rows""\s*:\s*\[([\s\S]*?)\](?=\s*,\s*""footer""|})");
                if (rowsMatch.Success)
                {
                    string rowsJson = rowsMatch.Groups[1].Value;

                    // 각 행 객체 추출 {....} 형식
                    var rowMatches = System.Text.RegularExpressions.Regex.Matches(rowsJson, @"\{[^{}]*\}");

                    foreach (System.Text.RegularExpressions.Match rowMatch in rowMatches)
                    {
                        string rowJson = rowMatch.Value;

                        // 각 속성 추출 (실제 jqGrid API JSON 키 사용)
                        // JSON 예시: {"gname":"메인메뉴","mname":"과일","mcode":"000685","sqty":"44","chk_svc":"0","tqty":"44","salesamt":"1061200","dcamount":"0","aveTamount":"...","amountPercent":"..."}
                        string 중분류 = ExtractJsonProperty(rowJson, new[] { "gname" });
                        string 메뉴명 = ExtractJsonProperty(rowJson, new[] { "mname" });
                        string 메뉴코드 = ExtractJsonProperty(rowJson, new[] { "mcode" });
                        string 판매수량 = ExtractJsonProperty(rowJson, new[] { "sqty" });
                        string 서비스수량 = ExtractJsonProperty(rowJson, new[] { "chk_svc" });
                        string 총수량 = ExtractJsonProperty(rowJson, new[] { "tqty" });
                        string 총매출액 = ExtractJsonProperty(rowJson, new[] { "salesamt" });
                        string 총할인액 = ExtractJsonProperty(rowJson, new[] { "dcamount" });
                        string 평균매출액 = ExtractJsonProperty(rowJson, new[] { "avetamount" });
                        string 매출비율 = ExtractJsonProperty(rowJson, new[] { "amountpercent" });

                        // 숫자 포맷 제거
                        판매수량 = CleanNumericValue(판매수량);
                        서비스수량 = CleanNumericValue(서비스수량);
                        총수량 = CleanNumericValue(총수량);
                        총매출액 = CleanNumericValue(총매출액);
                        총할인액 = CleanNumericValue(총할인액);
                        평균매출액 = CleanNumericValue(평균매출액);
                        매출비율 = CleanNumericValue(매출비율);

                        // 메뉴코드는 문자열로 유지 (HTML 엔티티 디코드)
                        메뉴코드 = System.Net.WebUtility.HtmlDecode(메뉴코드);

                        if (!string.IsNullOrWhiteSpace(메뉴명))
                        {
                            if (!string.IsNullOrWhiteSpace(storeName))
                            {
                                result.Add($"{storeName}\t{중분류}\t{메뉴명}\t{메뉴코드}\t{판매수량}\t{서비스수량}\t{총수량}\t{총매출액}\t{총할인액}\t{평균매출액}\t{매출비율}");
                            }
                            else
                            {
                                result.Add($"{중분류}\t{메뉴명}\t{메뉴코드}\t{판매수량}\t{서비스수량}\t{총수량}\t{총매출액}\t{총할인액}\t{평균매출액}\t{매출비율}");
                            }
                        }
                    }
                }

                // 합계 행은 통합 CSV에서 불필요하므로 제거
                // (필요시 Excel에서 SUM 함수로 계산 가능)
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"jqGrid JSON 파싱 오류: {ex.Message}");
                result.Add($"파싱 오류: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// JSON 객체에서 여러 가능한 키로 속성 값 추출 (대소문자 구분 없음)
        /// </summary>
        private string ExtractJsonProperty(string json, string[] possibleKeys)
        {
            foreach (var key in possibleKeys)
            {
                // 대소문자 구분 없이 매칭 (RegexOptions.IgnoreCase)
                var match = System.Text.RegularExpressions.Regex.Match(
                    json, 
                    $@"""{key}""\s*:\s*""([^""]*?)""", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                if (match.Success)
                {
                    return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
                }
            }
            return "";
        }

        /// <summary>
        /// 숫자 값에서 HTML 태그 및 포맷 제거
        /// </summary>
        private string CleanNumericValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            // HTML 태그 제거
            value = System.Text.RegularExpressions.Regex.Replace(value, @"<[^>]+>", "");

            // HTML 엔티티 디코드
            value = System.Net.WebUtility.HtmlDecode(value);

            // 콤마, 공백 제거
            value = value.Replace(",", "").Replace(" ", "").Trim();

            // &nbsp; 등 추가 제거
            value = value.Replace("&nbsp;", "");

            return value;
        }
    }
}
