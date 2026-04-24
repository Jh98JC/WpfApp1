using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WpfColor     = System.Windows.Media.Color;
using WpfPoint     = System.Windows.Point;
using WpfMouseArgs = System.Windows.Input.MouseEventArgs;

namespace WpfApp2
{
    public partial class Window3 : Window
    {
        private static readonly string AppDataFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WpfApp2");
        private static readonly string PositionFile = System.IO.Path.Combine(AppDataFolder, "window3_position.json");

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int idx);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int idx, int val);
        const int GWL_EXSTYLE      = -20;
        const int WS_EX_NOACTIVATE = 0x08000000;

        private bool     _isDragging;
        private WpfPoint _dragOffset;

        // ── 브러시 ─────────────────────────────────────────────────

        private static WpfColor C(string hex) =>
            (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex);

        private static T Freeze<T>(T b) where T : Freezable { b.Freeze(); return b; }

        private static LinearGradientBrush LGB(string top, string bot) => Freeze(
            new LinearGradientBrush(
                new GradientStopCollection { new GradientStop(C(top), 0), new GradientStop(C(bot), 1) },
                new WpfPoint(0, 0), new WpfPoint(0, 1)));

        private static LinearGradientBrush LGBDiag(string top, string bot) => Freeze(
            new LinearGradientBrush(
                new GradientStopCollection { new GradientStop(C(top), 0), new GradientStop(C(bot), 1) },
                new WpfPoint(0, 0), new WpfPoint(1, 1)));

        private static readonly LinearGradientBrush _bgN = LGB("#2E2E2E", "#141414");
        private static readonly LinearGradientBrush _bgH = LGB("#3C3C3C", "#202020");

        private static readonly SolidColorBrush _borderN = Freeze(new SolidColorBrush(C("#484848")));
        private static readonly SolidColorBrush _borderH = Freeze(new SolidColorBrush(C("#787878")));

        private static readonly LinearGradientBrush _gN = LGBDiag("#E8E8E8", "#A0A0A0");
        private static readonly LinearGradientBrush _gH = LGBDiag("#FFFFFF", "#D8D8D8");

        // ── 생성자 / 초기화 ────────────────────────────────────────

        public Window3()
        {
            if (!Directory.Exists(AppDataFolder)) Directory.CreateDirectory(AppDataFolder);
            InitializeComponent();
            RestorePosition();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        }

        // ── 호버 ───────────────────────────────────────────────────

        private void Pill_MouseEnter(object sender, WpfMouseArgs e)
        {
            Pill.Background   = _bgH;
            Pill.BorderBrush  = _borderH;
            GPath.Stroke      = _gH;
        }

        private void Pill_MouseLeave(object sender, WpfMouseArgs e)
        {
            Pill.Background   = _bgN;
            Pill.BorderBrush  = _borderN;
            GPath.Stroke      = _gN;
        }

        // ── 수동 드래그 ────────────────────────────────────────────

        private void MainGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragOffset = e.GetPosition(this);
            Pill.CaptureMouse();
            e.Handled = true;
        }

        private void MainGrid_MouseMove(object sender, WpfMouseArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed) return;
            var screen = PointToScreen(e.GetPosition(this));
            Left = screen.X - _dragOffset.X;
            Top  = screen.Y - _dragOffset.Y;
        }

        private void MainGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            Pill.ReleaseMouseCapture();
            SavePosition();
        }

        // ── 우클릭 위치 저장 ───────────────────────────────────────

        private void Grid_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            SavePosition();
            e.Handled = true;
        }

        private void SavePosition()
        {
            try { File.WriteAllText(PositionFile, JsonSerializer.Serialize(new Window3Position { Left = Left, Top = Top })); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Window3 save: " + ex); }
        }

        private void RestorePosition()
        {
            try
            {
                if (!File.Exists(PositionFile)) return;
                var p = JsonSerializer.Deserialize<Window3Position>(File.ReadAllText(PositionFile));
                if (p != null) { Left = p.Left; Top = p.Top; }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Window3 restore: " + ex); }
        }
    }

    public class Window3Position { public double Left { get; set; } public double Top { get; set; } }
}
