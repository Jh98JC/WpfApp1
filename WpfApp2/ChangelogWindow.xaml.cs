using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace WpfApp2
{
    public partial class ChangelogWindow : Window
    {
        private string changelogUrl;

        // 버전 정보만 받는 간단한 생성자 추가
        public ChangelogWindow(string version) : this(version, null, null)
        {
        }

        public ChangelogWindow(string version, string? changelog, string? changelogUrl)
        {
            InitializeComponent();

            this.changelogUrl = changelogUrl ?? "";

            // 버전 정보 설정
            VersionText.Text = $"버전 {version}(으)로 업데이트되었습니다";

            // 변경 사항 설정
            if (!string.IsNullOrEmpty(changelog))
            {
                ChangelogText.Text = changelog;
            }
            else
            {
                // GitHub 변경로그 URL에서 정보 로드 시도
                if (!string.IsNullOrEmpty(changelogUrl))
                {
                    LoadChangelogFromUrl(changelogUrl);
                }
                else
                {
                    ChangelogText.Text = "• 새로운 기능 및 개선 사항이 포함되어 있습니다.\n• 버그 수정 및 성능 개선";
                }
            }
        }

        private async void LoadChangelogFromUrl(string url)
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "WpfApp2-Updater");
                    var response = await client.GetStringAsync(url);

                    // 간단한 변경로그 표시 (GitHub Release 페이지를 열 수 있는 버튼 제공)
                    Dispatcher.Invoke(() =>
                    {
                        ChangelogText.Text = $"자세한 변경 내용을 보려면 '변경 로그 보기' 버튼을 클릭하세요.\n\n" +
                                            $"• 새로운 기능 및 개선 사항\n" +
                                            $"• 버그 수정 및 성능 개선\n" +
                                            $"• 사용자 경험 향상";
                    });
                }
            }
            catch
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
