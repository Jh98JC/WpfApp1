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
            CurrentVersionText.Text = CurrentVersion;
            LatestVersionText.Text = LatestVersion;
            VersionPanel.Visibility = Visibility.Visible;

            StatusText.Text = "새 업데이트가 있습니다!";
            UpdateButton.Visibility = Visibility.Visible;

            if (!string.IsNullOrEmpty(ChangelogContent))
            {
                ChangelogBorder.Visibility = Visibility.Visible;
                ChangelogText.Text = ChangelogContent;
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
