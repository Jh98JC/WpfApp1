using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace WpfApp2
{
    public partial class UpdateWindow : Window
    {
        public string CurrentVersion { get; set; }
        public string LatestVersion { get; set; }
        public string ChangelogContent { get; set; }

        private readonly string _downloadUrl;
        private CancellationTokenSource? _cts;

        public UpdateWindow(string currentVersion, string latestVersion, string changelog, string downloadUrl)
        {
            InitializeComponent();
            CurrentVersion = currentVersion;
            LatestVersion = latestVersion;
            ChangelogContent = changelog;
            _downloadUrl = downloadUrl;
            Loaded += UpdateWindow_Loaded;
        }

        private void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CurrentVersionText.Text = $"v{CurrentVersion}";
            LatestVersionText.Text = $"v{LatestVersion}";
            VersionInfoText.Text = $"v{LatestVersion} 사용 가능";
            VersionPanel.Visibility = Visibility.Visible;
            StatusText.Text = "새 버전 업데이트";
            UpdateButton.Visibility = Visibility.Visible;

            ChangelogBorder.Visibility = Visibility.Visible;
            ChangelogText.Text = !string.IsNullOrEmpty(ChangelogContent)
                ? ChangelogContent
                : "• 새로운 기능 및 개선사항이 포함되어 있습니다.\n• 버그 수정 및 성능 개선";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            DialogResult = false;
            Close();
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateButton.Visibility = Visibility.Collapsed;
            CloseButton.IsEnabled = false;
            HeaderCloseButton.IsEnabled = false;

            StatusText.Text = "업데이트 중...";
            FooterProgressPanel.Visibility = Visibility.Visible;
            FooterProgressText.Text = "다운로드 중... 0%";

            _cts = new CancellationTokenSource();
            try
            {
                var progress = new Progress<int>(pct =>
                {
                    DownloadProgressBar.Value = pct;
                    FooterProgressText.Text = $"다운로드 중... {pct}%";
                });

                string tempFile = await DownloadFileAsync(_downloadUrl, progress, _cts.Token);

                DownloadProgressBar.Value = 100;
                FooterProgressText.Text = "설치 중...";

                if (System.Windows.Application.Current is App app)
                    app.StartInstall(tempFile);
                // ZIP의 경우 StartInstall이 바로 반환 — 창은 "설치 중..." 상태 유지하다 자동 종료
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                StatusText.Text = "다운로드 실패";
                FooterProgressText.Text = $"오류: {ex.Message}";
                CloseButton.Content = "닫기";
                CloseButton.IsEnabled = true;
                HeaderCloseButton.IsEnabled = true;
            }
        }

        private static async Task<string> DownloadFileAsync(string url, IProgress<int> progress, CancellationToken ct)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;

            string ext = ".exe";
            try { ext = Path.GetExtension(new Uri(url).LocalPath); } catch { }
            if (string.IsNullOrEmpty(ext)) ext = ".exe";

            var tempFile = Path.Combine(Path.GetTempPath(), $"WpfApp2_update{ext}");

            using var httpStream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await httpStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                if (totalBytes > 0)
                    progress.Report((int)(downloaded * 100 / totalBytes));
            }

            return tempFile;
        }
    }
}
