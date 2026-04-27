using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices; // 핫키 등록용 & Win32 RECT
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Interop; // HwndSource
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;
using System.Windows.Media; // Added for brushes
using System.Linq; // for FirstOrDefault and collection helpers
using System.Windows.Shapes; // for Ellipse
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WPF;
using Newtonsoft.Json;
using System.Net.Http;
using System.Collections.ObjectModel;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Microsoft.Data.SqlClient;


namespace WpfApp2
{
    public partial class MainWindow : Window
    {
        // 설정 파일을 사용자 AppData 폴더에 저장
        private static readonly string AppDataFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "WpfApp2");

        private static readonly string SettingsFile = System.IO.Path.Combine(AppDataFolder, "mainwindow_settings.json");
        private static readonly string ButtonStateFile = System.IO.Path.Combine(AppDataFolder, "button_states.json");
        private static readonly string TabStateFile = System.IO.Path.Combine(AppDataFolder, "tab_states.json");
        private static readonly string ChartStateFile = System.IO.Path.Combine(AppDataFolder, "chart_states.json");

        private DispatcherTimer leaveTimer;

        private readonly Random _rand = new Random(); // Random 인스턴스는 readonly로 선언

        // 1, 2번 오류: 명확한 네임스페이스 지정
        private System.Windows.Point? _lastBorderRightClickPoint = null;

        // 단축키로 열었는지 추적하는 플래그 (마우스가 한 번 들어올 때까지 Window3 전환 방지)
        private bool _openedByHotkey = false;

        private const int HOTKEY_ID_ALT_F1 = 0xA701; // 임의 ID
        private const uint MOD_ALT = 0x0001;
        private const uint VK_F1 = 0x70;

        // 이미지 좌우 가장자리 및 내부 라벨 간격 상수 추가
        private const double HorizontalImageEdgePadding = 15.0; // 좌우 가장자리 여백 (기존 6 -> 15)
        private const double InnerLabelLeftGap = 15.0; // 내부 라벨 왼쪽(이미지 오른쪽) 간격 (기존 6 -> 15)

        // 드래그 앤 드롭 및 그리드 스냅 관련 상수
        private const double GridSize = 10.0; // 그리드 크기 (픽셀)
        private const double DragThreshold = 3.0; // 드래그로 인식하기 위한 최소 이동 거리

        // 그리드 라인 표시 상태
        private bool _isGridVisible = false;
        private DispatcherTimer _gridCheckTimer;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_SHOWNORMAL = 1;

        // 추가: 정확한 스크린 좌표 판정을 위한 Win32 RECT & GetWindowRect
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINPOINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public WINPOINT ptReserved;
            public WINPOINT ptMaxSize;
            public WINPOINT ptMaxPosition;
            public WINPOINT ptMinTrackSize;
            public WINPOINT ptMaxTrackSize;
        }
        private const int WM_GETMINMAXINFO = 0x0024;
        private static System.Drawing.Rectangle GetWindowScreenRect(Window w)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return System.Drawing.Rectangle.Empty;
            if (!GetWindowRect(hwnd, out var r)) return System.Drawing.Rectangle.Empty;
            return new System.Drawing.Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }

        public MainWindow()
        {
            if (!Directory.Exists(AppDataFolder))
                Directory.CreateDirectory(AppDataFolder);

            ThemeManager.LoadSaved();
            InitializeComponent();
            WindowStartupLocation = WindowStartupLocation.Manual;
            // 위치 복원은 SourceInitialized에서 수행
            RestoreAllTabs(); // 탭 복원을 먼저 수행
            RestoreAllButtonStates(); // 그 다음 버튼 복원
            RestoreAllChartStates(); // 차트 복원
            ThemeManager.ThemeChanged += (_, _) => Dispatcher.Invoke(RefreshAllChartColors);
            tabControl.SelectionChanged += TabControl_SelectionChanged;
            Loaded += MainWindow_Loaded;
            StateChanged += MainWindow_StateChanged;

            this.PreviewKeyDown += MainWindow_PreviewKeyDown; // ESC 처리
            this.MouseLeave += MainWindow_MouseLeave;
            this.MouseEnter += MainWindow_MouseEnter; // 단축키 플래그 리셋용

            leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            leaveTimer.Tick += LeaveTimer_Tick;

            // 그리드 표시 체크 타이머 (Shift 키 감지용)
            _gridCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _gridCheckTimer.Tick += GridCheckTimer_Tick;
            _gridCheckTimer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // 윈도우가 생성된 후 위치 복원
            RestoreWindowPosition();

            var source = (HwndSource)PresentationSource.FromVisual(this)!;
            source.AddHook(WndProc);
            RegisterHotKey(source.Handle, HOTKEY_ID_ALT_F1, MOD_ALT, VK_F1); // Alt+F1 등록
        }

        protected override void OnClosed(EventArgs e)
        {
            // 핫키 해제
            var source = (HwndSource)PresentationSource.FromVisual(this);
            if (source != null)
            {
                UnregisterHotKey(source.Handle, HOTKEY_ID_ALT_F1);
            }
            base.OnClosed(e);
            SaveWindowPosition();
            SaveAllTabs(); // 탭 정보 저장
            SaveAllButtonStates(); // 버튼 정보 저장
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // WM_HOTKEY =0x0312
            if (msg == 0x0312)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_ID_ALT_F1)
                {
                    ShowAndActivateMain();
                    handled = true;
                }
            }
            else if (msg == WM_GETMINMAXINFO)
            {
                var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;
                var screen = Forms.Screen.FromHandle(hwnd);
                var wa = screen.WorkingArea;
                mmi.ptMaxPosition = new WINPOINT { X = 0, Y = 0 };
                mmi.ptMaxSize = new WINPOINT { X = wa.Width, Y = wa.Height };
                Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ShowAndActivateMain()
        {
            // 단축키로 열었다는 플래그 설정
            _openedByHotkey = true;

            // Window2, Window3 숨기기
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Window2 w2 && w2.IsVisible) w2.Hide();
                else if (win is Window3 w3 && w3.IsVisible) w3.Hide();
            }
            // 메인윈도우 표시/활성화
            if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            var hwnd = new WindowInteropHelper(this).Handle;
            ShowWindow(hwnd, SW_SHOWNORMAL);
            SetForegroundWindow(hwnd);
            Activate();
            Focus();
            Keyboard.Focus(this);

            // Alt키 해제 직후 포커스 보강
            Dispatcher.InvokeAsync(() =>
            {
                if (!IsActive) Activate();
                Focus();
                Keyboard.Focus(this);
            }, DispatcherPriority.ApplicationIdle);

            // 단축키로 열었을 때는 마우스가 밖에 있어도 Window3 전환하지 않음
            // 마우스가 메인윈도우 안에 들어왔다가 나갈 때만 전환되도록 함
            if (!_openedByHotkey && IsMouseOutsideMainOrWindow1())
            {
                ShowWindow3AtLeftBottom();
                return;
            }
            // 보수적으로 타이머도 시작 (MouseLeave가 안들어오는 환경 대응)
            leaveTimer.Stop();
            leaveTimer.Start();
        }

        private bool IsMouseOutsideMainOrWindow1()
        {
            var mousePos = Forms.Control.MousePosition; // 디바이스 픽셀
            var mainRect = GetWindowScreenRect(this);
            if (!mainRect.IsEmpty && mainRect.Contains(mousePos)) return false;
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Window1 w1 && w1.IsVisible)
                {
                    var r = GetWindowScreenRect(w1);
                    if (!r.IsEmpty && r.Contains(mousePos)) return false;
                }
            }
            return true;
        }

        private void MainWindow_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // 단축키로 열었고 아직 마우스가 한 번도 안 들어온 경우 무시
            if (_openedByHotkey)
                return;

            // 마우스가 Window1 위에 있으면 무시 (스크린 좌표로 검증)
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Window1 w1 && w1.IsVisible)
                {
                    var mousePos = System.Windows.Forms.Control.MousePosition;
                    var r = GetWindowScreenRect(w1);
                    if (!r.IsEmpty && r.Contains(mousePos))
                        return;
                }
            }

            // 타이머 시작 (이미 동작 중이면 재시작)
            leaveTimer.Stop();
            leaveTimer.Start();
        }

        private void MainWindow_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // 마우스가 메인윈도우에 들어오면 단축키 플래그 리셋
            // 이제부터는 정상적인 MouseLeave 동작이 가능하도록
            _openedByHotkey = false;
        }

        private void LeaveTimer_Tick(object? sender, EventArgs e)
        {
            leaveTimer.Stop();

            // 단축키로 열었고 아직 마우스가 한 번도 안 들어온 경우 무시
            if (_openedByHotkey)
                return;

            // 마우스가 여전히 MainWindow, Window1 위에 있으면 아무것도 하지 않음 (스크린 좌표 판정)
            var mousePos = System.Windows.Forms.Control.MousePosition;
            var mainRect = GetWindowScreenRect(this);
            if (!mainRect.IsEmpty && mainRect.Contains(mousePos)) return;

            // 열려있는 모든 자식 Window와 컨텍스트 메뉴를 닫기
            foreach (Window win in System.Windows.Application.Current.Windows.Cast<Window>().ToList())
            {
                if (win == this) continue; // MainWindow 자체는 제외
                if (win is Window3) continue; // Window3는 제외 (런처 아이콘)

                if (win is Window1 w1 && w1.IsVisible)
                {
                    var r = GetWindowScreenRect(w1);
                    if (!r.IsEmpty && r.Contains(mousePos)) return;
                }

                // 버튼 설정 다이얼로그나 기타 자식 창 닫기
                if (win.Owner == this && win.IsVisible)
                {
                    try
                    {
                        win.Close();
                    }
                    catch { }
                }
            }

            // 열려있는 모든 컨텍스트 메뉴 닫기
            CloseAllContextMenus();

            ShowWindow3AtLeftBottom();
        }

        private void CloseAllContextMenus()
        {
            // ButtonCanvas와 Border의 컨텍스트 메뉴 닫기
            try
            {
                for (int i = 0; i < tabControl.Items.Count; i++)
                {
                    var canvas = GetCanvasByIndex(i);
                    if (canvas?.Parent is Border border && border.ContextMenu?.IsOpen == true)
                    {
                        border.ContextMenu.IsOpen = false;
                    }

                    if (canvas != null)
                    {
                        foreach (UIElement child in canvas.Children)
                        {
                            if (child is System.Windows.Controls.Button btn && btn.ContextMenu?.IsOpen == true)
                            {
                                btn.ContextMenu.IsOpen = false;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                ShowWindow3AtLeftBottom();
            }
        }

        // setbtn_Click 이벤트 핸들러에서 Window1을 올바르게 생성하고 표시
        private void setbtn_Click(object sender, RoutedEventArgs e)
        {
            // 메인윈도우가 보일 때만 Window1을 띄움
            if (this.Visibility != Visibility.Visible)
                return;

            System.Diagnostics.Debug.WriteLine($"[MainWindow] WindowState={WindowState}, Before creating Window1: Left={Left}, Top={Top}, Width={ActualWidth}, Height={ActualHeight}");

            // 이미 열려있는 Window1이 있으면 닫고 새로 생성
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Window1 w1 && w1.IsVisible)
                {
                    w1.Close();
                    break;
                }
            }

            var win1 = new Window1();
            win1.Owner = this;
            win1.Show();
        }

        private void personalbtn_Click(object sender, RoutedEventArgs e)
        {
            // Window1이 열려있으면 닫기
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Window1 w1 && w1.IsVisible)
                    w1.Close();
            }

            // Window2 재사용 (숨겨져 있어도 탭 유지 후 다시 표시)
            Window2? existingW2 = null;
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Window2 w2)
                {
                    existingW2 = w2;
                    break;
                }
            }

            if (existingW2 == null)
            {
                // 새로 생성
                existingW2 = new Window2();
                existingW2.Owner = this;
                existingW2.WindowStartupLocation = WindowStartupLocation.Manual;
                existingW2.Loaded += (s, e2) => PositionWindow2(existingW2);
                existingW2.Show();
            }
            else
            {
                if (!existingW2.IsVisible)
                {
                    PositionWindow2(existingW2);
                    existingW2.Show();
                }
                else
                {
                    existingW2.Activate();
                }
            }

            // MainWindow 숨기기
            this.Hide();
        }

        private void PositionWindow2(Window2 w2)
        {
            double offset = 25; // 추가 여유
            double win3Height = 0;
            foreach (Window w in System.Windows.Application.Current.Windows)
            {
                if (w is Window3 w3 && w3.IsVisible)
                {
                    win3Height = w3.Height;
                    break;
                }
            }
            w2.Left = this.Left;
            w2.Top = this.Top + this.Height - w2.Height - win3Height - offset;
        }

        // Window3이 떴을 때 Window1을 닫음 (수정: 숨겨진 Window3는 재표시)
        private void ShowWindow3AtLeftBottom()
        {
            Window3? existing = null;
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Window3 w3)
                {
                    existing = w3;
                    if (w3.IsVisible)
                        return; // 이미 표시중이면 그대로
                    break; // 숨겨진 인스턴스 발견
                }
            }

            // Window1 닫기 (Window2는 닫거나 숨기지 않음)
            foreach (Window w1 in System.Windows.Application.Current.Windows)
            {
                if (w1 is Window1 window1 && window1.IsVisible)
                    window1.Close();
            }

            if (existing != null && !existing.IsVisible)
            {
                // 숨겨진 Window3 재사용
                existing.Owner = this;
                existing.WindowStartupLocation = WindowStartupLocation.Manual;
                // 저장된 위치가 없으면 기본 위치 사용
                string window3PositionFile = System.IO.Path.Combine(AppDataFolder, "window3_position.json");
                if (!File.Exists(window3PositionFile))
                {
                    existing.Left = this.Left;
                    existing.Top = this.Top + this.Height - existing.Height;
                }
                // 저장된 위치가 있으면 RestorePosition()이 자동으로 처리
                existing.ShowInTaskbar = false;
                existing.Topmost = true;
                this.Visibility = Visibility.Hidden;
                existing.Show();
                existing.Topmost = true;
                return;
            }

            // 새로 생성
            var win3 = new Window3();
            win3.Owner = this;
            win3.WindowStartupLocation = WindowStartupLocation.Manual;
            win3.Topmost = true;
            win3.Loaded += (s, e) =>
            {
                // 저장된 위치가 없을 때만 기본 위치 설정
                string window3PositionFile = System.IO.Path.Combine(AppDataFolder, "window3_position.json");
                if (!File.Exists(window3PositionFile))
                {
                    win3.Left = this.Left;
                    win3.Top = this.Top + this.Height - win3.Height;
                }
                // 저장된 위치가 있으면 생성자의 RestorePosition()이 이미 처리함
                win3.Topmost = true;
            };

            bool isDragging = false;
            System.Windows.Point dragStartPoint = new System.Windows.Point();
            System.Windows.Point windowStartPoint = new System.Windows.Point();
            bool moved = false;

            win3.MouseLeftButtonDown += (s, e) =>
            {
                isDragging = true;
                moved = false;
                dragStartPoint = e.GetPosition(null);
                windowStartPoint = new System.Windows.Point(win3.Left, win3.Top);
                win3.CaptureMouse();
            };
            win3.MouseMove += (s, e) =>
            {
                if (isDragging)
                {
                    System.Windows.Point current = e.GetPosition(null);
                    System.Windows.Vector diff = current - dragStartPoint;
                    if (diff.Length > 2)
                    {
                        moved = true;
                        win3.Left = windowStartPoint.X + diff.X;
                        win3.Top = windowStartPoint.Y + diff.Y;
                    }
                }
            };
            win3.MouseLeftButtonUp += (s, e) =>
            {
                win3.ReleaseMouseCapture();
                if (!moved)
                {
                    foreach (Window win in System.Windows.Application.Current.Windows)
                    {
                        if (win is Window2 w2 && w2.IsVisible)
                            w2.Hide();
                    }
                    this.Visibility = Visibility.Visible;
                    this.Activate();
                    win3.Hide(); // Hide instead of Close to allow reuse
                }
                isDragging = false;
            };

            win3.ShowInTaskbar = false;
            win3.Closed += (s, e) =>
            {
                if (this.Visibility != Visibility.Visible)
                {
                    System.Windows.Application.Current.Shutdown();
                }
            };
            this.Visibility = Visibility.Hidden;
            win3.Show();
            win3.Topmost = true; // Show() 후에도 보장
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            double targetLeft = Left;
            double targetTop = Top;

            if (!IsPositionVisible(targetLeft, targetTop, Width, Height))
            {
                targetLeft = (SystemParameters.WorkArea.Width - Width) / 2 + SystemParameters.WorkArea.Left;
                targetTop = (SystemParameters.WorkArea.Height - Height) / 2 + SystemParameters.WorkArea.Top;
                Left = targetLeft;
                Top = targetTop;
            }

            // 저장된 위치 그대로 사용 (애니메이션 제거)
            Left = targetLeft;
            Top = targetTop;
        }

        private void RestoreWindowPosition()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var pos = JsonSerializer.Deserialize<WindowPosition>(json);

                    if (pos != null)
                    {
                        Left = pos.Left;
                        Top = pos.Top;
                        return;
                    }
                }
            }
            catch { /* 복원 실패 시 무시 */ }
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        private bool IsPositionVisible(double left, double top, double width, double height)
        {
            foreach (var screen in Forms.Screen.AllScreens)
            {
                var rect = screen.WorkingArea;
                if (left >= rect.Left && top >= rect.Top &&
                    left + width <= rect.Right && top + height <= rect.Bottom)
                    return true;
            }
            return false;
        }

        private void SaveWindowPosition()
        {
            var pos = new WindowPosition { Left = Left, Top = Top };
            var json = JsonSerializer.Serialize(pos);
            File.WriteAllText(SettingsFile, json);
        }

        private class WindowPosition
        {
            public double Left { get; set; }
            public double Top { get; set; }
        }

        // TitleBar_MouseLeftButtonDown 이벤트 핸들러 추가
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                    return;
                }
                try
                {
                    this.DragMove();
                }
                catch
                {
                    // DragMove 예외 무시 (이미 드래그 중인 경우 등)
                }
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (RootBorder == null) return;
            if (WindowState == WindowState.Maximized)
            {
                RootBorder.CornerRadius = new CornerRadius(0);
                상태창.CornerRadius = new CornerRadius(0);
                RootBorder.BorderThickness = new Thickness(0);
            }
            else
            {
                RootBorder.CornerRadius = new CornerRadius(16);
                상태창.CornerRadius = new CornerRadius(16, 16, 0, 0);
                RootBorder.BorderThickness = new Thickness(1);
            }
        }

        // 삭제 확인 공용 메서드 추가
        private void DeleteButtonWithConfirm(Canvas canvas, System.Windows.Controls.Button btn, ButtonMeta meta)
        {
            var dlg = new Window
            {
                Title = "버튼 삭제 확인",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight
            };
            MakeBorderless(dlg);
            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock { Text = "버튼을 삭제하시겠습니까?", Margin = new Thickness(0,0,0,12), FontWeight = FontWeights.Bold });
            var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            // make both buttons identical size and padding
            var yesBtn = new System.Windows.Controls.Button { Content = "예", Margin = new Thickness(0,0,8,0), Width = 90, Height = 32, Padding = new Thickness(12, 4, 12, 4) };
            var noBtn = new System.Windows.Controls.Button { Content = "아니오", Width = 90, Height = 32, Padding = new Thickness(12, 4, 12, 4) };
            yesBtn.Click += (s,e)=>{
                dlg.Close();
                canvas.Children.Remove(btn);
                if (meta.LabelBlock != null)
                {
                    canvas.Children.Remove(meta.LabelBlock);
                    meta.LabelBlock = null;
                }
                SaveAllButtonStates();
            };
            noBtn.Click += (s,e)=> dlg.Close();
            panel.Children.Add(yesBtn);
            panel.Children.Add(noBtn);
            stack.Children.Add(panel);
            dlg.Content = stack;
            ApplyDarkTheme(dlg);
            dlg.ShowDialog();
        }

        // 그리드 체크 타이머: Shift 키를 누르고 있으면 그리드 라인 표시
        private void GridCheckTimer_Tick(object? sender, EventArgs e)
        {
            bool shiftPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (shiftPressed && !_isGridVisible)
            {
                ShowGridLines();
                _isGridVisible = true;
            }
            else if (!shiftPressed && _isGridVisible)
            {
                HideGridLines();
                _isGridVisible = false;
            }
        }

        // 가상 그리드 라인 표시
        private void ShowGridLines()
        {
            var canvas = CurrentButtonCanvas;
            if (canvas == null) return;

            var gridBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0x80, 0x80, 0xFF));

            // 세로 그리드 라인
            for (double x = 0; x < canvas.ActualWidth; x += GridSize)
            {
                var line = new Line
                {
                    X1 = x, Y1 = 0,
                    X2 = x, Y2 = canvas.ActualHeight,
                    Stroke = gridBrush,
                    StrokeThickness = 1,
                    Tag = "GridLine",
                    IsHitTestVisible = false
                };
                canvas.Children.Add(line);
            }

            // 가로 그리드 라인
            for (double y = 0; y < canvas.ActualHeight; y += GridSize)
            {
                var line = new Line
                {
                    X1 = 0, Y1 = y,
                    X2 = canvas.ActualWidth, Y2 = y,
                    Stroke = gridBrush,
                    StrokeThickness = 1,
                    Tag = "GridLine",
                    IsHitTestVisible = false
                };
                canvas.Children.Add(line);
            }
        }

        // 그리드 라인 숨기기
        private void HideGridLines()
        {
            var canvas = CurrentButtonCanvas;
            if (canvas == null) return;

            var linesToRemove = canvas.Children.OfType<Line>()
                .Where(l => l.Tag?.ToString() == "GridLine")
                .ToList();

            foreach (var line in linesToRemove)
            {
                canvas.Children.Remove(line);
            }
        }

        // 크기 조절 방향 열거형
        private enum ResizeDirection
        {
            None,
            Left,
            Right,
            Top,
            Bottom,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        // 버튼에 드래그 앤 드롭 핸들러 연결
        private void AttachDragHandlers(System.Windows.Controls.Button btn, Canvas canvas, ButtonMeta meta)
        {
            bool isDragging = false;
            bool isResizing = false;
            bool shiftWasPressedOnStart = false;
            System.Windows.Point startPoint = new System.Windows.Point();
            System.Windows.Point originalPosition = new System.Windows.Point();
            double originalWidth = 0;
            double originalHeight = 0;
            ResizeDirection resizeDirection = ResizeDirection.None;
            const double ResizeGripSize = 8.0; // 크기 조절 영역 크기

            // 마우스 위치에 따른 크기 조절 방향 결정
            ResizeDirection GetResizeDirection(System.Windows.Point mousePos)
            {
                double width = btn.ActualWidth > 0 ? btn.ActualWidth : btn.Width;
                double height = btn.ActualHeight > 0 ? btn.ActualHeight : btn.Height;

                bool isLeft = mousePos.X <= ResizeGripSize;
                bool isRight = mousePos.X >= width - ResizeGripSize;
                bool isTop = mousePos.Y <= ResizeGripSize;
                bool isBottom = mousePos.Y >= height - ResizeGripSize;

                if (isTop && isLeft) return ResizeDirection.TopLeft;
                if (isTop && isRight) return ResizeDirection.TopRight;
                if (isBottom && isLeft) return ResizeDirection.BottomLeft;
                if (isBottom && isRight) return ResizeDirection.BottomRight;
                if (isTop) return ResizeDirection.Top;
                if (isBottom) return ResizeDirection.Bottom;
                if (isLeft) return ResizeDirection.Left;
                if (isRight) return ResizeDirection.Right;

                return ResizeDirection.None;
            }

            // 방향에 따른 커서 반환
            System.Windows.Input.Cursor GetCursorForDirection(ResizeDirection direction)
            {
                return direction switch
                {
                    ResizeDirection.Left or ResizeDirection.Right => System.Windows.Input.Cursors.SizeWE,
                    ResizeDirection.Top or ResizeDirection.Bottom => System.Windows.Input.Cursors.SizeNS,
                    ResizeDirection.TopLeft or ResizeDirection.BottomRight => System.Windows.Input.Cursors.SizeNWSE,
                    ResizeDirection.TopRight or ResizeDirection.BottomLeft => System.Windows.Input.Cursors.SizeNESW,
                    _ => System.Windows.Input.Cursors.Arrow
                };
            }

            // MouseMove 핸들러: Shift 키가 눌렸을 때 커서 변경
            btn.MouseMove += (s, e) =>
            {
                if (!btn.IsMouseCaptured && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    System.Windows.Point mousePos = e.GetPosition(btn);
                    ResizeDirection direction = GetResizeDirection(mousePos);
                    btn.Cursor = GetCursorForDirection(direction);
                }
                else if (!btn.IsMouseCaptured)
                {
                    btn.Cursor = System.Windows.Input.Cursors.Arrow;
                }
            };

            btn.PreviewMouseLeftButtonDown += (s, e) =>
            {
                // Shift 키를 누른 상태인지 체크
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    shiftWasPressedOnStart = true;
                    startPoint = e.GetPosition(canvas);
                    originalPosition = new System.Windows.Point(Canvas.GetLeft(btn), Canvas.GetTop(btn));
                    originalWidth = btn.ActualWidth > 0 ? btn.ActualWidth : btn.Width;
                    originalHeight = btn.ActualHeight > 0 ? btn.ActualHeight : btn.Height;

                    // 크기 조절 방향 결정
                    System.Windows.Point mousePosInBtn = e.GetPosition(btn);
                    resizeDirection = GetResizeDirection(mousePosInBtn);

                    if (resizeDirection != ResizeDirection.None)
                    {
                        isResizing = true;
                        isDragging = false;
                    }
                    else
                    {
                        isResizing = false;
                        isDragging = false;
                        resizeDirection = ResizeDirection.None;
                    }

                    btn.CaptureMouse();
                    e.Handled = true; // Shift 키가 눌렸을 때는 이벤트 전파 차단
                }
                else
                {
                    shiftWasPressedOnStart = false;
                }
            };

            btn.PreviewMouseMove += (s, e) =>
            {
                if (btn.IsMouseCaptured && shiftWasPressedOnStart)
                {
                    System.Windows.Point currentPoint = e.GetPosition(canvas);
                    System.Windows.Vector diff = currentPoint - startPoint;

                    // 일정 거리 이상 움직이면 모드 활성화
                    if (!isDragging && !isResizing && diff.Length > DragThreshold)
                    {
                        if (resizeDirection != ResizeDirection.None)
                        {
                            isResizing = true;
                        }
                        else
                        {
                            isDragging = true;
                        }
                    }

                    if (isResizing && resizeDirection != ResizeDirection.None)
                    {
                        // 크기 조절
                        double newWidth = originalWidth;
                        double newHeight = originalHeight;
                        double newX = originalPosition.X;
                        double newY = originalPosition.Y;

                        // 방향에 따른 크기 및 위치 조정
                        switch (resizeDirection)
                        {
                            case ResizeDirection.Right:
                                newWidth = originalWidth + diff.X;
                                break;
                            case ResizeDirection.Left:
                                newWidth = originalWidth - diff.X;
                                newX = originalPosition.X + diff.X;
                                break;
                            case ResizeDirection.Bottom:
                                newHeight = originalHeight + diff.Y;
                                break;
                            case ResizeDirection.Top:
                                newHeight = originalHeight - diff.Y;
                                newY = originalPosition.Y + diff.Y;
                                break;
                            case ResizeDirection.BottomRight:
                                newWidth = originalWidth + diff.X;
                                newHeight = originalHeight + diff.Y;
                                break;
                            case ResizeDirection.BottomLeft:
                                newWidth = originalWidth - diff.X;
                                newHeight = originalHeight + diff.Y;
                                newX = originalPosition.X + diff.X;
                                break;
                            case ResizeDirection.TopRight:
                                newWidth = originalWidth + diff.X;
                                newHeight = originalHeight - diff.Y;
                                newY = originalPosition.Y + diff.Y;
                                break;
                            case ResizeDirection.TopLeft:
                                newWidth = originalWidth - diff.X;
                                newHeight = originalHeight - diff.Y;
                                newX = originalPosition.X + diff.X;
                                newY = originalPosition.Y + diff.Y;
                                break;
                        }

                        // 그리드에 스냅
                        newWidth = Math.Round(newWidth / GridSize) * GridSize;
                        newHeight = Math.Round(newHeight / GridSize) * GridSize;

                        // 최소 크기 제한
                        double minSize = 30;
                        if (newWidth < minSize)
                        {
                            newWidth = minSize;
                            // 왼쪽 방향 크기 조절 시 위치 보정
                            if (resizeDirection == ResizeDirection.Left || 
                                resizeDirection == ResizeDirection.TopLeft || 
                                resizeDirection == ResizeDirection.BottomLeft)
                            {
                                newX = originalPosition.X + originalWidth - minSize;
                            }
                        }
                        if (newHeight < minSize)
                        {
                            newHeight = minSize;
                            // 위쪽 방향 크기 조절 시 위치 보정
                            if (resizeDirection == ResizeDirection.Top || 
                                resizeDirection == ResizeDirection.TopLeft || 
                                resizeDirection == ResizeDirection.TopRight)
                            {
                                newY = originalPosition.Y + originalHeight - minSize;
                            }
                        }

                        // 크기 적용
                        btn.Width = newWidth;
                        btn.Height = newHeight;

                        // 위치 적용 (왼쪽/위쪽 방향 크기 조절 시)
                        if (resizeDirection == ResizeDirection.Left || 
                            resizeDirection == ResizeDirection.Top ||
                            resizeDirection == ResizeDirection.TopLeft || 
                            resizeDirection == ResizeDirection.TopRight || 
                            resizeDirection == ResizeDirection.BottomLeft)
                        {
                            // 그리드에 스냅
                            newX = Math.Round(newX / GridSize) * GridSize;
                            newY = Math.Round(newY / GridSize) * GridSize;

                            Canvas.SetLeft(btn, newX);
                            Canvas.SetTop(btn, newY);
                        }

                        // 메타 데이터 업데이트
                        meta.Width = newWidth;
                        meta.Height = newHeight;

                        // 라벨이 있으면 함께 업데이트
                        if (!meta.LabelInside && meta.LabelBlock != null)
                        {
                            EnsureOrUpdateButtonLabel(canvas, btn, meta);
                        }

                        e.Handled = true;
                    }
                    else if (isDragging)
                    {
                        // 위치 이동
                        double newX = originalPosition.X + diff.X;
                        double newY = originalPosition.Y + diff.Y;

                        // 그리드에 스냅 (Shift 드래그는 항상 그리드 정렬)
                        newX = Math.Round(newX / GridSize) * GridSize;
                        newY = Math.Round(newY / GridSize) * GridSize;

                        // Canvas 범위 내로 제한 (실제 크기 사용)
                        double maxX = canvas.ActualWidth > 0 ? canvas.ActualWidth - btn.ActualWidth : 1000;
                        double maxY = canvas.ActualHeight > 0 ? canvas.ActualHeight - btn.ActualHeight : 600;
                        newX = Math.Max(0, Math.Min(newX, maxX));
                        newY = Math.Max(0, Math.Min(newY, maxY));

                        Canvas.SetLeft(btn, newX);
                        Canvas.SetTop(btn, newY);

                        // 라벨이 있으면 함께 이동
                        if (!meta.LabelInside && meta.LabelBlock != null)
                        {
                            EnsureOrUpdateButtonLabel(canvas, btn, meta);
                        }

                        e.Handled = true;
                    }
                }
            };

            btn.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (btn.IsMouseCaptured && shiftWasPressedOnStart)
                {
                    btn.ReleaseMouseCapture();

                    if (isDragging || isResizing)
                    {
                        // 위치/크기 저장
                        SaveAllButtonStates();
                        e.Handled = true;
                    }

                    isDragging = false;
                    isResizing = false;
                    shiftWasPressedOnStart = false;
                    resizeDirection = ResizeDirection.None;
                    btn.Cursor = System.Windows.Input.Cursors.Arrow;
                }
            };
        }

        // 기존 버튼 이벤트 핸들러 예시
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RenameTabItem_Click(object sender, RoutedEventArgs e)
        {
            // ContextMenu의 PlacementTarget을 통해 TabItem 가져오기
            if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
            {
                if (contextMenu.PlacementTarget is TabItem tabItem)
                {
                    // 이름 수정 다이얼로그 표시
                    var dlg = new Window
                    {
                        Title = "탭 이름 수정",
                        Owner = this,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        ResizeMode = ResizeMode.NoResize,
                        SizeToContent = SizeToContent.WidthAndHeight,
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 44, 44)),
                        Foreground = System.Windows.Media.Brushes.White,
                        ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None
                    };

                    var stack = new StackPanel { Margin = new Thickness(16), Width = 300 };
                    stack.Children.Add(new TextBlock 
                    { 
                        Text = "새 탭 이름:", 
                        Margin = new Thickness(0, 0, 0, 8), 
                        FontWeight = FontWeights.Bold 
                    });

                    var textBox = new System.Windows.Controls.TextBox
                    {
                        Text = tabItem.Header?.ToString() ?? "",
                        Height = 28,
                        Margin = new Thickness(0, 0, 0, 12),
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60)),
                        Foreground = System.Windows.Media.Brushes.White,
                        BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(90, 90, 90)),
                        Padding = new Thickness(4, 4, 4, 4),
                        VerticalContentAlignment = VerticalAlignment.Center
                    };
                    textBox.SelectAll();
                    stack.Children.Add(textBox);

                    var btnPanel = new StackPanel 
                    { 
                        Orientation = System.Windows.Controls.Orientation.Horizontal, 
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Right 
                    };
                    var okBtn = new System.Windows.Controls.Button 
                    { 
                        Content = "확인", 
                        Width = 80, 
                        Height = 32,
                        Margin = new Thickness(0, 0, 8, 0),
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 80, 80)),
                        BorderBrush = System.Windows.Media.Brushes.Transparent,
                        Foreground = System.Windows.Media.Brushes.White
                    };
                    var cancelBtn = new System.Windows.Controls.Button 
                    { 
                        Content = "취소", 
                        Width = 80,
                        Height = 32,
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 80, 80)),
                        BorderBrush = System.Windows.Media.Brushes.Transparent,
                        Foreground = System.Windows.Media.Brushes.White
                    };

                    okBtn.Click += (s, ev) =>
                    {
                        string newName = textBox.Text.Trim();
                        if (!string.IsNullOrEmpty(newName))
                        {
                            tabItem.Header = newName;
                        }
                        dlg.Close();
                    };
                    cancelBtn.Click += (s, ev) => dlg.Close();

                    // Enter 키로 확인
                    textBox.KeyDown += (s, ev) =>
                    {
                        if (ev.Key == Key.Enter)
                        {
                            string newName = textBox.Text.Trim();
                            if (!string.IsNullOrEmpty(newName))
                            {
                                tabItem.Header = newName;
                            }
                            dlg.Close();
                        }
                        else if (ev.Key == Key.Escape)
                        {
                            dlg.Close();
                        }
                    };

                    btnPanel.Children.Add(okBtn);
                    btnPanel.Children.Add(cancelBtn);
                    stack.Children.Add(btnPanel);

                    dlg.Content = stack;
                    dlg.Loaded += (s, ev) => textBox.Focus();
                    dlg.ShowDialog();
                }
            }
        }

        private void DeleteTabItem_Click(object sender, RoutedEventArgs e)
        {
            // ContextMenu의 PlacementTarget을 통해 TabItem 가져오기
            if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
            {
                if (contextMenu.PlacementTarget is TabItem tabItem)
                {
                    // 마지막 탭인 경우 삭제 불가
                    if (tabControl.Items.Count <= 1)
                    {
                        System.Windows.MessageBox.Show("최소 1개의 탭은 유지되어야 합니다.", "탭 삭제 불가", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // 삭제 확인 다이얼로그
                    var dlg = new Window
                    {
                        Title = "탭 삭제 확인",
                        Owner = this,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        ResizeMode = ResizeMode.NoResize,
                        SizeToContent = SizeToContent.WidthAndHeight,
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 44, 44)),
                        Foreground = System.Windows.Media.Brushes.White,
                        ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None
                    };

                    var stack = new StackPanel { Margin = new Thickness(16), Width = 300 };
                    stack.Children.Add(new TextBlock 
                    { 
                        Text = $"'{tabItem.Header}' 탭을 삭제하시겠습니까?", 
                        Margin = new Thickness(0, 0, 0, 8), 
                        FontWeight = FontWeights.Bold,
                        TextWrapping = TextWrapping.Wrap
                    });
                    stack.Children.Add(new TextBlock 
                    { 
                        Text = "탭 내의 모든 버튼도 함께 삭제됩니다.", 
                        Margin = new Thickness(0, 0, 0, 12), 
                        FontSize = 12,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200))
                    });

                    var btnPanel = new StackPanel 
                    { 
                        Orientation = System.Windows.Controls.Orientation.Horizontal, 
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Right 
                    };
                    var yesBtn = new System.Windows.Controls.Button 
                    { 
                        Content = "예", 
                        Width = 80, 
                        Height = 32,
                        Margin = new Thickness(0, 0, 8, 0),
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 50, 50)),
                        BorderBrush = System.Windows.Media.Brushes.Transparent,
                        Foreground = System.Windows.Media.Brushes.White
                    };
                    var noBtn = new System.Windows.Controls.Button 
                    { 
                        Content = "아니오", 
                        Width = 80,
                        Height = 32,
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 80, 80)),
                        BorderBrush = System.Windows.Media.Brushes.Transparent,
                        Foreground = System.Windows.Media.Brushes.White
                    };

                    yesBtn.Click += (s, ev) =>
                    {
                        // 탭 삭제 전에 다른 탭을 선택
                        int currentIndex = tabControl.Items.IndexOf(tabItem);
                        if (tabItem.IsSelected)
                        {
                            // 삭제할 탭이 선택되어 있으면 다른 탭 선택
                            if (currentIndex > 0)
                                tabControl.SelectedIndex = currentIndex - 1;
                            else if (tabControl.Items.Count > 1)
                                tabControl.SelectedIndex = 1;
                        }

                        // Canvas의 Name을 UnregisterName
                        if (tabItem.Content is Border border && border.Child is Canvas canvas && !string.IsNullOrEmpty(canvas.Name))
                        {
                            try
                            {
                                UnregisterName(canvas.Name);
                            }
                            catch { }
                        }

                        // 탭 삭제
                        tabControl.Items.Remove(tabItem);
                        dlg.Close();
                    };
                    noBtn.Click += (s, ev) => dlg.Close();

                    btnPanel.Children.Add(yesBtn);
                    btnPanel.Children.Add(noBtn);
                    stack.Children.Add(btnPanel);

                    dlg.Content = stack;
                    dlg.ShowDialog();
                }
            }
        }

        private void ShowWindow3_Click(object sender, RoutedEventArgs e)
        {
            // Window2가 열려 있으면 미리 숨기고 Window3을 띄움
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Window2 w2 && w2.IsVisible)
                {
                    w2.Hide();
                }
            }
            var win3 = new Window3();
            win3.Owner = this; // 필요시 부모 지정
            win3.Show();
        }

        private void DynamicButtonBorder_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Border 우클릭 위치 저장 (Border 내부 Canvas 좌표로 변환)
            var canvas = CurrentButtonCanvas;
            if (canvas == null) return;
            _lastBorderRightClickPoint = e.GetPosition(CurrentButtonCanvas);
        }

        private void EnsureOrUpdateButtonLabel(System.Windows.Controls.Canvas canvas, System.Windows.Controls.Button btn, ButtonMeta meta)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(meta.LabelText))
                {
                    if (meta.LabelBlock != null)
                    {
                        if (meta.LabelBlock.Parent is System.Windows.Controls.Panel p)
                            p.Children.Remove(meta.LabelBlock);
                        meta.LabelBlock = null;
                    }
                    return;
                }

                if (meta.LabelBlock == null)
                {
                    meta.LabelBlock = new System.Windows.Controls.TextBlock
                    {
                        Text = meta.LabelText,
                        Foreground = btn.Foreground, // follow button foreground
                        TextAlignment = TextAlignment.Center,
                        Width = btn.Width,
                        Tag = "DynamicLabel"
                    };
                    canvas.Children.Add(meta.LabelBlock);
                }
                else
                {
                    meta.LabelBlock.Text = meta.LabelText;
                    meta.LabelBlock.Width = btn.Width;
                    // keep color synced
                    meta.LabelBlock.Foreground = btn.Foreground;
                    if (meta.LabelBlock.Parent != canvas)
                    {
                        if (meta.LabelBlock.Parent is System.Windows.Controls.Panel oldPanel)
                            oldPanel.Children.Remove(meta.LabelBlock);
                        if (!canvas.Children.Contains(meta.LabelBlock))
                            canvas.Children.Add(meta.LabelBlock);
                    }
                }

                double left = System.Windows.Controls.Canvas.GetLeft(btn);
                double top = System.Windows.Controls.Canvas.GetTop(btn);
                System.Windows.Controls.Canvas.SetLeft(meta.LabelBlock, left);
                System.Windows.Controls.Canvas.SetTop(meta.LabelBlock, top + btn.Height + 2);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("EnsureOrUpdateButtonLabel error: " + ex);
                System.Windows.MessageBox.Show("텍스트 표시 중 오류가 발생했습니다.");
            }
        }

        private static System.Windows.Controls.Image? GetButtonImageControl(System.Windows.Controls.Button btn)
        {
            if (btn.Content is System.Windows.Controls.Image im) return im;
            if (btn.Content is System.Windows.Controls.Grid g)
            {
                foreach (var child in g.Children)
                {
                    if (child is System.Windows.Controls.Image im2) return im2;
                }
            }
            return null;
        }

        // Updated EnsureGridContentWithImage to safely detach existing Image from Button before wrapping in Grid
        private static System.Windows.Controls.Grid EnsureGridContentWithImage(System.Windows.Controls.Button btn, out System.Windows.Controls.Image image)
        {
            var existingImg = GetButtonImageControl(btn);
            if (existingImg == null && btn.Content is System.Windows.Controls.Image loneImg)
            {
                existingImg = loneImg; // capture
            }
            if (existingImg == null)
            {
                existingImg = new System.Windows.Controls.Image { Stretch = System.Windows.Media.Stretch.Uniform, Width = btn.Width * 0.8, Height = btn.Height * 0.8 };
            }
            var grid = btn.Content as System.Windows.Controls.Grid;
            if (grid == null)
            {
                // create new grid layout
                grid = new System.Windows.Controls.Grid();
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                // detach image from Button BEFORE adding as child
                if (btn.Content == existingImg)
                {
                    btn.Content = null;
                }
                System.Windows.Controls.Grid.SetColumn(existingImg, 0);
                System.Windows.Controls.Grid.SetColumnSpan(existingImg, 1);
                existingImg.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                existingImg.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                existingImg.Margin = new Thickness(15, 0, 0, 0); // 좌측 여백 적용 (기존 6)
                grid.Children.Add(existingImg);
                btn.Content = grid; // assign new grid
            }
            else
            {
                // ensure3 columns
                if (grid.ColumnDefinitions.Count < 3)
                {
                    grid.ColumnDefinitions.Clear();
                    grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                }
                if (existingImg.Parent == null)
                {
                    // detach if still button content
                    if (btn.Content == existingImg)
                    {
                        btn.Content = null;
                    }
                    System.Windows.Controls.Grid.SetColumn(existingImg, 0);
                    System.Windows.Controls.Grid.SetColumnSpan(existingImg, 1);
                    existingImg.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                    existingImg.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                    existingImg.Margin = new Thickness(15, 0, 0, 0); // 좌측 여백 적용 (기존 6)
                    grid.Children.Add(existingImg);
                }
                else
                {
                    // enforce placement in column0
                    System.Windows.Controls.Grid.SetColumn(existingImg, 0);
                    System.Windows.Controls.Grid.SetColumnSpan(existingImg, 1);
                    existingImg.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                    existingImg.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                }
            }
            image = existingImg;
            return grid;
        }

        private void EnsureOrUpdateInButtonLabel(System.Windows.Controls.Button btn, ButtonMeta meta)
        {
            var grid = EnsureGridContentWithImage(btn, out var img);
            if (meta.InnerLabelBlock != null)
            {
                grid.Children.Remove(meta.InnerLabelBlock);
                meta.InnerLabelBlock = null;
            }
            if (string.IsNullOrWhiteSpace(meta.LabelText)) return;
            if (grid.ColumnDefinitions.Count < 3)
            {
                grid.ColumnDefinitions.Clear();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            if (Grid.GetColumn(img) != 0)
            {
                Grid.SetColumn(img, 0);
                Grid.SetColumnSpan(img, 1);
                img.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            }
            var t = new System.Windows.Controls.TextBlock
            {
                Text = meta.LabelText,
                Foreground = btn.Foreground,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                IsHitTestVisible = false,
                TextAlignment = TextAlignment.Left
            };
            Grid.SetColumn(t, 1);
            Grid.SetColumnSpan(t, 2);
            t.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            // raise text by 3 pixels
            t.Margin = new Thickness(4, -3, 4, 0);
            t.MaxWidth = btn.Width * 2.0 / 3.0 - 8;
            grid.Children.Add(t);
            meta.InnerLabelBlock = t;
        }
        // Re-added helper: unwrap inner label and restore plain image content
        private void RemoveInnerLabelAndUnwrap(System.Windows.Controls.Button btn, ButtonMeta meta)
        {
            if (meta.InnerLabelBlock == null) return; // nothing to unwrap
            if (btn.Content is Grid g)
            {
                var img = GetButtonImageControl(btn);
                if (img != null)
                {
                    if (img.Parent is System.Windows.Controls.Panel p) p.Children.Remove(img); // detach using WPF Panel
                    btn.Content = img; // set image as sole content
                }
            }
            meta.InnerLabelBlock = null;
        }

        private void RestoreAllButtonStates()
        {
            if (!File.Exists(ButtonStateFile)) return;
            var json = File.ReadAllText(ButtonStateFile);
            var list = JsonSerializer.Deserialize<List<ButtonState>>(json);
            if (list == null) return;

            foreach (var state in list)
            {
                var canvas = GetCanvasByIndex(state.CanvasIndex);
                if (canvas == null) continue; // Canvas가 없으면 스킵

                var btn = new System.Windows.Controls.Button
                {
                    Width = state.Width,
                    Height = state.Height,
                    Content = state.Content ?? "",
                    ContextMenu = new System.Windows.Controls.ContextMenu()
                };
                btn.Style = FindResource("DynamicButtonStyle") as Style;
                // Make border transparent for dynamic buttons
                btn.BorderBrush = System.Windows.Media.Brushes.Transparent;
                btn.BorderThickness = new Thickness(0);

                // restore font settings (added)
                try
                {
                    if (!string.IsNullOrWhiteSpace(state.FontFamily))
                        btn.FontFamily = new System.Windows.Media.FontFamily(state.FontFamily);
                    if (state.FontSize > 0)
                        btn.FontSize = state.FontSize;
                    if (!string.IsNullOrWhiteSpace(state.FontWeightName))
                    {
                        var conv = new System.ComponentModel.TypeConverter();
                        try
                        {
                            var fwConv = new System.Windows.FontWeightConverter();
                            var fw = (System.Windows.FontWeight)fwConv.ConvertFromString(state.FontWeightName)!;
                            btn.FontWeight = fw;
                        }
                        catch { }
                    }
                    btn.FontStyle = state.Italic ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;
                    if (!string.IsNullOrWhiteSpace(state.FontColor))
                    {
                        try
                        {
                            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(state.FontColor)!;
                            btn.Foreground = new System.Windows.Media.SolidColorBrush(c);
                        }
                        catch { }
                    }
                    // BgColor: null = default(theme), "transparent" = none, hex = custom
                    // BackgroundTransparent kept for legacy JSON
                    if (state.BgColor == "transparent" || (string.IsNullOrEmpty(state.BgColor) && state.BackgroundTransparent))
                    {
                        btn.Background = System.Windows.Media.Brushes.Transparent;
                    }
                    else if (!string.IsNullOrWhiteSpace(state.BgColor))
                    {
                        try
                        {
                            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(state.BgColor)!;
                            btn.Background = new System.Windows.Media.SolidColorBrush(c);
                        }
                        catch { }
                    }
                    // CustomFontFamily: null = default(theme)
                    if (!string.IsNullOrWhiteSpace(state.CustomFontFamily))
                    {
                        try { btn.FontFamily = new System.Windows.Media.FontFamily(state.CustomFontFamily); } catch { }
                    }
                }
                catch { }

                System.Windows.Controls.Canvas.SetLeft(btn, state.X);
                System.Windows.Controls.Canvas.SetTop(btn, state.Y);

                var meta = new ButtonMeta 
                { 
                    Path = state.Path, 
                    IsFolder = state.IsFolder, 
                    LabelText = state.LabelText, 
                    LabelInside = state.LabelInside,
                    Width = state.Width,
                    Height = state.Height
                };
                btn.Tag = meta;
                btn.PreviewMouseLeftButtonDown += Btn_PreviewMouseLeftButtonDown;

                var img = GetButtonImageControl(btn);
                if (!string.IsNullOrEmpty(state.ImagePath) && File.Exists(state.ImagePath))
                {
                    try
                    {
                        var imgW = state.ImageWidth > 0 ? state.ImageWidth : btn.Width * 0.8;
                        var imgH = state.ImageHeight > 0 ? state.ImageHeight : btn.Height * 0.8;
                        var restoredImg = new System.Windows.Controls.Image
                        {
                            Source = TryLoadImageSource(state.ImagePath)!,
                            Stretch = System.Windows.Media.Stretch.Uniform,
                            Width = imgW,
                            Height = imgH
                        };
                        if (!string.IsNullOrEmpty(state.ImageHAlign) && Enum.TryParse<System.Windows.HorizontalAlignment>(state.ImageHAlign, out var ha)) restoredImg.HorizontalAlignment = ha; else restoredImg.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                        if (!string.IsNullOrEmpty(state.ImageVAlign) && Enum.TryParse<System.Windows.VerticalAlignment>(state.ImageVAlign, out var va)) restoredImg.VerticalAlignment = va; else restoredImg.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                        btn.Content = restoredImg;
                        if (restoredImg.HorizontalAlignment == System.Windows.HorizontalAlignment.Left || restoredImg.HorizontalAlignment == System.Windows.HorizontalAlignment.Right)
                        {
                            double sideMargin = Math.Max(0, (btn.Height - restoredImg.Height) / 2.0);
                            if (restoredImg.HorizontalAlignment == System.Windows.HorizontalAlignment.Left)
                                restoredImg.Margin = new Thickness(sideMargin, sideMargin, 0, sideMargin);
                            else
                                restoredImg.Margin = new Thickness(0, sideMargin, sideMargin, sideMargin);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Restore image error: " + ex);
                    }
                }

                if (!string.IsNullOrWhiteSpace(meta.LabelText))
                {
                    if (meta.LabelInside) EnsureOrUpdateInButtonLabel(btn, meta); else EnsureOrUpdateButtonLabel(canvas, btn, meta);
                }

                btn.Click += (s, ev) =>
                {
                    if (string.IsNullOrEmpty(meta.Path)) { System.Windows.MessageBox.Show("경로가 설정되지 않았습니다."); return; }
                    try
                    {
                        if (Uri.IsWellFormedUriString(meta.Path, UriKind.Absolute))
                        {
                            Process.Start(new ProcessStartInfo { FileName = meta.Path, UseShellExecute = true });
                        }
                        else if (meta.IsFolder && Directory.Exists(meta.Path))
                            Process.Start(new ProcessStartInfo { FileName = meta.Path, UseShellExecute = true });
                        else if (!meta.IsFolder && File.Exists(meta.Path))
                            Process.Start(new ProcessStartInfo { FileName = meta.Path, UseShellExecute = true });
                        else
                            System.Windows.MessageBox.Show("경로가 올바르지 않습니다.");
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"경로를 열 수 없습니다.\n{ex.Message}");
                    }
                };

                var delMenu = new System.Windows.Controls.MenuItem { Header = "버튼삭제" };
                delMenu.Click += (s, ev) =>
                {
                    DeleteButtonWithConfirm(canvas, btn, meta);
                };
                var editMenu = new System.Windows.Controls.MenuItem { Header = "버튼수정" };
                editMenu.Click += (s, ev) =>
                {
                    var dlg = new System.Windows.Window
                    {
                        Title = "버튼 수정",
                        WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                        Owner = this,
                        ResizeMode = System.Windows.ResizeMode.NoResize,
                        Content = null,
                        SizeToContent = SizeToContent.WidthAndHeight
                    };
                    MakeBorderless(dlg);

                    var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
                    var sizeBtn = new System.Windows.Controls.Button { Content = "버튼이미지", Margin = new Thickness(0, 0, 0, 8) };
                    var pathBtn = new System.Windows.Controls.Button { Content = "경로 설정", Margin = new Thickness(0, 0, 0, 8) };
                    var textBtn = new System.Windows.Controls.Button { Content = "버튼텍스트", Margin = new Thickness(0, 0, 0, 8) };
                    var closeBtn = new System.Windows.Controls.Button { Content = "닫기" };
                    sizeBtn.Click += (sss, eee) => { dlg.Close(); ShowSizeAdjustDialog(btn, canvas, meta); };
                    pathBtn.Click += (sss, eee) =>
                    {
                        dlg.Close();
                        var selectDlg = new System.Windows.Window
                        {
                            Title = "경로 종류 선택",
                            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                            Owner = this,
                            ResizeMode = System.Windows.ResizeMode.NoResize,
                            SizeToContent = SizeToContent.WidthAndHeight
                        };
                        MakeBorderless(selectDlg);

                        var selectStack = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
                        var fileBtn = new System.Windows.Controls.Button { Content = "파일 경로 설정", Margin = new Thickness(0, 0, 0, 8) };
                        var urlBtn = new System.Windows.Controls.Button { Content = "URL 경로 설정", Margin = new Thickness(0, 0, 0, 8) };
                        var folderBtn = new System.Windows.Controls.Button { Content = "폴더 경로 설정", Margin = new Thickness(0, 0, 0, 8) };
                        var cancelBtn = new System.Windows.Controls.Button { Content = "취소" };
                        fileBtn.Click += (fff, ffe) =>
                        {
                            selectDlg.Close();
                            var fd = new Microsoft.Win32.OpenFileDialog { Title = "파일 선택", CheckFileExists = true };
                            if (fd.ShowDialog() == true)
                            {
                                meta.Path = fd.FileName; meta.IsFolder = false; SaveAllButtonStates();
                            }
                        };
                        urlBtn.Click += (fff, ffe) =>
                        {
                            selectDlg.Close();
                            var urlDlg = new System.Windows.Window
                            {
                                Title = "URL 입력",
                                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                                Owner = this,
                                ResizeMode = System.Windows.ResizeMode.NoResize,
                                SizeToContent = SizeToContent.WidthAndHeight
                            };
                            MakeBorderless(urlDlg);
                            var stack2 = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
                            stack2.Children.Add(new System.Windows.Controls.TextBlock { Text = "URL: (예: https://www.naver.com)", Margin = new Thickness(0,0,0,6) });
                            var tb = new System.Windows.Controls.TextBox { Text = meta.Path ?? string.Empty, Margin = new Thickness(0,8,0,8), Width = 360 };
                            var ok = new System.Windows.Controls.Button { Content = "적용", Margin = new Thickness(0, 0, 8, 0) };
                            var cancel2 = new System.Windows.Controls.Button { Content = "취소" };
                            var btns = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                            ok.Click += (s2, e2) =>
                            {
                                var text = tb.Text?.Trim();
                                if (string.IsNullOrEmpty(text)) { System.Windows.MessageBox.Show("URL을 입력하세요."); return; }
                                if (!Uri.IsWellFormedUriString(text, UriKind.Absolute)) { System.Windows.MessageBox.Show("올바른 URL 형식이 아닙니다. 예: https://www.naver.com"); return; }
                                meta.Path = text; meta.IsFolder = false; SaveAllButtonStates(); urlDlg.Close();
                            };
                            cancel2.Click += (s2, e2) => urlDlg.Close();
                            btns.Children.Add(ok); btns.Children.Add(cancel2);
                            stack2.Children.Add(tb);
                            stack2.Children.Add(btns);
                            urlDlg.Content = stack2; ApplyDarkTheme(urlDlg); urlDlg.ShowDialog();
                        };
                        folderBtn.Click += (fff, ffe) =>
                        {
                            selectDlg.Close();
                            var folderDialog = new Forms.FolderBrowserDialog();
                            if (folderDialog.ShowDialog() == Forms.DialogResult.OK)
                            { meta.Path = folderDialog.SelectedPath; meta.IsFolder = true; SaveAllButtonStates(); }
                        };
                        cancelBtn.Click += (s, e) => selectDlg.Close();
                        selectStack.Children.Add(fileBtn); selectStack.Children.Add(urlBtn); selectStack.Children.Add(folderBtn); selectStack.Children.Add(cancelBtn);
                        selectDlg.Content = selectStack; ApplyDarkTheme(selectDlg); selectDlg.ShowDialog();
                    };
                    textBtn.Click += (sss, eee) => { dlg.Close(); ShowTextInputDialog(btn, canvas, meta); };
                    closeBtn.Click += (sss, eee) => dlg.Close();
                    stack.Children.Add(sizeBtn); stack.Children.Add(pathBtn); stack.Children.Add(textBtn); stack.Children.Add(closeBtn);
                    dlg.Content = stack; ApplyDarkTheme(dlg); dlg.ShowDialog();
                };

                // MISSING earlier: actually attach menu items and add button to canvas
                btn.ContextMenu.Items.Add(editMenu);
                btn.ContextMenu.Items.Add(delMenu);
                canvas.Children.Add(btn);

                // 드래그 앤 드롭 핸들러를 먼저 연결 (Shift 키 체크)
                AttachDragHandlers(btn, canvas, meta);

                // Ripple click effect handler (Shift가 아닐 때만 실행)
                btn.PreviewMouseLeftButtonDown += Btn_PreviewMouseLeftButtonDown;

                SaveAllButtonStates();
            }
        }
        // ...existing code...
        // 아래부터 누락된 메서드 복구
        // Harden SaveAllButtonStates with try/catch
        private void SaveAllButtonStates()
        {
            try
            {
                var list = new List<ButtonState>();
                // 동적으로 생성된 탭도 포함하도록 tabControl.Items.Count 사용
                int tabCount = tabControl?.Items.Count ?? 0;
                for (int i = 0; i < tabCount; i++)
                {
                    var canvas = GetCanvasByIndex(i);
                    if (canvas == null) continue; // Canvas가 없으면 스킵

                    foreach (UIElement child in canvas.Children)
                    {
                        if (child is System.Windows.Controls.Button btn)
                        {
                            var meta = btn.Tag as ButtonMeta;
                            var img = GetButtonImageControl(btn);
                            var state = new ButtonState
                            {
                                X = System.Windows.Controls.Canvas.GetLeft(btn),
                                Y = System.Windows.Controls.Canvas.GetTop(btn),
                                Width = btn.Width,
                                Height = btn.Height,
                                Content = btn.Content is string str ? str : null,
                                ImagePath = img != null && img.Source is System.Windows.Media.Imaging.BitmapImage bmp ? bmp.UriSource.LocalPath : null,
                                CanvasIndex = i,
                                Path = meta?.Path,
                                IsFolder = meta?.IsFolder ?? false,
                                LabelText = meta?.LabelText,
                                ImageWidth = img?.Width ?? 0,
                                ImageHeight = img?.Height ?? 0,
                                ImageHAlign = img != null ? img.HorizontalAlignment.ToString() : null,
                                ImageVAlign = img != null ? img.VerticalAlignment.ToString() : null,
                                LabelInside = meta?.LabelInside ?? false,
                                FontFamily = btn.FontFamily?.Source,
                                FontSize = btn.FontSize,
                                FontWeightName = btn.FontWeight.ToString(),
                                Italic = btn.FontStyle == System.Windows.FontStyles.Italic,
                                FontColor = btn.ReadLocalValue(System.Windows.Controls.Control.ForegroundProperty) != DependencyProperty.UnsetValue
                                    ? (btn.Foreground as System.Windows.Media.SolidColorBrush)?.Color.ToString() : null,
                                BgColor = btn.ReadLocalValue(System.Windows.Controls.Control.BackgroundProperty) != DependencyProperty.UnsetValue
                                    ? ((btn.Background as System.Windows.Media.SolidColorBrush)?.Color.A == 0 ? "transparent"
                                        : (btn.Background as System.Windows.Media.SolidColorBrush)?.Color.ToString()) : null,
                                CustomFontFamily = btn.ReadLocalValue(System.Windows.Controls.Control.FontFamilyProperty) != DependencyProperty.UnsetValue
                                    ? btn.FontFamily?.Source : null,
                                BackgroundTransparent = false
                            };
                            list.Add(state);
                        }
                    }
                }
                File.WriteAllText(ButtonStateFile, JsonSerializer.Serialize(list));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SaveAllButtonStates error: " + ex);
            }
        }

        private void ShowTextInputDialog(System.Windows.Controls.Button targetBtn, System.Windows.Controls.Canvas canvas, ButtonMeta meta)
        {
            var textDlg = new System.Windows.Window
            {
                Title = "텍스트 입력",
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight
            };
            MakeBorderless(textDlg);

            var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            var label = new System.Windows.Controls.TextBlock { Text = "텍스트:" };
            var box = new System.Windows.Controls.TextBox { Text = meta.LabelText ?? string.Empty, Margin = new Thickness(0, 8, 0, 8) };
            var placeLabel = new System.Windows.Controls.TextBlock { Text = "표시 위치:" };
            var posWrap = new System.Windows.Controls.WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
            string[] pos = { "버튼 밑", "이미지 오른쪽" };
            string current = meta.LabelInside ? "이미지 오른쪽" : "버튼 밑";
            var buttons = new List<System.Windows.Controls.Button>();
            var normalBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70));
            var selectedBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(110, 110, 110));
            void UpdateStyles()
            {
                foreach (var b in buttons)
                {
                    bool sel = (string)b.Content == current;
                    b.Background = sel ? selectedBrush : normalBrush;
                    b.BorderThickness = sel ? new Thickness(2) : new Thickness(0);
                    b.BorderBrush = sel ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 160, 160)) : null;
                }
            }
            foreach (var p in pos)
            {
                var b = new System.Windows.Controls.Button { Content = p, Margin = new Thickness(0, 0, 8, 0), MinWidth = 80, Padding = new Thickness(8, 4, 8, 4) };
                b.Click += (s, e) => { current = p; UpdateStyles(); };
                buttons.Add(b); posWrap.Children.Add(b);
            }
            UpdateStyles();

            var okBtn = new System.Windows.Controls.Button { Content = "적용", Margin = new Thickness(0, 8, 0, 0) };
            var cancelBtn = new System.Windows.Controls.Button { Content = "취소", Margin = new Thickness(0, 8, 0, 0) };
            okBtn.HorizontalAlignment = cancelBtn.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            okBtn.MinWidth = cancelBtn.MinWidth = 90;
            okBtn.Padding = cancelBtn.Padding = new Thickness(12, 4, 12, 4);
            okBtn.HorizontalAlignment = cancelBtn.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            textDlg.Loaded += (s, e) => StretchVerticalButtons(stack, 0, okBtn, cancelBtn);

            okBtn.Click += (s, e) =>
            {
                try
                {
                    meta.LabelText = box.Text;
                    bool inside = current == "이미지 오른쪽";
                    meta.LabelInside = inside;
                    if (inside)
                    {
                        if (meta.LabelBlock != null && canvas.Children.Contains(meta.LabelBlock))
                        {
                            canvas.Children.Remove(meta.LabelBlock);
                            meta.LabelBlock = null;
                        }
                        EnsureOrUpdateInButtonLabel(targetBtn, meta);
                    }
                    else
                    {
                        RemoveInnerLabelAndUnwrap(targetBtn, meta);
                        EnsureOrUpdateButtonLabel(canvas, targetBtn, meta);
                    }
                    SaveAllButtonStates();
                    textDlg.Close();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Text dialog apply error: " + ex);
                    System.Windows.MessageBox.Show("텍스트 적용 중 오류가 발생했습니다.");
                }
            };
            cancelBtn.Click += (s, e) => textDlg.Close();

            stack.Children.Add(label);
            stack.Children.Add(box);
            stack.Children.Add(placeLabel);
            stack.Children.Add(posWrap);
            stack.Children.Add(okBtn);
            stack.Children.Add(cancelBtn);

            textDlg.Content = stack;
            ApplyDarkTheme(textDlg);
            textDlg.ShowDialog();
        }

        private void MakeBorderless(Window w)
        {
            w.WindowStyle = WindowStyle.None;
            w.ResizeMode = ResizeMode.NoResize;
            w.ShowInTaskbar = false;
            var res = System.Windows.Application.Current.Resources;
            w.Background = (res["WindowBackgroundBrush"] as System.Windows.Media.Brush)
                           ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 44, 44));
            w.Foreground = (res["ForegroundBrush"] as System.Windows.Media.Brush)
                           ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
            w.BorderThickness = new Thickness(0);
        }

        private sealed class ButtonMeta
        {
            public string? Path { get; set; }
            public bool IsFolder { get; set; }
            public string? LabelText { get; set; }
            public System.Windows.Controls.TextBlock? LabelBlock { get; set; }
            public bool LabelInside { get; set; }
            public System.Windows.Controls.TextBlock? InnerLabelBlock { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
        }

        private sealed class ChartMeta
        {
            public string ChartType { get; set; } = "Line"; // Line, Bar, Pie, Gauge
            public string DataSource { get; set; } = "Static"; // Static, Json, Csv, Api, Db
            public string? DataPath { get; set; } // JSON/CSV 파일 경로 또는 API URL
            public List<double> StaticData { get; set; } = new List<double>();
            public List<string> Labels { get; set; } = new List<string>();
            public double Width { get; set; } = 300;
            public double Height { get; set; } = 200;
            public string Title { get; set; } = "";
            public int RefreshInterval { get; set; } = 0; // 0이면 실시간 업데이트 없음, 초 단위
            public DispatcherTimer? RefreshTimer { get; set; }
            // DB 전용 필드
            public string? DbStoreName { get; set; }
            public DateTime? DbStartDate { get; set; }
            public DateTime? DbEndDate { get; set; }
            public string DbValueColumn { get; set; } = "총매출액"; // 총매출액, 총수량, 판매수량, 서비스수량
            public string DbGroupBy { get; set; } = "매장명";       // 매장명, 중분류, 메뉴명
            public bool DbSortAscending { get; set; } = false;      // false = 내림차순(기본)
            public string? DbMiddleCategoryFilter { get; set; }     // 중분류 필터
            public string? DbMenuNameFilter { get; set; }           // 메뉴명 필터
        }

        private void CreateButtonInBorder_Click(object sender, RoutedEventArgs e)
        {
            var canvas = GetCanvasFromContextMenuSender(sender) ?? CurrentButtonCanvas;
            if (canvas == null) return;

            const double btnWidth = 54, btnHeight = 54, minGap = 5;
            double borderW = canvas.ActualWidth > 0 ? canvas.ActualWidth : canvas.RenderSize.Width;
            double borderH = canvas.ActualHeight > 0 ? canvas.ActualHeight : canvas.RenderSize.Height;

            double usableW = borderW - minGap * 2;
            double usableH = borderH - minGap * 2;
            int colCount = Math.Max(1, (int)((usableW + minGap) / (btnWidth + minGap)));
            int rowCount = Math.Max(1, (int)((usableH + minGap) / (btnHeight + minGap)));

            int btnCount = 0;
            foreach (UIElement child in canvas.Children)
                if (child is System.Windows.Controls.Button) btnCount++;
            int col = btnCount % colCount;
            int row = btnCount / colCount;
            if (row >= rowCount) { col = 0; row = 0; }
            double x = minGap + col * (btnWidth + minGap);
            double y = minGap + row * (btnHeight + minGap);

            var btn = new System.Windows.Controls.Button
            {
                Width = btnWidth,
                Height = btnHeight,
                Content = "",
                ContextMenu = new System.Windows.Controls.ContextMenu()
            };
            btn.Style = FindResource("DynamicButtonStyle") as Style;
            // Make border transparent for dynamic buttons
            btn.BorderBrush = System.Windows.Media.Brushes.Transparent;
            btn.BorderThickness = new Thickness(0);
            System.Windows.Controls.Canvas.SetLeft(btn, x);
            System.Windows.Controls.Canvas.SetTop(btn, y);

            var meta = new ButtonMeta 
            { 
                Width = btnWidth, 
                Height = btnHeight 
            };
            btn.Tag = meta;

            btn.Click += (s, ev) =>
            {
                if (string.IsNullOrEmpty(meta.Path)) { System.Windows.MessageBox.Show("경로가 설정되지 않았습니다."); return; }
                try
                {
                    if (Uri.IsWellFormedUriString(meta.Path, UriKind.Absolute))
                    {
                        Process.Start(new ProcessStartInfo { FileName = meta.Path, UseShellExecute = true });
                    }
                    else if (meta.IsFolder && Directory.Exists(meta.Path))
                        Process.Start(new ProcessStartInfo { FileName = meta.Path, UseShellExecute = true });
                    else if (!meta.IsFolder && File.Exists(meta.Path))
                        Process.Start(new ProcessStartInfo { FileName = meta.Path, UseShellExecute = true });
                    else
                        System.Windows.MessageBox.Show("경로가 올바르지 않습니다.");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"경로를 열 수 없습니다.\n{ex.Message}");
                }
            };

            var delMenu = new System.Windows.Controls.MenuItem { Header = "버튼삭제" };
            delMenu.Click += (s, ev) =>
            {
                DeleteButtonWithConfirm(canvas, btn, meta);
            };

            var editMenu = new System.Windows.Controls.MenuItem { Header = "버튼수정" };
            editMenu.Click += (s, ev) =>
            {
                var dlg = new System.Windows.Window
                {
                    Title = "버튼 수정",
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = System.Windows.ResizeMode.NoResize,
                    Content = null,
                    SizeToContent = SizeToContent.WidthAndHeight
                };
                MakeBorderless(dlg);

                var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
                var sizeBtn = new System.Windows.Controls.Button { Content = "버튼이미지", Margin = new Thickness(0, 0, 0, 8) };
                var pathBtn = new System.Windows.Controls.Button { Content = "경로 설정", Margin = new Thickness(0, 0, 0, 8) };
                var textBtn = new System.Windows.Controls.Button { Content = "버튼텍스트", Margin = new Thickness(0, 0, 0, 8) };
                var closeBtn = new System.Windows.Controls.Button { Content = "닫기" };
                sizeBtn.Click += (sss, eee) => { dlg.Close(); ShowSizeAdjustDialog(btn, canvas, meta); };
                pathBtn.Click += (sss, eee) =>
                {
                    dlg.Close();
                    var selectDlg = new System.Windows.Window
                    {
                        Title = "경로 종류 선택",
                        WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                        Owner = this,
                        ResizeMode = System.Windows.ResizeMode.NoResize,
                        SizeToContent = SizeToContent.WidthAndHeight
                    };
                    MakeBorderless(selectDlg);

                    var selectStack = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
                    var fileBtn = new System.Windows.Controls.Button { Content = "파일 경로 설정", Margin = new Thickness(0, 0, 0, 8) };
                    var urlBtn = new System.Windows.Controls.Button { Content = "URL 경로 설정", Margin = new Thickness(0, 0, 0, 8) };
                    var folderBtn = new System.Windows.Controls.Button { Content = "폴더 경로 설정", Margin = new Thickness(0, 0, 0, 8) };
                    var cancelBtn = new System.Windows.Controls.Button { Content = "취소" };
                    fileBtn.Click += (fff, ffe) =>
                    {
                        selectDlg.Close();
                        var fd = new Microsoft.Win32.OpenFileDialog { Title = "파일 선택", CheckFileExists = true };
                        if (fd.ShowDialog() == true)
                        {
                            meta.Path = fd.FileName; meta.IsFolder = false; SaveAllButtonStates();
                        }
                    };
                    urlBtn.Click += (fff, ffe) =>
                    {
                        selectDlg.Close();
                        var urlDlg = new System.Windows.Window
                        {
                            Title = "URL 입력",
                            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                            Owner = this,
                            ResizeMode = System.Windows.ResizeMode.NoResize,
                            SizeToContent = SizeToContent.WidthAndHeight
                        };
                        MakeBorderless(urlDlg);
                        var stack2 = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
                        stack2.Children.Add(new System.Windows.Controls.TextBlock { Text = "URL: (예: https://www.naver.com)", Margin = new Thickness(0,0,0,6) });
                        var tb = new System.Windows.Controls.TextBox { Text = meta.Path ?? string.Empty, Margin = new Thickness(0,8,0,8), Width = 360 };
                        var ok = new System.Windows.Controls.Button { Content = "적용", Margin = new Thickness(0, 0, 8, 0) };
                        var cancel2 = new System.Windows.Controls.Button { Content = "취소" };
                        var btns = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                        ok.Click += (s2, e2) =>
                        {
                            var text = tb.Text?.Trim();
                            if (string.IsNullOrEmpty(text)) { System.Windows.MessageBox.Show("URL을 입력하세요."); return; }
                            if (!Uri.IsWellFormedUriString(text, UriKind.Absolute)) { System.Windows.MessageBox.Show("올바른 URL 형식이 아닙니다. 예: https://www.naver.com"); return; }
                            meta.Path = text; meta.IsFolder = false; SaveAllButtonStates(); urlDlg.Close();
                        };
                        cancel2.Click += (s2, e2) => urlDlg.Close();
                        btns.Children.Add(ok); btns.Children.Add(cancel2);
                        stack2.Children.Add(tb);
                        stack2.Children.Add(btns);
                        urlDlg.Content = stack2; ApplyDarkTheme(urlDlg); urlDlg.ShowDialog();
                    };
                    folderBtn.Click += (fff, ffe) =>
                    {
                        selectDlg.Close();
                        var folderDialog = new Forms.FolderBrowserDialog();
                        if (folderDialog.ShowDialog() == Forms.DialogResult.OK)
                        { meta.Path = folderDialog.SelectedPath; meta.IsFolder = true; SaveAllButtonStates(); }
                    };
                    cancelBtn.Click += (s, e) => selectDlg.Close();
                    selectStack.Children.Add(fileBtn); selectStack.Children.Add(urlBtn); selectStack.Children.Add(folderBtn); selectStack.Children.Add(cancelBtn);
                    selectDlg.Content = selectStack; ApplyDarkTheme(selectDlg); selectDlg.ShowDialog();
                };
                textBtn.Click += (sss, eee) => { dlg.Close(); ShowTextInputDialog(btn, canvas, meta); };
                closeBtn.Click += (sss, eee) => dlg.Close();
                stack.Children.Add(sizeBtn); stack.Children.Add(pathBtn); stack.Children.Add(textBtn); stack.Children.Add(closeBtn);
                dlg.Content = stack; ApplyDarkTheme(dlg); dlg.ShowDialog();
            };

            btn.ContextMenu.Items.Add(editMenu);
            btn.ContextMenu.Items.Add(delMenu);
            canvas.Children.Add(btn);

            // 드래그 앤 드롭 핸들러를 먼저 연결 (Shift 키 체크)
            AttachDragHandlers(btn, canvas, meta);

            // Ripple 효과는 나중에 (Shift가 아닐 때만 실행)
            btn.PreviewMouseLeftButtonDown += Btn_PreviewMouseLeftButtonDown;

            SaveAllButtonStates();
        }

        private void CreateChartInBorder_Click(object sender, RoutedEventArgs e)
        {
            var canvas = GetCanvasFromContextMenuSender(sender) ?? CurrentButtonCanvas;
            if (canvas == null) return;

            const double dlgW = 520, dlgH = 360;
            var typeDlg = new System.Windows.Window
            {
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = this.Left + (this.ActualWidth  - dlgW) / 2,
                Top  = this.Top  + (this.ActualHeight - dlgH) / 2,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Width = dlgW, Height = dlgH,
                AllowsTransparency = true,
                WindowStyle = System.Windows.WindowStyle.None,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowInTaskbar = false,
            };

            var res = System.Windows.Application.Current.Resources;
            var winBg  = (res["WindowBackgroundBrush"]  as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.DimGray;
            var sbBg   = (res["StatusBarBackgroundBrush"] as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.Gray;
            var fg     = (res["ForegroundBrush"]         as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.White;
            var accent = (res["StatusBarBorderBrush"]    as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.SteelBlue;

            var root = new Border
            {
                Background = winBg,
                CornerRadius = new CornerRadius(14),
                BorderBrush = accent, BorderThickness = new Thickness(1)
            };
            root.Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 20, ShadowDepth = 6, Opacity = 0.4, Color = System.Windows.Media.Colors.Black };

            var outer = new System.Windows.Controls.StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            var title = new System.Windows.Controls.TextBlock
            {
                Text = "차트 종류 선택",
                FontSize = 16, FontWeight = System.Windows.FontWeights.Bold,
                Foreground = fg, Margin = new Thickness(0, 0, 0, 18)
            };
            outer.Children.Add(title);

            var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 2, Columns = 3, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };

            (string type, string label, string subLabel, UIElement icon)[] charts =
            {
                ("Line",  "선 그래프",    "Line Chart",    MakeLineIcon()),
                ("Bar",   "세로 막대",    "Column Chart",  MakeBarIcon()),
                ("HBar",  "가로 막대",    "Bar Chart",     MakeHBarIcon()),
                ("Pie",   "원형 차트",    "Pie Chart",     MakePieIcon()),
                ("Gauge", "게이지",       "Gauge",         MakeGaugeIcon()),
            };

            string? selectedChartType = null;
            bool typeDlgDone = false;
            void CloseTypeDlg() { if (typeDlgDone) return; typeDlgDone = true; typeDlg.Close(); }

            foreach (var (type, label, sub, icon) in charts)
            {
                var card = new Border
                {
                    Width = 110, Height = 110,
                    Margin = new Thickness(6),
                    CornerRadius = new CornerRadius(10),
                    Background = sbBg,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                var cardStack = new System.Windows.Controls.StackPanel
                    { VerticalAlignment = System.Windows.VerticalAlignment.Center,
                      HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
                cardStack.Children.Add(icon);
                cardStack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = label, FontSize = 11, FontWeight = System.Windows.FontWeights.SemiBold,
                    Foreground = fg, TextAlignment = System.Windows.TextAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                });
                cardStack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = sub, FontSize = 9, Foreground = accent,
                    TextAlignment = System.Windows.TextAlignment.Center
                });
                card.Child = cardStack;

                card.MouseEnter += (s, _) => card.Background = (res["ContextMenuItemHoverBrush"] as System.Windows.Media.Brush) ?? sbBg;
                card.MouseLeave += (s, _) => card.Background = sbBg;
                var capturedType = type;
                card.MouseLeftButtonUp += (s, _) => { selectedChartType = capturedType; CloseTypeDlg(); };
                grid.Children.Add(card);
            }
            outer.Children.Add(grid);

            var cancelRow = new System.Windows.Controls.TextBlock
            {
                Text = "ESC 또는 창 외부 클릭으로 닫기",
                FontSize = 10, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 14, 0, 0)
            };
            outer.Children.Add(cancelRow);

            root.Child = outer;
            typeDlg.Content = root;
            typeDlg.KeyDown += (s, e) => { if (e.Key == Key.Escape) CloseTypeDlg(); };
            typeDlg.Deactivated += (s, _) => { if (!typeDlgDone) CloseTypeDlg(); };

            typeDlg.ShowDialog();

            if (selectedChartType != null)
                ShowChartConfigDialog(canvas, selectedChartType);
        }

        private static System.Windows.Media.Brush IconStroke =>
            (System.Windows.Application.Current.Resources["StatusBarBorderBrush"] as System.Windows.Media.Brush)
            ?? System.Windows.Media.Brushes.SteelBlue;

        private static UIElement MakeLineIcon()
        {
            var c = new System.Windows.Controls.Canvas { Width = 56, Height = 36 };
            var pts = new System.Windows.Media.PointCollection
                { new System.Windows.Point(2,28), new System.Windows.Point(12,18), new System.Windows.Point(24,22),
                  new System.Windows.Point(34,8), new System.Windows.Point(44,14), new System.Windows.Point(54,4) };
            var poly = new System.Windows.Shapes.Polyline
                { Points = pts, Stroke = IconStroke, StrokeThickness = 2,
                  StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
                  StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
                  StrokeEndLineCap = System.Windows.Media.PenLineCap.Round };
            c.Children.Add(poly);
            return c;
        }

        private static UIElement MakeBarIcon()
        {
            var c = new System.Windows.Controls.Canvas { Width = 56, Height = 36 };
            var heights = new[] { 20.0, 30.0, 14.0, 26.0, 10.0 };
            for (int i = 0; i < heights.Length; i++)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = 7, Height = heights[i],
                    Fill = IconStroke, RadiusX = 2, RadiusY = 2
                };
                System.Windows.Controls.Canvas.SetLeft(rect, 2 + i * 11);
                System.Windows.Controls.Canvas.SetTop(rect, 36 - heights[i]);
                c.Children.Add(rect);
            }
            return c;
        }

        private static UIElement MakeHBarIcon()
        {
            var c = new System.Windows.Controls.Canvas { Width = 56, Height = 36 };
            var widths = new[] { 30.0, 46.0, 20.0, 38.0, 14.0 };
            for (int i = 0; i < widths.Length; i++)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = widths[i], Height = 5,
                    Fill = IconStroke, RadiusX = 2, RadiusY = 2
                };
                System.Windows.Controls.Canvas.SetLeft(rect, 0);
                System.Windows.Controls.Canvas.SetTop(rect, 2 + i * 7);
                c.Children.Add(rect);
            }
            return c;
        }

        private static UIElement MakePieIcon()
        {
            var c = new System.Windows.Controls.Canvas { Width = 40, Height = 40 };
            var res = System.Windows.Application.Current.Resources;
            var accent = (res["StatusBarBorderBrush"] as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.SteelBlue;
            var secondary = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 89, 165, 169));
            var tertiary   = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 232, 168, 56));

            System.Windows.Media.PathGeometry Sector(double cx, double cy, double r, double startDeg, double endDeg, System.Windows.Media.Brush fill)
            {
                double toRad(double d) => d * Math.PI / 180;
                var start = new System.Windows.Point(cx + r * Math.Cos(toRad(startDeg)), cy + r * Math.Sin(toRad(startDeg)));
                var end   = new System.Windows.Point(cx + r * Math.Cos(toRad(endDeg)),   cy + r * Math.Sin(toRad(endDeg)));
                var fig = new System.Windows.Media.PathFigure { StartPoint = new System.Windows.Point(cx, cy) };
                fig.Segments.Add(new System.Windows.Media.LineSegment(start, false));
                fig.Segments.Add(new System.Windows.Media.ArcSegment(end, new System.Windows.Size(r, r), 0,
                    endDeg - startDeg > 180, System.Windows.Media.SweepDirection.Clockwise, false));
                fig.Segments.Add(new System.Windows.Media.LineSegment(new System.Windows.Point(cx, cy), false));
                var geo = new System.Windows.Media.PathGeometry();
                geo.Figures.Add(fig);
                var path = new System.Windows.Shapes.Path { Data = geo, Fill = fill };
                c.Children.Add(path);
                return geo;
            }

            Sector(20, 20, 18, -90, 90, accent);
            Sector(20, 20, 18, 90, 210, secondary);
            Sector(20, 20, 18, 210, 270, tertiary);

            var hole = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14 };
            hole.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "WindowBackgroundBrush");
            System.Windows.Controls.Canvas.SetLeft(hole, 13);
            System.Windows.Controls.Canvas.SetTop(hole, 13);
            c.Children.Add(hole);
            return c;
        }

        private static UIElement MakeGaugeIcon()
        {
            var c = new System.Windows.Controls.Canvas { Width = 48, Height = 30 };
            var res = System.Windows.Application.Current.Resources;
            var accent = (res["StatusBarBorderBrush"] as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.SteelBlue;
            var dim    = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 120, 120, 130));

            System.Windows.Shapes.Path ArcPath(double cx, double cy, double r, double startDeg, double endDeg, System.Windows.Media.Brush stroke, double thickness)
            {
                double toRad(double d) => d * Math.PI / 180;
                var start = new System.Windows.Point(cx + r * Math.Cos(toRad(startDeg)), cy + r * Math.Sin(toRad(startDeg)));
                var end   = new System.Windows.Point(cx + r * Math.Cos(toRad(endDeg)),   cy + r * Math.Sin(toRad(endDeg)));
                var fig = new System.Windows.Media.PathFigure { StartPoint = start };
                fig.Segments.Add(new System.Windows.Media.ArcSegment(end, new System.Windows.Size(r, r), 0,
                    false, System.Windows.Media.SweepDirection.Clockwise, true));
                var geo = new System.Windows.Media.PathGeometry();
                geo.Figures.Add(fig);
                return new System.Windows.Shapes.Path
                    { Data = geo, Stroke = stroke, StrokeThickness = thickness,
                      StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
                      StrokeEndLineCap   = System.Windows.Media.PenLineCap.Round };
            }

            c.Children.Add(ArcPath(24, 28, 22, 180, 360, dim, 5));
            c.Children.Add(ArcPath(24, 28, 22, 180, 300, accent, 5));
            return c;
        }

        private void ShowChartConfigDialog(Canvas canvas, string chartType)
        {
            var configDlg = new System.Windows.Window
            {
                Title = $"{chartType} 차트 설정",
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                Width = 450
            };
            MakeBorderless(configDlg);

            // 현재 테마 색상을 다이얼로그 시스템 색상으로 오버라이드 (ComboBox popup 등에 반영)
            var _res = System.Windows.Application.Current.Resources;
            var _btnBg  = (_res["ContextMenuBorderBrush"]  as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.DimGray;
            var _fg     = (_res["ForegroundBrush"]         as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.White;
            var _accent = (_res["StatusBarBorderBrush"]    as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.SteelBlue;
            configDlg.Resources[System.Windows.SystemColors.WindowBrushKey]       = _btnBg;
            configDlg.Resources[System.Windows.SystemColors.WindowTextBrushKey]    = _fg;
            configDlg.Resources[System.Windows.SystemColors.ControlBrushKey]       = _btnBg;
            configDlg.Resources[System.Windows.SystemColors.ControlTextBrushKey]   = _fg;
            configDlg.Resources[System.Windows.SystemColors.HighlightBrushKey]     = _accent;
            configDlg.Resources[System.Windows.SystemColors.HighlightTextBrushKey] = System.Windows.Media.Brushes.White;

            var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };

            // 제목
            stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "차트 제목:", Margin = new Thickness(0, 0, 0, 4) });
            var titleBox = new System.Windows.Controls.TextBox { Text = $"{chartType} Chart", Margin = new Thickness(0, 0, 0, 12) };
            stack.Children.Add(titleBox);

            // 크기
            var sizePanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            sizePanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "너비:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var widthBox = new System.Windows.Controls.TextBox { Text = "300", Width = 60, Margin = new Thickness(0, 0, 16, 0) };
            sizePanel.Children.Add(widthBox);
            sizePanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "높이:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var heightBox = new System.Windows.Controls.TextBox { Text = "200", Width = 60 };
            sizePanel.Children.Add(heightBox);
            stack.Children.Add(sizePanel);

            // 데이터 소스 선택
            stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "데이터 소스:", Margin = new Thickness(0, 0, 0, 4) });
            var sourceCombo = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 12) };
            sourceCombo.Style = null; // MaterialDesign 스타일 제거 → WPF 기본 스타일 사용 (Background 적용됨)
            sourceCombo.Items.Add("정적 데이터 (수동 입력)");
            sourceCombo.Items.Add("JSON 파일");
            sourceCombo.Items.Add("CSV 파일");
            sourceCombo.Items.Add("API (실시간)");
            sourceCombo.Items.Add("DB (대진포스DB)");
            sourceCombo.SelectedIndex = 0;
            stack.Children.Add(sourceCombo);

            // 데이터 입력 영역 (동적으로 변경됨)
            var dataPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            stack.Children.Add(dataPanel);

            // 정적 데이터 입력 UI
            var staticDataPanel = new System.Windows.Controls.StackPanel();
            staticDataPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "데이터 값 (쉼표로 구분):", Margin = new Thickness(0, 0, 0, 4) });
            var dataBox = new System.Windows.Controls.TextBox { Text = "10, 25, 15, 30, 20", Margin = new Thickness(0, 0, 0, 8) };
            staticDataPanel.Children.Add(dataBox);
            staticDataPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "라벨 (쉼표로 구분):", Margin = new Thickness(0, 0, 0, 4) });
            var labelBox = new System.Windows.Controls.TextBox { Text = "항목1, 항목2, 항목3, 항목4, 항목5", Margin = new Thickness(0, 0, 0, 8) };
            staticDataPanel.Children.Add(labelBox);
            dataPanel.Children.Add(staticDataPanel);

            // 파일/API 경로 입력 UI
            var pathPanel = new System.Windows.Controls.StackPanel { Visibility = Visibility.Collapsed };
            pathPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "파일 경로 또는 API URL:", Margin = new Thickness(0, 0, 0, 4) });
            var pathBox = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 0, 0, 8) };
            var browseBtn = new System.Windows.Controls.Button { Content = "파일 선택...", HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Padding = new Thickness(12, 4, 12, 4) };
            browseBtn.Click += (s, ev) =>
            {
                var fd = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JSON 파일 (*.json)|*.json|CSV 파일 (*.csv)|*.csv|모든 파일 (*.*)|*.*"
                };
                if (fd.ShowDialog() == true)
                {
                    pathBox.Text = fd.FileName;
                }
            };
            pathPanel.Children.Add(pathBox);
            pathPanel.Children.Add(browseBtn);
            dataPanel.Children.Add(pathPanel);

            // DB 설정 패널
            var dbPanel = new System.Windows.Controls.StackPanel { Visibility = Visibility.Collapsed };
            dbPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "집계 기준:", Margin = new Thickness(0, 0, 0, 4) });
            var dbGroupByCombo = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            dbGroupByCombo.Style = null;
            dbGroupByCombo.Items.Add("매장명");
            dbGroupByCombo.Items.Add("중분류");
            dbGroupByCombo.Items.Add("메뉴명");
            dbGroupByCombo.SelectedIndex = 0;
            dbPanel.Children.Add(dbGroupByCombo);

            dbPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "집계 컬럼:", Margin = new Thickness(0, 0, 0, 4) });
            var dbValueCombo = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            dbValueCombo.Style = null;
            dbValueCombo.Items.Add("총매출액");
            dbValueCombo.Items.Add("총수량");
            dbValueCombo.Items.Add("판매수량");
            dbValueCombo.Items.Add("서비스수량");
            dbValueCombo.SelectedIndex = 0;
            dbPanel.Children.Add(dbValueCombo);

            var dbDateRow = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            dbDateRow.Children.Add(new System.Windows.Controls.TextBlock { Text = "시작 날짜:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var dbStartDatePicker = new System.Windows.Controls.DatePicker { Width = 120, Margin = new Thickness(0, 0, 12, 0), SelectedDate = DateTime.Today.AddDays(-7) };
            dbDateRow.Children.Add(dbStartDatePicker);
            dbDateRow.Children.Add(new System.Windows.Controls.TextBlock { Text = "종료 날짜:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var dbEndDatePicker = new System.Windows.Controls.DatePicker { Width = 120, SelectedDate = DateTime.Today.AddDays(-1) };
            dbDateRow.Children.Add(dbEndDatePicker);
            dbPanel.Children.Add(dbDateRow);

            dbPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "매장명 필터:", Margin = new Thickness(0, 0, 0, 4) });
            var dbStoreBox = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            dbStoreBox.Style = null;
            dbStoreBox.Items.Add("(전체)");
            foreach (var v in LoadDbDistinctValues("매장명")) dbStoreBox.Items.Add(v);
            dbStoreBox.SelectedIndex = 0;
            dbPanel.Children.Add(dbStoreBox);

            dbPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "중분류 필터:", Margin = new Thickness(0, 0, 0, 4) });
            var dbMiddleCatBox = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            dbMiddleCatBox.Style = null;
            dbMiddleCatBox.Items.Add("(전체)");
            foreach (var v in LoadDbDistinctValues("중분류")) dbMiddleCatBox.Items.Add(v);
            dbMiddleCatBox.SelectedIndex = 0;
            dbPanel.Children.Add(dbMiddleCatBox);

            dbPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "메뉴명 필터:", Margin = new Thickness(0, 0, 0, 4) });
            var dbMenuBox = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            dbMenuBox.Style = null;
            dbMenuBox.Items.Add("(전체)");
            foreach (var v in LoadDbDistinctValues("메뉴명")) dbMenuBox.Items.Add(v);
            dbMenuBox.SelectedIndex = 0;
            dbPanel.Children.Add(dbMenuBox);

            dbPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "정렬:", Margin = new Thickness(0, 0, 0, 4) });
            var dbSortCombo = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            dbSortCombo.Style = null;
            dbSortCombo.Items.Add("내림차순 (높은 값 먼저)");
            dbSortCombo.Items.Add("오름차순 (낮은 값 먼저)");
            dbSortCombo.SelectedIndex = 0;
            dbPanel.Children.Add(dbSortCombo);

            dataPanel.Children.Add(dbPanel);

            // 실시간 업데이트 설정
            var refreshPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
            refreshPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "새로고침 간격 (초):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var refreshBox = new System.Windows.Controls.TextBox { Text = "5", Width = 60 };
            refreshPanel.Children.Add(refreshBox);
            stack.Children.Add(refreshPanel);

            sourceCombo.SelectionChanged += (s, ev) =>
            {
                int idx = sourceCombo.SelectedIndex;
                staticDataPanel.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
                pathPanel.Visibility = (idx >= 1 && idx <= 3) ? Visibility.Visible : Visibility.Collapsed;
                browseBtn.Visibility = (idx >= 1 && idx <= 2) ? Visibility.Visible : Visibility.Collapsed;
                refreshPanel.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
                dbPanel.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;
            };

            // 버튼
            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var createBtn = new System.Windows.Controls.Button { Content = "생성", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
            var cancelBtn2 = new System.Windows.Controls.Button { Content = "취소", Padding = new Thickness(12, 6, 12, 6) };

            createBtn.Click += (s, ev) =>
            {
                try
                {
                    double width = double.Parse(widthBox.Text);
                    double height = double.Parse(heightBox.Text);

                    var meta = new ChartMeta
                    {
                        ChartType = chartType,
                        Width = width,
                        Height = height,
                        Title = titleBox.Text
                    };

                    int sourceIdx = sourceCombo.SelectedIndex;
                    switch (sourceIdx)
                    {
                        case 0: // 정적 데이터
                            meta.DataSource = "Static";
                            meta.StaticData = dataBox.Text.Split(',').Select(x => double.TryParse(x.Trim(), out var v) ? v : 0).ToList();
                            meta.Labels = labelBox.Text.Split(',').Select(x => x.Trim()).ToList();
                            break;
                        case 1: // JSON
                            meta.DataSource = "Json";
                            meta.DataPath = pathBox.Text;
                            break;
                        case 2: // CSV
                            meta.DataSource = "Csv";
                            meta.DataPath = pathBox.Text;
                            break;
                        case 3: // API
                            meta.DataSource = "Api";
                            meta.DataPath = pathBox.Text;
                            if (int.TryParse(refreshBox.Text, out int interval))
                                meta.RefreshInterval = interval;
                            break;
                        case 4: // DB
                            meta.DataSource = "Db";
                            meta.DbGroupBy = dbGroupByCombo.SelectedItem?.ToString() ?? "매장명";
                            meta.DbValueColumn = dbValueCombo.SelectedItem?.ToString() ?? "총매출액";
                            meta.DbStartDate = dbStartDatePicker.SelectedDate;
                            meta.DbEndDate = dbEndDatePicker.SelectedDate;
                            meta.DbStoreName = dbStoreBox.SelectedIndex <= 0 ? null : dbStoreBox.SelectedItem?.ToString();
                            meta.DbMiddleCategoryFilter = dbMiddleCatBox.SelectedIndex <= 0 ? null : dbMiddleCatBox.SelectedItem?.ToString();
                            meta.DbMenuNameFilter = dbMenuBox.SelectedIndex <= 0 ? null : dbMenuBox.SelectedItem?.ToString();
                            meta.DbSortAscending = dbSortCombo.SelectedIndex == 1;
                            break;
                    }

                    if (meta.DataSource == "Db")
                    {
                        try { LoadDbData(meta); } catch { }
                    }

                    CreateChartControl(canvas, meta);
                    configDlg.Close();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"차트 생성 오류: {ex.Message}");
                }
            };

            cancelBtn2.Click += (s, ev) => configDlg.Close();
            btnPanel.Children.Add(createBtn);
            btnPanel.Children.Add(cancelBtn2);
            stack.Children.Add(btnPanel);

            configDlg.Content = stack;
            ApplyDarkTheme(configDlg);
            configDlg.ShowDialog();
        }

        private void CreateChartControl(Canvas canvas, ChartMeta meta)
        {
            var border = new System.Windows.Controls.Border
            {
                Width = meta.Width,
                Height = meta.Height,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10)
            };
            border.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "StatusBarBackgroundBrush");
            border.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "StatusBarBorderBrush");

            // 차트에 따라 LiveCharts 컨트롤 생성
            FrameworkElement chartControl = null;

            switch (meta.ChartType)
            {
                case "Line":
                    chartControl = CreateLineChart(meta);
                    break;
                case "Bar":
                    chartControl = CreateBarChart(meta);
                    break;
                case "HBar":
                    chartControl = CreateHBarChart(meta);
                    break;
                case "Pie":
                    chartControl = CreatePieChart(meta);
                    break;
                case "Gauge":
                    chartControl = CreateGaugeChart(meta);
                    break;
            }

            if (chartControl != null)
            {
                if (chartControl is System.Windows.Controls.Control ctrl)
                    ctrl.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "StatusBarBackgroundBrush");
                border.Child = chartControl;
            }

            // Canvas에 배치
            double x = 20;
            double y = 20;
            System.Windows.Controls.Canvas.SetLeft(border, x);
            System.Windows.Controls.Canvas.SetTop(border, y);

            border.Tag = meta;

            // 컨텍스트 메뉴 추가
            var contextMenu = new System.Windows.Controls.ContextMenu();
            var editItem = new System.Windows.Controls.MenuItem { Header = "차트 수정" };
            var deleteItem = new System.Windows.Controls.MenuItem { Header = "차트 삭제" };
            var refreshItem = new System.Windows.Controls.MenuItem { Header = "새로고침" };

            editItem.Click += (s, ev) => EditChartDialog(border, canvas, meta);
            deleteItem.Click += (s, ev) =>
            {
                if (System.Windows.MessageBox.Show("차트를 삭제하시겠습니까?", "확인", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    if (meta.RefreshTimer != null)
                    {
                        meta.RefreshTimer.Stop();
                        meta.RefreshTimer = null;
                    }
                    canvas.Children.Remove(border);
                    SaveAllChartStates();
                }
            };
            refreshItem.Click += (s, ev) => RefreshChartData(border, meta);

            contextMenu.Items.Add(editItem);
            contextMenu.Items.Add(refreshItem);
            contextMenu.Items.Add(deleteItem);
            border.ContextMenu = contextMenu;

            // 드래그 가능하게 설정
            AttachChartDragHandlers(border, canvas);

            canvas.Children.Add(border);

            // 실시간 업데이트 타이머 설정
            if (meta.RefreshInterval > 0 && meta.DataSource == "Api")
            {
                meta.RefreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(meta.RefreshInterval)
                };
                meta.RefreshTimer.Tick += (s, ev) => RefreshChartData(border, meta);
                meta.RefreshTimer.Start();
            }

            SaveAllChartStates();
        }

        private (SKColor bg, SKColor axisLabel, SKColor gridLine) GetChartColors() =>
            ThemeManager.CurrentTheme switch
            {
                ThemeManager.Theme.White => (
                    new SKColor(228, 228, 240),
                    new SKColor(80,  80,  112),
                    new SKColor(196, 196, 212)),
                ThemeManager.Theme.Black => (
                    new SKColor(7,   7,   7),
                    new SKColor(144, 144, 144),
                    new SKColor(37,  37,  37)),
                _ => (
                    new SKColor(19, 19, 27),
                    new SKColor(160, 160, 170),
                    new SKColor(55,  55,  65))
            };

        // 전문적인 차트 색상 팔레트 (채도 낮춘 Tableau 스타일)
        private static readonly SKColor P_Blue   = SKColor.Parse("#4E79A7");
        private static readonly SKColor P_Amber  = SKColor.Parse("#E8A838");
        private static readonly SKColor P_Teal   = SKColor.Parse("#59A5A9");
        private static readonly SKColor P_Purple = SKColor.Parse("#9A6AA0");
        private static readonly SKColor P_Rust   = SKColor.Parse("#C0614A");
        private static readonly SKColor P_Sage   = SKColor.Parse("#4F8C5E");

        private static void AttachCtrlScrollZoom(CartesianChart chart)
        {
            chart.ZoomMode = LiveChartsCore.Measure.ZoomAndPanMode.None;
            chart.PreviewMouseWheel += (s, e) =>
            {
                e.Handled = true; // 항상 캡처 → ScrollViewer로 절대 안 넘어감

                bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                if (!ctrl)
                {
                    // 일반 휠 → 부모 ScrollViewer 스크롤
                    DependencyObject p = VisualTreeHelper.GetParent(chart);
                    while (p != null && p is not System.Windows.Controls.ScrollViewer)
                        p = VisualTreeHelper.GetParent(p);
                    if (p is System.Windows.Controls.ScrollViewer sv)
                        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
                    return;
                }

                // Ctrl+휠 → 축 범위 수동 조정
                double factor = e.Delta > 0 ? 0.8 : 1.25;
                try
                {
                    foreach (var axis in chart.XAxes.OfType<Axis>())
                    {
                        double lo = axis.MinLimit ?? (axis.DataBounds.Min - 0.5);
                        double hi = axis.MaxLimit ?? (axis.DataBounds.Max + 0.5);
                        double c = (lo + hi) / 2;
                        double h = (hi - lo) / 2 * factor;
                        axis.MinLimit = c - h;
                        axis.MaxLimit = c + h;
                    }
                    foreach (var axis in chart.YAxes.OfType<Axis>())
                    {
                        double lo = axis.MinLimit ?? 0;
                        double hi = axis.MaxLimit ?? (axis.DataBounds.Max * 1.15);
                        double c = (lo + hi) / 2;
                        double h = (hi - lo) / 2 * factor;
                        double newLo = c - h;
                        axis.MinLimit = newLo < 0 ? 0 : newLo;
                        axis.MaxLimit = c + h;
                    }
                }
                catch { }
            };
        }

        private CartesianChart CreateLineChart(ChartMeta meta)
        {
            var (_, axisLabel, gridLine) = GetChartColors();
            var lineColor = P_Blue;
            var chart = new CartesianChart
            {
                TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Auto,
                TooltipTextPaint = new SolidColorPaint(new SKColor(230, 230, 230)) { SKTypeface = SKTypeface.FromFamilyName("Malgun Gothic") },
                TooltipBackgroundPaint = new SolidColorPaint(new SKColor(30, 30, 38, 230)),
                Series = new ISeries[]
                {
                    new LineSeries<double>
                    {
                        Values = meta.StaticData,
                        Stroke = new SolidColorPaint(lineColor) { StrokeThickness = 1.5f },
                        Fill = new LinearGradientPaint(
                            new[] { lineColor.WithAlpha(55), lineColor.WithAlpha(0) },
                            new SKPoint(0, 0), new SKPoint(0, 1)),
                        GeometrySize = 6,
                        GeometryFill = new SolidColorPaint(lineColor),
                        GeometryStroke = null,
                        LineSmoothness = 0.65,
                        DataLabelsPaint = new SolidColorPaint(axisLabel) { SKTypeface = SKTypeface.FromFamilyName("Malgun Gothic") },
                        DataLabelsSize = 10,
                        DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top,
                        DataLabelsFormatter = p => p.Coordinate.PrimaryValue.ToString("N0")
                    }
                },
                XAxes = new[]
                {
                    new Axis
                    {
                        Labels = meta.Labels,
                        LabelsRotation = 0,
                        LabelsPaint = new SolidColorPaint(axisLabel) { SKTypeface = SKTypeface.FromFamilyName("Malgun Gothic") },
                        SeparatorsPaint = new SolidColorPaint(gridLine.WithAlpha(80)) { StrokeThickness = 1 },
                        TicksPaint = null
                    }
                },
                YAxes = new[]
                {
                    new Axis
                    {
                        LabelsPaint = new SolidColorPaint(axisLabel) { SKTypeface = SKTypeface.FromFamilyName("Malgun Gothic") },
                        SeparatorsPaint = new SolidColorPaint(gridLine.WithAlpha(80)) { StrokeThickness = 1 },
                        TicksPaint = null
                    }
                }
            };
            AttachCtrlScrollZoom(chart);
            return chart;
        }

        private CartesianChart CreateBarChart(ChartMeta meta)
        {
            var (_, axisLabel, gridLine) = GetChartColors();
            var chart = new CartesianChart
            {
                TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Auto,
                TooltipTextPaint = new SolidColorPaint(new SKColor(230, 230, 230)) { SKTypeface = SKTypeface.FromFamilyName("Malgun Gothic") },
                TooltipBackgroundPaint = new SolidColorPaint(new SKColor(30, 30, 38, 230)),
                Series = new ISeries[]
                {
                    new ColumnSeries<double>
                    {
                        Values = meta.StaticData,
                        Fill = new LinearGradientPaint(
                            new[] { P_Blue.WithAlpha(220), P_Teal.WithAlpha(200) },
                            new SKPoint(0, 0), new SKPoint(0, 1)),
                        Stroke = null,
                        Rx = 3,
                        Ry = 3,
                        MaxBarWidth = double.PositiveInfinity,
                        DataLabelsPaint = new SolidColorPaint(axisLabel) { SKTypeface = SKTypeface.FromFamilyName("Malgun Gothic") },
                        DataLabelsSize = 10,
                        DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top,
                        DataLabelsFormatter = p => p.Coordinate.PrimaryValue.ToString("N0")
                    }
                },
                XAxes = new[]
                {
                    new Axis
                    {
                        Labels = meta.Labels,
                        LabelsPaint = new SolidColorPaint(axisLabel) { SKTypeface = SKTypeface.FromFamilyName("Malgun Gothic") },
                        SeparatorsPaint = null,
                        TicksPaint = null
                    }
                },
                YAxes = new[]
                {
                    new Axis
                    {
                        LabelsPaint = new SolidColorPaint(axisLabel) { SKTypeface = SKTypeface.FromFamilyName("Malgun Gothic") },
                        SeparatorsPaint = new SolidColorPaint(gridLine.WithAlpha(80)) { StrokeThickness = 1 },
                        TicksPaint = null
                    }
                }
            };
            AttachCtrlScrollZoom(chart);
            return chart;
        }

        private CartesianChart CreateHBarChart(ChartMeta meta)
        {
            var (_, axisLabel, gridLine) = GetChartColors();

            // LiveCharts2 RowSeries는 index 0이 맨 아래 → 역순으로 넣어야 높은 값이 위에 표시됨
            var displayValues = meta.StaticData.AsEnumerable().Reverse().ToList();
            var displayLabels = meta.Labels.AsEnumerable().Reverse().ToList();

            var labelPaint = new SolidColorPaint(new SKColor(240, 240, 240))
                { SKTypeface = SKTypeface.FromFamilyName("Malgun Gothic") };

            var chart = new CartesianChart
            {
                TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Auto,
                TooltipTextPaint = new SolidColorPaint(new SKColor(230, 230, 230)) { SKTypeface = SKTypeface.FromFamilyName("Malgun Gothic") },
                TooltipBackgroundPaint = new SolidColorPaint(new SKColor(30, 30, 38, 230)),
                Series = new ISeries[]
                {
                    new RowSeries<double>
                    {
                        Values = displayValues,
                        Fill = new LinearGradientPaint(
                            new[] { P_Teal.WithAlpha(220), P_Blue.WithAlpha(200) },
                            new SKPoint(0, 0), new SKPoint(1, 0)),
                        Stroke = null,
                        Rx = 3,
                        Ry = 3,
                        MaxBarWidth = 40,
                        DataLabelsPaint = labelPaint,
                        DataLabelsSize = 10,
                        DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Middle,
                        DataLabelsFormatter = p => p.Coordinate.PrimaryValue.ToString("N0")
                    }
                },
                YAxes = new[]
                {
                    new Axis
                    {
                        Labels = displayLabels,
                        LabelsPaint = new SolidColorPaint(axisLabel) { SKTypeface = SKTypeface.FromFamilyName("Malgun Gothic") },
                        SeparatorsPaint = null,
                        TicksPaint = null,
                        TextSize = 11
                    }
                },
                XAxes = new[]
                {
                    new Axis
                    {
                        LabelsPaint = new SolidColorPaint(axisLabel) { SKTypeface = SKTypeface.FromFamilyName("Malgun Gothic") },
                        SeparatorsPaint = new SolidColorPaint(gridLine.WithAlpha(80)) { StrokeThickness = 1 },
                        TicksPaint = null
                    }
                }
            };
            AttachCtrlScrollZoom(chart);
            return chart;
        }

        private PieChart CreatePieChart(ChartMeta meta)
        {
            var (chartBg, axisLabel, _) = GetChartColors();
            var palette = new[] { P_Blue, P_Amber, P_Teal, P_Purple, P_Rust, P_Sage };
            var series = new List<ISeries>();
            for (int i = 0; i < meta.StaticData.Count; i++)
            {
                series.Add(new PieSeries<double>
                {
                    Values = new[] { meta.StaticData[i] },
                    Name = i < meta.Labels.Count ? meta.Labels[i] : $"항목{i + 1}",
                    Fill = new SolidColorPaint(palette[i % palette.Length]),
                    Stroke = new SolidColorPaint(chartBg) { StrokeThickness = 1.5f },
                    InnerRadius = 38,
                    OuterRadiusOffset = 0,
                    HoverPushout = 4
                });
            }
            var chart = new PieChart
            {
                TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Hidden,
                Series = series,
                LegendPosition = LiveChartsCore.Measure.LegendPosition.Right,
                LegendTextPaint = new SolidColorPaint(axisLabel) { SKTypeface = SKTypeface.FromFamilyName("Malgun Gothic") }
            };
            return chart;
        }

        private PieChart CreateGaugeChart(ChartMeta meta)
        {
            var (_, _, gridLine) = GetChartColors();
            double value    = meta.StaticData.Count > 0 ? meta.StaticData[0] : 0;
            double maxValue = meta.StaticData.Count > 1 ? meta.StaticData[1] : 100;

            var chart = new PieChart
            {
                TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Hidden,
                Series = new ISeries[]
                {
                    new PieSeries<double>
                    {
                        Values = new[] { value },
                        Fill = new LinearGradientPaint(
                            new[] { P_Blue, P_Teal },
                            new SKPoint(0, 0), new SKPoint(1, 0)),
                        Stroke = null,
                        InnerRadius = 55,
                        HoverPushout = 0
                    },
                    new PieSeries<double>
                    {
                        Values = new[] { maxValue - value },
                        Fill = new SolidColorPaint(gridLine.WithAlpha(120)),
                        Stroke = null,
                        InnerRadius = 55,
                        HoverPushout = 0
                    }
                },
                InitialRotation = -90,
                MaxAngle = 360
            };
            return chart;
        }

        private void RefreshAllChartColors()
        {
            for (int i = 0; i < tabControl.Items.Count; i++)
            {
                var canvas = GetCanvasByIndex(i);
                if (canvas == null) continue;
                foreach (UIElement child in canvas.Children)
                {
                    if (child is System.Windows.Controls.Border chartBorder && chartBorder.Tag is ChartMeta meta)
                    {
                        chartBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "StatusBarBackgroundBrush");
                        chartBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "StatusBarBorderBrush");

                        FrameworkElement? newChart = meta.ChartType switch
                        {
                            "Line"  => CreateLineChart(meta),
                            "Bar"   => CreateBarChart(meta),
                            "HBar"  => CreateHBarChart(meta),
                            "Pie"   => CreatePieChart(meta),
                            "Gauge" => CreateGaugeChart(meta),
                            _       => null
                        };
                        if (newChart != null)
                        {
                            if (newChart is System.Windows.Controls.Control ctrl)
                                ctrl.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "StatusBarBackgroundBrush");
                            chartBorder.Child = newChart;
                        }
                    }
                }
            }
        }

        private void TabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (e.Source != tabControl) return; // 내부 컨트롤의 이벤트 버블링 무시
            int idx = tabControl.SelectedIndex;
            if (idx < 0) return;
            var canvas = GetCanvasByIndex(idx);
            if (canvas == null) return;

            foreach (UIElement child in canvas.Children.OfType<UIElement>().ToList())
            {
                if (child is System.Windows.Controls.Border chartBorder && chartBorder.Tag is ChartMeta meta)
                    RefreshChartData(chartBorder, meta);
            }
        }

        private void AttachChartDragHandlers(Border border, Canvas canvas)
        {
            bool isDragging = false;
            System.Windows.Point dragStart = new System.Windows.Point();
            System.Windows.Point elementStart = new System.Windows.Point();

            border.MouseLeftButtonDown += (s, e) =>
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    isDragging = true;
                    dragStart = e.GetPosition(canvas);
                    elementStart = new System.Windows.Point(
                        System.Windows.Controls.Canvas.GetLeft(border),
                        System.Windows.Controls.Canvas.GetTop(border));
                    border.CaptureMouse();
                    e.Handled = true;
                }
            };

            border.MouseMove += (s, e) =>
            {
                if (isDragging)
                {
                    var current = e.GetPosition(canvas);
                    double newX = elementStart.X + (current.X - dragStart.X);
                    double newY = elementStart.Y + (current.Y - dragStart.Y);

                    // 그리드 스냅
                    newX = Math.Round(newX / GridSize) * GridSize;
                    newY = Math.Round(newY / GridSize) * GridSize;

                    System.Windows.Controls.Canvas.SetLeft(border, newX);
                    System.Windows.Controls.Canvas.SetTop(border, newY);
                }
            };

            border.MouseLeftButtonUp += (s, e) =>
            {
                if (isDragging)
                {
                    isDragging = false;
                    border.ReleaseMouseCapture();
                    SaveAllChartStates();
                }
            };
        }

        private void EditChartDialog(Border border, Canvas canvas, ChartMeta meta)
        {
            // 기존 ShowChartConfigDialog를 재활용하여 편집 모드로 사용
            var configDlg = new System.Windows.Window
            {
                Title = $"{meta.ChartType} 차트 수정",
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                Width = 450
            };
            MakeBorderless(configDlg);

            var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };

            // 크기 수정
            var sizePanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            sizePanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "너비:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var widthBox = new System.Windows.Controls.TextBox { Text = meta.Width.ToString(), Width = 60, Margin = new Thickness(0, 0, 16, 0) };
            sizePanel.Children.Add(widthBox);
            sizePanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "높이:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var heightBox = new System.Windows.Controls.TextBox { Text = meta.Height.ToString(), Width = 60 };
            sizePanel.Children.Add(heightBox);
            stack.Children.Add(sizePanel);

            // 데이터 수정 (정적 데이터만)
            if (meta.DataSource == "Static")
            {
                stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "데이터 값 (쉼표로 구분):", Margin = new Thickness(0, 0, 0, 4) });
                var dataBox = new System.Windows.Controls.TextBox { Text = string.Join(", ", meta.StaticData), Margin = new Thickness(0, 0, 0, 8) };
                stack.Children.Add(dataBox);

                stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "라벨 (쉼표로 구분):", Margin = new Thickness(0, 0, 0, 4) });
                var labelBox = new System.Windows.Controls.TextBox { Text = string.Join(", ", meta.Labels), Margin = new Thickness(0, 0, 0, 12) };
                stack.Children.Add(labelBox);

                var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                var applyBtn = new System.Windows.Controls.Button { Content = "적용", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
                var cancelBtn = new System.Windows.Controls.Button { Content = "취소", Padding = new Thickness(12, 6, 12, 6) };

                applyBtn.Click += (s, ev) =>
                {
                    meta.Width = double.Parse(widthBox.Text);
                    meta.Height = double.Parse(heightBox.Text);
                    meta.StaticData = dataBox.Text.Split(',').Select(x => double.TryParse(x.Trim(), out var v) ? v : 0).ToList();
                    meta.Labels = labelBox.Text.Split(',').Select(x => x.Trim()).ToList();

                    border.Width = meta.Width;
                    border.Height = meta.Height;

                    // 차트 다시 생성
                    FrameworkElement newChart = null;
                    switch (meta.ChartType)
                    {
                        case "Line": newChart = CreateLineChart(meta); break;
                        case "Bar": newChart = CreateBarChart(meta); break;
                        case "HBar": newChart = CreateHBarChart(meta); break;
                        case "Pie": newChart = CreatePieChart(meta); break;
                        case "Gauge": newChart = CreateGaugeChart(meta); break;
                    }
                    if (newChart != null)
                        border.Child = newChart;

                    SaveAllChartStates();
                    configDlg.Close();
                };
                cancelBtn.Click += (s, ev) => configDlg.Close();

                btnPanel.Children.Add(applyBtn);
                btnPanel.Children.Add(cancelBtn);
                stack.Children.Add(btnPanel);
            }
            else if (meta.DataSource == "Db")
            {
                // 집계 기준
                stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "집계 기준:", Margin = new Thickness(0, 0, 0, 4) });
                var eGroupByCombo = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 8) };
                eGroupByCombo.Style = null;
                eGroupByCombo.Items.Add("매장명"); eGroupByCombo.Items.Add("중분류"); eGroupByCombo.Items.Add("메뉴명");
                eGroupByCombo.SelectedItem = meta.DbGroupBy ?? "매장명";
                stack.Children.Add(eGroupByCombo);

                // 집계 컬럼
                stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "집계 컬럼:", Margin = new Thickness(0, 0, 0, 4) });
                var eValueCombo = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 8) };
                eValueCombo.Style = null;
                eValueCombo.Items.Add("총매출액"); eValueCombo.Items.Add("총수량"); eValueCombo.Items.Add("판매수량"); eValueCombo.Items.Add("서비스수량");
                eValueCombo.SelectedItem = meta.DbValueColumn ?? "총매출액";
                stack.Children.Add(eValueCombo);

                // 날짜
                var eDateRow = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                eDateRow.Children.Add(new System.Windows.Controls.TextBlock { Text = "시작:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                var eStartDate = new System.Windows.Controls.DatePicker { Width = 110, Margin = new Thickness(0, 0, 10, 0), SelectedDate = meta.DbStartDate ?? DateTime.Today.AddDays(-7) };
                eDateRow.Children.Add(eStartDate);
                eDateRow.Children.Add(new System.Windows.Controls.TextBlock { Text = "종료:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                var eEndDate = new System.Windows.Controls.DatePicker { Width = 110, SelectedDate = meta.DbEndDate ?? DateTime.Today.AddDays(-1) };
                eDateRow.Children.Add(eEndDate);
                stack.Children.Add(eDateRow);

                // 필터들 (ComboBox - DB에서 값 가져옴)
                stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "매장명 필터:", Margin = new Thickness(0, 0, 0, 4) });
                var eStoreBox = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 8) };
                eStoreBox.Style = null;
                eStoreBox.Items.Add("(전체)");
                foreach (var v in LoadDbDistinctValues("매장명")) eStoreBox.Items.Add(v);
                eStoreBox.SelectedItem = meta.DbStoreName ?? "(전체)";
                if (eStoreBox.SelectedIndex < 0) eStoreBox.SelectedIndex = 0;
                stack.Children.Add(eStoreBox);

                stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "중분류 필터:", Margin = new Thickness(0, 0, 0, 4) });
                var eMiddleCatBox = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 8) };
                eMiddleCatBox.Style = null;
                eMiddleCatBox.Items.Add("(전체)");
                foreach (var v in LoadDbDistinctValues("중분류")) eMiddleCatBox.Items.Add(v);
                eMiddleCatBox.SelectedItem = meta.DbMiddleCategoryFilter ?? "(전체)";
                if (eMiddleCatBox.SelectedIndex < 0) eMiddleCatBox.SelectedIndex = 0;
                stack.Children.Add(eMiddleCatBox);

                stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "메뉴명 필터:", Margin = new Thickness(0, 0, 0, 4) });
                var eMenuBox = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 8) };
                eMenuBox.Style = null;
                eMenuBox.Items.Add("(전체)");
                foreach (var v in LoadDbDistinctValues("메뉴명")) eMenuBox.Items.Add(v);
                eMenuBox.SelectedItem = meta.DbMenuNameFilter ?? "(전체)";
                if (eMenuBox.SelectedIndex < 0) eMenuBox.SelectedIndex = 0;
                stack.Children.Add(eMenuBox);

                // 정렬
                stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "정렬:", Margin = new Thickness(0, 0, 0, 4) });
                var eSortCombo = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 12) };
                eSortCombo.Style = null;
                eSortCombo.Items.Add("내림차순 (높은 값 먼저)");
                eSortCombo.Items.Add("오름차순 (낮은 값 먼저)");
                eSortCombo.SelectedIndex = meta.DbSortAscending ? 1 : 0;
                stack.Children.Add(eSortCombo);

                var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
                var applyBtn = new System.Windows.Controls.Button { Content = "적용", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
                var cancelBtn = new System.Windows.Controls.Button { Content = "취소", Padding = new Thickness(12, 6, 12, 6) };

                applyBtn.Click += (s, ev) =>
                {
                    meta.Width = double.Parse(widthBox.Text);
                    meta.Height = double.Parse(heightBox.Text);
                    meta.DbGroupBy = eGroupByCombo.SelectedItem?.ToString() ?? "매장명";
                    meta.DbValueColumn = eValueCombo.SelectedItem?.ToString() ?? "총매출액";
                    meta.DbStartDate = eStartDate.SelectedDate;
                    meta.DbEndDate = eEndDate.SelectedDate;
                    meta.DbStoreName = eStoreBox.SelectedIndex <= 0 ? null : eStoreBox.SelectedItem?.ToString();
                    meta.DbMiddleCategoryFilter = eMiddleCatBox.SelectedIndex <= 0 ? null : eMiddleCatBox.SelectedItem?.ToString();
                    meta.DbMenuNameFilter = eMenuBox.SelectedIndex <= 0 ? null : eMenuBox.SelectedItem?.ToString();
                    meta.DbSortAscending = eSortCombo.SelectedIndex == 1;

                    border.Width = meta.Width;
                    border.Height = meta.Height;

                    try { LoadDbData(meta); } catch { }

                    FrameworkElement newChart = null;
                    switch (meta.ChartType)
                    {
                        case "Line": newChart = CreateLineChart(meta); break;
                        case "Bar": newChart = CreateBarChart(meta); break;
                        case "HBar": newChart = CreateHBarChart(meta); break;
                        case "Pie": newChart = CreatePieChart(meta); break;
                        case "Gauge": newChart = CreateGaugeChart(meta); break;
                    }
                    if (newChart != null)
                        border.Child = newChart;

                    SaveAllChartStates();
                    configDlg.Close();
                };
                cancelBtn.Click += (s, ev) => configDlg.Close();
                btnPanel.Children.Add(applyBtn);
                btnPanel.Children.Add(cancelBtn);
                stack.Children.Add(btnPanel);
            }
            else
            {
                stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "외부 데이터 소스는 새로고침 버튼을 사용하세요.", Margin = new Thickness(0, 0, 0, 12) });
                var closeBtn = new System.Windows.Controls.Button { Content = "닫기", HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Padding = new Thickness(12, 6, 12, 6) };
                closeBtn.Click += (s, ev) => configDlg.Close();
                stack.Children.Add(closeBtn);
            }

            configDlg.Content = stack;
            ApplyDarkTheme(configDlg);
            configDlg.ShowDialog();
        }

        private async void RefreshChartData(Border border, ChartMeta meta)
        {
            try
            {
                switch (meta.DataSource)
                {
                    case "Json":
                        await LoadJsonData(meta);
                        break;
                    case "Csv":
                        LoadCsvData(meta);
                        break;
                    case "Api":
                        await LoadApiData(meta);
                        break;
                    case "Db":
                        LoadDbData(meta);
                        break;
                }

                // 차트 다시 생성
                FrameworkElement newChart = null;
                switch (meta.ChartType)
                {
                    case "Line": newChart = CreateLineChart(meta); break;
                    case "Bar": newChart = CreateBarChart(meta); break;
                    case "HBar": newChart = CreateHBarChart(meta); break;
                    case "Pie": newChart = CreatePieChart(meta); break;
                    case "Gauge": newChart = CreateGaugeChart(meta); break;
                }
                if (newChart != null)
                    border.Child = newChart;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"데이터 새로고침 오류: {ex.Message}");
            }
        }

        private async Task LoadJsonData(ChartMeta meta)
        {
            if (string.IsNullOrEmpty(meta.DataPath)) return;

            string json;
            if (File.Exists(meta.DataPath))
            {
                json = File.ReadAllText(meta.DataPath);
            }
            else if (Uri.IsWellFormedUriString(meta.DataPath, UriKind.Absolute))
            {
                using var client = new HttpClient();
                json = await client.GetStringAsync(meta.DataPath);
            }
            else
            {
                return;
            }

            var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            if (data != null)
            {
                if (data.ContainsKey("values"))
                {
                    var values = Newtonsoft.Json.JsonConvert.DeserializeObject<List<double>>(data["values"].ToString());
                    meta.StaticData = values ?? new List<double>();
                }
                if (data.ContainsKey("labels"))
                {
                    var labels = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(data["labels"].ToString());
                    meta.Labels = labels ?? new List<string>();
                }
            }
        }

        private void LoadCsvData(ChartMeta meta)
        {
            if (string.IsNullOrEmpty(meta.DataPath) || !File.Exists(meta.DataPath)) return;

            var lines = File.ReadAllLines(meta.DataPath);
            if (lines.Length > 0)
            {
                meta.Labels = lines[0].Split(',').Select(x => x.Trim()).ToList();
            }
            if (lines.Length > 1)
            {
                meta.StaticData = lines[1].Split(',').Select(x => double.TryParse(x.Trim(), out var v) ? v : 0).ToList();
            }
        }

        private async Task LoadApiData(ChartMeta meta)
        {
            if (string.IsNullOrEmpty(meta.DataPath)) return;

            using var client = new HttpClient();
            var json = await client.GetStringAsync(meta.DataPath);
            var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);

            if (data != null)
            {
                if (data.ContainsKey("values"))
                {
                    var values = Newtonsoft.Json.JsonConvert.DeserializeObject<List<double>>(data["values"].ToString());
                    meta.StaticData = values ?? new List<double>();
                }
                if (data.ContainsKey("labels"))
                {
                    var labels = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(data["labels"].ToString());
                    meta.Labels = labels ?? new List<string>();
                }
            }
        }

        private List<string> LoadDbDistinctValues(string column)
        {
            var result = new List<string>();
            try
            {
                const string cs = "Server=localhost\\SQLEXPRESS;Database=대진포스DB;Integrated Security=True;TrustServerCertificate=True;";
                using var conn = new SqlConnection(cs);
                conn.Open();
                using var cmd = new SqlCommand($"SELECT DISTINCT [{column}] FROM 매출데이터 WHERE [{column}] IS NOT NULL AND [{column}] <> '' ORDER BY [{column}]", conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) result.Add(reader.GetString(0));
            }
            catch { }
            return result;
        }

        private void LoadDbData(ChartMeta meta)
        {
            const string cs = "Server=localhost\\SQLEXPRESS;Database=대진포스DB;Integrated Security=True;TrustServerCertificate=True;";
            var groupBy = meta.DbGroupBy ?? "매장명";
            var valueCol = meta.DbValueColumn ?? "총매출액";
            var start = meta.DbStartDate ?? DateTime.Today.AddDays(-7);
            var end = meta.DbEndDate ?? DateTime.Today.AddDays(-1);
            var sortDir = meta.DbSortAscending ? "ASC" : "DESC";

            var sql = $@"
SELECT TOP 50 [{groupBy}], SUM([{valueCol}]) AS 값
FROM 매출데이터
WHERE 날짜 BETWEEN @start AND @end
  AND (@store IS NULL OR 매장명 = @store)
  AND (@middleCat IS NULL OR 중분류 = @middleCat)
  AND (@menuName IS NULL OR 메뉴명 = @menuName)
  AND [{groupBy}] IS NOT NULL AND [{groupBy}] <> ''
GROUP BY [{groupBy}]
ORDER BY 값 {sortDir}";

            var labels = new List<string>();
            var values = new List<double>();

            using (var conn = new SqlConnection(cs))
            {
                conn.Open();
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@start", start.Date);
                cmd.Parameters.AddWithValue("@end", end.Date);
                cmd.Parameters.AddWithValue("@store", (object?)meta.DbStoreName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@middleCat", (object?)meta.DbMiddleCategoryFilter ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@menuName", (object?)meta.DbMenuNameFilter ?? DBNull.Value);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    labels.Add(reader.IsDBNull(0) ? "" : reader.GetString(0));
                    values.Add(reader.IsDBNull(1) ? 0 : Convert.ToDouble(reader.GetValue(1)));
                }
            }

            meta.Labels = labels;
            meta.StaticData = values;
        }

        private void SaveAllChartStates()
        {
            // 차트 상태 저장 로직 (버튼 상태 저장과 유사)
            var allCharts = new List<object>();

            for (int tabIndex = 0; tabIndex < 10; tabIndex++)
            {
                var canvas = GetCanvasByIndex(tabIndex);
                if (canvas == null) continue;

                foreach (UIElement child in canvas.Children)
                {
                    if (child is Border border && border.Tag is ChartMeta meta)
                    {
                        var chartData = new
                        {
                            TabIndex = tabIndex,
                            X = System.Windows.Controls.Canvas.GetLeft(border),
                            Y = System.Windows.Controls.Canvas.GetTop(border),
                            ChartType = meta.ChartType,
                            DataSource = meta.DataSource,
                            DataPath = meta.DataPath,
                            StaticData = meta.StaticData,
                            Labels = meta.Labels,
                            Width = meta.Width,
                            Height = meta.Height,
                            Title = meta.Title,
                            RefreshInterval = meta.RefreshInterval,
                            DbStoreName = meta.DbStoreName,
                            DbStartDate = meta.DbStartDate,
                            DbEndDate = meta.DbEndDate,
                            DbValueColumn = meta.DbValueColumn,
                            DbGroupBy = meta.DbGroupBy,
                            DbSortAscending = meta.DbSortAscending,
                            DbMiddleCategoryFilter = meta.DbMiddleCategoryFilter,
                            DbMenuNameFilter = meta.DbMenuNameFilter
                        };
                        allCharts.Add(chartData);
                    }
                }
            }

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(allCharts, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(ChartStateFile, json);
        }

        private void RestoreAllChartStates()
        {
            if (!File.Exists(ChartStateFile)) return;

            try
            {
                var json = File.ReadAllText(ChartStateFile);
                var chartList = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);

                if (chartList == null) return;

                foreach (var chartData in chartList)
                {
                    int tabIndex = Convert.ToInt32(chartData["TabIndex"]);
                    var canvas = GetCanvasByIndex(tabIndex);
                    if (canvas == null) continue;

                    var meta = new ChartMeta
                    {
                        ChartType = chartData["ChartType"].ToString(),
                        DataSource = chartData["DataSource"].ToString(),
                        DataPath = chartData.ContainsKey("DataPath") ? chartData["DataPath"]?.ToString() : null,
                        Width = Convert.ToDouble(chartData["Width"]),
                        Height = Convert.ToDouble(chartData["Height"]),
                        Title = chartData.ContainsKey("Title") ? chartData["Title"]?.ToString() ?? "" : "",
                        RefreshInterval = chartData.ContainsKey("RefreshInterval") ? Convert.ToInt32(chartData["RefreshInterval"]) : 0,
                        DbStoreName = chartData.ContainsKey("DbStoreName") ? chartData["DbStoreName"]?.ToString() : null,
                        DbStartDate = chartData.ContainsKey("DbStartDate") && chartData["DbStartDate"] != null
                            ? DateTime.TryParse(chartData["DbStartDate"].ToString(), out var ds) ? ds : (DateTime?)null : null,
                        DbEndDate = chartData.ContainsKey("DbEndDate") && chartData["DbEndDate"] != null
                            ? DateTime.TryParse(chartData["DbEndDate"].ToString(), out var de) ? de : (DateTime?)null : null,
                        DbValueColumn = chartData.ContainsKey("DbValueColumn") ? chartData["DbValueColumn"]?.ToString() ?? "총매출액" : "총매출액",
                        DbGroupBy = chartData.ContainsKey("DbGroupBy") ? chartData["DbGroupBy"]?.ToString() ?? "매장명" : "매장명",
                        DbSortAscending = chartData.ContainsKey("DbSortAscending") && Convert.ToBoolean(chartData["DbSortAscending"]),
                        DbMiddleCategoryFilter = chartData.ContainsKey("DbMiddleCategoryFilter") ? chartData["DbMiddleCategoryFilter"]?.ToString() : null,
                        DbMenuNameFilter = chartData.ContainsKey("DbMenuNameFilter") ? chartData["DbMenuNameFilter"]?.ToString() : null
                    };

                    // StaticData 복원
                    if (chartData.ContainsKey("StaticData"))
                    {
                        var staticDataJson = chartData["StaticData"].ToString();
                        meta.StaticData = Newtonsoft.Json.JsonConvert.DeserializeObject<List<double>>(staticDataJson) ?? new List<double>();
                    }

                    // Labels 복원
                    if (chartData.ContainsKey("Labels"))
                    {
                        var labelsJson = chartData["Labels"].ToString();
                        meta.Labels = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(labelsJson) ?? new List<string>();
                    }

                    // Border 생성 (SetResourceReference로 테마 동적 적용)
                    var border = new System.Windows.Controls.Border
                    {
                        Width = meta.Width,
                        Height = meta.Height,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Tag = meta
                    };
                    border.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "StatusBarBackgroundBrush");
                    border.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "StatusBarBorderBrush");

                    // 위치 설정
                    double x = Convert.ToDouble(chartData["X"]);
                    double y = Convert.ToDouble(chartData["Y"]);
                    System.Windows.Controls.Canvas.SetLeft(border, x);
                    System.Windows.Controls.Canvas.SetTop(border, y);

                    // 차트 컨트롤 생성
                    FrameworkElement chartControl = null;
                    switch (meta.ChartType)
                    {
                        case "Line":
                            chartControl = CreateLineChart(meta);
                            break;
                        case "Bar":
                            chartControl = CreateBarChart(meta);
                            break;
                        case "HBar":
                            chartControl = CreateHBarChart(meta);
                            break;
                        case "Pie":
                            chartControl = CreatePieChart(meta);
                            break;
                        case "Gauge":
                            chartControl = CreateGaugeChart(meta);
                            break;
                    }

                    if (chartControl != null)
                    {
                        if (chartControl is System.Windows.Controls.Control ctrl2)
                            ctrl2.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "StatusBarBackgroundBrush");
                        border.Child = chartControl;
                    }

                    // 컨텍스트 메뉴 추가
                    var contextMenu = new System.Windows.Controls.ContextMenu();
                    var editItem = new System.Windows.Controls.MenuItem { Header = "차트 수정" };
                    var deleteItem = new System.Windows.Controls.MenuItem { Header = "차트 삭제" };
                    var refreshItem = new System.Windows.Controls.MenuItem { Header = "새로고침" };

                    editItem.Click += (s, ev) => EditChartDialog(border, canvas, meta);
                    deleteItem.Click += (s, ev) =>
                    {
                        if (System.Windows.MessageBox.Show("차트를 삭제하시겠습니까?", "확인", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        {
                            if (meta.RefreshTimer != null)
                            {
                                meta.RefreshTimer.Stop();
                                meta.RefreshTimer = null;
                            }
                            canvas.Children.Remove(border);
                            SaveAllChartStates();
                        }
                    };
                    refreshItem.Click += (s, ev) => RefreshChartData(border, meta);

                    contextMenu.Items.Add(editItem);
                    contextMenu.Items.Add(refreshItem);
                    contextMenu.Items.Add(deleteItem);
                    border.ContextMenu = contextMenu;

                    // 드래그 핸들러
                    AttachChartDragHandlers(border, canvas);

                    // Canvas에 추가
                    canvas.Children.Add(border);

                    // 실시간 업데이트 타이머 설정
                    if (meta.RefreshInterval > 0 && meta.DataSource == "Api")
                    {
                        meta.RefreshTimer = new DispatcherTimer
                        {
                            Interval = TimeSpan.FromSeconds(meta.RefreshInterval)
                        };
                        meta.RefreshTimer.Tick += (s, ev) => RefreshChartData(border, meta);
                        meta.RefreshTimer.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RestoreAllChartStates error: " + ex);
            }
        }

        private Canvas? GetCanvasByIndex(int index)
        {
            return FindName($"ButtonCanvas{index + 1}") as Canvas;
        }

        private Canvas? GetCanvasFromContextMenuSender(object sender)
        {
            if (sender is MenuItem menuItem &&
                menuItem.Parent is ContextMenu cm &&
                cm.PlacementTarget is Border b)
            {
                if (b.Child is Canvas c) return c;
                if (b.Child is System.Windows.Controls.ScrollViewer sv && sv.Content is Canvas sc) return sc;
            }
            return null;
        }

        private Canvas? CurrentButtonCanvas
        {
            get
            {
                if (tabControl == null) return null;
                int idx = tabControl.SelectedIndex;
                if (idx < 0) idx = 0;
                return GetCanvasByIndex(idx);
            }
        }

        private void Btn_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Shift 키가 눌렸거나 이벤트가 이미 처리되었으면 Ripple 효과 건너뛰기
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || e.Handled)
                return;

            if (sender is not System.Windows.Controls.Button btn) return;
            var host = btn.Template?.FindName("RippleHost", btn) as System.Windows.Controls.Canvas;
            if (host == null) return;
            var p = e.GetPosition(btn);
            double w = btn.ActualWidth > 0 ? btn.ActualWidth : btn.Width;
            double h = btn.ActualHeight > 0 ? btn.ActualHeight : btn.Height;
            if (w <= 0 || h <= 0) return;
            double dx = Math.Max(p.X, w - p.X);
            double dy = Math.Max(p.Y, h - p.Y);
            double radius = Math.Sqrt(dx * dx + dy * dy);
            double diameter = radius * 2.0;
            var ripple = new System.Windows.Shapes.Ellipse
            {
                Width = diameter,
                Height = diameter,
                Opacity = 0.35,
                Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new System.Windows.Media.ScaleTransform(0.0, 0.0)
            };
            System.Windows.Controls.Canvas.SetLeft(ripple, p.X - radius);
            System.Windows.Controls.Canvas.SetTop(ripple, p.Y - radius);
            host.Children.Add(ripple);
            var scale = (System.Windows.Media.ScaleTransform)ripple.RenderTransform;
            var duration = TimeSpan.FromMilliseconds(480);
            var daX = new DoubleAnimation(1.0, new Duration(duration)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            var daY = new DoubleAnimation(1.0, new Duration(duration)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            var fade = new DoubleAnimation(0.0, new Duration(TimeSpan.FromMilliseconds(380))) { BeginTime = TimeSpan.FromMilliseconds(120) };
            fade.Completed += (s, _) => host.Children.Remove(ripple);
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, daX);
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, daY);
            ripple.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void DynamicButtonBorder_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            CurrentButtonCanvas?.UpdateLayout();
        }

        private void ApplyDarkTheme(Window dlg)
        {
            if (dlg.Tag is string tag && tag == "DarkThemedSimple") return;
            var res = System.Windows.Application.Current.Resources;
            var windowBg = (res["WindowBackgroundBrush"] as System.Windows.Media.Brush)
                           ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 44, 44));
            var panelBg = windowBg;
            var buttonBg = (res["ContextMenuBorderBrush"] as System.Windows.Media.Brush)
                           ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 58, 58));
            var hoverBg = (res["ContextMenuItemHoverBrush"] as System.Windows.Media.Brush)
                          ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(69, 69, 69));
            var fgBrush = (res["ForegroundBrush"] as System.Windows.Media.Brush)
                          ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
            dlg.Background = windowBg;
            dlg.Foreground = fgBrush;
            dlg.Tag = "DarkThemedSimple";
            dlg.ShowInTaskbar = false;
            void StyleElement(System.Windows.FrameworkElement fe)
            {
                switch (fe)
                {
                    case System.Windows.Controls.Button b:
                        b.Background = buttonBg;
                        b.Foreground = fgBrush;
                        b.BorderThickness = new System.Windows.Thickness(0);
                        b.Padding = new System.Windows.Thickness(8, 4, 8, 4);
                        b.Cursor = System.Windows.Input.Cursors.Hand;
                        break;
                    case System.Windows.Controls.TextBox tb:
                        tb.Background = buttonBg;
                        tb.Foreground = fgBrush;
                        tb.BorderBrush = (res["StatusBarBorderBrush"] as System.Windows.Media.Brush)
                                         ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 85, 85));
                        tb.CaretBrush = fgBrush;
                        break;
                    case System.Windows.Controls.TextBlock t:
                        t.Foreground = fgBrush;
                        break;
                    case System.Windows.Controls.ComboBox cb:
                        cb.Background = buttonBg;
                        cb.Foreground = System.Windows.Media.Brushes.Black;
                        cb.BorderThickness = new System.Windows.Thickness(0);
                        break;
                    case System.Windows.Controls.ListBox lb:
                        lb.Background = buttonBg;
                        lb.Foreground = fgBrush;
                        break;
                    case System.Windows.Controls.Border border:
                        if (border.Background == null || border.Background == System.Windows.Media.Brushes.Transparent)
                            border.Background = panelBg;
                        break;
                    case System.Windows.Controls.Panel pnl:
                        if (pnl.Background == null || pnl.Background == System.Windows.Media.Brushes.Transparent)
                            pnl.Background = panelBg;
                        break;
                }
            }
            void Traverse(object? obj)
            {
                if (obj is System.Windows.Controls.Panel p)
                {
                    StyleElement(p);
                    foreach (var child in p.Children)
                    {
                        if (child is System.Windows.FrameworkElement fe)
                        {
                            StyleElement(fe);
                            Traverse(fe);
                        }
                    }
                }
                else if (obj is System.Windows.Controls.ContentControl cc)
                {
                    if (cc.Content is System.Windows.FrameworkElement fe)
                    {
                        StyleElement(fe);
                        Traverse(fe);
                    }
                }
                else if (obj is System.Windows.Controls.Border b)
                {
                    StyleElement(b);
                    if (b.Child is System.Windows.FrameworkElement fe)
                    {
                        StyleElement(fe);
                        Traverse(fe);
                    }
                }
                else if (obj is System.Windows.Controls.ItemsControl ic)
                {
                    StyleElement(ic);
                    foreach (var item in ic.Items)
                    {
                        if (item is System.Windows.FrameworkElement fe2)
                        {
                            StyleElement(fe2);
                            Traverse(fe2);
                        }
                    }
                }
            }
            Traverse(dlg.Content);
        }

        private void ShowPositionAdjustDialog(System.Windows.Controls.Button targetBtn)
        {
            var posDlg = new Window
            {
                Title = "버튼 위치 조정",
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight
            };
            MakeBorderless(posDlg);

            var stack = new StackPanel { Margin = new Thickness(16) };

            var xyRow = new WrapPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var xLabel = new TextBlock { Text = "X:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            var xBox = new System.Windows.Controls.TextBox { Width = 90, Height = 28, Text = $"{System.Windows.Controls.Canvas.GetLeft(targetBtn)}", Margin = new Thickness(0, 0, 8, 8), VerticalContentAlignment = VerticalAlignment.Center };
            var yLabel = new TextBlock { Text = "Y:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            var yBox = new System.Windows.Controls.TextBox { Width = 90, Height = 28, Text = $"{System.Windows.Controls.Canvas.GetTop(targetBtn)}", Margin = new Thickness(0, 0, 0, 8), VerticalContentAlignment = VerticalAlignment.Center };
            xyRow.Children.Add(xLabel); xyRow.Children.Add(xBox); xyRow.Children.Add(yLabel); xyRow.Children.Add(yBox);

            var mousePosLabel = new TextBlock { Text = "현재 마우스 위치: -", Margin = new Thickness(0, 0, 0, 8) };

            var buttonsRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Vertical, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            var okBtn = new System.Windows.Controls.Button { Content = "적용", Margin = new System.Windows.Thickness(0, 0, 0, 8), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch, Height = 32 };
            var mouseBtn = new System.Windows.Controls.Button { Content = "마우스 위치로 이동", Margin = new System.Windows.Thickness(0, 0, 0, 8), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch, Height = 32 };
            var cancelBtn = new System.Windows.Controls.Button { Content = "취소", Margin = new System.Windows.Thickness(0, 0, 0, 0), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch, Height = 32 };
            buttonsRow.Children.Add(okBtn); buttonsRow.Children.Add(mouseBtn); buttonsRow.Children.Add(cancelBtn);

            System.Windows.Point lastMouseCanvasPos = new System.Windows.Point(System.Windows.Controls.Canvas.GetLeft(targetBtn), System.Windows.Controls.Canvas.GetTop(targetBtn));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            timer.Tick += (s, e) =>
            {
                var canvas = CurrentButtonCanvas;
                if (canvas == null)
                {
                    mousePosLabel.Text = "현재 마우스 위치: (캔버스 없음)";
                    return;
                }
                var mp = Forms.Control.MousePosition;
                var pt = canvas.PointFromScreen(new System.Windows.Point(mp.X, mp.Y));
                if (pt.X < 0) pt.X = 0; if (pt.Y < 0) pt.Y = 0;
                if (pt.X > canvas.ActualWidth) pt.X = canvas.ActualWidth;
                if (pt.Y > canvas.ActualHeight) pt.Y = canvas.ActualHeight;
                lastMouseCanvasPos = pt;
                mousePosLabel.Text = $"현재 마우스 위치: X={pt.X:0}, Y={pt.Y:0}";
            };
            timer.Start();

            okBtn.Click += (s, e) =>
            {
                try
                {
                    if (double.TryParse(xBox.Text, out double x2) && double.TryParse(yBox.Text, out double y2))
                    {
                        System.Windows.Controls.Canvas.SetLeft(targetBtn, x2);
                        System.Windows.Controls.Canvas.SetTop(targetBtn, y2);
                        if (targetBtn.Tag is ButtonMeta meta && !meta.LabelInside && meta.LabelBlock != null && CurrentButtonCanvas != null)
                        {
                            EnsureOrUpdateButtonLabel(CurrentButtonCanvas, targetBtn, meta);
                        }
                    }
                    SaveAllButtonStates();
                }
                finally
                {
                    timer.Stop();
                    posDlg.Close();
                }
            };

            mouseBtn.Click += (s, e) =>
            {
                var canvas = CurrentButtonCanvas;
                if (canvas != null)
                {
                    System.Windows.Controls.Canvas.SetLeft(targetBtn, lastMouseCanvasPos.X);
                    System.Windows.Controls.Canvas.SetTop(targetBtn, lastMouseCanvasPos.Y);
                    if (targetBtn.Tag is ButtonMeta meta && !meta.LabelInside && meta.LabelBlock != null)
                        EnsureOrUpdateButtonLabel(canvas, targetBtn, meta);
                    SaveAllButtonStates();
                }
                timer.Stop();
                posDlg.Close();
            };
            cancelBtn.Click += (s, e) => { timer.Stop(); posDlg.Close(); };

            stack.Children.Add(xyRow);
            stack.Children.Add(mousePosLabel);
            stack.Children.Add(buttonsRow);
            posDlg.Content = stack;
            ApplyDarkTheme(posDlg);
            posDlg.Loaded += (s, e) => StretchVerticalButtons(stack, 0, okBtn, mouseBtn, cancelBtn);
            posDlg.ShowDialog();
        }

        private void ShowSizeAdjustDialog(System.Windows.Controls.Button targetBtn, System.Windows.Controls.Canvas canvas, ButtonMeta meta)
        {
            var sizeDlg = new System.Windows.Window
            {
                Title = "버튼/이미지 크기 및 위치 조정",
                WindowStartupLocation = this.IsVisible ? System.Windows.WindowStartupLocation.CenterOwner : System.Windows.WindowStartupLocation.CenterScreen,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 620
            };
            if (this.IsVisible) sizeDlg.Owner = this;
            sizeDlg.Topmost = true;
            MakeBorderless(sizeDlg);
            var rootGrid = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(16) };
            rootGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            rootGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(40) });
            rootGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

            // 왼쪽 패널: 버튼 크기 + 미리보기
            var leftPanel = new System.Windows.Controls.StackPanel();
            leftPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "버튼 크기", FontWeight = System.Windows.FontWeights.Bold, Margin = new System.Windows.Thickness(0, 0, 0, 8) });
            var wBox2 = new System.Windows.Controls.TextBox { Text = targetBtn.Width.ToString(), Width = 80, Margin = new System.Windows.Thickness(0, 0, 0, 8) };
            var hBox2 = new System.Windows.Controls.TextBox { Text = targetBtn.Height.ToString(), Width = 80, Margin = new System.Windows.Thickness(0, 0, 0, 8) };
            leftPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "너비:" });
            leftPanel.Children.Add(wBox2);
            leftPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "높이:" });
            leftPanel.Children.Add(hBox2);

            // 미리보기 영역
            var previewBorder = new System.Windows.Controls.Border 
            { 
                Width = 120, 
                Height = 120, 
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70)),
                BorderThickness = new Thickness(1),
                Margin = new System.Windows.Thickness(0, 8, 0, 8),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50))
            };
            var previewImage = new System.Windows.Controls.Image 
            { 
                Stretch = System.Windows.Media.Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            var currentImg = GetButtonImageControl(targetBtn);
            if (currentImg?.Source != null)
            {
                previewImage.Source = currentImg.Source;
            }
            previewBorder.Child = previewImage;
            leftPanel.Children.Add(previewBorder);

            // 이미지 선택 버튼
            var selectImageBtn = new System.Windows.Controls.Button 
            { 
                Content = "이미지 선택", 
                Margin = new System.Windows.Thickness(0, 0, 0, 8),
                Padding = new Thickness(8, 4, 8, 4)
            };
            selectImageBtn.Click += (s, e) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "이미지 선택",
                    Filter = "이미지 파일|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
                };
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        var src = TryLoadImageSource(dialog.FileName);
                        if (src == null) return;
                        previewImage.Source = src;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Set image error: " + ex);
                        System.Windows.MessageBox.Show("이미지를 설정할 수 없습니다.\n" + ex.Message);
                    }
                }
            };
            leftPanel.Children.Add(selectImageBtn);

            var rightPanel = new System.Windows.Controls.StackPanel();
            rightPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "이미지 크기 및 위치", FontWeight = System.Windows.FontWeights.Bold, Margin = new System.Windows.Thickness(0, 0, 0, 8) });
            var imgRef = GetButtonImageControl(targetBtn);
            double initImgW2 = imgRef?.Width > 0 ? imgRef.Width : targetBtn.Width * 0.8;
            double initImgH2 = imgRef?.Height > 0 ? imgRef.Height : targetBtn.Height * 0.8;
            var iwBox2 = new System.Windows.Controls.TextBox { Text = initImgW2.ToString(), Width = 80, Margin = new System.Windows.Thickness(0, 0, 0, 8) };
            var ihBox2 = new System.Windows.Controls.TextBox { Text = initImgH2.ToString(), Width = 80, Margin = new System.Windows.Thickness(0, 0, 0, 8) };
            rightPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "이미지 너비:" });
            rightPanel.Children.Add(iwBox2);
            rightPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "이미지 높이:" });
            rightPanel.Children.Add(ihBox2);
            rightPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "위치 선택:", Margin = new System.Windows.Thickness(0, 8, 0, 4) });
            var posWrap2 = new System.Windows.Controls.WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
            string[] posNames2 = { "중앙", "위", "아래", "왼쪽", "오른쪽" };
            var posButtons2 = new System.Collections.Generic.List<System.Windows.Controls.Button>();

            // 기본값은 항상 중앙, 기존 이미지가 있을 경우에만 해당 위치로 설정
            string current2 = "중앙";
            if (imgRef != null && imgRef.Source != null)
            {
                if (imgRef.VerticalAlignment == System.Windows.VerticalAlignment.Top) current2 = "위";
                else if (imgRef.VerticalAlignment == System.Windows.VerticalAlignment.Bottom) current2 = "아래";
                else if (imgRef.HorizontalAlignment == System.Windows.HorizontalAlignment.Left) current2 = "왼쪽";
                else if (imgRef.HorizontalAlignment == System.Windows.HorizontalAlignment.Right) current2 = "오른쪽";
            }
            var normalBrush2 = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70));
            var selectedBrush2 = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(110, 110, 110));
            void UpdateButtonStyles2()
            {
                foreach (var b in posButtons2)
                {
                    bool sel = (string)b.Content == current2;
                    b.Background = sel ? selectedBrush2 : normalBrush2;
                    b.BorderThickness = sel ? new Thickness(2) : new Thickness(0);
                    b.BorderBrush = sel ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 160, 160)) : null;
                }
            }
            foreach (var p in posNames2)
            {
                var b = new System.Windows.Controls.Button { Content = p, Margin = new System.Windows.Thickness(0, 0, 8, 8), MinWidth = 60, Padding = new System.Windows.Thickness(8, 4, 8, 4) };
                b.Click += (s, e) => { current2 = p; UpdateButtonStyles2(); };
                posButtons2.Add(b);
                posWrap2.Children.Add(b);
            }
            rightPanel.Children.Add(posWrap2);
            UpdateButtonStyles2();

            // ── 배경색 ──────────────────────────────────────────────
            string? selectedBgColor = targetBtn.ReadLocalValue(System.Windows.Controls.Control.BackgroundProperty) != DependencyProperty.UnsetValue
                ? ((targetBtn.Background as System.Windows.Media.SolidColorBrush)?.Color.A == 0 ? "transparent"
                    : (targetBtn.Background as System.Windows.Media.SolidColorBrush)?.Color.ToString())
                : null;

            rightPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "배경색", FontWeight = System.Windows.FontWeights.Bold, Margin = new System.Windows.Thickness(0, 8, 0, 4) });

            var bgWrap = new System.Windows.Controls.WrapPanel { Margin = new System.Windows.Thickness(0, 0, 0, 4) };
            var bgSwatches = new System.Collections.Generic.List<System.Windows.Controls.Border>();

            System.Windows.Controls.Border MakeSwatch(string? colorKey, string label, System.Windows.Media.Brush fill)
            {
                var outer = new System.Windows.Controls.Border
                {
                    Width = 26, Height = 26, CornerRadius = new System.Windows.CornerRadius(4),
                    BorderThickness = new System.Windows.Thickness(2),
                    BorderBrush = System.Windows.Media.Brushes.Transparent,
                    Margin = new System.Windows.Thickness(0, 0, 4, 4),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = label
                };
                var inner = new System.Windows.Controls.Border
                {
                    Background = fill, CornerRadius = new System.Windows.CornerRadius(2)
                };
                if (colorKey == "transparent")
                {
                    var grid = new System.Windows.Controls.Grid();
                    grid.Children.Add(new System.Windows.Controls.Border { Background = System.Windows.Media.Brushes.White, CornerRadius = new System.Windows.CornerRadius(2) });
                    var line = new System.Windows.Shapes.Line { X1 = 0, Y1 = 26, X2 = 26, Y2 = 0, Stroke = System.Windows.Media.Brushes.Red, StrokeThickness = 1.5 };
                    grid.Children.Add(line);
                    inner.Child = grid;
                }
                outer.Child = inner;
                return outer;
            }

            void RefreshBgSwatchBorders()
            {
                foreach (var sw in bgSwatches)
                {
                    var key = sw.Tag as string;
                    bool sel = selectedBgColor == key;
                    sw.BorderBrush = sel ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent;
                }
            }

            void AddBgSwatch(string? colorKey, string label, System.Windows.Media.Brush fill)
            {
                var sw = MakeSwatch(colorKey, label, fill);
                sw.Tag = colorKey;
                sw.MouseLeftButtonDown += (s2, _) => { selectedBgColor = colorKey; RefreshBgSwatchBorders(); };
                bgSwatches.Add(sw);
                bgWrap.Children.Add(sw);
            }

            AddBgSwatch(null, "기본값(테마)", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 80)));
            AddBgSwatch("transparent", "없음(투명)", System.Windows.Media.Brushes.Transparent);

            var bgColors = new[] {
                ("#1E1E1E","검정"), ("#3A3A3A","진회색"), ("#707070","회색"), ("#C8C8C8","밝은 회색"),
                ("#FFFFFF","흰색"), ("#C0392B","빨강"), ("#E67E22","주황"), ("#F1C40F","노랑"),
                ("#27AE60","초록"), ("#2980B9","파랑"), ("#8E44AD","보라"), ("#E91E63","핑크")
            };
            foreach (var (hex, name) in bgColors)
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                AddBgSwatch(hex, name, new System.Windows.Media.SolidColorBrush(c));
            }
            rightPanel.Children.Add(bgWrap);
            RefreshBgSwatchBorders();

            // ── 글씨색 ──────────────────────────────────────────────
            string? selectedFgColor = targetBtn.ReadLocalValue(System.Windows.Controls.Control.ForegroundProperty) != DependencyProperty.UnsetValue
                ? (targetBtn.Foreground as System.Windows.Media.SolidColorBrush)?.Color.ToString()
                : null;

            rightPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "글씨색", FontWeight = System.Windows.FontWeights.Bold, Margin = new System.Windows.Thickness(0, 4, 0, 4) });

            var fgWrap = new System.Windows.Controls.WrapPanel { Margin = new System.Windows.Thickness(0, 0, 0, 4) };
            var fgSwatches = new System.Collections.Generic.List<System.Windows.Controls.Border>();

            void RefreshFgSwatchBorders()
            {
                foreach (var sw in fgSwatches)
                {
                    var key = sw.Tag as string;
                    bool sel = selectedFgColor == key;
                    sw.BorderBrush = sel ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent;
                }
            }

            void AddFgSwatch(string? colorKey, string label, System.Windows.Media.Brush fill)
            {
                var sw = MakeSwatch(colorKey, label, fill);
                sw.Tag = colorKey;
                sw.MouseLeftButtonDown += (s2, _) => { selectedFgColor = colorKey; RefreshFgSwatchBorders(); };
                fgSwatches.Add(sw);
                fgWrap.Children.Add(sw);
            }

            AddFgSwatch(null, "기본값(테마)", new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 80)));
            var fgColors = new[] {
                ("#FFFFFF","흰색"), ("#C8C8C8","밝은 회색"), ("#909090","회색"), ("#1E1E1E","검정"),
                ("#C0392B","빨강"), ("#E67E22","주황"), ("#F1C40F","노랑"), ("#27AE60","초록"),
                ("#2980B9","파랑"), ("#8E44AD","보라"), ("#E91E63","핑크")
            };
            foreach (var (hex, name) in fgColors)
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                AddFgSwatch(hex, name, new System.Windows.Media.SolidColorBrush(c));
            }
            rightPanel.Children.Add(fgWrap);
            RefreshFgSwatchBorders();

            // ── 글꼴 ────────────────────────────────────────────────
            string? selectedFontFamily = targetBtn.ReadLocalValue(System.Windows.Controls.Control.FontFamilyProperty) != DependencyProperty.UnsetValue
                ? targetBtn.FontFamily?.Source : null;

            rightPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "글꼴", FontWeight = System.Windows.FontWeights.Bold, Margin = new System.Windows.Thickness(0, 4, 0, 4) });
            var fontRow = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var darkComboBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50));
            var darkComboHover = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 75, 75));
            var fontCombo = new System.Windows.Controls.ComboBox
            {
                IsEditable = true,
                Width = 180,
                Style = null,
                Background = darkComboBg,
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(90, 90, 90))
            };
            // 드롭다운 팝업 배경색 다크 테마 적용
            fontCombo.Resources[System.Windows.SystemColors.WindowBrushKey] = darkComboBg;
            fontCombo.Resources[System.Windows.SystemColors.WindowTextBrushKey] = System.Windows.Media.Brushes.White;
            fontCombo.Resources[System.Windows.SystemColors.HighlightBrushKey] = darkComboHover;
            fontCombo.Resources[System.Windows.SystemColors.HighlightTextBrushKey] = System.Windows.Media.Brushes.White;
            // 각 아이템 스타일: 어두운 배경 + 흰 글자
            var comboItemStyle = new Style(typeof(System.Windows.Controls.ComboBoxItem));
            comboItemStyle.Setters.Add(new Setter(System.Windows.Controls.ComboBoxItem.BackgroundProperty, darkComboBg));
            comboItemStyle.Setters.Add(new Setter(System.Windows.Controls.ComboBoxItem.ForegroundProperty, System.Windows.Media.Brushes.White));
            comboItemStyle.Setters.Add(new Setter(System.Windows.Controls.ComboBoxItem.PaddingProperty, new Thickness(6, 3, 6, 3)));
            var hoverTrigger = new Trigger { Property = System.Windows.Controls.ComboBoxItem.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(System.Windows.Controls.ComboBoxItem.BackgroundProperty, darkComboHover));
            comboItemStyle.Triggers.Add(hoverTrigger);
            var selectedTrigger = new Trigger { Property = System.Windows.Controls.ComboBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(System.Windows.Controls.ComboBoxItem.BackgroundProperty, darkComboHover));
            comboItemStyle.Triggers.Add(selectedTrigger);
            fontCombo.ItemContainerStyle = comboItemStyle;
            string[] preferredFonts = { "Malgun Gothic", "맑은 고딕", "Gulim", "굴림", "Dotum", "돋움", "Batang", "바탕", "Segoe UI", "Arial", "Tahoma", "Consolas" };
            foreach (var f in preferredFonts) fontCombo.Items.Add(f);
            fontCombo.Text = selectedFontFamily ?? "";
            fontCombo.SelectionChanged += (s2, _) => { selectedFontFamily = string.IsNullOrWhiteSpace(fontCombo.Text) ? null : fontCombo.Text; };
            fontCombo.LostFocus += (s2, _) => { selectedFontFamily = string.IsNullOrWhiteSpace(fontCombo.Text) ? null : fontCombo.Text; };

            var fontDefaultBtn = new System.Windows.Controls.Button { Content = "기본값", Margin = new System.Windows.Thickness(6, 0, 0, 0), Padding = new System.Windows.Thickness(6, 2, 6, 2) };
            fontDefaultBtn.Click += (s2, _) => { selectedFontFamily = null; fontCombo.Text = ""; };
            fontRow.Children.Add(fontCombo);
            fontRow.Children.Add(fontDefaultBtn);
            rightPanel.Children.Add(fontRow);

            System.Windows.Controls.Grid.SetColumn(leftPanel, 0); System.Windows.Controls.Grid.SetColumn(rightPanel, 2);
            rootGrid.Children.Add(leftPanel); rootGrid.Children.Add(rightPanel);
            var bottomPanel2 = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new System.Windows.Thickness(0, 12, 0, 0) };
            var applyBtn2 = new System.Windows.Controls.Button { Content = "적용", Margin = new System.Windows.Thickness(0, 0, 8, 0), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Height = 32 };
            var cancelBtn22 = new System.Windows.Controls.Button { Content = "취소", Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Height = 32 };
            bottomPanel2.Children.Add(applyBtn2);
            bottomPanel2.Children.Add(cancelBtn22);
            rootGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            rootGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            System.Windows.Controls.Grid.SetRow(leftPanel, 0);
            System.Windows.Controls.Grid.SetRow(rightPanel, 0);
            System.Windows.Controls.Grid.SetRow(bottomPanel2, 1);
            System.Windows.Controls.Grid.SetColumnSpan(bottomPanel2, 3);
            rootGrid.Children.Add(bottomPanel2);
            applyBtn2.Click += (s, e) =>
            {
                try
                {
                    // 이미지를 먼저 적용
                    if (previewImage.Source != null)
                    {
                        var imgCtrl = GetButtonImageControl(targetBtn) ?? new System.Windows.Controls.Image();
                        imgCtrl.Source = previewImage.Source;
                        imgCtrl.Stretch = System.Windows.Media.Stretch.Uniform;
                    }

                    double bw, bh, iw, ih;
                    double? nbw = double.TryParse(wBox2.Text, out bw) ? bw : (double?)null;
                    double? nbh = double.TryParse(hBox2.Text, out bh) ? bh : (double?)null;
                    iw = double.TryParse(iwBox2.Text, out var iwt) ? iwt : (GetButtonImageControl(targetBtn)?.Width ?? targetBtn.Width * 0.8);
                    ih = double.TryParse(ihBox2.Text, out var iht) ? iht : (GetButtonImageControl(targetBtn)?.Height ?? targetBtn.Height * 0.8);
                    ApplyImageSizeAndPosition(targetBtn, canvas, meta, nbw, nbh, iw, ih, current2);

                    // 배경색 적용
                    if (selectedBgColor == null)
                        targetBtn.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
                    else if (selectedBgColor == "transparent")
                        targetBtn.Background = System.Windows.Media.Brushes.Transparent;
                    else
                    {
                        var bc = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(selectedBgColor);
                        targetBtn.Background = new System.Windows.Media.SolidColorBrush(bc);
                    }

                    // 글씨색 적용
                    if (selectedFgColor == null)
                        targetBtn.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
                    else
                    {
                        var fc = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(selectedFgColor);
                        targetBtn.Foreground = new System.Windows.Media.SolidColorBrush(fc);
                    }

                    // 글꼴 적용
                    if (selectedFontFamily == null)
                        targetBtn.ClearValue(System.Windows.Controls.Control.FontFamilyProperty);
                    else
                        targetBtn.FontFamily = new System.Windows.Media.FontFamily(selectedFontFamily);

                    SaveAllButtonStates();
                    sizeDlg.Close();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Resize apply error: " + ex);
                    System.Windows.MessageBox.Show("크기 적용 중 오류 발생");
                }
            };
            cancelBtn22.Click += (s, e) => sizeDlg.Close();
            sizeDlg.Content = rootGrid;
            ApplyDarkTheme(sizeDlg);
            try { sizeDlg.ShowDialog(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Show size dialog error: " + ex);
                System.Windows.MessageBox.Show("크기 조정 창을 표시할 수 없습니다.");
            }
        }

        private static void StretchVerticalButtons(FrameworkElement container, int sideMargin, params System.Windows.Controls.Button[] buttons)
        {
            container.Dispatcher.InvokeAsync(() =>
            {
                double w = container.ActualWidth - sideMargin * 2;
                if (w <= 0) return;
                foreach (var b in buttons)
                {
                    b.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                    b.Width = w;
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ApplyImageSizeAndPosition(System.Windows.Controls.Button targetBtn, System.Windows.Controls.Canvas canvas, ButtonMeta meta,
            double? newBtnWidth, double? newBtnHeight, double newImgWidth, double newImgHeight, string position)
        {
            if (newBtnWidth.HasValue) targetBtn.Width = newBtnWidth.Value;
            if (newBtnHeight.HasValue) targetBtn.Height = newBtnHeight.Value;
            targetBtn.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
            targetBtn.VerticalContentAlignment = System.Windows.VerticalAlignment.Stretch;
            var img = GetButtonImageControl(targetBtn) ?? new System.Windows.Controls.Image { Stretch = System.Windows.Media.Stretch.Uniform };
            img.Width = newImgWidth; img.Height = newImgHeight;
            img.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            img.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            img.Margin = new Thickness(0);
            switch (position)
            {
                case "위": img.VerticalAlignment = System.Windows.VerticalAlignment.Top; img.HorizontalAlignment = System.Windows.HorizontalAlignment.Center; img.Margin = new Thickness(0, 4, 0, 0); break;
                case "아래": img.VerticalAlignment = System.Windows.VerticalAlignment.Bottom; img.HorizontalAlignment = System.Windows.HorizontalAlignment.Center; img.Margin = new Thickness(0, 0, 0, 4); break;
                case "왼쪽": img.HorizontalAlignment = System.Windows.HorizontalAlignment.Left; img.VerticalAlignment = System.Windows.VerticalAlignment.Center; img.Margin = new Thickness(15, 0, 0, 0); break;
                case "오른쪽": img.HorizontalAlignment = System.Windows.HorizontalAlignment.Right; img.VerticalAlignment = System.Windows.VerticalAlignment.Center; img.Margin = new Thickness(0, 0, 15, 0); break;
            }
            if (meta.LabelInside || targetBtn.Content is System.Windows.Controls.Grid)
            {
                var grid = EnsureGridContentWithImage(targetBtn, out var ensuredImg);
                ensuredImg.Width = img.Width; ensuredImg.Height = img.Height;
                ensuredImg.HorizontalAlignment = img.HorizontalAlignment; ensuredImg.VerticalAlignment = img.VerticalAlignment; ensuredImg.Margin = img.Margin;
                EnsureOrUpdateInButtonLabel(targetBtn, meta);
            }
            else
            {
                if (img.Parent is System.Windows.Controls.Panel p) p.Children.Remove(img);
                targetBtn.Content = img;
                EnsureOrUpdateButtonLabel(canvas, targetBtn, meta);
            }
        }

        private static System.Windows.Media.ImageSource? TryLoadImageSource(string path)
        {
            try
            {
                var bi = new System.Windows.Media.Imaging.BitmapImage();
                bi.BeginInit();
                bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bi.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                bi.UriSource = new Uri(path, UriKind.Absolute);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Image load error: " + ex);
                System.Windows.MessageBox.Show("이미지를 불러올 수 없습니다.\n" + ex.Message);
                return null;
            }
        }
    }

    public class TabState
    {
        public string Header { get; set; } = "새 탭";
        public int TabIndex { get; set; }
    }

    public class ButtonState
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string? Content { get; set; }
        public string? ImagePath { get; set; }
        public int CanvasIndex { get; set; }
        public string? Path { get; set; }
        public bool IsFolder { get; set; }
        public string? LabelText { get; set; }
        public double ImageWidth { get; set; }
        public double ImageHeight { get; set; }
        public string? ImageHAlign { get; set; }
        public string? ImageVAlign { get; set; }
        public bool LabelInside { get; set; }
        public string? FontFamily { get; set; }
        public double FontSize { get; set; }
        public string? FontWeightName { get; set; } // ensure setter
        public bool Italic { get; set; }
        public string? FontColor { get; set; }        // null = default(theme), hex = custom
        public bool BackgroundTransparent { get; set; } // legacy compat
        public string? BgColor { get; set; }           // null = default(theme), "transparent" = none, hex = custom
        public string? CustomFontFamily { get; set; }  // null = default(theme)
    }

    // MainWindow에 TabItem 추가 메서드 (partial class 확장용)
    public partial class MainWindow
    {
        private int _nextTabNumber = 2; // ButtonCanvas1 다음부터 시작

        public void AddNewTabItem()
        {
            var newTabItem = new TabItem
            {
                Header = "새 탭",
                Height = 40
            };

            // TabItem ContextMenu 추가 (이름 수정 및 탭 삭제 기능)
            var tabContextMenu = new ContextMenu();
            var renameMenuItem = new MenuItem { Header = "이름 수정" };
            renameMenuItem.Click += RenameTabItem_Click;
            tabContextMenu.Items.Add(renameMenuItem);
            var deleteMenuItem = new MenuItem { Header = "탭 삭제" };
            deleteMenuItem.Click += DeleteTabItem_Click;
            tabContextMenu.Items.Add(deleteMenuItem);
            newTabItem.ContextMenu = tabContextMenu;

            var border = new Border
            {
                CornerRadius = new CornerRadius(16),
                Margin = new Thickness(0, 0, 0, 3)
            };
            border.SetResourceReference(Border.BackgroundProperty, "StatusBarBackgroundBrush");

            // ContextMenu 추가
            var contextMenu = new ContextMenu();
            var menuItem = new MenuItem { Header = "버튼생성" };
            menuItem.Click += CreateButtonInBorder_Click;
            contextMenu.Items.Add(menuItem);
            var chartMenuItem = new MenuItem { Header = "차트생성" };
            chartMenuItem.Click += CreateChartInBorder_Click;
            contextMenu.Items.Add(chartMenuItem);
            border.ContextMenu = contextMenu;
            border.ContextMenuOpening += DynamicButtonBorder_ContextMenuOpening;

            // Canvas 추가
            var canvas = new Canvas { Name = $"ButtonCanvas{_nextTabNumber}", Width = 3000, Height = 2000 };
            RegisterName(canvas.Name, canvas);
            var scrollViewer = new System.Windows.Controls.ScrollViewer
            {
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Content = canvas
            };
            border.Child = scrollViewer;

            newTabItem.Content = border;
            tabControl.Items.Add(newTabItem);

            _nextTabNumber++;

            // Window1이 열려있으면 Window1 중앙에, 없으면 MainWindow 중앙에 메시지 표시
            Window? ownerWindow = null;
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Window1 w1 && w1.IsVisible)
                {
                    ownerWindow = w1;
                    break;
                }
            }

            if (ownerWindow == null)
                ownerWindow = this;

            var messageDialog = new Window
            {
                Title = "탭 추가 완료",
                Width = 300,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = ownerWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent
            };

            var messageBorder = new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("WindowBackgroundBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("StatusBarBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20)
            };

            var stackPanel = new System.Windows.Controls.StackPanel();
            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = "새 탭이 추가되었습니다.",
                FontSize = 14,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 20),
                Foreground = (System.Windows.Media.Brush)FindResource("ForegroundBrush")
            };

            var okButton = new System.Windows.Controls.Button
            {
                Content = "확인",
                Width = 80,
                Height = 30,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Background = (System.Windows.Media.Brush)FindResource("StatusBarBackgroundBrush"),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            okButton.Click += (s, e) => messageDialog.Close();

            stackPanel.Children.Add(textBlock);
            stackPanel.Children.Add(okButton);
            messageBorder.Child = stackPanel;
            messageDialog.Content = messageBorder;

            messageDialog.ShowDialog();
        }

        private void SaveAllTabs()
        {
            try
            {
                var tabStates = new List<TabState>();

                // XAML에 정의된 첫 번째 탭(Tabitem1)은 제외하고 동적으로 추가된 탭만 저장
                for (int i = 1; i < tabControl.Items.Count; i++)
                {
                    if (tabControl.Items[i] is TabItem tabItem)
                    {
                        tabStates.Add(new TabState
                        {
                            Header = tabItem.Header?.ToString() ?? "새 탭",
                            TabIndex = i
                        });
                    }
                }

                var json = JsonSerializer.Serialize(tabStates);
                File.WriteAllText(TabStateFile, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SaveAllTabs error: " + ex);
            }
        }

        private void RestoreAllTabs()
        {
            try
            {
                if (!File.Exists(TabStateFile)) return;

                var json = File.ReadAllText(TabStateFile);
                var tabStates = JsonSerializer.Deserialize<List<TabState>>(json);
                if (tabStates == null || tabStates.Count == 0) return;

                foreach (var state in tabStates)
                {
                    var newTabItem = new TabItem
                    {
                        Header = state.Header,
                        Height = 40
                    };

                    // TabItem ContextMenu 추가
                    var tabContextMenu = new ContextMenu();
                    var renameMenuItem = new MenuItem { Header = "이름 수정" };
                    renameMenuItem.Click += RenameTabItem_Click;
                    tabContextMenu.Items.Add(renameMenuItem);
                    var deleteMenuItem = new MenuItem { Header = "탭 삭제" };
                    deleteMenuItem.Click += DeleteTabItem_Click;
                    tabContextMenu.Items.Add(deleteMenuItem);
                    newTabItem.ContextMenu = tabContextMenu;

                    var border = new Border
                    {
                        CornerRadius = new CornerRadius(16),
                        Margin = new Thickness(0, 0, 0, 3)
                    };
                    border.SetResourceReference(Border.BackgroundProperty, "StatusBarBackgroundBrush");

                    // ContextMenu 추가
                    var contextMenu = new ContextMenu();
                    var menuItem = new MenuItem { Header = "버튼생성" };
                    menuItem.Click += CreateButtonInBorder_Click;
                    contextMenu.Items.Add(menuItem);
                    var chartMenuItem = new MenuItem { Header = "차트생성" };
                    chartMenuItem.Click += CreateChartInBorder_Click;
                    contextMenu.Items.Add(chartMenuItem);
                    border.ContextMenu = contextMenu;
                    border.ContextMenuOpening += DynamicButtonBorder_ContextMenuOpening;

                    // Canvas 추가 - state.TabIndex에 맞는 Canvas 이름 사용
                    var canvas = new Canvas { Name = $"ButtonCanvas{state.TabIndex + 1}", Width = 3000, Height = 2000 };
                    RegisterName(canvas.Name, canvas);
                    var scrollViewer = new System.Windows.Controls.ScrollViewer
                    {
                        HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                        Content = canvas
                    };
                    border.Child = scrollViewer;

                    newTabItem.Content = border;
                    tabControl.Items.Add(newTabItem);

                    // _nextTabNumber 업데이트
                    if (_nextTabNumber <= state.TabIndex + 1)
                    {
                        _nextTabNumber = state.TabIndex + 2;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RestoreAllTabs error: " + ex);
            }
        }
    }
}