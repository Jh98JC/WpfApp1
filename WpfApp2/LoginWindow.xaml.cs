using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace WpfApp2
{
    public partial class LoginWindow : Window
    {
        private bool _showingPassword = false;
        private CancellationTokenSource? _pollCts;

        public LoginWindow()
        {
            InitializeComponent();
            Loaded += LoginWindow_Loaded;
            Closed += (_, _) => _pollCts?.Cancel();
            PreviewKeyUp += (_, _) => UpdateCapsLock();
            PreviewKeyDown += (_, _) => UpdateCapsLock();
        }

        private void UpdateCapsLock()
        {
            CapsLockText.Visibility = Keyboard.IsKeyToggled(Key.CapsLock)
                ? Visibility.Visible
                : Visibility.Hidden;
        }

        private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _pollCts = new CancellationTokenSource();
            _ = PollDbStatusAsync(_pollCts.Token);

            var saved = AutoLoginService.Load();
            if (saved != null)
            {
                UsernameBox.Text = saved.Value.Username;
                PasswordBox.Password = saved.Value.Password;
                AutoLoginCheck.IsChecked = true;

                if (DatabaseService.IsConfigured)
                    await AttemptLoginAsync(saved.Value.Username, saved.Value.Password);
            }
            else
            {
                UsernameBox.Focus();
            }
        }

        private async Task PollDbStatusAsync(CancellationToken ct)
        {
            SetDbStatus(null); // "확인 중..." 즉시 표시

            while (!ct.IsCancellationRequested)
            {
                bool connected = DatabaseService.IsConfigured &&
                                 await DatabaseService.TestConnectionAsync(DatabaseService.ConnectionString);
                if (ct.IsCancellationRequested) break;
                SetDbStatus(connected);

                try { await Task.Delay(2500, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        private void SetDbStatus(bool? connected)
        {
            switch (connected)
            {
                case true:
                    DbStatusText.Text = "서버연결됨";
                    DbStatusText.Foreground = new SolidColorBrush(WpfColor.FromRgb(0x4C, 0xAF, 0x50));
                    break;
                case false:
                    DbStatusText.Text = "서버연결필요";
                    DbStatusText.Foreground = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0x5A, 0x5A));
                    break;
                default:
                    DbStatusText.Text = "서버확인 중...";
                    DbStatusText.Foreground = new SolidColorBrush(WpfColor.FromRgb(0xAA, 0xAA, 0xAA));
                    break;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowPwBtn_Click(object sender, RoutedEventArgs e)
        {
            _showingPassword = !_showingPassword;
            if (_showingPassword)
            {
                PasswordTextBox.Text = PasswordBox.Password;
                PasswordBox.Visibility = Visibility.Collapsed;
                PasswordTextBox.Visibility = Visibility.Visible;
                PasswordTextBox.Focus();
                PasswordTextBox.CaretIndex = PasswordTextBox.Text.Length;
            }
            else
            {
                PasswordBox.Password = PasswordTextBox.Text;
                PasswordTextBox.Visibility = Visibility.Collapsed;
                PasswordBox.Visibility = Visibility.Visible;
                PasswordBox.Focus();
            }
        }

        private string GetPassword() =>
            _showingPassword ? PasswordTextBox.Text : PasswordBox.Password;

        private async void LoginBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!DatabaseService.IsConfigured)
            {
                SetStatus("DB 연결이 설정되지 않았습니다.\n아래 'DB 연결 설정'에서 먼저 설정해 주세요.", false);
                return;
            }

            string username = UsernameBox.Text.Trim();
            string password = GetPassword();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                SetStatus("사용자명과 비밀번호를 입력하세요.", false);
                return;
            }

            await AttemptLoginAsync(username, password);
        }

        private async Task AttemptLoginAsync(string username, string password)
        {
            LoginBtn.IsEnabled = false;
            RegisterBtn.IsEnabled = false;
            SetStatus("로그인 중...", null);

            try
            {
                var (success, role, userId, displayName) = await DatabaseService.AuthenticateAsync(username, password);

                if (success)
                {
                    Session.UserId = userId;
                    Session.Username = username;
                    Session.DisplayName = displayName;
                    Session.Role = role;

                    if (AutoLoginCheck.IsChecked == true)
                        AutoLoginService.Save(username, password);
                    else
                        AutoLoginService.Clear();

                    DialogResult = true;
                    Close();
                    return;
                }
                else
                {
                    SetStatus("아이디 또는 비밀번호가 올바르지 않거나\n승인되지 않은 계정입니다.", false);
                    AutoLoginService.Clear();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Login] {ex.Message}");
                SetStatus("서버에 연결할 수 없습니다.", false);
            }

            LoginBtn.IsEnabled = true;
            RegisterBtn.IsEnabled = true;
        }

        private void RegisterBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!DatabaseService.IsConfigured)
            {
                SetStatus("DB 연결이 설정되지 않았습니다.\n아래 'DB 연결 설정'에서 먼저 설정해 주세요.", false);
                return;
            }
            var win = new RegisterWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            if (win.ShowDialog() == true)
                SetStatus("가입 신청이 완료되었습니다. 관리자 승인 후 로그인할 수 있습니다.", true);
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter && LoginBtn.IsEnabled)
            {
                LoginBtn_Click(sender, new RoutedEventArgs());
                return;
            }

            // Ctrl+Shift+D : 마스터 전용 DB 설정 (숨겨진 단축키)
            if (e.Key == Key.D &&
                Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                var win = new DbConfigWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                win.ShowDialog();
                e.Handled = true;
            }
        }

        private void SetStatus(string msg, bool? success)
        {
            StatusText.Text = msg;
            StatusText.Foreground = success switch
            {
                true  => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)),
                false => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x5A, 0x5A)),
                null  => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White)
            };
        }
    }
}
