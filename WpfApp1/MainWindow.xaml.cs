using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Forms;

namespace WpfApp1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DispatcherTimer _mouseLeaveTimer;
        public static RoutedUICommand ShowOnLeftMonitorCommand { get; } = new RoutedUICommand("ShowOnLeftMonitor", "ShowOnLeftMonitorCommand", typeof(MainWindow));

        public MainWindow()
        {
            InitializeComponent();

            CommandBindings.Add(new CommandBinding(ShowOnLeftMonitorCommand, ShowOnLeftMonitorBottomLeft));

            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            this.MouseLeave += MainWindow_MouseLeave;
            this.MouseEnter += MainWindow_MouseEnter;

            _mouseLeaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _mouseLeaveTimer.Tick += (s, e) =>
            {
                _mouseLeaveTimer.Stop();
                this.WindowState = WindowState.Minimized;
            };
        }

        private void ShowOnLeftMonitorBottomLeft(object sender, ExecutedRoutedEventArgs e)
        {
            var screens = Screen.AllScreens;
            Screen leftScreen;
            if (screens.Length < 2)
            {
                leftScreen = screens[0];
            }
            else
            {
                leftScreen = screens.OrderBy(s => s.Bounds.Left).First();
            }
            MoveWindowToScreen(leftScreen);
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void MoveWindowToScreen(Screen screen)
        {
            var workingArea = screen.WorkingArea;
            this.Left = workingArea.Left;
            this.Top = workingArea.Bottom - this.Height;
        }

        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.WindowState = WindowState.Minimized;
            }
        }

        private void MainWindow_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _mouseLeaveTimer.Start();
        }

        private void MainWindow_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _mouseLeaveTimer.Stop();
        }
    }
}