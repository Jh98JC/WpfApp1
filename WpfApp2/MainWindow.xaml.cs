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
using System.Data;


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
        private DispatcherTimer _serverCheckTimer;

        private readonly Random _rand = new Random(); // Random 인스턴스는 readonly로 선언

        // 1, 2번 오류: 명확한 네임스페이스 지정
        private System.Windows.Point? _lastBorderRightClickPoint = null;

        // 단축키로 열었는지 추적하는 플래그 (마우스가 한 번 들어올 때까지 LogoWindow 전환 방지)
        private bool _openedByHotkey = false;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_MENU = 0x12;
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelKeyboardProc _keyboardProc;
        private IntPtr _hookHandle = IntPtr.Zero;

        private Forms.NotifyIcon? _notifyIcon;
        private bool _showTaskbarIcon = true;
        private bool _showTrayIcon = true;

        internal bool TaskbarIconVisible => _showTaskbarIcon;
        internal bool TrayIconVisible => _showTrayIcon;
        internal void SetTaskbarIconVisible(bool visible)
        {
            _showTaskbarIcon = visible;
            ApplyTaskbarVisibility(this, visible);
            foreach (Window w in System.Windows.Application.Current.Windows)
            {
                if (w is LogoWindow w3v) ApplyTaskbarVisibility(w3v, visible);
            }
            SaveWindowPosition();
        }

        private static void ApplyTaskbarVisibility(Window win, bool visible)
        {
            const int GWL_EXSTYLE      = -20;
            const int WS_EX_APPWINDOW  = 0x00040000;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const uint SWP_NOMOVE       = 0x0002;
            const uint SWP_NOSIZE       = 0x0001;
            const uint SWP_NOZORDER     = 0x0004;
            const uint SWP_FRAMECHANGED = 0x0020;

            var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            if (hwnd == IntPtr.Zero) return;

            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (visible)
                style = (style | WS_EX_APPWINDOW) & ~WS_EX_TOOLWINDOW;
            else
                style = (style & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, style);
            // SWP_FRAMECHANGED: SetWindowLong 후 스타일 변경을 작업표시줄에 즉시 반영
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }

        internal void SetTrayIconVisible(bool visible)
        {
            _showTrayIcon = visible;
            if (_notifyIcon != null) _notifyIcon.Visible = visible;
            SaveWindowPosition();
        }

        private System.Drawing.Icon? LoadTrayIcon()
        {
            try
            {
                var sri = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/대시보드아이콘 3.ico"));
                if (sri != null)
                {
                    var ms = new System.IO.MemoryStream();
                    sri.Stream.CopyTo(ms);
                    ms.Position = 0;
                    return new System.Drawing.Icon(ms);
                }
            }
            catch { }
            return null;
        }

        // 핫키 설정 (기본값: Alt+`)
        private int _hotkeyVk = 0xC0;
        private bool _hotkeyAlt = true;
        private bool _hotkeyCtrl = false;
        private bool _hotkeyShift = false;

        internal int HotkeyVk => _hotkeyVk;
        internal bool HotkeyAlt => _hotkeyAlt;
        internal bool HotkeyCtrl => _hotkeyCtrl;
        internal bool HotkeyShift => _hotkeyShift;

        internal void ApplyHotkeySettings(int vk, bool alt, bool ctrl, bool shift)
        {
            _hotkeyVk = vk;
            _hotkeyAlt = alt;
            _hotkeyCtrl = ctrl;
            _hotkeyShift = shift;
            SaveWindowPosition();
        }

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
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);
        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_SHOWNORMAL = 1;
        private const int SW_SHOWNOACTIVATE = 4;

        // 추가: 정확한 스크린 좌표 판정을 위한 Win32 RECT & GetWindowRect
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
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

            _keyboardProc = KeyboardHookCallback;
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(curModule.ModuleName), 0);

            InitNotifyIcon();
        }

        protected override void OnClosed(EventArgs e)
        {
            // 핫키 해제
            if (_hookHandle != IntPtr.Zero)
                UnhookWindowsHookEx(_hookHandle);
            _notifyIcon?.Dispose();
            _notifyHostForm?.Dispose();
            base.OnClosed(e);
            SaveWindowPosition();
            SaveAllTabs(); // 탭 정보 저장
            SaveAllButtonStates(); // 버튼 정보 저장
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode == _hotkeyVk)
                {
                    bool altOk  = !_hotkeyAlt  || (GetKeyState(VK_MENU)    & 0x8000) != 0;
                    bool ctrlOk = !_hotkeyCtrl || (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool shiftOk= !_hotkeyShift|| (GetKeyState(VK_SHIFT)   & 0x8000) != 0;
                    if (altOk && ctrlOk && shiftOk)
                        Dispatcher.Invoke(() => ShowAndActivateMain());
                }
            }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
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

            // MobileWindow, LogoWindow 숨기기
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is MobileWindow w2 && w2.IsVisible) w2.Hide();
                else if (win is LogoWindow w3 && w3.IsVisible) w3.Hide();
            }
            // 메인윈도우 표시/활성화 (맨 앞으로)
            if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            var hwnd = new WindowInteropHelper(this).Handle;
            ShowWindow(hwnd, SW_SHOWNORMAL);
            ForceToForeground(hwnd);
            Activate();
            Focus();
            Keyboard.Focus(this);

            Dispatcher.InvokeAsync(() =>
            {
                if (!IsActive) { ForceToForeground(hwnd); Activate(); }
                Focus();
                Keyboard.Focus(this);
            }, DispatcherPriority.ApplicationIdle);

            // 단축키로 열었을 때는 마우스가 밖에 있어도 LogoWindow 전환하지 않음
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
                if (win == this || win is LogoWindow) continue;
                if (win.IsVisible)
                {
                    var r = GetWindowScreenRect(win);
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

            // 마우스가 소유 자식 창 위에 있으면 무시 (스크린 좌표로 검증)
            {
                var mousePos = System.Windows.Forms.Control.MousePosition;
                foreach (Window win in System.Windows.Application.Current.Windows)
                {
                    if (win == this || win is LogoWindow) continue;
                    if (win.IsVisible)
                    {
                        var r = GetWindowScreenRect(win);
                        if (!r.IsEmpty && r.Contains(mousePos)) return;
                    }
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

            // 마우스가 여전히 MainWindow 또는 소유 자식창 위에 있으면 아무것도 하지 않음
            var mousePos = System.Windows.Forms.Control.MousePosition;
            var mainRect = GetWindowScreenRect(this);
            if (!mainRect.IsEmpty && mainRect.Contains(mousePos)) return;

            foreach (Window win in System.Windows.Application.Current.Windows.Cast<Window>().ToList())
            {
                if (win == this || win is LogoWindow) continue;
                if (win.IsVisible)
                {
                    var r = GetWindowScreenRect(win);
                    if (!r.IsEmpty && r.Contains(mousePos)) return; // 마우스가 자식창 위 → 아무것도 하지 않음
                }
            }

            // 마우스가 어떤 자식창 위에도 없을 때만 닫고 LogoWindow로 전환
            foreach (Window win in System.Windows.Application.Current.Windows.Cast<Window>().ToList())
            {
                if (win == this || win is LogoWindow) continue;
                if (win.Owner == this && win.IsVisible && !(win is MobileWindow))
                {
                    try { win.Close(); } catch { }
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

        public void DoLogout()
        {
            // 열린 자식 창 모두 닫기
            foreach (Window win in System.Windows.Application.Current.Windows.Cast<Window>().ToList())
            {
                if (win != this) win.Close();
            }

            Hide();

            var loginWin = new LoginWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
            if (loginWin.ShowDialog() == true)
            {
                ApplyRoleVisibility();
                Show();
                Activate();
            }
            else
            {
                System.Windows.Application.Current.Shutdown();
            }
        }

        public void ApplyRoleVisibility()
        {
            bool isMaster = Session.IsMaster;
            personalbtn.Visibility = isMaster ? Visibility.Visible : Visibility.Collapsed;
            adminbtn.Visibility = isMaster ? Visibility.Visible : Visibility.Collapsed;
        }

        private void adminbtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is AdminWindow existing)
                {
                    existing.Activate();
                    return;
                }
            }
            var adminWin = new AdminWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            adminWin.Show();
        }

        // setbtn_Click 이벤트 핸들러에서 Window1을 올바르게 생성하고 표시
        // ── LogoWindow ↔ MainWindow 전환 애니메이션 ─────────────────────────────
        private static readonly Duration _w3TransDuration = new Duration(TimeSpan.FromMilliseconds(260));

        private static System.Windows.Point CalcOriginToward(double mainLeft, double mainTop, double mainW, double mainH, LogoWindow w3)
        {
            double relX = (w3.Left + w3.Width  / 2.0 - mainLeft) / mainW;
            double relY = (w3.Top  + w3.Height / 2.0 - mainTop)  / mainH;
            return new System.Windows.Point(
                Math.Max(0, Math.Min(1, relX)),
                Math.Max(0, Math.Min(1, relY)));
        }

        private void ShowMainWindowAnimated(LogoWindow w3)
        {
            double mainLeft = Left, mainTop = Top;
            double sx = w3.Width / ActualWidth, sy = w3.Height / ActualHeight;
            var origin = CalcOriginToward(mainLeft, mainTop, ActualWidth, ActualHeight, w3);

            var scale = new ScaleTransform(sx, sy);
            if (Content is FrameworkElement root)
            {
                root.RenderTransformOrigin = origin;
                root.RenderTransform = scale;
            }

            Visibility = Visibility.Visible;
            w3.Hide();

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            DoubleAnimation Anim(double from, double to) =>
                new DoubleAnimation(from, to, _w3TransDuration) { EasingFunction = ease };

            var syAnim = Anim(sy, 1.0);
            syAnim.Completed += (_, __) =>
            {
                if (Content is FrameworkElement r) r.RenderTransform = null;
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(sx, 1.0));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, syAnim);
        }

        private void HideMainWindowAnimated(LogoWindow w3, Action onComplete)
        {
            double mainLeft = Left, mainTop = Top;
            double sx = w3.Width / ActualWidth, sy = w3.Height / ActualHeight;
            var origin = CalcOriginToward(mainLeft, mainTop, ActualWidth, ActualHeight, w3);

            var scale = new ScaleTransform(1.0, 1.0);
            if (Content is FrameworkElement root)
            {
                root.RenderTransformOrigin = origin;
                root.RenderTransform = scale;
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            DoubleAnimation Anim(double from, double to) =>
                new DoubleAnimation(from, to, _w3TransDuration) { EasingFunction = ease };

            var syAnim = Anim(1.0, sy);
            syAnim.Completed += (_, __) =>
            {
                Visibility = Visibility.Hidden;
                if (Content is FrameworkElement r) r.RenderTransform = null;
                onComplete();
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1.0, sx));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, syAnim);
        }
        // ─────────────────────────────────────────────────────────────────────

        private void setbtn_Click(object sender, RoutedEventArgs e)
        {
            // 메인윈도우가 보일 때만 Window1을 띄움
            if (this.Visibility != Visibility.Visible)
                return;

            System.Diagnostics.Debug.WriteLine($"[MainWindow] WindowState={WindowState}, Before creating SettingWindow: Left={Left}, Top={Top}, Width={ActualWidth}, Height={ActualHeight}");

            // 이미 열려있는 Window1이 있으면 닫고 새로 생성
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is SettingWindow w1 && w1.IsVisible)
                {
                    w1.Close();
                    break;
                }
            }

            var win1 = new SettingWindow();
            win1.Owner = this;
            win1.ShowInTaskbar = false;
            win1.Show();
        }

        private void personalbtn_Click(object sender, RoutedEventArgs e)
        {
            // Window1이 열려있으면 닫기
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is SettingWindow w1 && w1.IsVisible)
                    w1.Close();
            }

            // MobileWindow 재사용 (숨겨져 있어도 탭 유지 후 다시 표시)
            MobileWindow? existingW2 = null;
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is MobileWindow w2)
                {
                    existingW2 = w2;
                    break;
                }
            }

            if (existingW2 == null)
            {
                // 새로 생성
                existingW2 = new MobileWindow();
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

        private void PositionWindow2(MobileWindow w2)
        {
            double offset = 25; // 추가 여유
            double win3Height = 0;
            foreach (Window w in System.Windows.Application.Current.Windows)
            {
                if (w is LogoWindow w3 && w3.IsVisible)
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
            string window3PositionFile = System.IO.Path.Combine(AppDataFolder, "window3_position.json");
            LogoWindow? existing = null;
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is LogoWindow w3)
                {
                    existing = w3;
                    if (w3.IsVisible)
                        return; // 이미 표시중이면 그대로
                    break; // 숨겨진 인스턴스 발견
                }
            }

            // SettingWindow 닫기 (Window2는 닫거나 숨기지 않음)
            foreach (Window w1 in System.Windows.Application.Current.Windows)
            {
                if (w1 is SettingWindow SettingWindow && SettingWindow.IsVisible)
                    SettingWindow.Close();
            }

            if (existing != null && !existing.IsVisible)
            {
                // 숨겨진 LogoWindow 재사용
                existing.Owner = this;
                existing.WindowStartupLocation = WindowStartupLocation.Manual;
                if (!File.Exists(window3PositionFile))
                {
                    existing.Left = this.Left;
                    existing.Top = this.Top + this.Height - existing.Height;
                }
                existing.ShowInTaskbar = _showTaskbarIcon;
                existing.Topmost = true;
                HideMainWindowAnimated(existing, () =>
                {
                    existing.Show();
                    existing.Topmost = true;
                });
                return;
            }

            // 새로 생성
            var win3 = new LogoWindow();
            win3.Owner = this;
            win3.WindowStartupLocation = WindowStartupLocation.Manual;
            win3.Topmost = true;

            // 위치를 미리 계산 (애니메이션 목표 지점으로 사용)
            if (!File.Exists(window3PositionFile))
            {
                win3.Left = this.Left;
                win3.Top  = this.Top + this.Height - win3.Height;
            }

            win3.Loaded += (s, e) => win3.Topmost = true;

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
                        if (win is MobileWindow w2 && w2.IsVisible)
                            w2.Hide();
                    }
                    ShowMainWindowAnimated(win3);
                    this.Activate();
                }
                isDragging = false;
            };

            win3.ShowInTaskbar = _showTaskbarIcon;
            win3.Closed += (s, e) =>
            {
                if (this.Visibility != Visibility.Visible)
                {
                    System.Windows.Application.Current.Shutdown();
                }
            };
            HideMainWindowAnimated(win3, () =>
            {
                win3.Show();
                win3.Topmost = true;
            });
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _ = CheckServerStatusAsync();
            _serverCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _serverCheckTimer.Tick += async (_, _) => await CheckServerStatusAsync();
            _serverCheckTimer.Start();

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

        private async Task CheckServerStatusAsync()
        {
            if (!DatabaseService.IsConfigured)
            {
                Dispatcher.Invoke(() => SetServerStatusUI("미설정", "#888888", "DB 연결이 설정되지 않았습니다."));
                return;
            }

            Dispatcher.Invoke(() => SetServerStatusUI("확인중...", "#888888", null));
            bool ok = await DatabaseService.TestConnectionAsync(DatabaseService.ConnectionString);
            Dispatcher.Invoke(() =>
            {
                if (ok)
                    SetServerStatusUI("● 서버 연결됨", "#4CAF50", "DB 서버와 정상 연결되어 있습니다.");
                else
                    SetServerStatusUI("● 서버 오프라인", "#F44336", "DB 서버에 연결할 수 없습니다.");
            });
        }

        private void SetServerStatusUI(string text, string colorHex, string? tooltip)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
            ServerStatusText.Text = text;
            ServerStatusText.Foreground = new System.Windows.Media.SolidColorBrush(color);
            ServerStatusText.ToolTip = tooltip;
        }

        private void RestoreWindowPosition()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        Left = s.Left;
                        Top = s.Top;
                        if (s.HotkeyVirtualKey != 0) _hotkeyVk = s.HotkeyVirtualKey;
                        _hotkeyAlt       = s.HotkeyAlt;
                        _hotkeyCtrl      = s.HotkeyCtrl;
                        _hotkeyShift     = s.HotkeyShift;
                        _showTaskbarIcon = s.ShowTaskbarIcon;
                        ShowInTaskbar    = _showTaskbarIcon;
                        _showTrayIcon    = s.ShowTrayIcon;
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
            var s = new AppSettings
            {
                Left = Left, Top = Top,
                HotkeyVirtualKey = _hotkeyVk,
                HotkeyAlt        = _hotkeyAlt,
                HotkeyCtrl       = _hotkeyCtrl,
                HotkeyShift      = _hotkeyShift,
                ShowTaskbarIcon  = _showTaskbarIcon,
                ShowTrayIcon     = _showTrayIcon,
            };
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(s));
        }

        private class AppSettings
        {
            public double Left { get; set; }
            public double Top { get; set; }
            public int  HotkeyVirtualKey { get; set; } = 0xC0;
            public bool HotkeyAlt        { get; set; } = true;
            public bool HotkeyCtrl       { get; set; } = false;
            public bool HotkeyShift      { get; set; } = false;
            public bool ShowTaskbarIcon  { get; set; } = true;
            public bool ShowTrayIcon     { get; set; } = true;
        }

        private void ForceToForeground(IntPtr hwnd)
        {
            var fg = GetForegroundWindow();
            var fgThread = GetWindowThreadProcessId(fg, out _);
            var myThread = GetCurrentThreadId();
            bool attached = fgThread != myThread && AttachThreadInput(fgThread, myThread, true);
            try
            {
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
                SwitchToThisWindow(hwnd, true);
            }
            finally
            {
                if (attached) AttachThreadInput(fgThread, myThread, false);
            }
        }

        private Forms.Form? _notifyHostForm;

        private void InitNotifyIcon()
        {
            // 숨김 Form을 호스트로 사용 → LogoWindow 전환 시에도 NotifyIcon 유지
            _notifyHostForm = new Forms.Form
            {
                ShowInTaskbar = false,
                WindowState = Forms.FormWindowState.Minimized,
                FormBorderStyle = Forms.FormBorderStyle.None,
                Opacity = 0,
            };

            System.Drawing.Icon? appIcon = LoadTrayIcon();
            if (appIcon == null)
                try { appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule!.FileName); }
                catch { appIcon = System.Drawing.SystemIcons.Application; }

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("열기", null, (_, _) => Dispatcher.Invoke(ShowAndActivateMain));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown()));

            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = appIcon,
                Text = "WpfApp2",
                Visible = _showTrayIcon,
                ContextMenuStrip = menu,
            };
            _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowAndActivateMain);
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
                if (win is MobileWindow w2 && w2.IsVisible)
                {
                    w2.Hide();
                }
            }
            var win3 = new LogoWindow();
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
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RestoreButtonState] content={state.Content} : {ex.Message}"); }

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
                        if (Uri.TryCreate(meta.Path, UriKind.Absolute, out var uri) &&
                            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
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
            public double InnerHeight { get; set; } = 0;            // 내부 CartesianChart 높이 (0=기본값)
            public string ChartFont { get; set; } = "Malgun Gothic"; // 차트 전체 글꼴
            public string? ChartLabelColor { get; set; }
            public double ChartLabelSize { get; set; } = 0;
            public bool ShowBars { get; set; } = true;
            public List<string> RankListVisibleColumns { get; set; } = new List<string> { "날짜", "매장명", "중분류", "메뉴명", "총매출액" };
            public string RankListLabelFont { get; set; } = "Malgun Gothic";
            public double RankListLabelSize { get; set; } = 13;
            public string? RankListLabelColor { get; set; } = null;
            public string RankListValueFont { get; set; } = "Malgun Gothic";
            public double RankListValueSize { get; set; } = 13;
            public string? RankListValueColor { get; set; } = null;
            public Dictionary<string, double> RankListColumnWidths { get; set; } = new Dictionary<string, double>();
            public List<string> RankListColumnOrder { get; set; } = new List<string>();
            public Dictionary<string, string> RankListColumnAlignments { get; set; } = new Dictionary<string, string>();
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
                    if (Uri.TryCreate(meta.Path, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
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
                ("Line",     "선 그래프",  "Line Chart",   MakeLineIcon()),
                ("Bar",      "세로 막대",  "Column Chart", MakeBarIcon()),
                ("HBar",     "가로 막대",  "Bar Chart",    MakeHBarIcon()),
                ("Pie",      "원형 차트",  "Pie Chart",    MakePieIcon()),
                ("Gauge",    "게이지",     "Gauge",        MakeGaugeIcon()),
                ("RankList", "순위표",     "Rank List",    MakeRankListIcon()),
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

        private static UIElement MakeRankListIcon()
        {
            var c = new System.Windows.Controls.Canvas { Width = 56, Height = 36 };
            var widths = new[] { 46.0, 36.0, 28.0, 20.0, 14.0 };
            for (int i = 0; i < widths.Length; i++)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = widths[i], Height = 4,
                    Fill = IconStroke, RadiusX = 2, RadiusY = 2,
                    Opacity = 1.0 - i * 0.15
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
                Width = 450,
                MaxHeight = SystemParameters.PrimaryScreenHeight * 0.85
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

            // 데이터 소스 선택
            stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "데이터 소스:", Margin = new Thickness(0, 0, 0, 4) });
            var sourceCombo = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 12) };
            sourceCombo.Style = null; // MaterialDesign 스타일 제거 → WPF 기본 스타일 사용 (Background 적용됨)
            sourceCombo.Items.Add("정적 데이터 (수동 입력)");
            sourceCombo.Items.Add("JSON 파일");
            sourceCombo.Items.Add("CSV 파일");
            sourceCombo.Items.Add("API (실시간)");
            sourceCombo.Items.Add("DB (대진포스DB)");
            sourceCombo.SelectedIndex = chartType == "RankList" ? 4 : 0;
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
                    var meta = new ChartMeta
                    {
                        ChartType = chartType,
                        Width = 300,
                        Height = 200,
                        Title = $"{chartType} Chart"
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
                        try { LoadDbData(meta); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LoadDbData] {ex.Message}"); }
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

            var scrollViewer = new System.Windows.Controls.ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled
            };
            configDlg.Content = scrollViewer;
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
                case "RankList":
                    chartControl = CreateRankList(meta);
                    break;
            }

            if (chartControl != null)
            {
                if (chartControl is System.Windows.Controls.Control ctrl)
                    ctrl.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "StatusBarBackgroundBrush");
                border.Child = WrapWithDbDates(chartControl, meta);
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
                    new SKColor(50,  50,  80),
                    new SKColor(196, 196, 212)),
                ThemeManager.Theme.Black => (
                    new SKColor(7,   7,   7),
                    new SKColor(210, 210, 210),
                    new SKColor(37,  37,  37)),
                _ => (
                    new SKColor(19, 19, 27),
                    new SKColor(220, 220, 230),
                    new SKColor(55,  55,  65))
            };

        // 전문적인 차트 색상 팔레트 (채도 낮춘 Tableau 스타일)
        private static readonly SKColor P_Blue   = SKColor.Parse("#4E79A7");
        private static readonly SKColor P_Amber  = SKColor.Parse("#E8A838");
        private static readonly SKColor P_Teal   = SKColor.Parse("#59A5A9");
        private static readonly SKColor P_Purple = SKColor.Parse("#9A6AA0");
        private static readonly SKColor P_Rust   = SKColor.Parse("#C0614A");
        private static readonly SKColor P_Sage   = SKColor.Parse("#4F8C5E");

        private static System.Windows.Controls.ScrollViewer WrapInScrollViewer(CartesianChart chart)
        {
            var sv = new System.Windows.Controls.ScrollViewer
            {
                Content = chart,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled
            };
            sv.PreviewMouseWheel += (s, e) =>
            {
                e.Handled = true; // 캔버스 ScrollViewer로 전달 차단
                double delta = -e.Delta / 3.0;
                double newOffset = Math.Max(0, Math.Min(sv.VerticalOffset + delta,
                    sv.ScrollableHeight));
                sv.ScrollToVerticalOffset(newOffset);
            };
            return sv;
        }

        private static void AttachCtrlScrollZoom(CartesianChart chart)
        {
            chart.ZoomMode = LiveChartsCore.Measure.ZoomAndPanMode.None;
            chart.PreviewMouseWheel += (s, e) =>
            {
                e.Handled = true; // 캔버스 ScrollViewer로 전달 차단

                bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                if (!ctrl) return; // 일반 휠은 차트에서 소비하고 종료

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
            var tf = SKTypeface.FromFamilyName(meta.ChartFont);
            var labelColor = ParseChartColor(meta.ChartLabelColor, axisLabel);
            float xSize = meta.ChartLabelSize > 0 ? (float)meta.ChartLabelSize : 9f;
            float ySize = meta.ChartLabelSize > 0 ? (float)meta.ChartLabelSize : 11f;
            float dlSize = meta.ChartLabelSize > 0 ? (float)meta.ChartLabelSize : 10f;
            var chart = new CartesianChart
            {
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Auto,
                TooltipTextPaint = new SolidColorPaint(new SKColor(230, 230, 230)) { SKTypeface = tf },
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
                        DataLabelsPaint = new SolidColorPaint(labelColor) { SKTypeface = tf },
                        DataLabelsSize = dlSize,
                        DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top,
                        DataLabelsFormatter = p => p.Coordinate.PrimaryValue.ToString("N0")
                    }
                },
                XAxes = new[]
                {
                    new Axis
                    {
                        Labels = meta.Labels,
                        LabelsRotation = -45,
                        TextSize = xSize,
                        MinStep = 1,
                        ForceStepToMin = true,
                        LabelsPaint = new SolidColorPaint(labelColor) { SKTypeface = tf },
                        SeparatorsPaint = new SolidColorPaint(gridLine.WithAlpha(80)) { StrokeThickness = 1 },
                        TicksPaint = null
                    }
                },
                YAxes = new[]
                {
                    new Axis
                    {
                        TextSize = ySize,
                        LabelsPaint = new SolidColorPaint(labelColor) { SKTypeface = tf },
                        SeparatorsPaint = new SolidColorPaint(gridLine.WithAlpha(80)) { StrokeThickness = 1 },
                        TicksPaint = null
                    }
                }
            };
            AttachCtrlScrollZoom(chart);
            return chart;
        }

        private FrameworkElement CreateBarChart(ChartMeta meta)
        {
            var (_, axisLabel, gridLine) = GetChartColors();
            double naturalH = Math.Max(300, meta.StaticData.Count * 40 + 80);
            var tf = SKTypeface.FromFamilyName(meta.ChartFont);
            var labelColor = ParseChartColor(meta.ChartLabelColor, axisLabel);
            float axisSize = meta.ChartLabelSize > 0 ? (float)meta.ChartLabelSize : 22f;
            float dlSize = meta.ChartLabelSize > 0 ? (float)meta.ChartLabelSize : 10f;
            var chart = new CartesianChart
            {
                Height = naturalH,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                ZoomMode = LiveChartsCore.Measure.ZoomAndPanMode.None,
                TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Auto,
                TooltipTextPaint = new SolidColorPaint(new SKColor(230, 230, 230)) { SKTypeface = tf },
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
                        DataLabelsPaint = new SolidColorPaint(labelColor) { SKTypeface = tf },
                        DataLabelsSize = dlSize,
                        DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top,
                        DataLabelsFormatter = p => p.Coordinate.PrimaryValue.ToString("N0")
                    }
                },
                XAxes = new[]
                {
                    new Axis
                    {
                        Labels = meta.Labels,
                        LabelsRotation = -35,
                        TextSize = axisSize,
                        MinStep = 1,
                        ForceStepToMin = true,
                        LabelsPaint = new SolidColorPaint(labelColor) { SKTypeface = tf },
                        SeparatorsPaint = null,
                        TicksPaint = null
                    }
                },
                YAxes = new[]
                {
                    new Axis
                    {
                        TextSize = axisSize,
                        LabelsPaint = new SolidColorPaint(labelColor) { SKTypeface = tf },
                        SeparatorsPaint = new SolidColorPaint(gridLine.WithAlpha(80)) { StrokeThickness = 1 },
                        TicksPaint = null
                    }
                }
            };
            AttachChartClickDetails(chart, meta);
            return WrapInScrollViewer(chart);
        }

        private FrameworkElement CreateHBarChart(ChartMeta meta)
        {
            var (_, axisLabel, gridLine) = GetChartColors();

            // LiveCharts2 RowSeries는 index 0이 맨 아래 → 역순으로 넣어야 높은 값이 위에 표시됨
            var displayValues = meta.StaticData.AsEnumerable().Reverse().ToList();
            var displayLabels = meta.Labels.AsEnumerable().Reverse().ToList();

            var tf = SKTypeface.FromFamilyName(meta.ChartFont);
            var (_, axisLabelHBar, _) = GetChartColors();
            var labelColorHBar = ParseChartColor(meta.ChartLabelColor, new SKColor(240, 240, 240));
            var axisColorHBar = ParseChartColor(meta.ChartLabelColor, axisLabelHBar);
            float yHSize = meta.ChartLabelSize > 0 ? (float)meta.ChartLabelSize : 18f;
            float xHSize = meta.ChartLabelSize > 0 ? (float)meta.ChartLabelSize : 20f;
            float dlHSize = meta.ChartLabelSize > 0 ? (float)meta.ChartLabelSize : 10f;
            var labelPaint = new SolidColorPaint(labelColorHBar) { SKTypeface = tf };
            LiveChartsCore.Drawing.IPaint<LiveChartsCore.SkiaSharpView.Drawing.SkiaSharpDrawingContext>? barFill = meta.ShowBars
                ? new LinearGradientPaint(new[] { P_Teal.WithAlpha(220), P_Blue.WithAlpha(200) }, new SKPoint(0, 0), new SKPoint(1, 0))
                : null;
            double naturalH = Math.Max(200, displayValues.Count * 38 + 60);

            var chart = new CartesianChart
            {
                Height = naturalH,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                ZoomMode = LiveChartsCore.Measure.ZoomAndPanMode.None,
                TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Auto,
                TooltipTextPaint = new SolidColorPaint(new SKColor(230, 230, 230)) { SKTypeface = tf },
                TooltipBackgroundPaint = new SolidColorPaint(new SKColor(30, 30, 38, 230)),
                Series = new ISeries[]
                {
                    new RowSeries<double>
                    {
                        Values = displayValues,
                        Fill = barFill,
                        Stroke = null,
                        Rx = 3,
                        Ry = 3,
                        MaxBarWidth = 40,
                        DataLabelsPaint = labelPaint,
                        DataLabelsSize = dlHSize,
                        DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Start,
                        DataLabelsTranslate = new LiveChartsCore.Drawing.LvcPoint(1.0, 0),
                        DataLabelsFormatter = p => p.Coordinate.PrimaryValue.ToString("N0") + "원"
                    }
                },
                YAxes = new[]
                {
                    new Axis
                    {
                        Labels = displayLabels,
                        LabelsPaint = new SolidColorPaint(axisColorHBar) { SKTypeface = tf },
                        SeparatorsPaint = null,
                        TicksPaint = null,
                        TextSize = yHSize,
                        MinStep = 1,
                        ForceStepToMin = true
                    }
                },
                XAxes = new[]
                {
                    new Axis
                    {
                        TextSize = xHSize,
                        LabelsPaint = new SolidColorPaint(axisColorHBar) { SKTypeface = tf },
                        SeparatorsPaint = null,
                        TicksPaint = null,
                        Labeler = v => v.ToString("N0") + "원"
                    }
                }
            };
            AttachChartClickDetails(chart, meta);
            return WrapInScrollViewer(chart);
        }

        private static readonly string[] AllRawColumns = { "날짜", "매장명", "중분류", "메뉴명", "총매출액", "총수량", "판매수량", "서비스수량" };

        private FrameworkElement CreateRankList(ChartMeta meta)
        {
            float labelSize = meta.RankListLabelSize > 0 ? (float)meta.RankListLabelSize : 13f;
            float valueSize = meta.RankListValueSize > 0 ? (float)meta.RankListValueSize : 13f;

            var outer = new System.Windows.Controls.StackPanel();
            outer.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "WindowBackgroundBrush");

            var list = new System.Windows.Controls.StackPanel();

            for (int i = 0; i < meta.Labels.Count && i < meta.StaticData.Count; i++)
            {
                var label = meta.Labels[i];
                var value = meta.StaticData[i];
                var idx = i;

                var row = new Border
                {
                    Padding = new Thickness(12, 7, 12, 7),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = (label, meta)
                };

                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameBlock = new System.Windows.Controls.TextBlock
                {
                    Text = label,
                    FontSize = labelSize,
                    FontFamily = new System.Windows.Media.FontFamily(meta.RankListLabelFont),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                if (!string.IsNullOrEmpty(meta.RankListLabelColor))
                    nameBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(meta.RankListLabelColor));
                else
                    nameBlock.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");

                var valBlock = new System.Windows.Controls.TextBlock
                {
                    Text = value.ToString("N0") + "원",
                    FontSize = valueSize,
                    FontFamily = new System.Windows.Media.FontFamily(meta.RankListValueFont),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                    FontWeight = FontWeights.SemiBold
                };
                if (!string.IsNullOrEmpty(meta.RankListValueColor))
                    valBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(meta.RankListValueColor));
                else
                    valBlock.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "StatusBarBorderBrush");

                Grid.SetColumn(nameBlock, 0);
                Grid.SetColumn(valBlock, 1);
                rowGrid.Children.Add(nameBlock);
                rowGrid.Children.Add(valBlock);
                row.Child = rowGrid;

                row.MouseEnter += (s, _) =>
                {
                    var hb = (System.Windows.Application.Current.Resources["ContextMenuBorderBrush"] as System.Windows.Media.Brush)
                             ?? System.Windows.Media.Brushes.DimGray;
                    ((Border)s).Background = hb;
                };
                row.MouseLeave  += (s, _) => ((Border)s).Background = System.Windows.Media.Brushes.Transparent;
                row.MouseLeftButtonUp += (s, _) =>
                {
                    var (lbl, m) = ((string, ChartMeta))((Border)s).Tag;
                    ShowRankDetailWindow(lbl, m);
                };

                if (idx > 0)
                {
                    var sep = new System.Windows.Shapes.Rectangle { Height = 1, Margin = new Thickness(8, 0, 8, 0) };
                    sep.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ContextMenuBorderBrush");
                    list.Children.Add(sep);
                }
                list.Children.Add(row);
            }

            if (meta.Labels.Count == 0)
            {
                var emptyBlock = new System.Windows.Controls.TextBlock
                {
                    Text = "데이터 없음", Opacity = 0.5,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 16, 0, 16), FontSize = labelSize
                };
                emptyBlock.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");
                list.Children.Add(emptyBlock);
            }

            outer.Children.Add(list);

            var sv = new System.Windows.Controls.ScrollViewer
            {
                Content = outer,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled
            };
            return sv;
        }

        private void ShowRankDetailWindow(string groupValue, ChartMeta meta)
        {
            var cs = DatabaseService.DataConnectionString;
            if (string.IsNullOrWhiteSpace(cs))
            {
                System.Windows.MessageBox.Show("데이터 DB가 설정되지 않았습니다.");
                return;
            }

            var groupBy = AllowedGroupByColumns.Contains(meta.DbGroupBy ?? "") ? meta.DbGroupBy! : "매장명";
            var start   = meta.DbStartDate ?? DateTime.Today.AddDays(-7);
            var end     = meta.DbEndDate   ?? DateTime.Today.AddDays(-1);

            // 원본 레코드 조회
            var rows = new System.Data.DataTable();
            try
            {
                var sql = $@"SELECT 날짜, 매장명, 중분류, 메뉴명, 총매출액, 총수량, 판매수량, 서비스수량
FROM 매출데이터
WHERE 날짜 BETWEEN @start AND @end AND [{groupBy}] = @groupVal
ORDER BY 날짜 DESC";
                using var conn = new SqlConnection(cs);
                conn.Open();
                using var adapter = new SqlDataAdapter(sql, conn);
                adapter.SelectCommand.Parameters.AddWithValue("@start",    start.Date);
                adapter.SelectCommand.Parameters.AddWithValue("@end",      end.Date);
                adapter.SelectCommand.Parameters.AddWithValue("@groupVal", groupValue);
                adapter.Fill(rows);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"조회 오류: {ex.Message}");
                return;
            }

            // 테마 색상 가져오기
            var res2 = System.Windows.Application.Current.Resources;
            var winBg   = (res2["WindowBackgroundBrush"]   as System.Windows.Media.Brush) ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 22, 30));
            var barBg   = (res2["ContextMenuBorderBrush"]  as System.Windows.Media.Brush) ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 44));
            var fg2     = (res2["ForegroundBrush"]         as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.White;
            var accent2 = (res2["StatusBarBorderBrush"]    as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.SteelBlue;
            var altRow  = (res2["ContextMenuBorderBrush"]  as System.Windows.Media.Brush) ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 36, 48));

            // 팝업 창 구성
            var win = new System.Windows.Window
            {
                Title = groupValue,
                Width = 820, Height = 600,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                Owner = this,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.Transparent,
                WindowStyle = System.Windows.WindowStyle.None,
                AllowsTransparency = true,
                ResizeMode = System.Windows.ResizeMode.CanResizeWithGrip
            };

            var outerBorder2 = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true };
            outerBorder2.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
            outerBorder2.SetResourceReference(Border.BorderBrushProperty, "StatusBarBorderBrush");
            var root = new System.Windows.Controls.DockPanel();
            outerBorder2.Child = root;

            // 타이틀바
            var titleBar = new Border { Height = 38, CornerRadius = new CornerRadius(10, 10, 0, 0) };
            titleBar.SetResourceReference(Border.BackgroundProperty, "StatusBarBackgroundBrush");
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) win.DragMove(); };
            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            var titleTb = new System.Windows.Controls.TextBlock
            {
                Text = $"{groupValue}  ({start:yyyy-MM-dd} ~ {end:yyyy-MM-dd})",
                FontSize = 13, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                TextAlignment = System.Windows.TextAlignment.Center,
                Foreground = System.Windows.Media.Brushes.White
            };
            var closeHost = new Border
            {
                Width = 36, Height = 28,
                Background = System.Windows.Media.Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Opacity = 0.75
            };
            var closeTb = new System.Windows.Controls.TextBlock
            {
                Text = "✕", FontSize = 14, FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            closeHost.Child = closeTb;
            closeHost.MouseEnter += (s, e) => { ((Border)s).Opacity = 1.0; ((Border)s).Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)); };
            closeHost.MouseLeave += (s, e) => { ((Border)s).Opacity = 0.75; ((Border)s).Background = System.Windows.Media.Brushes.Transparent; };
            closeHost.PreviewMouseLeftButtonDown += (s, e) => { win.Close(); e.Handled = true; };
            Grid.SetColumn(titleTb, 1);
            Grid.SetColumn(closeHost, 2);
            titleRow.Children.Add(titleTb);
            titleRow.Children.Add(closeHost);
            titleBar.Child = titleRow;
            System.Windows.Controls.DockPanel.SetDock(titleBar, System.Windows.Controls.Dock.Top);
            root.Children.Add(titleBar);

            // 열 선택 패널
            var colPanel = new WrapPanel { Margin = new Thickness(10, 8, 10, 4) };
            System.Windows.Controls.DockPanel.SetDock(colPanel, System.Windows.Controls.Dock.Top);
            root.Children.Add(colPanel);

            var grid = new System.Windows.Controls.DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserReorderColumns = true,
                CanUserResizeColumns = true,
                CanUserSortColumns = true,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(8, 0, 8, 8),
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled
            };
            grid.SetResourceReference(System.Windows.Controls.DataGrid.BackgroundProperty, "WindowBackgroundBrush");
            grid.SetResourceReference(System.Windows.Controls.DataGrid.ForegroundProperty, "ForegroundBrush");
            grid.SetResourceReference(System.Windows.Controls.DataGrid.RowBackgroundProperty, "WindowBackgroundBrush");
            grid.SetResourceReference(System.Windows.Controls.DataGrid.AlternatingRowBackgroundProperty, "ContextMenuBorderBrush");
            grid.SetResourceReference(System.Windows.Controls.DataGrid.HorizontalGridLinesBrushProperty, "ContextMenuBorderBrush");
            grid.SetResourceReference(System.Windows.Controls.DataGrid.VerticalGridLinesBrushProperty, "ContextMenuBorderBrush");

            // 셀 스타일 — MaterialDesign이 ForegroundBrush를 덮어쓰지 않도록 명시
            var cellStyle = new Style(typeof(System.Windows.Controls.DataGridCell));
            cellStyle.Setters.Add(new Setter(System.Windows.Controls.Control.BorderThicknessProperty, new Thickness(0)));
            cellStyle.Setters.Add(new Setter(System.Windows.Controls.TextBlock.ForegroundProperty, fg2));
            grid.CellStyle = cellStyle;

            bool gridLoaded = false;

            // 열 헤더 스타일 (테마)
            var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, barBg));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, fg2));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.FontWeightProperty, FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.BorderBrushProperty, barBg));
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Center));
            headerStyle.Setters.Add(new EventSetter(
                UIElement.MouseRightButtonDownEvent,
                new System.Windows.Input.MouseButtonEventHandler((s, e) =>
                {
                    if (s is not System.Windows.Controls.Primitives.DataGridColumnHeader colHeader) return;
                    if (colHeader.Column?.Header is not string colName) return;
                    var ctxMenu = new System.Windows.Controls.ContextMenu();
                    foreach (var (label, alignKey) in new (string, string)[] {
                        ("왼쪽 정렬",   "Left"),
                        ("가운데 정렬", "Center"),
                        ("오른쪽 정렬", "Right")
                    })
                    {
                        var key = alignKey;
                        var cn  = colName;
                        var mi  = new System.Windows.Controls.MenuItem { Header = label };
                        mi.Click += (_, _) =>
                        {
                            meta.RankListColumnAlignments[cn] = key;
                            grid.Dispatcher.InvokeAsync(() => { RebuildColumns(); SaveAllChartStates(); });
                        };
                        ctxMenu.Items.Add(mi);
                    }
                    ctxMenu.PlacementTarget = colHeader;
                    ctxMenu.IsOpen = true;
                    e.Handled = true;
                })
            ));
            grid.ColumnHeaderStyle = headerStyle;

            var colHeaderCbMap = new Dictionary<string, System.Windows.Controls.CheckBox>();

            void DistributeEqualWidths()
            {
                // 저장된 너비가 없을 때만 창 너비에 맞게 균등 분배
                bool hasSaved = grid.Columns.Any(c => c.Header is string h && meta.RankListColumnWidths.TryGetValue(h, out var ww) && ww > 0);
                if (hasSaved) return;
                double avail = grid.ActualWidth - 2;
                int cnt = grid.Columns.Count;
                if (cnt > 0 && avail > cnt * 40)
                {
                    double cw = Math.Floor(avail / cnt);
                    foreach (var c in grid.Columns)
                        c.Width = new System.Windows.Controls.DataGridLength(cw);
                }
            }

            void SaveColumnWidths()
            {
                foreach (var dgCol in grid.Columns)
                {
                    if (dgCol.Header is string h && dgCol.ActualWidth > 0)
                        meta.RankListColumnWidths[h] = dgCol.ActualWidth;
                }
                meta.RankListColumnOrder = grid.Columns
                    .OrderBy(c => c.DisplayIndex)
                    .Where(c => c.Header is string)
                    .Select(c => (string)c.Header!)
                    .ToList();
                SaveAllChartStates();
            }

            void RebuildColumns()
            {
                grid.Columns.Clear();

                var orderedCols = meta.RankListColumnOrder.Count > 0
                    ? meta.RankListColumnOrder
                        .Where(c => meta.RankListVisibleColumns.Contains(c) && rows.Columns.Contains(c))
                        .ToList()
                    : AllRawColumns
                        .Where(c => meta.RankListVisibleColumns.Contains(c) && rows.Columns.Contains(c))
                        .ToList();
                foreach (var c2 in AllRawColumns)
                    if (!orderedCols.Contains(c2) && meta.RankListVisibleColumns.Contains(c2) && rows.Columns.Contains(c2))
                        orderedCols.Add(c2);

                bool hasSavedWidths = orderedCols.Any(c => meta.RankListColumnWidths.TryGetValue(c, out var ww) && ww > 0);

                foreach (var col in orderedCols)
                {
                    double savedW = meta.RankListColumnWidths.TryGetValue(col, out var w) ? w : 0;
                    var colWidth = savedW > 0
                        ? new System.Windows.Controls.DataGridLength(savedW)
                        : new System.Windows.Controls.DataGridLength(130);

                    var alignStr = meta.RankListColumnAlignments.TryGetValue(col, out var a) ? a : "Center";
                    var cellAlign = alignStr == "Left" ? System.Windows.HorizontalAlignment.Left
                                  : alignStr == "Right" ? System.Windows.HorizontalAlignment.Right
                                  : System.Windows.HorizontalAlignment.Center;

                    var cellElemStyle = new Style(typeof(System.Windows.Controls.TextBlock));
                    cellElemStyle.Setters.Add(new Setter(System.Windows.Controls.TextBlock.HorizontalAlignmentProperty, cellAlign));
                    cellElemStyle.Setters.Add(new Setter(System.Windows.Controls.TextBlock.ForegroundProperty, fg2));

                    var dgCol = col == "날짜"
                        ? (System.Windows.Controls.DataGridColumn)new System.Windows.Controls.DataGridTextColumn
                          {
                              Header = col,
                              Binding = new System.Windows.Data.Binding($"[{col}]") { StringFormat = "yyyy-MM-dd" },
                              Width = colWidth,
                              ElementStyle = cellElemStyle
                          }
                        : (System.Windows.Controls.DataGridColumn)new System.Windows.Controls.DataGridTextColumn
                          {
                              Header = col,
                              Binding = new System.Windows.Data.Binding($"[{col}]")
                              {
                                  StringFormat = (col == "총매출액" || col == "총수량" || col == "판매수량" || col == "서비스수량") ? "N0" : null
                              },
                              Width = colWidth,
                              ElementStyle = cellElemStyle
                          };
                    grid.Columns.Add(dgCol);
                }

                if (!hasSavedWidths)
                    grid.Dispatcher.InvokeAsync(DistributeEqualWidths, System.Windows.Threading.DispatcherPriority.Loaded);
            }

            // 열 선택 체크박스 생성
            foreach (var col in AllRawColumns)
            {
                var cb = new System.Windows.Controls.CheckBox
                {
                    Content = col,
                    IsChecked = meta.RankListVisibleColumns.Contains(col),
                    Margin = new Thickness(6, 3, 12, 3),
                    VerticalAlignment = VerticalAlignment.Center
                };
                cb.SetResourceReference(System.Windows.Controls.CheckBox.ForegroundProperty, "ForegroundBrush");
                cb.Checked   += (_, _) => { if (!meta.RankListVisibleColumns.Contains(col)) meta.RankListVisibleColumns.Add(col); grid.Dispatcher.InvokeAsync(() => { RebuildColumns(); SaveAllChartStates(); }); };
                cb.Unchecked += (_, _) => { meta.RankListVisibleColumns.Remove(col); grid.Dispatcher.InvokeAsync(() => { RebuildColumns(); SaveAllChartStates(); }); };
                colPanel.Children.Add(cb);
                colHeaderCbMap[col] = cb;
            }

            // DataGrid 데이터 바인딩
            grid.ItemsSource = rows.DefaultView;
            RebuildColumns();
            grid.Loaded += (_, _) => { gridLoaded = true; DistributeEqualWidths(); };
            root.Children.Add(grid);

            win.Content = outerBorder2;
            win.Closing += (_, _) => SaveColumnWidths();
            win.ShowDialog();
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
                LegendTextPaint = new SolidColorPaint(ParseChartColor(meta.ChartLabelColor, axisLabel)) { SKTypeface = SKTypeface.FromFamilyName(meta.ChartFont) }
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
                            "Line"     => CreateLineChart(meta),
                            "Bar"      => CreateBarChart(meta),
                            "HBar"     => CreateHBarChart(meta),
                            "Pie"      => CreatePieChart(meta),
                            "Gauge"    => CreateGaugeChart(meta),
                            "RankList" => CreateRankList(meta),
                            _          => null
                        };
                        if (newChart != null)
                        {
                            if (newChart is System.Windows.Controls.Control ctrl)
                                ctrl.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "StatusBarBackgroundBrush");
                            if (meta.InnerHeight > 0 &&
                                newChart is System.Windows.Controls.ScrollViewer svTh &&
                                svTh.Content is CartesianChart ccTh)
                                ApplyInnerChartHeight(ccTh, meta);
                            chartBorder.Child = WrapWithDbDates(newChart, meta);
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
            bool isResizing = false;
            bool isResizingInner = false;
            bool shiftWasPressedOnStart = false;
            System.Windows.Point dragStart = new();
            System.Windows.Point elementStart = new();
            double origW = 0, origH = 0, innerStartH = 0;
            ResizeDirection resizeDirection = ResizeDirection.None;
            const double ResizeGripSize = 10.0;
            const double DragThreshold = 3.0;

            CartesianChart? GetInnerChart() => GetInnerChartFromChild(border.Child);

            ResizeDirection GetChartResizeDir(System.Windows.Point pos)
            {
                double w = border.ActualWidth > 0 ? border.ActualWidth : border.Width;
                double h = border.ActualHeight > 0 ? border.ActualHeight : border.Height;
                bool L = pos.X <= ResizeGripSize, R = pos.X >= w - ResizeGripSize;
                bool T = pos.Y <= ResizeGripSize, B = pos.Y >= h - ResizeGripSize;
                if (T && L) return ResizeDirection.TopLeft;
                if (T && R) return ResizeDirection.TopRight;
                if (B && L) return ResizeDirection.BottomLeft;
                if (B && R) return ResizeDirection.BottomRight;
                if (T) return ResizeDirection.Top;
                if (B) return ResizeDirection.Bottom;
                if (L) return ResizeDirection.Left;
                if (R) return ResizeDirection.Right;
                return ResizeDirection.None;
            }

            System.Windows.Input.Cursor DirToCursor(ResizeDirection dir) => dir switch
            {
                ResizeDirection.Left or ResizeDirection.Right => System.Windows.Input.Cursors.SizeWE,
                ResizeDirection.Top or ResizeDirection.Bottom => System.Windows.Input.Cursors.SizeNS,
                ResizeDirection.TopLeft or ResizeDirection.BottomRight => System.Windows.Input.Cursors.SizeNWSE,
                ResizeDirection.TopRight or ResizeDirection.BottomLeft => System.Windows.Input.Cursors.SizeNESW,
                _ => System.Windows.Input.Cursors.Arrow
            };

            border.PreviewMouseLeftButtonDown += (s, e) =>
            {
                bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                bool ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

                if (shift && ctrl)
                {
                    isResizingInner = true;
                    innerStartH = GetInnerChart()?.Height ?? border.Height;
                    dragStart = e.GetPosition(canvas);
                    border.CaptureMouse();
                    e.Handled = true;
                }
                else if (shift)
                {
                    shiftWasPressedOnStart = true;
                    dragStart = e.GetPosition(canvas);
                    elementStart = new System.Windows.Point(
                        System.Windows.Controls.Canvas.GetLeft(border),
                        System.Windows.Controls.Canvas.GetTop(border));
                    origW = border.ActualWidth > 0 ? border.ActualWidth : border.Width;
                    origH = border.ActualHeight > 0 ? border.ActualHeight : border.Height;
                    resizeDirection = GetChartResizeDir(e.GetPosition(border));
                    isResizing = resizeDirection != ResizeDirection.None;
                    isDragging = false;
                    border.CaptureMouse();
                    e.Handled = true;
                }
                // Shift 없으면 이벤트 전파 (컨텍스트 메뉴 등 정상 동작)
            };

            border.MouseMove += (s, e) =>
            {
                if (!border.IsMouseCaptured)
                {
                    bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                    bool ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                    if (shift && ctrl)
                        border.Cursor = System.Windows.Input.Cursors.SizeNS;
                    else if (shift)
                    {
                        var dir = GetChartResizeDir(e.GetPosition(border));
                        border.Cursor = dir == ResizeDirection.None
                            ? System.Windows.Input.Cursors.SizeAll
                            : DirToCursor(dir);
                    }
                    else
                        border.Cursor = System.Windows.Input.Cursors.Arrow;
                    return;
                }

                var current = e.GetPosition(canvas);

                if (isResizingInner)
                {
                    var inner = GetInnerChart();
                    if (inner != null)
                    {
                        double newH = Math.Max(100, innerStartH + (current.Y - dragStart.Y));
                        if (border.Tag is ChartMeta mi)
                        {
                            mi.InnerHeight = newH;
                            ApplyInnerChartHeight(inner, mi);
                        }
                        else
                            inner.Height = newH;
                    }
                }
                else if (shiftWasPressedOnStart)
                {
                    var diff = current - dragStart;
                    if (!isDragging && !isResizing && diff.Length > DragThreshold)
                    {
                        if (resizeDirection != ResizeDirection.None) isResizing = true;
                        else isDragging = true;
                    }

                    if (isResizing)
                    {
                        double newX = elementStart.X, newY = elementStart.Y;
                        double newW = origW, newH = origH;
                        switch (resizeDirection)
                        {
                            case ResizeDirection.Right:       newW = origW + diff.X; break;
                            case ResizeDirection.Left:        newW = origW - diff.X; newX = elementStart.X + diff.X; break;
                            case ResizeDirection.Bottom:      newH = origH + diff.Y; break;
                            case ResizeDirection.Top:         newH = origH - diff.Y; newY = elementStart.Y + diff.Y; break;
                            case ResizeDirection.BottomRight: newW = origW + diff.X; newH = origH + diff.Y; break;
                            case ResizeDirection.BottomLeft:  newW = origW - diff.X; newH = origH + diff.Y; newX = elementStart.X + diff.X; break;
                            case ResizeDirection.TopRight:    newW = origW + diff.X; newH = origH - diff.Y; newY = elementStart.Y + diff.Y; break;
                            case ResizeDirection.TopLeft:     newW = origW - diff.X; newH = origH - diff.Y; newX = elementStart.X + diff.X; newY = elementStart.Y + diff.Y; break;
                        }
                        newW = Math.Round(Math.Max(80, Math.Min(newW, canvas.Width - newX)) / GridSize) * GridSize;
                        newH = Math.Round(Math.Max(60, Math.Min(newH, canvas.Height - newY)) / GridSize) * GridSize;
                        newX = Math.Round(Math.Max(0, Math.Min(newX, canvas.Width  - newW)) / GridSize) * GridSize;
                        newY = Math.Round(Math.Max(0, Math.Min(newY, canvas.Height - newH)) / GridSize) * GridSize;
                        border.Width = newW; border.Height = newH;
                        System.Windows.Controls.Canvas.SetLeft(border, newX);
                        System.Windows.Controls.Canvas.SetTop(border, newY);
                        if (border.Tag is ChartMeta mr) { mr.Width = newW; mr.Height = newH; }
                    }
                    else if (isDragging)
                    {
                        double newX = Math.Round(Math.Max(0, Math.Min(elementStart.X + diff.X, canvas.Width  - border.Width))  / GridSize) * GridSize;
                        double newY = Math.Round(Math.Max(0, Math.Min(elementStart.Y + diff.Y, canvas.Height - border.Height)) / GridSize) * GridSize;
                        System.Windows.Controls.Canvas.SetLeft(border, newX);
                        System.Windows.Controls.Canvas.SetTop(border, newY);
                    }
                }
            };

            border.MouseLeftButtonUp += (s, e) =>
            {
                if (isDragging || isResizing || isResizingInner || shiftWasPressedOnStart)
                {
                    isDragging = false;
                    isResizing = false;
                    isResizingInner = false;
                    shiftWasPressedOnStart = false;
                    border.ReleaseMouseCapture();
                    SaveAllChartStates();
                }
            };
        }

        private static System.Windows.Controls.ComboBox BuildChartFontCombo(string currentFont)
        {
            var combo = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 12) };
            combo.Style = null;
            var fonts = new[] { "Malgun Gothic", "맑은 고딕", "굴림", "돋움", "바탕", "나눔고딕", "나눔바른고딕", "Segoe UI", "Arial", "Tahoma", "Consolas" };
            foreach (var f in fonts) combo.Items.Add(f);
            combo.SelectedItem = fonts.Contains(currentFont) ? currentFont : fonts[0];
            return combo;
        }

        private static readonly (string Label, string? Hex)[] ChartColorPresets =
        {
            ("기본 (테마)", null),
            ("흰색",   "#F0F0F0"),
            ("밝은 회색", "#C0C0C0"),
            ("진한 회색", "#606060"),
            ("검정",   "#202020"),
            ("파랑",   "#5599FF"),
            ("하늘색", "#44CCEE"),
            ("초록",   "#4CAF50"),
            ("빨강",   "#FF5A5A"),
            ("노랑",   "#FFD700"),
            ("주황",   "#FF9800"),
        };

        private static System.Windows.Controls.ComboBox BuildChartColorCombo(string? currentHex)
        {
            var combo = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 12) };
            combo.Style = null;
            foreach (var (label, _) in ChartColorPresets) combo.Items.Add(label);
            int idx = 0;
            for (int i = 0; i < ChartColorPresets.Length; i++)
                if (ChartColorPresets[i].Hex == currentHex) { idx = i; break; }
            combo.SelectedIndex = idx;
            return combo;
        }

        private static System.Windows.Controls.ComboBox BuildChartSizeCombo(double currentSize)
        {
            var combo = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 12) };
            combo.Style = null;
            var sizes = new[] { "기본", "8", "10", "11", "12", "13", "14", "16", "18", "20" };
            foreach (var s in sizes) combo.Items.Add(s);
            string cur = currentSize > 0 ? ((int)currentSize).ToString() : "기본";
            combo.SelectedItem = sizes.Contains(cur) ? cur : "기본";
            return combo;
        }

        private static SKColor ParseChartColor(string? hex, SKColor fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                return new SKColor(c.R, c.G, c.B, c.A);
            }
            catch { return fallback; }
        }

        private void EditChartDialog(Border border, Canvas canvas, ChartMeta meta)
        {
            try { EditChartDialogImpl(border, canvas, meta); }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"차트 수정 창 오류:\n{ex.GetType().Name}\n{ex.Message}\n\n{ex.StackTrace?.Split('\n')[0]}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditChartDialogImpl(Border border, Canvas canvas, ChartMeta meta)
        {
            var configDlg = new System.Windows.Window
            {
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Width = 480,
                MaxHeight = SystemParameters.PrimaryScreenHeight * 0.88,
                SizeToContent = SizeToContent.Height,
                Background = System.Windows.Media.Brushes.Transparent,
                WindowStyle = System.Windows.WindowStyle.None,
                AllowsTransparency = true
            };

            // ── 로컬 헬퍼 ──────────────────────────────────────────────────────
            var res = System.Windows.Application.Current.Resources;
            var cardBg  = (res["ContextMenuBorderBrush"]   as System.Windows.Media.Brush) ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 42, 56));
            var winBg   = (res["WindowBackgroundBrush"]    as System.Windows.Media.Brush) ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 36));
            var fg      = (res["ForegroundBrush"]          as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.White;
            var accent  = (res["StatusBarBorderBrush"]     as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.SteelBlue;
            var sepBrush = (res["StatusBarBorderBrush"] as System.Windows.Media.SolidColorBrush)?.Color is System.Windows.Media.Color ac
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(60, ac.R, ac.G, ac.B))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 100, 140, 200));

            System.Windows.Controls.ComboBox MakeCombo(string selected, params string[] items)
            {
                var cb = new System.Windows.Controls.ComboBox { Style = null, Margin = new Thickness(0), Height = 28 };
                cb.SetResourceReference(System.Windows.Controls.ComboBox.BackgroundProperty, "ContextMenuBackgroundBrush");
                cb.SetResourceReference(System.Windows.Controls.ComboBox.ForegroundProperty, "ForegroundBrush");
                foreach (var it in items) cb.Items.Add(it);
                cb.SelectedItem = selected;
                if (cb.SelectedIndex < 0 && items.Length > 0) cb.SelectedIndex = 0;
                return cb;
            }

            System.Windows.Controls.ComboBox MakeFontCombo(string cur)
            {
                var c = BuildChartFontCombo(cur); c.Margin = new Thickness(0); c.Height = 28; return c;
            }
            System.Windows.Controls.ComboBox MakeSizeCombo(double cur)
            {
                var c = BuildChartSizeCombo(cur); c.Margin = new Thickness(0); c.Height = 28; return c;
            }

            // 2-컬럼 라벨+컨트롤 행
            Grid LRow(string label, UIElement ctrl, double lw = 78)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(lw) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var tb = new System.Windows.Controls.TextBlock
                {
                    Text = label, FontSize = 12, Opacity = 0.75,
                    VerticalAlignment = VerticalAlignment.Center
                };
                tb.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");
                Grid.SetColumn(tb, 0); Grid.SetColumn(ctrl, 1);
                g.Children.Add(tb); g.Children.Add(ctrl);
                return g;
            }

            // 섹션 카드
            Border SectionCard(string title, System.Windows.Controls.StackPanel body)
            {
                var hdr = new System.Windows.Controls.TextBlock
                {
                    Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 10), Opacity = 0.55
                };
                hdr.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");
                var inner = new System.Windows.Controls.StackPanel();
                inner.Children.Add(hdr);
                var childList = body.Children.Cast<UIElement>().ToList();
                body.Children.Clear();
                foreach (UIElement child in childList) inner.Children.Add(child);
                var card = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 12, 14, 4),
                    Margin = new Thickness(0, 0, 0, 8),
                    Background = cardBg,
                    Child = inner
                };
                return card;
            }

            // 색상 스와치 피커
            string? chosenColorHex = meta.ChartLabelColor;
            List<Border> swatches = new();

            FrameworkElement BuildColorPicker(System.Action onChanged)
            {
                var panel = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
                swatches.Clear();

                void Refresh()
                {
                    foreach (var sw in swatches)
                    {
                        bool sel = (sw.Tag as string) == chosenColorHex ||
                                   (sw.Tag == null && chosenColorHex == null);
                        sw.BorderThickness = new Thickness(sel ? 2.5 : 0);
                        sw.BorderBrush = System.Windows.Media.Brushes.White;
                        sw.Opacity = sel ? 1.0 : 0.75;
                    }
                }

                foreach (var (label, hex) in ChartColorPresets)
                {
                    System.Windows.Media.Brush swBg;
                    UIElement? inner = null;
                    if (hex == null)
                    {
                        swBg = new System.Windows.Media.LinearGradientBrush(
                            new System.Windows.Media.GradientStopCollection {
                                new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(255,90,90), 0),
                                new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(90,200,255), 0.5),
                                new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(255,220,80), 1)
                            }, 45);
                        inner = new System.Windows.Controls.TextBlock
                        {
                            Text = "A", FontSize = 10, FontWeight = FontWeights.Bold,
                            Foreground = System.Windows.Media.Brushes.White,
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                    }
                    else
                    {
                        swBg = new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
                    }

                    var swatch = new Border
                    {
                        Width = 26, Height = 26,
                        CornerRadius = new CornerRadius(5),
                        Margin = new Thickness(0, 0, 5, 5),
                        Background = swBg,
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Tag = hex,
                        ToolTip = label,
                        Child = inner
                    };

                    swatch.MouseLeftButtonUp += (s, _) =>
                    {
                        chosenColorHex = ((Border)s).Tag as string;
                        Refresh();
                        onChanged();
                    };
                    swatches.Add(swatch);
                    panel.Children.Add(swatch);
                }
                Refresh();
                return panel;
            }

            // ── 외부 Border + DockPanel ──────────────────────────────────────
            var outerBorder = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Background = winBg };
            outerBorder.SetResourceReference(Border.BorderBrushProperty, "StatusBarBorderBrush");
            var outerDock = new System.Windows.Controls.DockPanel();
            outerBorder.Child = outerDock;

            // 타이틀바
            string typeLabel = meta.ChartType switch
            {
                "Line" => "선 그래프", "Bar" => "세로 막대", "HBar" => "가로 막대",
                "Pie" => "원형 차트", "Gauge" => "게이지", "RankList" => "순위표", _ => meta.ChartType
            };
            var titleBar2 = new Border { Height = 44, Padding = new Thickness(18, 0, 10, 0), Background = winBg };
            titleBar2.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) configDlg.DragMove(); };
            var tg = new Grid();
            tg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ttb = new System.Windows.Controls.TextBlock
            {
                Text = $"{typeLabel} 수정", FontSize = 14, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            ttb.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");
            var xb = new System.Windows.Controls.Button
            {
                Content = "✕", Width = 32, Height = 32, BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent, FontSize = 13,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            xb.SetResourceReference(System.Windows.Controls.Button.ForegroundProperty, "ForegroundBrush");
            xb.Click += (_, _) => configDlg.Close();
            Grid.SetColumn(ttb, 0); Grid.SetColumn(xb, 1);
            tg.Children.Add(ttb); tg.Children.Add(xb);
            titleBar2.Child = tg;
            System.Windows.Controls.DockPanel.SetDock(titleBar2, System.Windows.Controls.Dock.Top);
            outerDock.Children.Add(titleBar2);

            // 타이틀 구분선
            var titleLine = new System.Windows.Shapes.Rectangle { Height = 1, Fill = sepBrush };
            System.Windows.Controls.DockPanel.SetDock(titleLine, System.Windows.Controls.Dock.Top);
            outerDock.Children.Add(titleLine);

            // 버튼 바 (하단)
            var btnBarBorder = new Border { Padding = new Thickness(16, 10, 16, 14), Background = winBg };
            var btnRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            var applyBtn = new System.Windows.Controls.Button
            {
                Content = "적용", Padding = new Thickness(20, 7, 20, 7),
                Margin = new Thickness(8, 0, 0, 0), FontWeight = FontWeights.SemiBold
            };
            applyBtn.SetResourceReference(System.Windows.Controls.Button.BackgroundProperty, "StatusBarBackgroundBrush");
            applyBtn.Foreground = System.Windows.Media.Brushes.White;
            applyBtn.BorderThickness = new Thickness(0);

            var cancelBtn2 = new System.Windows.Controls.Button
            {
                Content = "취소", Padding = new Thickness(16, 7, 16, 7),
                Background = cardBg, BorderThickness = new Thickness(0)
            };
            cancelBtn2.SetResourceReference(System.Windows.Controls.Button.ForegroundProperty, "ForegroundBrush");
            cancelBtn2.Click += (_, _) => configDlg.Close();

            btnRow.Children.Add(cancelBtn2); btnRow.Children.Add(applyBtn);
            btnBarBorder.Child = btnRow;
            System.Windows.Controls.DockPanel.SetDock(btnBarBorder, System.Windows.Controls.Dock.Bottom);
            outerDock.Children.Add(btnBarBorder);

            // 스크롤 콘텐츠
            var sv3 = new System.Windows.Controls.ScrollViewer
            {
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled
            };
            var mainStack = new System.Windows.Controls.StackPanel { Margin = new Thickness(14, 10, 14, 6) };
            sv3.Content = mainStack;
            outerDock.Children.Add(sv3);

            // ── 섹션 구성 ──────────────────────────────────────────────────────
            if (meta.DataSource == "Static")
            {
                // 데이터 섹션
                var dataBox = new System.Windows.Controls.TextBox { Text = string.Join(", ", meta.StaticData), Height = 28 };
                dataBox.SetResourceReference(System.Windows.Controls.TextBox.BackgroundProperty, "ContextMenuBackgroundBrush");
                dataBox.SetResourceReference(System.Windows.Controls.TextBox.ForegroundProperty, "ForegroundBrush");
                var labelBox = new System.Windows.Controls.TextBox { Text = string.Join(", ", meta.Labels), Height = 28 };
                labelBox.SetResourceReference(System.Windows.Controls.TextBox.BackgroundProperty, "ContextMenuBackgroundBrush");
                labelBox.SetResourceReference(System.Windows.Controls.TextBox.ForegroundProperty, "ForegroundBrush");

                var dataBody = new System.Windows.Controls.StackPanel();
                dataBody.Children.Add(LRow("데이터 값", dataBox));
                dataBody.Children.Add(LRow("라벨", labelBox));
                mainStack.Children.Add(SectionCard("데이터", dataBody));

                // 스타일 섹션
                var sFontCombo = MakeFontCombo(meta.ChartFont);
                var sSizeCombo = MakeSizeCombo(meta.ChartLabelSize);
                var colorPicker = BuildColorPicker(() => { });

                var styleBody = new System.Windows.Controls.StackPanel();
                styleBody.Children.Add(LRow("글꼴", sFontCombo));
                styleBody.Children.Add(LRow("크기", sSizeCombo));
                var colorLRow = LRow("글씨 색상", colorPicker, 78);
                colorLRow.Margin = new Thickness(0, 0, 0, 4);
                styleBody.Children.Add(colorLRow);

                System.Windows.Controls.CheckBox? showBarsCheck = null;
                if (meta.ChartType == "HBar")
                {
                    showBarsCheck = new System.Windows.Controls.CheckBox
                    {
                        Content = "막대 표시", IsChecked = meta.ShowBars, Margin = new Thickness(0, 4, 0, 4)
                    };
                    showBarsCheck.SetResourceReference(System.Windows.Controls.CheckBox.ForegroundProperty, "ForegroundBrush");
                    styleBody.Children.Add(showBarsCheck);
                }
                mainStack.Children.Add(SectionCard("스타일", styleBody));

                applyBtn.Click += (s, ev) =>
                {
                    meta.StaticData = dataBox.Text.Split(',').Select(x => double.TryParse(x.Trim(), out var v) ? v : 0).ToList();
                    meta.Labels = labelBox.Text.Split(',').Select(x => x.Trim()).ToList();
                    meta.ChartFont = sFontCombo.SelectedItem?.ToString() ?? "Malgun Gothic";
                    meta.ChartLabelColor = chosenColorHex;
                    meta.ChartLabelSize = sSizeCombo.SelectedItem?.ToString() is string sv && double.TryParse(sv, out var sd) ? sd : 0;
                    if (showBarsCheck != null) meta.ShowBars = showBarsCheck.IsChecked == true;
                    FrameworkElement? nc = null;
                    switch (meta.ChartType)
                    {
                        case "Line": nc = CreateLineChart(meta); break; case "Bar": nc = CreateBarChart(meta); break;
                        case "HBar": nc = CreateHBarChart(meta); break; case "Pie": nc = CreatePieChart(meta); break;
                        case "Gauge": nc = CreateGaugeChart(meta); break; case "RankList": nc = CreateRankList(meta); break;
                    }
                    if (nc != null) { if (meta.InnerHeight > 0 && nc is System.Windows.Controls.ScrollViewer sv2 && sv2.Content is CartesianChart cc2) ApplyInnerChartHeight(cc2, meta); border.Child = WrapWithDbDates(nc, meta); }
                    SaveAllChartStates(); configDlg.Close();
                };
            }
            else if (meta.DataSource == "Db" || meta.ChartType == "RankList")
            {
                // 집계 섹션
                var eGroupByCombo = MakeCombo(meta.DbGroupBy ?? "매장명", "매장명", "중분류", "메뉴명");
                var eValueCombo   = MakeCombo(meta.DbValueColumn ?? "총매출액", "총매출액", "총수량", "판매수량", "서비스수량");
                var eSortCombo    = MakeCombo(meta.DbSortAscending ? "오름차순 (낮은 값 먼저)" : "내림차순 (높은 값 먼저)",
                                             "내림차순 (높은 값 먼저)", "오름차순 (낮은 값 먼저)");
                var aggBody = new System.Windows.Controls.StackPanel();
                aggBody.Children.Add(LRow("기준", eGroupByCombo));
                aggBody.Children.Add(LRow("컬럼", eValueCombo));
                aggBody.Children.Add(LRow("정렬", eSortCombo));
                mainStack.Children.Add(SectionCard("집계", aggBody));

                // 기간 섹션
                var eStartDate = new System.Windows.Controls.DatePicker { Height = 28, Margin = new Thickness(0, 0, 6, 0), SelectedDate = meta.DbStartDate ?? DateTime.Today.AddDays(-7) };
                var eEndDate   = new System.Windows.Controls.DatePicker { Height = 28, SelectedDate = meta.DbEndDate ?? DateTime.Today.AddDays(-1) };
                var dateRow = new Grid();
                dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                dateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var sepTb = new System.Windows.Controls.TextBlock { Text = "~", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), FontSize = 14 };
                sepTb.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");
                Grid.SetColumn(eStartDate, 0); Grid.SetColumn(sepTb, 1); Grid.SetColumn(eEndDate, 2);
                dateRow.Children.Add(eStartDate); dateRow.Children.Add(sepTb); dateRow.Children.Add(eEndDate);
                var dateBody = new System.Windows.Controls.StackPanel();
                dateBody.Children.Add(dateRow);
                mainStack.Children.Add(SectionCard("기간", dateBody));

                // 필터 섹션
                var eStoreBox = MakeCombo(meta.DbStoreName ?? "(전체)", new[] { "(전체)" }.Concat(LoadDbDistinctValues("매장명")).ToArray());
                var eMiddleCatBox = MakeCombo(meta.DbMiddleCategoryFilter ?? "(전체)", new[] { "(전체)" }.Concat(LoadDbDistinctValues("중분류")).ToArray());
                var eMenuBox = MakeCombo(meta.DbMenuNameFilter ?? "(전체)", new[] { "(전체)" }.Concat(LoadDbDistinctValues("메뉴명")).ToArray());
                var filterBody = new System.Windows.Controls.StackPanel();
                filterBody.Children.Add(LRow("매장명", eStoreBox));
                filterBody.Children.Add(LRow("중분류", eMiddleCatBox));
                filterBody.Children.Add(LRow("메뉴명", eMenuBox));
                mainStack.Children.Add(SectionCard("필터", filterBody));

                // 스타일 섹션 (RankList는 별도)
                System.Windows.Controls.ComboBox? eFontCombo = null, eSizeCombo = null;
                System.Windows.Controls.ComboBox? rankLabelFontCombo = null, rankLabelSizeCombo = null;
                System.Windows.Controls.ComboBox? rankValueFontCombo = null, rankValueSizeCombo = null;
                System.Windows.Controls.CheckBox? dbShowBarsCheck = null;
                string? chosenLabelColorHex = meta.RankListLabelColor;
                string? chosenValueColorHex = meta.RankListValueColor;

                if (meta.ChartType == "RankList")
                {
                    rankLabelFontCombo = MakeFontCombo(meta.RankListLabelFont);
                    rankLabelSizeCombo = MakeSizeCombo(meta.RankListLabelSize);
                    rankValueFontCombo = MakeFontCombo(meta.RankListValueFont);
                    rankValueSizeCombo = MakeSizeCombo(meta.RankListValueSize);

                    // 색상 스와치 피커 (기준용 / 컬럼용 각각)
                    List<Border> labelSwatches = new();
                    List<Border> valueSwatches = new();

                    FrameworkElement BuildRankColorPicker(List<Border> swatchList, ref string? chosen, System.Action refresh)
                    {
                        var panel = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
                        swatchList.Clear();
                        foreach (var (lbl, hex) in ChartColorPresets)
                        {
                            System.Windows.Media.Brush swBg;
                            UIElement? swInner = null;
                            if (hex == null)
                            {
                                swBg = new System.Windows.Media.LinearGradientBrush(
                                    new System.Windows.Media.GradientStopCollection {
                                        new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(255,90,90), 0),
                                        new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(90,200,255), 0.5),
                                        new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(255,220,80), 1)
                                    }, 45);
                                swInner = new System.Windows.Controls.TextBlock
                                {
                                    Text = "A", FontSize = 10, FontWeight = FontWeights.Bold,
                                    Foreground = System.Windows.Media.Brushes.White,
                                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                                    VerticalAlignment = VerticalAlignment.Center
                                };
                            }
                            else
                            {
                                swBg = new System.Windows.Media.SolidColorBrush(
                                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
                            }
                            var sw = new Border
                            {
                                Width = 22, Height = 22, CornerRadius = new CornerRadius(4),
                                Margin = new Thickness(0, 0, 4, 4),
                                Background = swBg, Cursor = System.Windows.Input.Cursors.Hand,
                                Tag = hex, ToolTip = lbl, Child = swInner
                            };
                            swatchList.Add(sw);
                            panel.Children.Add(sw);
                        }
                        refresh();
                        return panel;
                    }

                    void RefreshLabel()
                    {
                        foreach (var sw in labelSwatches)
                        {
                            bool sel = (sw.Tag as string) == chosenLabelColorHex || (sw.Tag == null && chosenLabelColorHex == null);
                            sw.BorderThickness = new Thickness(sel ? 2.5 : 0);
                            sw.BorderBrush = System.Windows.Media.Brushes.White;
                            sw.Opacity = sel ? 1.0 : 0.75;
                        }
                    }
                    void RefreshValue()
                    {
                        foreach (var sw in valueSwatches)
                        {
                            bool sel = (sw.Tag as string) == chosenValueColorHex || (sw.Tag == null && chosenValueColorHex == null);
                            sw.BorderThickness = new Thickness(sel ? 2.5 : 0);
                            sw.BorderBrush = System.Windows.Media.Brushes.White;
                            sw.Opacity = sel ? 1.0 : 0.75;
                        }
                    }

                    var labelColorPanel = BuildRankColorPicker(labelSwatches, ref chosenLabelColorHex, RefreshLabel);
                    foreach (Border sw in labelSwatches)
                    {
                        sw.MouseLeftButtonUp += (s, _) => { chosenLabelColorHex = ((Border)s).Tag as string; RefreshLabel(); };
                    }
                    var valueColorPanel = BuildRankColorPicker(valueSwatches, ref chosenValueColorHex, RefreshValue);
                    foreach (Border sw in valueSwatches)
                    {
                        sw.MouseLeftButtonUp += (s, _) => { chosenValueColorHex = ((Border)s).Tag as string; RefreshValue(); };
                    }

                    // 2열 그리드 카드
                    var twoColGrid = new Grid { Margin = new Thickness(0) };
                    twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                    twoColGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    // 왼쪽: 기준 열
                    var leftCol = new System.Windows.Controls.StackPanel();
                    var leftHdr = new System.Windows.Controls.TextBlock
                    {
                        Text = "집계 기준", FontSize = 11, FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 8), Opacity = 0.6
                    };
                    leftHdr.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");
                    leftCol.Children.Add(leftHdr);
                    leftCol.Children.Add(LRow("글꼴", rankLabelFontCombo, 42));
                    leftCol.Children.Add(LRow("크기", rankLabelSizeCombo, 42));
                    var labelColorLbl = new System.Windows.Controls.TextBlock { Text = "색상", FontSize = 12, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 4) };
                    labelColorLbl.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");
                    leftCol.Children.Add(labelColorLbl);
                    leftCol.Children.Add(labelColorPanel);

                    // 오른쪽: 컬럼 열
                    var rightCol = new System.Windows.Controls.StackPanel();
                    var rightHdr = new System.Windows.Controls.TextBlock
                    {
                        Text = "집계 컬럼", FontSize = 11, FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 8), Opacity = 0.6
                    };
                    rightHdr.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");
                    rightCol.Children.Add(rightHdr);
                    rightCol.Children.Add(LRow("글꼴", rankValueFontCombo, 42));
                    rightCol.Children.Add(LRow("크기", rankValueSizeCombo, 42));
                    var valueColorLbl = new System.Windows.Controls.TextBlock { Text = "색상", FontSize = 12, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 4) };
                    valueColorLbl.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");
                    rightCol.Children.Add(valueColorLbl);
                    rightCol.Children.Add(valueColorPanel);

                    Grid.SetColumn(leftCol, 0);
                    Grid.SetColumn(rightCol, 2);
                    twoColGrid.Children.Add(leftCol);
                    twoColGrid.Children.Add(rightCol);

                    var rlCardInner = new System.Windows.Controls.StackPanel();
                    var rlCardHdr = new System.Windows.Controls.TextBlock
                    {
                        Text = "글꼴 / 색상", FontSize = 11, FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 10), Opacity = 0.55
                    };
                    rlCardHdr.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");
                    rlCardInner.Children.Add(rlCardHdr);
                    rlCardInner.Children.Add(twoColGrid);
                    var rlCard = new Border
                    {
                        CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 8),
                        Margin = new Thickness(0, 0, 0, 8), Background = cardBg, Child = rlCardInner
                    };
                    mainStack.Children.Add(rlCard);
                }
                else
                {
                    eFontCombo = MakeFontCombo(meta.ChartFont);
                    eSizeCombo = MakeSizeCombo(meta.ChartLabelSize);
                    var colorPicker2 = BuildColorPicker(() => { });
                    var styleBody2 = new System.Windows.Controls.StackPanel();
                    styleBody2.Children.Add(LRow("글꼴", eFontCombo));
                    styleBody2.Children.Add(LRow("크기", eSizeCombo));
                    styleBody2.Children.Add(LRow("글씨 색상", colorPicker2, 78));
                    if (meta.ChartType == "HBar")
                    {
                        dbShowBarsCheck = new System.Windows.Controls.CheckBox { Content = "막대 표시", IsChecked = meta.ShowBars, Margin = new Thickness(0, 4, 0, 4) };
                        dbShowBarsCheck.SetResourceReference(System.Windows.Controls.CheckBox.ForegroundProperty, "ForegroundBrush");
                        styleBody2.Children.Add(dbShowBarsCheck);
                    }
                    mainStack.Children.Add(SectionCard("스타일", styleBody2));
                }

                applyBtn.Click += (s, ev) =>
                {
                    meta.DbGroupBy = eGroupByCombo.SelectedItem?.ToString() ?? "매장명";
                    meta.DbValueColumn = eValueCombo.SelectedItem?.ToString() ?? "총매출액";
                    meta.DbStartDate = eStartDate.SelectedDate;
                    meta.DbEndDate = eEndDate.SelectedDate;
                    meta.DbStoreName = eStoreBox.SelectedIndex <= 0 ? null : eStoreBox.SelectedItem?.ToString();
                    meta.DbMiddleCategoryFilter = eMiddleCatBox.SelectedIndex <= 0 ? null : eMiddleCatBox.SelectedItem?.ToString();
                    meta.DbMenuNameFilter = eMenuBox.SelectedIndex <= 0 ? null : eMenuBox.SelectedItem?.ToString();
                    meta.DbSortAscending = eSortCombo.SelectedIndex == 1;
                    if (eFontCombo != null) meta.ChartFont = eFontCombo.SelectedItem?.ToString() ?? "Malgun Gothic";
                    if (eSizeCombo != null) meta.ChartLabelSize = eSizeCombo.SelectedItem?.ToString() is string ev2 && double.TryParse(ev2, out var ed) ? ed : 0;
                    if (eFontCombo != null) meta.ChartLabelColor = chosenColorHex;
                    if (dbShowBarsCheck != null) meta.ShowBars = dbShowBarsCheck.IsChecked == true;
                    if (rankLabelFontCombo != null) meta.RankListLabelFont = rankLabelFontCombo.SelectedItem?.ToString() ?? "Malgun Gothic";
                    if (rankLabelSizeCombo != null) meta.RankListLabelSize = rankLabelSizeCombo.SelectedItem?.ToString() is string ls && double.TryParse(ls, out var ld) ? ld : 13;
                    if (rankValueFontCombo != null) meta.RankListValueFont = rankValueFontCombo.SelectedItem?.ToString() ?? "Malgun Gothic";
                    if (rankValueSizeCombo != null) meta.RankListValueSize = rankValueSizeCombo.SelectedItem?.ToString() is string vs && double.TryParse(vs, out var vd) ? vd : 13;
                    if (meta.ChartType == "RankList") { meta.RankListLabelColor = chosenLabelColorHex; meta.RankListValueColor = chosenValueColorHex; }
                    try { LoadDbData(meta); } catch (Exception ex2) { System.Diagnostics.Debug.WriteLine($"[LoadDbData] {ex2.Message}"); }
                    FrameworkElement? nc2 = null;
                    switch (meta.ChartType)
                    {
                        case "Line": nc2 = CreateLineChart(meta); break; case "Bar": nc2 = CreateBarChart(meta); break;
                        case "HBar": nc2 = CreateHBarChart(meta); break; case "Pie": nc2 = CreatePieChart(meta); break;
                        case "Gauge": nc2 = CreateGaugeChart(meta); break; case "RankList": nc2 = CreateRankList(meta); break;
                    }
                    if (nc2 != null) { if (meta.InnerHeight > 0 && nc2 is System.Windows.Controls.ScrollViewer sv4 && sv4.Content is CartesianChart cc4) ApplyInnerChartHeight(cc4, meta); border.Child = WrapWithDbDates(nc2, meta); }
                    SaveAllChartStates(); configDlg.Close();
                };
            }
            else
            {
                var extBody = new System.Windows.Controls.StackPanel();
                extBody.Children.Add(new System.Windows.Controls.TextBlock { Text = "외부 데이터 소스는 새로고침 버튼을 사용하세요.", Opacity = 0.7 });
                mainStack.Children.Add(SectionCard("안내", extBody));
                applyBtn.Content = "닫기";
                applyBtn.Click += (_, _) => configDlg.Close();
            }

            configDlg.Content = outerBorder;
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
                    case "RankList": newChart = CreateRankList(meta); break;
                }
                if (newChart != null)
                {
                    if (meta.InnerHeight > 0 &&
                        newChart is System.Windows.Controls.ScrollViewer svRef &&
                        svRef.Content is CartesianChart ccRef)
                        ApplyInnerChartHeight(ccRef, meta);
                    border.Child = WrapWithDbDates(newChart, meta);
                }
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
            else if (Uri.TryCreate(meta.DataPath, UriKind.Absolute, out var dataUri) &&
                     (dataUri.Scheme == Uri.UriSchemeHttps || dataUri.Scheme == Uri.UriSchemeHttp))
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
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

        private static readonly HashSet<string> AllowedDistinctColumns = new() { "매장명", "중분류", "메뉴명" };

        private List<string> LoadDbDistinctValues(string column)
        {
            var result = new List<string>();
            if (!AllowedDistinctColumns.Contains(column)) return result;
            try
            {
                string cs = DatabaseService.DataConnectionString;
                if (string.IsNullOrWhiteSpace(cs)) return result;
                using var conn = new SqlConnection(cs);
                conn.Open();
                using var cmd = new SqlCommand($"SELECT DISTINCT [{column}] FROM 매출데이터 WHERE [{column}] IS NOT NULL AND [{column}] <> '' ORDER BY [{column}]", conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) result.Add(reader.GetString(0));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LoadDbDistinctValues] column={column} : {ex.Message}"); }
            return result;
        }

        private static readonly HashSet<string> AllowedGroupByColumns = new() { "매장명", "중분류", "메뉴명" };
        private static readonly HashSet<string> AllowedValueColumns = new() { "총매출액", "총수량", "판매수량", "서비스수량" };

        private void LoadDbData(ChartMeta meta)
        {
            string cs = DatabaseService.DataConnectionString;
            if (string.IsNullOrWhiteSpace(cs)) return;
            var groupBy = AllowedGroupByColumns.Contains(meta.DbGroupBy ?? "") ? meta.DbGroupBy! : "매장명";
            var valueCol = AllowedValueColumns.Contains(meta.DbValueColumn ?? "") ? meta.DbValueColumn! : "총매출액";
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

        private FrameworkElement WrapWithDbDates(FrameworkElement chartControl, ChartMeta meta)
        {
            var outerGrid = new Grid { Tag = "DbChartWrapper" };

            // ── Row 0: 제목 ──────────────────────────────────────────────────────
            outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleBlock = new System.Windows.Controls.TextBlock
            {
                Text = meta.Title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(4, 4, 4, 2),
                Cursor = System.Windows.Input.Cursors.IBeam,
                Visibility = string.IsNullOrWhiteSpace(meta.Title) ? Visibility.Collapsed : Visibility.Visible
            };
            titleBlock.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");

            var titleHint = new System.Windows.Controls.TextBlock
            {
                Text = "+ 제목",
                FontSize = 11,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(4, 4, 4, 2),
                Cursor = System.Windows.Input.Cursors.IBeam,
                Opacity = 0.3,
                Visibility = string.IsNullOrWhiteSpace(meta.Title) ? Visibility.Visible : Visibility.Collapsed
            };
            titleHint.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");

            var titlePanel = new Grid { Background = System.Windows.Media.Brushes.Transparent };
            titlePanel.Children.Add(titleHint);
            titlePanel.Children.Add(titleBlock);
            Grid.SetRow(titlePanel, 0);
            outerGrid.Children.Add(titlePanel);

            bool titleEditActive = false;

            void EnterTitleEdit()
            {
                if (titleEditActive) return;
                titleEditActive = true;

                var tb = new System.Windows.Controls.TextBox
                {
                    Text = meta.Title,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    TextAlignment = TextAlignment.Center,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Margin = new Thickness(4, 4, 4, 2)
                };
                tb.SetResourceReference(System.Windows.Controls.TextBox.ForegroundProperty, "ForegroundBrush");
                tb.SetResourceReference(System.Windows.Controls.TextBox.BorderBrushProperty, "StatusBarBorderBrush");

                titleBlock.Visibility = Visibility.Collapsed;
                titleHint.Visibility  = Visibility.Collapsed;
                titlePanel.Children.Add(tb);

                bool finished = false;
                void FinishEdit()
                {
                    if (finished) return;
                    finished = true;
                    titleEditActive = false;
                    meta.Title = tb.Text.Trim();
                    titleBlock.Text = meta.Title;
                    titleBlock.Visibility = string.IsNullOrWhiteSpace(meta.Title) ? Visibility.Collapsed : Visibility.Visible;
                    titleHint.Visibility  = string.IsNullOrWhiteSpace(meta.Title) ? Visibility.Visible  : Visibility.Collapsed;
                    titlePanel.Children.Remove(tb);
                    SaveAllChartStates();
                }

                tb.KeyDown   += (_, ke) => { if (ke.Key == Key.Return || ke.Key == Key.Escape) FinishEdit(); };
                tb.LostFocus += (_, __)  => FinishEdit();

                Dispatcher.BeginInvoke(new Action(() => { tb.Focus(); tb.SelectAll(); }),
                    System.Windows.Threading.DispatcherPriority.Input);
            }

            titlePanel.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (titleEditActive) return;
                bool onHint  = titleHint.Visibility  == Visibility.Visible;
                bool onBlock = titleBlock.Visibility == Visibility.Visible;
                if (onHint && e.ClickCount >= 1) { e.Handled = true; EnterTitleEdit(); }
                else if (onBlock && e.ClickCount >= 2) { e.Handled = true; EnterTitleEdit(); }
            };

            if (meta.DataSource == "Db")
            {
                // ── Row 1: 날짜 컨트롤 ───────────────────────────────────────────
                outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                // ── Row 2: 차트 ──────────────────────────────────────────────────
                outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                Func<DateTime?> getStart = () => null;
                Func<DateTime?> getEnd   = () => null;
                FrameworkElement startCtrl, endCtrl;
                (startCtrl, getStart) = CreateThemeDatePicker(meta.DbStartDate, () => RefreshInner());
                (endCtrl,   getEnd)   = CreateThemeDatePicker(meta.DbEndDate,   () => RefreshInner());
                startCtrl.Margin = new Thickness(0, 0, 3, 0);

                var lblStart = new System.Windows.Controls.TextBlock { Text = "시작", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0), FontSize = 10 };
                var lblSep   = new System.Windows.Controls.TextBlock { Text = "~",   VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 3, 0), FontSize = 10 };
                lblStart.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");
                lblSep.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");

                var datePanel = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Margin = new Thickness(4, 2, 4, 2),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                };
                datePanel.Children.Add(lblStart);
                datePanel.Children.Add(startCtrl);
                datePanel.Children.Add(lblSep);
                datePanel.Children.Add(endCtrl);

                Grid.SetRow(datePanel, 1);
                Grid.SetRow(chartControl, 2);
                outerGrid.Children.Add(datePanel);
                outerGrid.Children.Add(chartControl);

                void RefreshInner()
                {
                    var s = getStart(); var e = getEnd();
                    if (!s.HasValue || !e.HasValue) return;
                    meta.DbStartDate = s; meta.DbEndDate = e;
                    try { LoadDbData(meta); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LoadDbData] {ex.Message}"); }

                    FrameworkElement newInner = meta.ChartType switch
                    {
                        "Line"     => CreateLineChart(meta),
                        "Bar"      => CreateBarChart(meta),
                        "HBar"     => CreateHBarChart(meta),
                        "Pie"      => CreatePieChart(meta),
                        "Gauge"    => CreateGaugeChart(meta),
                        "RankList" => CreateRankList(meta),
                        _ => null
                    };
                    if (newInner == null) return;
                    if (meta.InnerHeight > 0 &&
                        newInner is System.Windows.Controls.ScrollViewer sv2 &&
                        sv2.Content is CartesianChart cc2)
                        ApplyInnerChartHeight(cc2, meta);

                    var oldChart = outerGrid.Children.OfType<FrameworkElement>().FirstOrDefault(c => Grid.GetRow(c) == 2);
                    if (oldChart != null) outerGrid.Children.Remove(oldChart);
                    Grid.SetRow(newInner, 2);
                    outerGrid.Children.Add(newInner);
                    SaveAllChartStates();
                }
            }
            else
            {
                // ── Row 1: 차트 ──────────────────────────────────────────────────
                outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(chartControl, 1);
                outerGrid.Children.Add(chartControl);
            }

            return outerGrid;
        }

        private (FrameworkElement control, Func<DateTime?> getDate) CreateThemeDatePicker(
            DateTime? initialDate, Action onChanged)
        {
            DateTime? selected = initialDate;

            var btn = new Border
            {
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5, 2, 5, 2),
                Cursor = System.Windows.Input.Cursors.Hand,
                MinWidth = 85
            };
            btn.SetResourceReference(Border.BackgroundProperty, "ContextMenuBackgroundBrush");
            btn.SetResourceReference(Border.BorderBrushProperty, "StatusBarBorderBrush");

            var txt = new System.Windows.Controls.TextBlock
            {
                Text = initialDate?.ToString("yy-MM-dd") ?? "날짜 선택",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                FontSize = 10
            };
            txt.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");
            btn.Child = txt;

            var cal = new System.Windows.Controls.Calendar
            {
                DisplayMode = System.Windows.Controls.CalendarMode.Month,
                SelectedDate = initialDate
            };
            cal.SetResourceReference(System.Windows.Controls.Calendar.BackgroundProperty, "ContextMenuBackgroundBrush");
            cal.SetResourceReference(System.Windows.Controls.Calendar.ForegroundProperty, "ForegroundBrush");
            cal.SetResourceReference(System.Windows.Controls.Calendar.BorderBrushProperty, "StatusBarBorderBrush");
            cal.BorderThickness = new Thickness(1);

            void ApplyCalDayStyle()
            {
                bool isDark = ThemeManager.CurrentTheme != ThemeManager.Theme.White;
                var res = System.Windows.Application.Current.Resources;
                var themeBg = (res["ContextMenuBackgroundBrush"] as System.Windows.Media.Brush)
                              ?? new System.Windows.Media.SolidColorBrush(
                                  isDark ? System.Windows.Media.Color.FromRgb(30, 30, 40)
                                         : System.Windows.Media.Color.FromRgb(245, 245, 252));
                var dayFg = new System.Windows.Media.SolidColorBrush(isDark
                    ? System.Windows.Media.Color.FromRgb(220, 220, 235)
                    : System.Windows.Media.Color.FromRgb(20, 20, 30));
                var dayHov = new System.Windows.Media.SolidColorBrush(isDark
                    ? System.Windows.Media.Color.FromRgb(50, 50, 85)
                    : System.Windows.Media.Color.FromRgb(195, 210, 255));

                // CalendarItem: 헤더(월/년 + 화살표) 배경/글씨 색상
                var calItemStyle = new System.Windows.Style(typeof(System.Windows.Controls.Primitives.CalendarItem));
                calItemStyle.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Primitives.CalendarItem.BackgroundProperty, themeBg));
                calItemStyle.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Primitives.CalendarItem.ForegroundProperty, dayFg));
                cal.CalendarItemStyle = calItemStyle;

                var dayStyle = new System.Windows.Style(typeof(System.Windows.Controls.Primitives.CalendarDayButton));
                dayStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Primitives.CalendarDayButton.ForegroundProperty, dayFg));
                dayStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Primitives.CalendarDayButton.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
                var hoverTrig = new Trigger { Property = System.Windows.UIElement.IsMouseOverProperty, Value = true };
                hoverTrig.Setters.Add(new Setter(System.Windows.Controls.Primitives.CalendarDayButton.BackgroundProperty, dayHov));
                dayStyle.Triggers.Add(hoverTrig);
                cal.CalendarDayButtonStyle = dayStyle;

                var calBtnStyle = new System.Windows.Style(typeof(System.Windows.Controls.Primitives.CalendarButton));
                calBtnStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Primitives.CalendarButton.ForegroundProperty, dayFg));
                calBtnStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Primitives.CalendarButton.BackgroundProperty, themeBg));
                cal.CalendarButtonStyle = calBtnStyle;
            }
            ApplyCalDayStyle();

            EventHandler themeHandler = (sender2, args2) => ApplyCalDayStyle();
            ThemeManager.ThemeChanged += themeHandler;

            var popupBorder = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(2) };
            popupBorder.SetResourceReference(Border.BackgroundProperty, "ContextMenuBackgroundBrush");
            popupBorder.SetResourceReference(Border.BorderBrushProperty, "StatusBarBorderBrush");
            popupBorder.Child = cal;

            var popup = new System.Windows.Controls.Primitives.Popup
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
                StaysOpen = true,
                AllowsTransparency = true,
                Child = popupBorder
            };

            DateTime? prevApplied = initialDate;

            cal.SelectedDatesChanged += (sender2, args2) =>
            {
                if (cal.SelectedDate.HasValue)
                {
                    selected = cal.SelectedDate;
                    txt.Text = cal.SelectedDate.Value.ToString("yy-MM-dd");
                    popup.IsOpen = false;
                    // onChanged()는 popup.Closed에서 날짜가 바뀐 경우에만 호출
                }
            };

            popup.Closed += (_, _) =>
            {
                if (selected.HasValue && selected != prevApplied)
                {
                    prevApplied = selected;
                    onChanged();
                }
            };

            // 바깥 클릭 시 닫기: 창 레벨 PreviewMouseDown 감지
            MouseButtonEventHandler? winMouseDown = null;
            winMouseDown = (sender2, args2) =>
            {
                if (!popup.IsOpen) return;
                // 클릭 대상이 popup 내부인지 확인 (OriginalSource를 타고 올라가서 체크)
                var src = args2.OriginalSource as DependencyObject;
                while (src != null)
                {
                    if (src == popupBorder || src == btn) return; // 팝업 내부 또는 버튼 → 닫지 않음
                    src = VisualTreeHelper.GetParent(src);
                }
                popup.IsOpen = false;
            };

            EventHandler? winStateChanged = null;
            System.ComponentModel.CancelEventHandler? winClosing = null;
            DependencyPropertyChangedEventHandler? winVisibleChanged = null;

            btn.Loaded += (sender2, args2) =>
            {
                if (System.Windows.Window.GetWindow(btn) is System.Windows.Window win)
                {
                    win.PreviewMouseDown += winMouseDown;
                    winStateChanged = (s, e) =>
                    {
                        if ((s as System.Windows.Window)?.WindowState == WindowState.Minimized)
                            popup.IsOpen = false;
                    };
                    winClosing      = (s, e) => popup.IsOpen = false;
                    winVisibleChanged = (s, e) => { if (!(bool)e.NewValue) popup.IsOpen = false; };
                    win.StateChanged     += winStateChanged;
                    win.Closing          += winClosing;
                    win.IsVisibleChanged += winVisibleChanged;
                }
            };
            btn.Unloaded += (sender2, args2) =>
            {
                if (System.Windows.Window.GetWindow(btn) is System.Windows.Window win)
                {
                    win.PreviewMouseDown -= winMouseDown;
                    if (winStateChanged   != null) win.StateChanged     -= winStateChanged;
                    if (winClosing        != null) win.Closing          -= winClosing;
                    if (winVisibleChanged != null) win.IsVisibleChanged -= winVisibleChanged;
                }
                ThemeManager.ThemeChanged -= themeHandler;
            };

            btn.MouseLeftButtonDown += (sender2, args2) =>
            {
                popup.IsOpen = !popup.IsOpen;
                args2.Handled = true;
            };

            return (btn, () => selected);
        }

        private static CartesianChart? GetInnerChartFromChild(UIElement? child)
        {
            if (child is System.Windows.Controls.ScrollViewer sv && sv.Content is CartesianChart c) return c;
            if (child is CartesianChart dc) return dc;
            if (child is Grid g)
            {
                foreach (var fe in g.Children.OfType<FrameworkElement>())
                {
                    if (fe is System.Windows.Controls.ScrollViewer svC && svC.Content is CartesianChart cC) return cC;
                    if (fe is CartesianChart dcC) return dcC;
                }
            }
            return null;
        }

        private void ApplyInnerChartHeight(CartesianChart chart, ChartMeta meta)
        {
            if (meta.InnerHeight <= 0) return;
            chart.Height = meta.InnerHeight;
            int count = Math.Max(1, meta.Labels.Count > 0 ? meta.Labels.Count : meta.StaticData.Count);
            if (meta.ChartType == "HBar")
            {
                double naturalH = Math.Max(200, count * 38 + 60);
                if (chart.YAxes?.FirstOrDefault() is LiveChartsCore.SkiaSharpView.Axis yAxis)
                    yAxis.TextSize = (float)Math.Clamp(18.0 * (meta.InnerHeight / naturalH), 10.0, 32.0);
            }
            else if (meta.ChartType == "Bar")
            {
                double naturalH = Math.Max(300, count * 40 + 80);
                if (chart.XAxes?.FirstOrDefault() is LiveChartsCore.SkiaSharpView.Axis xAxis)
                    xAxis.TextSize = (float)Math.Clamp(22.0 * (meta.InnerHeight / naturalH), 14.0, 36.0);
            }
        }

        private void AttachChartClickDetails(CartesianChart chart, ChartMeta meta)
        {
            var detailText = new System.Windows.Controls.TextBlock
            {
                FontSize = 13,
                TextAlignment = TextAlignment.Center,
                LineHeight = 20
            };
            detailText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ForegroundBrush");

            var detailBorder = new Border
            {
                Child = detailText,
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(14, 9, 14, 9),
                BorderThickness = new Thickness(1)
            };
            detailBorder.SetResourceReference(Border.BackgroundProperty, "ContextMenuBackgroundBrush");
            detailBorder.SetResourceReference(Border.BorderBrushProperty, "StatusBarBorderBrush");

            var popup = new System.Windows.Controls.Primitives.Popup
            {
                Child = detailBorder,
                StaysOpen = false,
                AllowsTransparency = true,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
                PlacementTarget = chart
            };

            chart.DataPointerDown += (_, points) =>
            {
                var pt = points?.FirstOrDefault();
                if (pt == null) return;

                string label;
                double val;
                if (meta.ChartType == "HBar")
                {
                    int idx = (int)Math.Round(pt.Coordinate.PrimaryValue);
                    val = pt.Coordinate.SecondaryValue;
                    var rev = meta.Labels.AsEnumerable().Reverse().ToList();
                    label = idx >= 0 && idx < rev.Count ? rev[idx] : (idx + 1).ToString();
                }
                else
                {
                    int idx = (int)Math.Round(pt.Coordinate.SecondaryValue);
                    val = pt.Coordinate.PrimaryValue;
                    label = idx >= 0 && idx < meta.Labels.Count ? meta.Labels[idx] : (idx + 1).ToString();
                }

                double total = meta.StaticData.Sum();
                string pct = total > 0 ? $"\n{val / total:P1}" : "";
                detailText.Text = $"{label}\n{val:N0}{pct}";
                popup.IsOpen = false;
                popup.IsOpen = true;
            };

            System.Windows.Point clickPos = new();
            chart.DataPointerDown += (_, __) => { clickPos = Mouse.GetPosition(chart); };
            chart.MouseMove += (_, e) =>
            {
                if (!popup.IsOpen) return;
                var pos = e.GetPosition(chart);
                if (Math.Abs(pos.X - clickPos.X) > 15 || Math.Abs(pos.Y - clickPos.Y) > 15)
                    popup.IsOpen = false;
            };
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
                            DbMenuNameFilter = meta.DbMenuNameFilter,
                            InnerHeight = meta.InnerHeight,
                            ChartFont = meta.ChartFont,
                            ChartLabelColor = meta.ChartLabelColor,
                            ChartLabelSize = meta.ChartLabelSize,
                            ShowBars = meta.ShowBars,
                            RankListVisibleColumns = meta.RankListVisibleColumns,
                            RankListLabelFont = meta.RankListLabelFont,
                            RankListLabelSize = meta.RankListLabelSize,
                            RankListLabelColor = meta.RankListLabelColor,
                            RankListValueFont = meta.RankListValueFont,
                            RankListValueSize = meta.RankListValueSize,
                            RankListValueColor = meta.RankListValueColor,
                            RankListColumnWidths = meta.RankListColumnWidths,
                            RankListColumnOrder = meta.RankListColumnOrder,
                            RankListColumnAlignments = meta.RankListColumnAlignments
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
                        DbMenuNameFilter = chartData.ContainsKey("DbMenuNameFilter") ? chartData["DbMenuNameFilter"]?.ToString() : null,
                        InnerHeight = chartData.ContainsKey("InnerHeight") ? Convert.ToDouble(chartData["InnerHeight"]) : 0,
                        ChartFont = chartData.ContainsKey("ChartFont") && chartData["ChartFont"] != null ? chartData["ChartFont"].ToString() : "Malgun Gothic",
                        ChartLabelColor = chartData.ContainsKey("ChartLabelColor") ? chartData["ChartLabelColor"]?.ToString() : null,
                        ChartLabelSize = chartData.ContainsKey("ChartLabelSize") ? Convert.ToDouble(chartData["ChartLabelSize"]) : 0,
                        ShowBars = !chartData.ContainsKey("ShowBars") || Convert.ToBoolean(chartData["ShowBars"]),
                        RankListLabelFont = chartData.ContainsKey("RankListLabelFont") && chartData["RankListLabelFont"] != null ? chartData["RankListLabelFont"].ToString() : "Malgun Gothic",
                        RankListLabelSize = chartData.ContainsKey("RankListLabelSize") ? Convert.ToDouble(chartData["RankListLabelSize"]) : 13,
                        RankListLabelColor = chartData.ContainsKey("RankListLabelColor") ? chartData["RankListLabelColor"]?.ToString() : null,
                        RankListValueFont = chartData.ContainsKey("RankListValueFont") && chartData["RankListValueFont"] != null ? chartData["RankListValueFont"].ToString() : "Malgun Gothic",
                        RankListValueSize = chartData.ContainsKey("RankListValueSize") ? Convert.ToDouble(chartData["RankListValueSize"]) : 13,
                        RankListValueColor = chartData.ContainsKey("RankListValueColor") ? chartData["RankListValueColor"]?.ToString() : null
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

                    // RankList 컬럼 설정 복원
                    if (chartData.ContainsKey("RankListVisibleColumns") && chartData["RankListVisibleColumns"] != null)
                        meta.RankListVisibleColumns = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(chartData["RankListVisibleColumns"].ToString()) ?? meta.RankListVisibleColumns;
                    if (chartData.ContainsKey("RankListColumnWidths") && chartData["RankListColumnWidths"] != null)
                        meta.RankListColumnWidths = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, double>>(chartData["RankListColumnWidths"].ToString()) ?? meta.RankListColumnWidths;
                    if (chartData.ContainsKey("RankListColumnOrder") && chartData["RankListColumnOrder"] != null)
                        meta.RankListColumnOrder = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(chartData["RankListColumnOrder"].ToString()) ?? meta.RankListColumnOrder;
                    if (chartData.ContainsKey("RankListColumnAlignments") && chartData["RankListColumnAlignments"] != null)
                        meta.RankListColumnAlignments = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(chartData["RankListColumnAlignments"].ToString()) ?? meta.RankListColumnAlignments;

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
                        case "RankList":
                            chartControl = CreateRankList(meta);
                            break;
                    }

                    if (chartControl != null)
                    {
                        if (chartControl is System.Windows.Controls.Control ctrl2)
                            ctrl2.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "StatusBarBackgroundBrush");
                        if (meta.InnerHeight > 0 &&
                            chartControl is System.Windows.Controls.ScrollViewer svRestore &&
                            svRestore.Content is CartesianChart ccRestore)
                            ApplyInnerChartHeight(ccRestore, meta);
                        border.Child = WrapWithDbDates(chartControl, meta);
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
                        if (tb is System.Windows.Controls.Primitives.DatePickerTextBox)
                        {
                            var wBg = System.Windows.Media.Brushes.White;
                            var wFg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30));
                            tb.Background = wBg;
                            tb.Foreground = wFg;
                            tb.CaretBrush = wFg;
                            tb.BorderThickness = new System.Windows.Thickness(0);
                        }
                        else
                        {
                            tb.Background = buttonBg;
                            tb.Foreground = fgBrush;
                            tb.BorderBrush = (res["StatusBarBorderBrush"] as System.Windows.Media.Brush)
                                             ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 85, 85));
                            tb.CaretBrush = fgBrush;
                        }
                        break;
                    case System.Windows.Controls.TextBlock t:
                        t.Foreground = fgBrush;
                        break;
                    case System.Windows.Controls.ComboBox cb:
                        // 항상 흰 배경 + 어두운 글씨로 고정 (테마 무관)
                        var cbBg  = System.Windows.Media.Brushes.White;
                        var cbFg  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30));
                        var cbHov = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 235, 255));
                        cb.Background = cbBg;
                        cb.Foreground = cbFg;
                        cb.BorderThickness = new System.Windows.Thickness(1);
                        cb.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 180, 180));
                        cb.Resources[System.Windows.SystemColors.WindowBrushKey]       = cbBg;
                        cb.Resources[System.Windows.SystemColors.WindowTextBrushKey]    = cbFg;
                        cb.Resources[System.Windows.SystemColors.HighlightBrushKey]     = cbHov;
                        cb.Resources[System.Windows.SystemColors.HighlightTextBrushKey] = cbFg;
                        if (cb.ItemContainerStyle == null)
                        {
                            var itemStyle = new System.Windows.Style(typeof(System.Windows.Controls.ComboBoxItem));
                            itemStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.BackgroundProperty, cbBg));
                            itemStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.ForegroundProperty, cbFg));
                            cb.ItemContainerStyle = itemStyle;
                        }
                        break;
                    case System.Windows.Controls.DatePicker dp:
                        // 항상 흰 배경 + 어두운 글씨로 고정 (테마 무관)
                        var dpBg  = System.Windows.Media.Brushes.White;
                        var dpFg  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30));
                        var dpHov = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 235, 255));
                        dp.Background = dpBg;
                        dp.Foreground = dpFg;
                        dp.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 180, 180));
                        dp.Resources[System.Windows.SystemColors.WindowBrushKey]       = dpBg;
                        dp.Resources[System.Windows.SystemColors.WindowTextBrushKey]    = dpFg;
                        dp.Resources[System.Windows.SystemColors.HighlightBrushKey]     = dpHov;
                        dp.Resources[System.Windows.SystemColors.HighlightTextBrushKey] = dpFg;
                        dp.Resources[System.Windows.SystemColors.ControlBrushKey]       = dpBg;
                        dp.Resources[System.Windows.SystemColors.ControlTextBrushKey]   = dpFg;
                        var calStyle = new System.Windows.Style(typeof(System.Windows.Controls.Calendar));
                        calStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Calendar.BackgroundProperty, dpBg));
                        calStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Calendar.ForegroundProperty, dpFg));
                        calStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Calendar.BorderBrushProperty,
                            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 180, 180))));
                        calStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Calendar.BorderThicknessProperty, new System.Windows.Thickness(1)));
                        dp.CalendarStyle = calStyle;
                        void ApplyDpTextBox()
                        {
                            if (dp.Template?.FindName("PART_TextBox", dp) is System.Windows.Controls.Primitives.DatePickerTextBox dpTb)
                            {
                                dpTb.Background = dpBg;
                                dpTb.Foreground = dpFg;
                                dpTb.CaretBrush = dpFg;
                                dpTb.BorderThickness = new System.Windows.Thickness(0);
                                dpTb.Resources[System.Windows.SystemColors.WindowBrushKey]     = dpBg;
                                dpTb.Resources[System.Windows.SystemColors.WindowTextBrushKey] = dpFg;
                            }
                        }
                        if (dp.IsLoaded) ApplyDpTextBox();
                        else dp.Loaded += (_, __) => ApplyDpTextBox();
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
            ApplySubtleScrollBarStyle(border);

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

            // Window1이 열려있으면 SettingWindow 중앙에, 없으면 MainWindow 중앙에 메시지 표시
            Window? ownerWindow = null;
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is SettingWindow w1 && w1.IsVisible)
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
                    ApplySubtleScrollBarStyle(border);

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

        private static void ApplySubtleScrollBarStyle(Border border)
        {
            if (System.Windows.Application.Current.Resources["SlimScrollBarStyle"] is Style slim)
                border.Resources[typeof(ScrollBar)] = slim;
        }
    }
}
