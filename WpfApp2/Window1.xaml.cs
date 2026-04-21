using System;
using System.Collections.Generic;
using System.Linq;
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
        public Window1(Window owner)
        {
            InitializeComponent();
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.Manual;

            // 메인윈도우 중앙에 위치
            if (owner != null)
            {
                Left = owner.Left + (owner.Width - Width) / 2;
                Top = owner.Top + (owner.Height - Height) / 2;
            }
        }

        // 기존 생성자도 필요하다면 추가
        public Window1() : this(System.Windows.Application.Current.MainWindow) { }

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

        private void textsharp_Click(object sender, RoutedEventArgs e)
        {
            var main = Owner as MainWindow;
            if (main == null) { System.Windows.MessageBox.Show("메인 창을 찾을 수 없습니다."); return; }

            // Collect current font defaults from dynamic style
            var dynStyle = main.FindResource("DynamicButtonStyle") as Style;
            double currentSize = 12;
            System.Windows.Media.FontFamily currentFamily = new System.Windows.Media.FontFamily("Segoe UI");
            System.Windows.FontWeight currentWeight = System.Windows.FontWeights.Regular;
            System.Windows.FontStyle currentStyle = System.Windows.FontStyles.Normal;
            System.Windows.Media.Brush currentColor = main.Foreground;

            // Try get from any existing dynamic button
            var sampleBtn = FindAnyDynamicButton(main);
            if (sampleBtn is not null)
            {
                currentSize = sampleBtn.FontSize;
                currentFamily = sampleBtn.FontFamily;
                currentWeight = sampleBtn.FontWeight;
                currentStyle = sampleBtn.FontStyle;
                currentColor = sampleBtn.Foreground;
            }

            var dlg = new Window
            {
                Title = "글꼴 설정",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStyle = WindowStyle.None,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(44,44,44)),
                Foreground = System.Windows.Media.Brushes.White,
                ShowInTaskbar = false
            };

            var root = new System.Windows.Controls.StackPanel { Margin = new Thickness(16), Width = 340 };
            root.Children.Add(new System.Windows.Controls.TextBlock { Text = "동적 버튼 글꼴 설정", FontWeight = System.Windows.FontWeights.Bold, Margin = new Thickness(0,0,0,12) });

            // Font family
            root.Children.Add(new System.Windows.Controls.TextBlock { Text = "폰트", Margin = new Thickness(0,0,0,4) });
            var familyBox = new System.Windows.Controls.ComboBox { IsEditable = true, Margin = new Thickness(0,0,0,8) };
            // Local alias mapper to avoid external dependency
            string AliasMap(string name) => name switch
            {
                "돋움" => "Dotum", "돋움체" => "Dotum",
                "굴림" => "Gulim", "굴림체" => "Gulim",
                "바탕" => "Batang", "바탕체" => "Batang",
                "맑은 고딕" => "Malgun Gothic",
                "궁서" => "Gungsuh",
                _ => name
            };
            var systemFamilies = System.Windows.Media.Fonts.SystemFontFamilies.OrderBy(f=>f.Source).Select(f=>f.Source).ToList();
            foreach (var famName in systemFamilies) familyBox.Items.Add(famName);
            string[] aliasCandidates = { "Dotum", "돋움", "Gulim", "굴림", "Batang", "바탕", "Malgun Gothic", "맑은 고딕", "Gungsuh", "궁서" };
            foreach (var alias in aliasCandidates)
            {
                if (!familyBox.Items.Contains(alias) && (systemFamilies.Contains(alias) || systemFamilies.Contains(AliasMap(alias))))
                    familyBox.Items.Add(alias);
            }
            // Opaque dropdown styling
            var itemStyle = new System.Windows.Style(typeof(System.Windows.Controls.ComboBoxItem));
            itemStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(58,58,58))));
            itemStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.ForegroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White)));
            itemStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.PaddingProperty, new System.Windows.Thickness(4,2,4,2)));
            var hoverTrigger = new System.Windows.Trigger { Property = System.Windows.Controls.ComboBoxItem.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(72,72,72))));
            itemStyle.Triggers.Add(hoverTrigger);
            var selTrigger = new System.Windows.Trigger { Property = System.Windows.Controls.ComboBoxItem.IsSelectedProperty, Value = true };
            selTrigger.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(90,90,90))));
            selTrigger.Setters.Add(new System.Windows.Setter(System.Windows.Controls.ComboBoxItem.FontWeightProperty, System.Windows.FontWeights.Bold));
            itemStyle.Triggers.Add(selTrigger);
            familyBox.ItemContainerStyle = itemStyle;
            familyBox.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50,50,50));
            familyBox.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(90,90,90));
            familyBox.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
            familyBox.DropDownOpened += (s,ev)=>
            {
                var popup = familyBox.Template.FindName("PART_Popup", familyBox) as System.Windows.Controls.Primitives.Popup;
                if (popup != null)
                {
                    popup.AllowsTransparency = false;
                    if (popup.Child is System.Windows.Controls.Border b)
                        b.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50,50,50));
                    else if (popup.Child is System.Windows.Controls.Panel p)
                        p.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50,50,50));
                    else if (popup.Child is System.Windows.Controls.Control c)
                        c.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50,50,50));
                }
            };
            familyBox.Text = currentFamily.Source;
            root.Children.Add(familyBox);

            // Font size
            root.Children.Add(new System.Windows.Controls.TextBlock { Text = "크기", Margin = new Thickness(0,0,0,4) });
            var sizeBox = new System.Windows.Controls.TextBox { Text = currentSize.ToString(), Margin = new Thickness(0,0,0,8) };
            root.Children.Add(sizeBox);

            // Weight & style (simplified: only Bold toggle + Italic toggle)
            var stylePanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0,0,0,8) };
            var boldCheck = new System.Windows.Controls.CheckBox { Content = "굵게", IsChecked = currentWeight >= System.Windows.FontWeights.Bold, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0,0,12,0) };
            var italicCheck = new System.Windows.Controls.CheckBox { Content = "기울임", IsChecked = currentStyle == System.Windows.FontStyles.Italic, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            stylePanel.Children.Add(boldCheck);
            stylePanel.Children.Add(italicCheck);
            root.Children.Add(new System.Windows.Controls.TextBlock { Text = "굵기 / 기울기", Margin = new Thickness(0,0,0,4) });
            root.Children.Add(stylePanel);

            // Color
            root.Children.Add(new System.Windows.Controls.TextBlock { Text = "색상 (HEX)", Margin = new Thickness(0,0,0,4) });
            var colorBox = new System.Windows.Controls.TextBox { Text = (currentColor as System.Windows.Media.SolidColorBrush)?.Color.ToString() ?? "#FFFFFFFF", Margin = new Thickness(0,0,0,8) };
            root.Children.Add(colorBox);

            // Preview
            var preview = new System.Windows.Controls.Button { Content = "미리보기", Height = 40, Margin = new Thickness(0,0,0,12) };
            root.Children.Add(preview);

            void UpdatePreview()
            {
                preview.FontFamily = new System.Windows.Media.FontFamily(string.IsNullOrWhiteSpace(familyBox.Text) ? currentFamily.Source : familyBox.Text);
                if (double.TryParse(sizeBox.Text, out double fs) && fs > 0) preview.FontSize = fs; else preview.FontSize = currentSize;
                preview.FontWeight = boldCheck.IsChecked == true ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Regular;
                preview.FontStyle = italicCheck.IsChecked == true ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;
                var c = ParseColor(colorBox.Text?.Trim(), (currentColor as System.Windows.Media.SolidColorBrush)?.Color ?? System.Windows.Media.Colors.White);
                preview.Foreground = new System.Windows.Media.SolidColorBrush(c);
            }
            UpdatePreview();
            familyBox.SelectionChanged += (s,ev)=>UpdatePreview();
            familyBox.LostFocus += (s,ev)=>UpdatePreview();
            sizeBox.TextChanged += (s,ev)=>UpdatePreview();
            boldCheck.Checked += (s,ev)=>UpdatePreview();
            boldCheck.Unchecked += (s,ev)=>UpdatePreview();
            italicCheck.Checked += (s,ev)=>UpdatePreview();
            italicCheck.Unchecked += (s,ev)=>UpdatePreview();
            colorBox.TextChanged += (s,ev)=>UpdatePreview();

            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var applyBtn = new System.Windows.Controls.Button { Content = "적용", Width = 80, Margin = new Thickness(0,0,8,0) };
            var cancelBtn = new System.Windows.Controls.Button { Content = "취소", Width = 80 };
            btnPanel.Children.Add(applyBtn); btnPanel.Children.Add(cancelBtn);
            root.Children.Add(btnPanel);

            applyBtn.Click += (s,ev)=>
            {
                try
                {
                    var fam = new System.Windows.Media.FontFamily(string.IsNullOrWhiteSpace(familyBox.Text) ? currentFamily.Source : familyBox.Text);
                    double size = double.TryParse(sizeBox.Text, out double fs) && fs>0 ? fs : currentSize;
                    var weight = boldCheck.IsChecked == true ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Regular;
                    var style = italicCheck.IsChecked == true ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;
                    var col = ParseColor(colorBox.Text?.Trim(), (currentColor as System.Windows.Media.SolidColorBrush)?.Color ?? System.Windows.Media.Colors.White);
                    var brush = new System.Windows.Media.SolidColorBrush(col);

                    // Apply to all dynamic buttons across canvases
                    for (int i=0;i<3;i++)
                    {
                        var cv = main.FindName($"ButtonCanvas{i+1}") as System.Windows.Controls.Canvas;
                        if (cv == null) continue;
                        foreach (UIElement child in cv.Children)
                        {
                            if (child is System.Windows.Controls.Button b)
                            {
                                b.FontFamily = fam;
                                b.FontSize = size;
                                b.FontWeight = weight;
                                b.FontStyle = style;
                                b.Foreground = brush;
                            }
                        }
                    }
                    dlg.Close();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("적용 중 오류: " + ex.Message);
                }
            };
            cancelBtn.Click += (s,ev)=>dlg.Close();

            dlg.Content = root;
            dlg.ShowDialog();
        }

        private System.Windows.Controls.Button? FindAnyDynamicButton(MainWindow main)
        {
            for (int i=0;i<3;i++)
            {
                var cv = main.FindName($"ButtonCanvas{i+1}") as System.Windows.Controls.Canvas;
                if (cv == null) continue;
                foreach (UIElement child in cv.Children)
                {
                    if (child is System.Windows.Controls.Button b) return b;
                }
            }
            return null;
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

        private void checkUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // App.xaml.cs와 동일한 GitHub 정보 사용
                string githubUser = "Jh98JC";
                string githubRepo = "DASH.TEST";
                string updateUrl = $"https://raw.githubusercontent.com/{githubUser}/{githubRepo}/main/updates/update.xml";

                // 업데이트 체크 전 사용자에게 알림
                checkUpdate.IsEnabled = false;
                checkUpdate.Content = "확인 중...";

                // AutoUpdater 이벤트 핸들러 등록
                AutoUpdater.CheckForUpdateEvent += AutoUpdater_CheckForUpdateEvent;

                // 업데이트 체크 시작
                AutoUpdater.Start(updateUrl);
            }
            catch (Exception ex)
            {
                checkUpdate.IsEnabled = true;
                checkUpdate.Content = "업데이트 확인";
                System.Windows.MessageBox.Show($"업데이트 확인 중 오류가 발생했습니다:\n{ex.Message}", 
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AutoUpdater_CheckForUpdateEvent(AutoUpdaterDotNET.UpdateInfoEventArgs args)
        {
            // 이벤트 핸들러 해제
            AutoUpdater.CheckForUpdateEvent -= AutoUpdater_CheckForUpdateEvent;

            // UI 스레드에서 실행
            Dispatcher.Invoke(() =>
            {
                checkUpdate.IsEnabled = true;
                checkUpdate.Content = "업데이트 확인";

                if (args.Error == null)
                {
                    if (args.IsUpdateAvailable)
                    {
                        System.Windows.MessageBox.Show(
                            $"새로운 버전이 있습니다!\n\n" +
                            $"현재 버전: {args.InstalledVersion}\n" +
                            $"최신 버전: {args.CurrentVersion}\n\n" +
                            $"업데이트를 시작합니다.",
                            "업데이트 알림",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(
                            $"현재 최신 버전을 사용하고 있습니다.\n\n버전: {args.InstalledVersion}",
                            "업데이트 확인",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                else
                {
                    if (args.Error is System.Net.WebException)
                    {
                        System.Windows.MessageBox.Show(
                            "업데이트 서버에 연결할 수 없습니다.\n\n" +
                            "인터넷 연결을 확인하거나\n" +
                            "GitHub 저장소 설정을 확인해주세요.",
                            "연결 오류",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(
                            $"업데이트 확인 중 오류 발생:\n{args.Error.Message}",
                            "오류",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            });
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
