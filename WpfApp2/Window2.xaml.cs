using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls; // for DockPanel
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace WpfApp2
{
    public partial class Window2 : System.Windows.Window
    {
        private const string TabDragDataFormat = "WpfApp2.TabItem";
        private System.Windows.Point? _dragStartPoint;
        private bool _phoneMode = false;
        private bool _phoneWebViewInitialized = false;
        private DockPanel? _addressBarPanel; // cached reference

        public Window2()
        {
            InitializeComponent();
            this.Loaded += Window2_Loaded;
            this.Activated += Window2_Activated;
            this.Closed += Window2_Closed;
        }

        private void Window2_Activated(object sender, EventArgs e)
        {
            // Window2가 활성화되면 Window3 숨기기
            HideWindow3();
        }

        private void Window2_Closed(object? sender, EventArgs e)
        {
            // Window2가 닫히면 Window3만 표시 (MainWindow는 숨김 유지)
            ShowWindow3();
        }

        private void HideWindow3()
        {
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Window3 w3 && w3.IsVisible)
                {
                    w3.Hide();
                }
            }
        }

        private void ShowMainWindow()
        {
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is MainWindow mw && !mw.IsVisible)
                {
                    mw.Show();
                    return;
                }
            }
        }

        private void ShowWindow3()
        {
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Window3 w3)
                {
                    w3.Show();
                    w3.Topmost = true;
                    return;
                }
            }
        }

        private async Task EnsurePhoneWebViewAsync()
        {
            if (!_phoneWebViewInitialized)
            {
                await PhoneWebView.EnsureCoreWebView2Async();
                await ApplyMobileEmulationAsync(PhoneWebView);
                PhoneWebView.Source = new Uri("https://m.naver.com");
                _phoneWebViewInitialized = true;
            }
            else if (PhoneWebView.Source == null)
            {
                PhoneWebView.Source = new Uri("https://m.naver.com");
            }
        }

        private async void PhoneToggle_Click(object sender, RoutedEventArgs e)
        {
            _phoneMode = !_phoneMode;
            if (_phoneMode)
            {
                // Lazy resolve AddressBarPanel if not yet
                _addressBarPanel ??= FindName("AddressBarPanel") as DockPanel;
                if (_addressBarPanel != null) _addressBarPanel.Visibility = Visibility.Collapsed;
                Tabs.Visibility = Visibility.Collapsed;
                PhoneHost.Visibility = Visibility.Visible;
                PhoneToggleButton.Content = "↩"; // back icon
                try { await EnsurePhoneWebViewAsync(); } catch (Exception ex) { System.Windows.MessageBox.Show($"휴대폰 WebView 초기화 실패\n{ex.Message}"); }
            }
            else
            {
                PhoneHost.Visibility = Visibility.Collapsed;
                _addressBarPanel ??= FindName("AddressBarPanel") as DockPanel;
                if (_addressBarPanel != null) _addressBarPanel.Visibility = Visibility.Visible;
                Tabs.Visibility = Visibility.Visible;
                PhoneToggleButton.Content = "📱";
            }
        }

        // === 추가: 타이틀바 및 창 제어 버튼 핸들러 ===
        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                this.WindowState = this.WindowState == System.Windows.WindowState.Maximized ? System.Windows.WindowState.Normal : System.Windows.WindowState.Maximized;
            }
            else
            {
                try { this.DragMove(); } catch { }
            }
        }
        private void Minimize_Click(object sender, System.Windows.RoutedEventArgs e) => this.WindowState = System.Windows.WindowState.Minimized;
        private void MaximizeRestore_Click(object sender, System.Windows.RoutedEventArgs e) => this.WindowState = this.WindowState == System.Windows.WindowState.Maximized ? System.Windows.WindowState.Normal : System.Windows.WindowState.Maximized;
        private void Close_Click(object sender, System.Windows.RoutedEventArgs e) => this.Close();
        // === 끝 ===

        private async void Window2_Loaded(object? sender, System.Windows.RoutedEventArgs e)
        {
            // cache address panel once loaded
            _addressBarPanel = FindName("AddressBarPanel") as DockPanel;
            try
            {
                Tabs.AllowDrop = true;
                Tabs.DragOver += Tabs_DragOver;
                Tabs.Drop += Tabs_Drop;

                System.Windows.Controls.TabItem firstTab;
                WebView2 firstWebView;
                if (Tabs.Items.Count == 0)
                {
                    firstTab = new System.Windows.Controls.TabItem();
                    firstWebView = new WebView2();
                    firstTab.Content = firstWebView;
                    Tabs.Items.Add(firstTab);
                }
                else
                {
                    firstTab = Tabs.Items[0] as System.Windows.Controls.TabItem
                               ?? throw new InvalidOperationException("초기 탭을 찾을 수 없습니다.");
                    firstWebView = FindWebViewInTab(firstTab)
                                   ?? throw new InvalidOperationException("초기 WebView2를 찾을 수 없습니다.");
                }

                SetTabHeader(firstTab, "탭1");

                await firstWebView.EnsureCoreWebView2Async();
                AttachCoreHandlers(firstWebView, firstTab);
                await ApplyMobileEmulationAsync(firstWebView); // android profile

                firstWebView.Source = new Uri("https://www.naver.com");
                Tabs.SelectedItem = firstTab;
            }
            catch (WebView2RuntimeNotFoundException)
            {
                System.Windows.MessageBox.Show(
                    "Microsoft Edge WebView2 런타임이 필요합니다.\n설치 후 다시 시도하세요:\nhttps://developer.microsoft.com/microsoft-edge/webview2/",
                    "WebView2 런타임 필요",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"초기화 실패\n{ex.Message}",
                    "오류",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task ApplyMobileEmulationAsync(WebView2 wv)
        {
            if (wv.CoreWebView2 == null)
            {
                await wv.EnsureCoreWebView2Async();
            }
            // ANDROID MOBILE EMULATION
            // User-Agent: Android (Pixel7, Chrome Mobile)
            var uaPayload = "{\"userAgent\":\"Mozilla/5.0 (Linux; Android13; Pixel7 Build/TQ3A.230705.001) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36\",\"platform\":\"Android\"}";
            await wv.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setUserAgentOverride", uaPayload);

            // Device metrics: Pixel-like CSS viewport (portrait)
            var metricsPayload = "{\"width\":412,\"height\":915,\"deviceScaleFactor\":2.625,\"mobile\":true,\"screenWidth\":412,\"screenHeight\":915,\"screenOrientation\":{\"type\":\"portraitPrimary\",\"angle\":0}}";
            await wv.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", metricsPayload);

            // Touch emulation
            var touchPayload = "{\"enabled\":true,\"maxTouchPoints\":5}";
            await wv.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setTouchEmulationEnabled", touchPayload);
        }

        private WebView2? FindWebViewInTab(System.Windows.Controls.TabItem tab) => tab.Content as WebView2;

        private void GoButton_Click(object sender, System.Windows.RoutedEventArgs e) => NavigateToAddress();

        private void AddressBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                NavigateToAddress();
                e.Handled = true;
            }
        }

        private void NavigateToAddress()
        {
            string? input = AddressBox?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(input)) return;

            if (!input.Contains("://", StringComparison.OrdinalIgnoreCase))
                input = "https://" + input;

            if (!Uri.TryCreate(input, UriKind.Absolute, out Uri? uri))
            {
                System.Windows.MessageBox.Show(
                    "유효한 URL이 아닙니다.",
                    "알림",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            WebView2? wv = GetActiveWebView();
            if (wv == null) return;

            try
            {
                wv.Source = uri;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"페이지를 열 수 없습니다.\n{ex.Message}",
                    "오류",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private WebView2? GetActiveWebView() =>
            Tabs.SelectedItem is System.Windows.Controls.TabItem ti ? FindWebViewInTab(ti) : null;

        private async Task<WebView2> AddNewTabAsync(string? navigateUri = null,
            CoreWebView2NewWindowRequestedEventArgs? newWindowArgs = null)
        {
            var tab = new System.Windows.Controls.TabItem();
            SetTabHeader(tab, "새 탭");

            var wv = new WebView2();
            tab.Content = wv;

            Tabs.Items.Add(tab);
            Tabs.SelectedItem = tab;

            await wv.EnsureCoreWebView2Async();
            AttachCoreHandlers(wv, tab);
            await ApplyMobileEmulationAsync(wv);

            if (newWindowArgs != null)
            {
                newWindowArgs.Handled = true;
                newWindowArgs.NewWindow = wv.CoreWebView2;
            }
            else if (!string.IsNullOrEmpty(navigateUri))
            {
                wv.Source = new Uri(navigateUri);
            }

            return wv;
        }

        private void AttachCoreHandlers(WebView2 wv, System.Windows.Controls.TabItem tab)
        {
            wv.CoreWebView2.NewWindowRequested += async (s, e) =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(e.Uri))
                    {
                        e.Handled = true;
                        await AddNewTabAsync(e.Uri, null);
                    }
                    else
                    {
                        await AddNewTabAsync(null, e);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"새 탭 생성 실패\n{ex.Message}",
                        "오류",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            };

            wv.CoreWebView2.DocumentTitleChanged += (s, e) =>
            {
                string title = wv.CoreWebView2.DocumentTitle;
                UpdateTabHeaderTitle(tab, string.IsNullOrWhiteSpace(title) ? "새 탭" : title);
            };

            wv.NavigationStarting += (s, e) =>
            {
                if (Tabs.SelectedItem == tab && !string.IsNullOrEmpty(e.Uri))
                    AddressBox.Text = e.Uri!;
            };
        }

        private void SetTabHeader(System.Windows.Controls.TabItem tab, string title) => tab.Header = title;

        private void CloseTab(System.Windows.Controls.TabItem tab)
        {
            if (Tabs.Items.Count <= 1)
            {
                System.Windows.MessageBox.Show(
                    "마지막 탭은 닫을 수 없습니다.",
                    "알림",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            int idx = Tabs.Items.IndexOf(tab);
            Tabs.Items.Remove(tab);

            if (Tabs.Items.Count > 0)
            {
                int newIndex = Math.Min(Math.Max(idx - 1, 0), Tabs.Items.Count - 1);
                Tabs.SelectedIndex = newIndex;
            }
        }

        private void UpdateTabHeaderTitle(System.Windows.Controls.TabItem tab, string title)
        {
            if (tab.Tag is System.Windows.Controls.TextBlock tb)
                tb.Text = title;
            else
                tab.Header = title;
        }

        private void Tabs_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(TabDragDataFormat))
            {
                e.Effects = System.Windows.DragDropEffects.None;
                return;
            }
            e.Effects = System.Windows.DragDropEffects.Move;
            e.Handled = true;
        }

        private void Tabs_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(TabDragDataFormat)) return;

            var sourceTab = e.Data.GetData(TabDragDataFormat) as System.Windows.Controls.TabItem;
            if (sourceTab == null) return;

            System.Windows.Point pos = e.GetPosition(Tabs);
            var element = Tabs.InputHitTest(pos) as System.Windows.DependencyObject;
            var targetTab = FindParentTabItem(element);

            if (targetTab == null || targetTab == sourceTab) return;

            int sourceIndex = Tabs.Items.IndexOf(sourceTab);
            int targetIndex = Tabs.Items.IndexOf(targetTab);
            if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) return;

            Tabs.Items.Remove(sourceTab);
            Tabs.Items.Insert(targetIndex, sourceTab);
            Tabs.SelectedItem = sourceTab;
        }

        private static System.Windows.Controls.TabItem? FindParentTabItem(System.Windows.DependencyObject? obj)
        {
            while (obj != null && obj is not System.Windows.Controls.TabItem)
            {
                obj = VisualTreeHelper.GetParent(obj);
            }
            return obj as System.Windows.Controls.TabItem;
        }

        private void TabCloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;
            var tab = FindParentTabItem(btn);
            if (tab != null)
            {
                CloseTab(tab);
            }
        }
    }
}
