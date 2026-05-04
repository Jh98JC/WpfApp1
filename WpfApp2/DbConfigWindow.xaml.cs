using System.Windows;
using System.Windows.Input;

namespace WpfApp2
{
    public partial class DbConfigWindow : Window
    {
        public DbConfigWindow()
        {
            InitializeComponent();
            ConnectionStringBox.Text = DatabaseService.ConnectionString;
            DataConnectionStringBox.Text = DatabaseService.DataConnectionString;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private async void TestBtn_Click(object sender, RoutedEventArgs e)
        {
            string cs = ConnectionStringBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(cs))
            {
                SetStatus("연결 문자열을 입력하세요.", false);
                return;
            }
            TestBtn.IsEnabled = false;
            SetStatus("연결 확인 중...", null);
            bool ok = await DatabaseService.TestConnectionAsync(cs);
            SetStatus(ok ? "연결 성공!" : "연결 실패. 연결 문자열을 확인하세요.", ok);
            TestBtn.IsEnabled = true;
        }

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            string cs = ConnectionStringBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(cs))
            {
                SetStatus("연결 문자열을 입력하세요.", false);
                return;
            }

            SaveBtn.IsEnabled = false;
            SetStatus("저장 중...", null);

            bool connected = await DatabaseService.TestConnectionAsync(cs);
            if (!connected)
            {
                SetStatus("연결 실패. 연결 문자열을 확인하세요.", false);
                SaveBtn.IsEnabled = true;
                return;
            }

            DatabaseService.SaveConnectionString(cs);

            try
            {
                await DatabaseService.InitializeDatabaseAsync();

                bool hasUser = await DatabaseService.HasAnyUserAsync();
                if (!hasUser)
                {
                    string displayName = MasterDisplayNameBox.Text.Trim();
                    string username = MasterUsernameBox.Text.Trim();
                    string password = MasterPasswordBox.Password;

                    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    {
                        SetStatus("DB에 계정이 없습니다. 마스터 아이디와 비밀번호를 입력하세요.", false);
                        SaveBtn.IsEnabled = true;
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(displayName)) displayName = username;
                    await DatabaseService.CreateMasterAsync(username, displayName, password);
                    SetStatus("저장 완료! 마스터 계정이 생성되었습니다.", true);
                }
                else
                {
                    SetStatus("저장 완료!", true);
                }

                ConfirmBtn.Visibility = System.Windows.Visibility.Visible;
                SaveBtn.Visibility = System.Windows.Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                SetStatus($"오류: {ex.Message}", false);
                SaveBtn.IsEnabled = true;
            }
        }

        private void ConfirmBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private async void DataTestBtn_Click(object sender, RoutedEventArgs e)
        {
            string cs = DataConnectionStringBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(cs))
            {
                SetDataStatus("연결 문자열을 입력하세요.", false);
                return;
            }
            DataTestBtn.IsEnabled = false;
            SetDataStatus("연결 확인 중...", null);
            bool ok = await DatabaseService.TestConnectionAsync(cs);
            SetDataStatus(ok ? "연결 성공!" : "연결 실패. 연결 문자열을 확인하세요.", ok);
            DataTestBtn.IsEnabled = true;
        }

        private async void DataSaveBtn_Click(object sender, RoutedEventArgs e)
        {
            string cs = DataConnectionStringBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(cs))
            {
                SetDataStatus("연결 문자열을 입력하세요.", false);
                return;
            }
            DataSaveBtn.IsEnabled = false;
            SetDataStatus("연결 확인 중...", null);
            bool ok = await DatabaseService.TestConnectionAsync(cs);
            if (!ok)
            {
                SetDataStatus("연결 실패. 연결 문자열을 확인하세요.", false);
                DataSaveBtn.IsEnabled = true;
                return;
            }
            DatabaseService.SaveDataConnectionString(cs);
            SetDataStatus("데이터 DB 저장 완료!", true);
            DataSaveBtn.IsEnabled = true;
        }

        private void SetStatus(string msg, bool? success)
        {
            TestStatusText.Text = msg;
            TestStatusText.Foreground = success switch
            {
                true  => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)),
                false => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x5A, 0x5A)),
                null  => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White)
            };
        }

        private void SetDataStatus(string msg, bool? success)
        {
            DataTestStatusText.Text = msg;
            DataTestStatusText.Foreground = success switch
            {
                true  => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)),
                false => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x5A, 0x5A)),
                null  => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White)
            };
        }
    }
}
