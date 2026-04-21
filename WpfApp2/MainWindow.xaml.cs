using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices; // 핫키 등록용 & Win32 RECT
using System.Text.Json;
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


namespace WpfApp2
{
    public partial class MainWindow : Window
    {
        private const string SettingsFile = "mainwindow_settings.json";
        private const string ButtonStateFile = "button_states.json";
        private const string TabStateFile = "tab_states.json";

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
        private static System.Drawing.Rectangle GetWindowScreenRect(Window w)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return System.Drawing.Rectangle.Empty;
            if (!GetWindowRect(hwnd, out var r)) return System.Drawing.Rectangle.Empty;
            return new System.Drawing.Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }

        public MainWindow()
        {
            InitializeComponent();
            WindowStartupLocation = WindowStartupLocation.Manual;
            RestoreWindowPosition();
            RestoreAllTabs(); // 탭 복원을 먼저 수행
            RestoreAllButtonStates(); // 그 다음 버튼 복원
            Loaded += MainWindow_Loaded;

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
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Window1 w1 && w1.IsVisible)
                {
                    var r = GetWindowScreenRect(w1);
                    if (!r.IsEmpty && r.Contains(mousePos)) return;
                }
            }
            ShowWindow3AtLeftBottom();
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

            // 이미 열려있는 Window1이 있으면 포커스
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Window1 w1 && w1.IsVisible)
                {
                    w1.Activate();
                    return;
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

            // Window3 띄우기 (이미 열려있지 않으면)
            bool window3ExistsVisible = false;
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Window3 w3 && w3.IsVisible)
                {
                    window3ExistsVisible = true;
                    break;
                }
            }
            if (!window3ExistsVisible)
            {
                ShowWindow3AtLeftBottom();
            }
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
                if (!File.Exists("window3_position.json"))
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
                if (!File.Exists("window3_position.json"))
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

            Left = SystemParameters.WorkArea.Left;
            Top = SystemParameters.WorkArea.Bottom - Height;

            var anim = new DoubleAnimation
            {
                From = Top,
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(Window.TopProperty, anim);
            Left = targetLeft;
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
            // 드래그 이동만 지원 (최대화 기능 제거)
            if (e.ChangedButton == MouseButton.Left)
            {
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
                    Content = state.Content ?? "Button",
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
                    if (state.BackgroundTransparent)
                    {
                        btn.Background = System.Windows.Media.Brushes.Transparent;
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
                    var sizeBtn = new System.Windows.Controls.Button { Content = "버튼크기 조절", Margin = new Thickness(0, 0, 0, 8) };
                    var imgBtn = new System.Windows.Controls.Button { Content = "버튼이미지 설정", Margin = new Thickness(0, 0, 0, 8) };
                    var pathBtn = new System.Windows.Controls.Button { Content = "경로 설정", Margin = new Thickness(0, 0, 0, 8) };
                    var textBtn = new System.Windows.Controls.Button { Content = "버튼텍스트 수정", Margin = new Thickness(0, 0, 0, 8) };
                    var closeBtn = new System.Windows.Controls.Button { Content = "닫기" };
                    sizeBtn.Click += (sss, eee) => { dlg.Close(); ShowSizeAdjustDialog(btn, canvas, meta); };
                    imgBtn.Click += (sss, eee) =>
                    {
                        dlg.Close();
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
                                var imgCtrl = GetButtonImageControl(btn) ?? new System.Windows.Controls.Image();
                                imgCtrl.Source = src;
                                imgCtrl.Stretch = System.Windows.Media.Stretch.Uniform;
                                imgCtrl.Width = btn.Width * 0.8;
                                imgCtrl.Height = btn.Height * 0.8;
                                btn.Content = imgCtrl;
                                SaveAllButtonStates();
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine("Set image error: " + ex);
                                System.Windows.MessageBox.Show("이미지를 설정할 수 없습니다.\n" + ex.Message);
                            }
                        }
                    };
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
                    stack.Children.Add(sizeBtn); stack.Children.Add(imgBtn); stack.Children.Add(pathBtn); stack.Children.Add(textBtn); stack.Children.Add(closeBtn);
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
                                FontColor = (btn.Foreground as System.Windows.Media.SolidColorBrush)?.Color.ToString(),
                                BackgroundTransparent = (btn.Background as System.Windows.Media.SolidColorBrush)?.Color.A == 0
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
            w.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 44, 44));
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

        private void CreateButtonInBorder_Click(object sender, RoutedEventArgs e)
        {
            var canvas = CurrentButtonCanvas;
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
                Content = "Button",
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
                var sizeBtn = new System.Windows.Controls.Button { Content = "버튼크기 조절", Margin = new Thickness(0, 0, 0, 8) };
                var imgBtn = new System.Windows.Controls.Button { Content = "버튼이미지 설정", Margin = new Thickness(0, 0, 0, 8) };
                var pathBtn = new System.Windows.Controls.Button { Content = "경로 설정", Margin = new Thickness(0, 0, 0, 8) };
                var textBtn = new System.Windows.Controls.Button { Content = "버튼텍스트 수정", Margin = new Thickness(0, 0, 0, 8) };
                var closeBtn = new System.Windows.Controls.Button { Content = "닫기" };
                sizeBtn.Click += (sss, eee) => { dlg.Close(); ShowSizeAdjustDialog(btn, canvas, meta); };
                imgBtn.Click += (sss, eee) =>
                {
                    dlg.Close();
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
                            var imgCtrl = GetButtonImageControl(btn) ?? new System.Windows.Controls.Image();
                            imgCtrl.Source = src;
                            imgCtrl.Stretch = System.Windows.Media.Stretch.Uniform;
                            imgCtrl.Width = btn.Width * 0.8;
                            imgCtrl.Height = btn.Height * 0.8;
                            btn.Content = imgCtrl;
                            SaveAllButtonStates();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("Set image error: " + ex);
                            System.Windows.MessageBox.Show("이미지를 설정할 수 없습니다.\n" + ex.Message);
                        }
                    }
                };
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
                stack.Children.Add(sizeBtn); stack.Children.Add(imgBtn); stack.Children.Add(pathBtn); stack.Children.Add(textBtn); stack.Children.Add(closeBtn);
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

        private Canvas? GetCanvasByIndex(int index)
        {
            return FindName($"ButtonCanvas{index + 1}") as Canvas;
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
            var windowBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 44, 44));
            var panelBg = windowBg;
            var buttonBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 58, 58));
            var hoverBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(69, 69, 69));
            var fgBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
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
                        tb.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 85, 85));
                        tb.CaretBrush = fgBrush;
                        break;
                    case System.Windows.Controls.TextBlock t:
                        t.Foreground = fgBrush;
                        break;
                    case System.Windows.Controls.ComboBox cb:
                        cb.Background = buttonBg;
                        cb.Foreground = fgBrush;
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
                SizeToContent = SizeToContent.WidthAndHeight
            };
            if (this.IsVisible) sizeDlg.Owner = this;
            sizeDlg.Topmost = true;
            MakeBorderless(sizeDlg);
            var rootGrid = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(16) };
            rootGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            rootGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(40) });
            rootGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            var leftPanel = new System.Windows.Controls.StackPanel();
            leftPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "버튼 크기", FontWeight = System.Windows.FontWeights.Bold, Margin = new System.Windows.Thickness(0, 0, 0, 8) });
            var wBox2 = new System.Windows.Controls.TextBox { Text = targetBtn.Width.ToString(), Width = 80, Margin = new System.Windows.Thickness(0, 0, 0, 8) };
            var hBox2 = new System.Windows.Controls.TextBox { Text = targetBtn.Height.ToString(), Width = 80, Margin = new System.Windows.Thickness(0, 0, 0, 8) };
            leftPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "너비:" });
            leftPanel.Children.Add(wBox2);
            leftPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = "높이:" });
            leftPanel.Children.Add(hBox2);
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
            string current2 = "중앙";
            if (imgRef != null)
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

            // Background remove toggle
            bool initBgTransparent = (targetBtn.Background as System.Windows.Media.SolidColorBrush)?.Color.A == 0;
            var bgToggle = new System.Windows.Controls.CheckBox { Content = "바탕색 제거", IsChecked = initBgTransparent, Margin = new System.Windows.Thickness(0, 8, 0, 0) };
            bgToggle.Checked += (s, e2) => { targetBtn.Background = System.Windows.Media.Brushes.Transparent; SaveAllButtonStates(); };
            bgToggle.Unchecked += (s, e2) => { targetBtn.ClearValue(System.Windows.Controls.Control.BackgroundProperty); SaveAllButtonStates(); };
            rightPanel.Children.Add(bgToggle);

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
                    double bw, bh, iw, ih;
                    double? nbw = double.TryParse(wBox2.Text, out bw) ? bw : (double?)null;
                    double? nbh = double.TryParse(hBox2.Text, out bh) ? bh : (double?)null;
                    iw = double.TryParse(iwBox2.Text, out var iwt) ? iwt : (GetButtonImageControl(targetBtn)?.Width ?? targetBtn.Width * 0.8);
                    ih = double.TryParse(ihBox2.Text, out var iht) ? iht : (GetButtonImageControl(targetBtn)?.Height ?? targetBtn.Height * 0.8);
                    ApplyImageSizeAndPosition(targetBtn, canvas, meta, nbw, nbh, iw, ih, current2);
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
        public string? FontColor { get; set; }
        public bool BackgroundTransparent { get; set; }
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
                Height = 40,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                BorderBrush = (System.Windows.Media.Brush)FindResource("WindowBackgroundBrush"),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 35))
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
                Background = (System.Windows.Media.Brush)FindResource("StatusBarBackgroundBrush"),
                CornerRadius = new CornerRadius(16),
                Margin = new Thickness(0, 0, 0, 3)
            };

            // Border Resources 추가
            var buttonStyle = new Style(typeof(System.Windows.Controls.Button));
            var pressTrigger = new Trigger { Property = System.Windows.Controls.Button.IsPressedProperty, Value = true };
            pressTrigger.Setters.Add(new Setter(System.Windows.Controls.Button.BackgroundProperty, FindResource("StatusBarBorderBrush")));
            pressTrigger.Setters.Add(new Setter(System.Windows.Controls.Button.ForegroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White)));
            buttonStyle.Triggers.Add(pressTrigger);
            border.Resources.Add(typeof(System.Windows.Controls.Button), buttonStyle);

            // ContextMenu 추가
            var contextMenu = new ContextMenu();
            var menuItem = new MenuItem { Header = "버튼생성" };
            menuItem.Click += CreateButtonInBorder_Click;
            contextMenu.Items.Add(menuItem);
            border.ContextMenu = contextMenu;
            border.ContextMenuOpening += DynamicButtonBorder_ContextMenuOpening;

            // Canvas 추가
            var canvas = new Canvas { Name = $"ButtonCanvas{_nextTabNumber}" };
            RegisterName(canvas.Name, canvas);
            border.Child = canvas;

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
                        Height = 40,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                        BorderBrush = (System.Windows.Media.Brush)FindResource("WindowBackgroundBrush"),
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 35))
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
                        Background = (System.Windows.Media.Brush)FindResource("StatusBarBackgroundBrush"),
                        CornerRadius = new CornerRadius(16),
                        Margin = new Thickness(0, 0, 0, 3)
                    };

                    // Border Resources 추가
                    var buttonStyle = new Style(typeof(System.Windows.Controls.Button));
                    var pressTrigger = new Trigger { Property = System.Windows.Controls.Button.IsPressedProperty, Value = true };
                    pressTrigger.Setters.Add(new Setter(System.Windows.Controls.Button.BackgroundProperty, FindResource("StatusBarBorderBrush")));
                    pressTrigger.Setters.Add(new Setter(System.Windows.Controls.Button.ForegroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White)));
                    buttonStyle.Triggers.Add(pressTrigger);
                    border.Resources.Add(typeof(System.Windows.Controls.Button), buttonStyle);

                    // ContextMenu 추가
                    var contextMenu = new ContextMenu();
                    var menuItem = new MenuItem { Header = "버튼생성" };
                    menuItem.Click += CreateButtonInBorder_Click;
                    contextMenu.Items.Add(menuItem);
                    border.ContextMenu = contextMenu;
                    border.ContextMenuOpening += DynamicButtonBorder_ContextMenuOpening;

                    // Canvas 추가 - state.TabIndex에 맞는 Canvas 이름 사용
                    var canvas = new Canvas { Name = $"ButtonCanvas{state.TabIndex + 1}" };
                    RegisterName(canvas.Name, canvas);
                    border.Child = canvas;

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