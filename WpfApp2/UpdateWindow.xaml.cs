using System;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace WpfApp2
{
    public partial class UpdateWindow : Window
    {
        public string CurrentVersion { get; set; }
        public string LatestVersion { get; set; }
        public string ChangelogContent { get; set; }

        public UpdateWindow(string currentVersion, string latestVersion, string changelog)
        {
            InitializeComponent();

            CurrentVersion = currentVersion;
            LatestVersion = latestVersion;
            ChangelogContent = changelog;

            Loaded += UpdateWindow_Loaded;
        }

        private void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 버전 정보 업데이트
            CurrentVersionText.Text = $"v{CurrentVersion}";
            LatestVersionText.Text = $"v{LatestVersion}";
            VersionInfoText.Text = $"v{LatestVersion} 사용 가능";

            VersionPanel.Visibility = Visibility.Visible;
            StatusText.Text = "새 버전 업데이트";
            UpdateButton.Visibility = Visibility.Visible;

            // 변경 내용이 있으면 표시
            if (!string.IsNullOrEmpty(ChangelogContent))
            {
                ChangelogBorder.Visibility = Visibility.Visible;
                ChangelogText.Text = ChangelogContent;
            }
            else
            {
                ChangelogBorder.Visibility = Visibility.Visible;
                ChangelogText.Text = "• 새로운 기능 및 개선사항이 포함되어 있습니다.\n• 버그 수정 및 성능 개선";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
