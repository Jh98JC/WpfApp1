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
                // GitHub 릴리즈 URL에서 태그 추출
                // url 형식: https://github.com/Jh98JC/WpfApp1/releases/tag/v1.0.9
                var parts = url.Split('/');
                var tag = parts[^1];  // 마지막 부분: v1.0.9

                // GitHub API URL 구성
                var apiUrl = $"https://api.github.com/repos/Jh98JC/WpfApp1/releases/tags/{tag}";

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "WpfApp2-Updater");
                    var jsonResponse = await client.GetStringAsync(apiUrl);

                    // JSON에서 body 필드 추출
                    var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonResponse);
                    var body = jsonDoc.RootElement.GetProperty("body").GetString();

                    Dispatcher.Invoke(() =>
                    {
                        if (!string.IsNullOrWhiteSpace(body))
                        {
                            ChangelogText.Text = body;
                        }
                        else
                        {
                            ChangelogText.Text = "• 새로운 기능 및 개선 사항이 포함되어 있습니다.\n• 버그 수정 및 성능 개선";
                        }
                    });
                }
            }
            catch
            {
                Dispatcher.Invoke(() =>
                {
                    ChangelogText.Text = "• 새로운 기능 및 개선 사항이 포함되어 있습니다.\n• 버그 수정 및 성능 개선";
                });
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
