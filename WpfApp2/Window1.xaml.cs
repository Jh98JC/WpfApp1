using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls; // WPF Button 명확히 사용
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SWC = System.Windows.Controls;
using AutoUpdaterDotNET;

namespace WpfApp2
{
    /// <summary>
    /// Window1.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class Window1 : Window
    {
        public Window1()
        {
            InitializeComponent();
            SourceInitialized += Window1_SourceInitialized;

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
                versionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";

            BuildThemeCards();
        }

        // ── 테마 카드 ─────────────────────────────────────────────────────────

        private readonly (ThemeManager.Theme theme, string label,
                           string bgTop, string bgBot,
                           string tabSelTop, string tabSelBot,
                           string tabNormTop, string tabNormBot,
                           string accent, string textSel, string textNorm)[] _themes =
        {
            (ThemeManager.Theme.Purple, "PURPLE",
             "#1A1A22", "#13131B", "#2A2A3E", "#1C1C2E", "#1D1D26", "#141419",
             "#5575EE", "#FFFFFF", "#8888A0"),
            (ThemeManager.Theme.Black, "BLACK",
             "#0D0D0D", "#070707", "#282828", "#1C1C1C", "#111111", "#080808",
             "#606060", "#D0D0D0", "#505060"),
            (ThemeManager.Theme.White, "WHITE",
             "#F0F0F6", "#E4E4F0", "#C8C8E4", "#B8B8D4", "#E6E6F2", "#DCDCE8",
             "#5575EE", "#1A1A2A", "#8080A0"),
        };

        private Border[] _cards = Array.Empty<Border>();

        private void BuildThemeCards()
        {
            _cards = new Border[_themes.Length];
            for (int i = 0; i < _themes.Length; i++)
            {
                var t = _themes[i];
                var card = CreateThemeCard(t.theme, t.label,
                    t.bgTop, t.bgBot,
                    t.tabSelTop, t.tabSelBot,
                    t.tabNormTop, t.tabNormBot,
                    t.accent, t.textSel, t.textNorm);
                _cards[i] = card;
                ThemePanel.Children.Add(card);
            }
            UpdateCardSelection();
        }

        private static System.Windows.Media.Color TC(string h) =>
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(h);
        private static System.Windows.Media.LinearGradientBrush TLGB(string top, string bot) =>
            new System.Windows.Media.LinearGradientBrush(
                new System.Windows.Media.GradientStopCollection
                {
                    new System.Windows.Media.GradientStop(TC(top), 0),
                    new System.Windows.Media.GradientStop(TC(bot), 1)
                },
                new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));
        private static System.Windows.Media.SolidColorBrush TSB(string h) =>
            new System.Windows.Media.SolidColorBrush(TC(h));

        private Border CreateThemeCard(ThemeManager.Theme theme, string label,
            string bgTop, string bgBot,
            string selTop, string selBot,
            string normTop, string normBot,
            string accent, string textSel, string textNorm)
        {
            var card = new Border
            {
                Width = 174, Height = 100,
                Margin = new Thickness(0, 0, 8, 0),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(2),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = theme, ClipToBounds = true,
                Background = TLGB(bgTop, bgBot)
            };

            var grid = new Grid();

            // 미니 탭 스트립
            var tabStrip = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(10, 0, 0, 10),
                Width = 60
            };

            var selTab = new Border
            {
                Height = 20, Margin = new Thickness(0, 0, 0, 4),
                CornerRadius = new CornerRadius(4),
                Background = TLGB(selTop, selBot)
            };
            selTab.Child = new Border
            {
                Width = 3,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(2),
                Background = TSB(accent)
            };

            var normTab = new Border
            {
                Height = 20, CornerRadius = new CornerRadius(4),
                Background = TLGB(normTop, normBot)
            };
            normTab.Child = new TextBlock
            {
                Text = "탭", FontSize = 8, Foreground = TSB(textNorm),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            tabStrip.Children.Add(selTab);
            tabStrip.Children.Add(normTab);

            var contentArea = new Border
            {
                Margin = new Thickness(75, 10, 10, 35),
                CornerRadius = new CornerRadius(4),
                Background = TLGB(bgTop, bgBot),
                Opacity = 0.5
            };

            var bgBotColor = TC(bgBot);
            var labelBar = new Border
            {
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Height = 32,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(200, bgBotColor.R, bgBotColor.G, bgBotColor.B))
            };
            var labelGrid = new Grid();
            labelGrid.Children.Add(new TextBlock
            {
                Text = label, FontWeight = System.Windows.FontWeights.Bold, FontSize = 11,
                Foreground = TSB(accent),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
            var checkDot = new Ellipse
            {
                Width = 8, Height = 8, Fill = TSB(accent),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Visibility = System.Windows.Visibility.Collapsed
            };
            labelGrid.Children.Add(checkDot);
            labelBar.Child = labelGrid;

            grid.Children.Add(tabStrip);
            grid.Children.Add(contentArea);
            grid.Children.Add(labelBar);
            card.Child = grid;

            card.MouseEnter += (s, e) =>
            {
                if ((ThemeManager.Theme)card.Tag != ThemeManager.CurrentTheme)
                    card.BorderBrush = TSB(accent);
            };
            card.MouseLeave += (s, e) =>
            {
                if ((ThemeManager.Theme)card.Tag != ThemeManager.CurrentTheme)
                    card.BorderBrush = System.Windows.Media.Brushes.Transparent;
            };
            card.MouseLeftButtonUp += (s, e) =>
            {
                ThemeManager.Apply((ThemeManager.Theme)card.Tag);
                UpdateCardSelection();
            };

            return card;
        }

        private void UpdateCardSelection()
        {
            for (int i = 0; i < _cards.Length; i++)
            {
                var card = _cards[i];
                var t = _themes[i];
                bool selected = ThemeManager.CurrentTheme == t.theme;

                card.BorderBrush = selected
                    ? TSB(t.accent)
                    : System.Windows.Media.Brushes.Transparent;

                if (card.Child is Grid grid)
                {
                    foreach (var child in grid.Children)
                    {
                        if (child is Border bar && bar.Child is Grid lGrid)
                        {
                            foreach (var lc in lGrid.Children)
                            {
                                if (lc is Ellipse dot)
                                    dot.Visibility = selected
                                        ? System.Windows.Visibility.Visible
                                        : System.Windows.Visibility.Collapsed;
                            }
                        }
                    }
                }
            }
        }

        private void Window1_SourceInitialized(object sender, EventArgs e)
        {
            // Owner가 설정되어 있으면 Owner 중앙에 배치
            // SourceInitialized는 창이 생성되었지만 아직 표시되기 전에 발생
            if (Owner != null)
            {
                // Width와 Height는 XAML에서 명시적으로 설정되어 있으므로 사용 가능
                double centerX = Owner.Left + (Owner.ActualWidth - Width) / 2;
                double centerY = Owner.Top + (Owner.ActualHeight - Height) / 2;

                System.Diagnostics.Debug.WriteLine($"Owner: Left={Owner.Left}, Top={Owner.Top}, Width={Owner.ActualWidth}, Height={Owner.ActualHeight}");
                System.Diagnostics.Debug.WriteLine($"Window1: Width={Width}, Height={Height}");
                System.Diagnostics.Debug.WriteLine($"Calculated: Left={centerX}, Top={centerY}");

                Left = centerX;
                Top = centerY;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 더블클릭 시 최대화/복원
                if (WindowState == WindowState.Normal)
                    WindowState = WindowState.Maximized;
                else
                    WindowState = WindowState.Normal;
            }
            else
            {
                // 드래그 이동
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Normal)
                WindowState = WindowState.Maximized;
            else
                WindowState = WindowState.Normal;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window1_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void addTab_Click(object sender, RoutedEventArgs e)
        {
            var main = Owner as MainWindow;
            if (main == null)
            {
                System.Windows.MessageBox.Show("메인 창을 찾을 수 없습니다.");
                return;
            }

            main.AddNewTabItem();
        }

        private void clear_Click(object sender, RoutedEventArgs e)
        {
            var main = Owner as MainWindow;
            if (main == null) { System.Windows.MessageBox.Show("메인 창을 찾을 수 없습니다."); return; }

            var headers = new List<string>();
            foreach (var item in main.tabControl.Items)
            {
                if (item is System.Windows.Controls.TabItem ti && ti.Header is string hs)
                    headers.Add(hs);
            }
            if (headers.Count == 0) { System.Windows.MessageBox.Show("탭이 없습니다."); return; }

            var dlg = new Window
            {
                Title = "버튼 초기화",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(44,44,44)),
                Foreground = System.Windows.Media.Brushes.White,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };

            var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "창에 버튼을 초기화하시겠습니까?", Margin = new Thickness(0,0,0,8), FontWeight = FontWeights.Bold });
            stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "탭을 선택 후 '예'를 누르면 해당 탭의 버튼이 모두 삭제됩니다.", Margin = new Thickness(0,0,0,12), FontSize = 12 });

            var list = new System.Windows.Controls.ListBox { Height = 120, Width = 200, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60,60,60)), BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(90,90,90)) };
            foreach (var h in headers) list.Items.Add(h);
            list.SelectedIndex = 0;
            stack.Children.Add(list);

            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
            var yesBtn = new System.Windows.Controls.Button { Content = "예", Width = 70, Margin = new Thickness(0,0,8,0), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80,80,80)), BorderBrush = System.Windows.Media.Brushes.Transparent };
            var noBtn = new System.Windows.Controls.Button { Content = "아니오", Width = 70, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80,80,80)), BorderBrush = System.Windows.Media.Brushes.Transparent };
            btnPanel.Children.Add(yesBtn); btnPanel.Children.Add(noBtn);
            stack.Children.Add(btnPanel);

            yesBtn.Click += (s, ev) =>
            {
                if (list.SelectedItem is string selHeader)
                {
                    System.Windows.Controls.TabItem? target = null;
                    foreach (var item in main.tabControl.Items)
                    {
                        if (item is System.Windows.Controls.TabItem ti && ti.Header is string hs && hs == selHeader)
                        { target = ti; break; }
                    }
                    if (target != null)
                    {
                        if (target.Content is System.Windows.Controls.Border b && b.Child is System.Windows.Controls.Canvas cv)
                        {
                            var toRemove = new List<UIElement>();
                            foreach (UIElement child in cv.Children)
                            {
                                if (child is System.Windows.Controls.Button) toRemove.Add(child);
                                else if (child is System.Windows.Controls.TextBlock tb && tb.Tag as string == "DynamicLabel") toRemove.Add(child);
                            }
                            foreach (var rm in toRemove) cv.Children.Remove(rm);
                        }
                    }
                    dlg.Close();
                }
            };
            noBtn.Click += (s, ev) => dlg.Close();

            dlg.Content = stack;
            dlg.ShowDialog();
        }

        private string MapWeightToName(System.Windows.FontWeight w)
        {
            if (w == System.Windows.FontWeights.Thin) return "Thin";
            if (w == System.Windows.FontWeights.ExtraLight || w==System.Windows.FontWeights.Light) return "Light";
            if (w == System.Windows.FontWeights.Regular || w==System.Windows.FontWeights.Normal) return "Regular";
            if (w == System.Windows.FontWeights.Medium) return "Medium";
            if (w == System.Windows.FontWeights.SemiBold) return "SemiBold";
            if (w == System.Windows.FontWeights.Bold) return "Bold";
            if (w == System.Windows.FontWeights.ExtraBold) return "ExtraBold";
            if (w == System.Windows.FontWeights.Black || w==System.Windows.FontWeights.ExtraBlack) return "Black";
            return "Regular";
        }
        private System.Windows.FontWeight MapNameToWeight(string name)
        {
            return name switch
            {
                "Thin" => System.Windows.FontWeights.Thin,
                "Light" => System.Windows.FontWeights.Light,
                "Regular" => System.Windows.FontWeights.Regular,
                "Medium" => System.Windows.FontWeights.Medium,
                "SemiBold" => System.Windows.FontWeights.SemiBold,
                "Bold" => System.Windows.FontWeights.Bold,
                "ExtraBold" => System.Windows.FontWeights.ExtraBold,
                "Black" => System.Windows.FontWeights.Black,
                _ => System.Windows.FontWeights.Regular
            };
        }
        private System.Windows.Media.Color ParseColor(string? text, System.Windows.Media.Color fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return fallback;
                text = text.Trim();
                if (text.StartsWith("#"))
                {
                    // Allow #RGB, #RRGGBB, #AARRGGBB
                    if (text.Length == 4)
                    {
                        byte r = Convert.ToByte(new string(text[1],2),16);
                        byte g = Convert.ToByte(new string(text[2],2),16);
                        byte b = Convert.ToByte(new string(text[3],2),16);
                        return System.Windows.Media.Color.FromRgb(r,g,b);
                    }
                    if (text.Length == 7)
                    {
                        byte r = Convert.ToByte(text.Substring(1,2),16);
                        byte g = Convert.ToByte(text.Substring(3,2),16);
                        byte b = Convert.ToByte(text.Substring(5,2),16);
                        return System.Windows.Media.Color.FromRgb(r,g,b);
                    }
                    if (text.Length == 9)
                    {
                        byte a = Convert.ToByte(text.Substring(1,2),16);
                        byte r = Convert.ToByte(text.Substring(3,2),16);
                        byte g = Convert.ToByte(text.Substring(5,2),16);
                        byte b = Convert.ToByte(text.Substring(7,2),16);
                        return System.Windows.Media.Color.FromArgb(a,r,g,b);
                    }
                }
                // Named colors
                var prop = typeof(System.Windows.Media.Colors).GetProperties().FirstOrDefault(p=>p.Name.Equals(text, StringComparison.OrdinalIgnoreCase));
                if (prop != null) return (System.Windows.Media.Color)prop.GetValue(null)!;
            }
            catch { }
            return fallback;
        }
        private string MapKoreanAliasToEnglish(string name)
        {
            return name switch
            {
                "돋움" => "Dotum",
                "돋움체" => "Dotum",
                "굴림" => "Gulim",
                "굴림체" => "Gulim",
                "바탕" => "Batang",
                "바탕체" => "Batang",
                "맑은 고딕" => "Malgun Gothic",
                "궁서" => "Gungsuh",
                _ => name
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (Owner is MainWindow main)
            {
                try
                {
                    if (main.Visibility != Visibility.Visible)
                        main.Visibility = Visibility.Visible;
                    // Show ensures window handle exists if hidden
                    main.Show();
                    main.Activate();
                    main.Focus();
                    Keyboard.Focus(main);
                }
                catch { }
            }
        }
    }
}
