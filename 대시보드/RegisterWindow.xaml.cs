using System.Windows;
using System.Windows.Input;

namespace WpfApp2
{
    public partial class RegisterWindow : Window
    {
        public RegisterWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => { DisplayNameBox.Focus(); CheckCapsLock(); };
            PreviewKeyUp += (_, _) => CheckCapsLock();
        }

        private void CheckCapsLock()
        {
            bool capsOn = Keyboard.IsKeyToggled(Key.CapsLock);
            bool pwdFocused = PasswordBox.IsKeyboardFocused || ConfirmPasswordBox.IsKeyboardFocused;
            CapsLockIndicator.Visibility = capsOn && pwdFocused ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PasswordField_FocusChanged(object sender, KeyboardFocusChangedEventArgs e) => CheckCapsLock();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private async void SubmitBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!DatabaseService.IsConfigured)
            {
                SetStatus("DB 연결이 설정되지 않았습니다.", false);
                return;
            }

            string displayName = DisplayNameBox.Text.Trim();
            string username = UsernameBox.Text.Trim();
            string password = PasswordBox.Password;
            string confirm = ConfirmPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username))
            {
                SetStatus("사용자명을 입력하세요.", false);
                return;
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                SetStatus("비밀번호를 입력하세요.", false);
                return;
            }
            if (password != confirm)
            {
                SetStatus("비밀번호가 일치하지 않습니다.", false);
                return;
            }
            if (string.IsNullOrWhiteSpace(displayName)) displayName = username;

            SubmitBtn.IsEnabled = false;
            SetStatus("신청 중...", null);

            try
            {
                bool ok = await DatabaseService.RegisterRequestAsync(username, displayName, password);
                if (ok)
                {
                    DialogResult = true;
                    Close();
                }
                else
                {
                    SetStatus("이미 사용 중인 사용자명입니다.", false);
                    SubmitBtn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                SetStatus($"오류: {ex.Message}", false);
                SubmitBtn.IsEnabled = true;
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
