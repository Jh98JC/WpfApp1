using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace WpfApp2
{
    public partial class ChangelogWindow : Window
    {
        private string changelogUrl;

        public ChangelogWindow(string version, string changelog, string changelogUrl)
        {
            InitializeComponent();

            this.changelogUrl = changelogUrl;

            // 버전 정보 설정
            VersionText.Text = $"버전 {version}로 업데이트되었습니다";

            // 변경 사항 설정
            if (!string.IsNullOrEmpty(changelog))
            {
                ChangelogText.Text = changelog;
            }
            else
            {
                ChangelogText.Text = "• 새로운 기능 및 개선 사항이 포함되어 있습니다.\n• 버그 수정 및 성능 개선";
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ViewChangelog_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(changelogUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = changelogUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"변경 로그를 열 수 없습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
