using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace WpfApp2
{
    public class NotMasterConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string role && role != "master";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class NotNullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class ProcessedStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string s && s == "승인처리"
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x5A, 0x5A));
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public partial class AdminWindow : Window
    {
        public static readonly DependencyProperty SelectedPendingUserProperty =
            DependencyProperty.Register(nameof(SelectedPendingUser), typeof(PendingUser), typeof(AdminWindow));

        public PendingUser? SelectedPendingUser
        {
            get => (PendingUser?)GetValue(SelectedPendingUserProperty);
            set => SetValue(SelectedPendingUserProperty, value);
        }

        public AdminWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private async void AdminTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource != AdminTabControl) return; // DataGrid 등 자식 SelectionChanged 버블링 무시
            if (AdminTabControl.SelectedIndex == 0) await LoadUsersAsync();
            else if (AdminTabControl.SelectedIndex == 1) await LoadPendingAsync();
        }

        private async void RefreshUsersBtn_Click(object sender, RoutedEventArgs e) => await LoadUsersAsync();
        private async void RefreshPendingBtn_Click(object sender, RoutedEventArgs e) => await LoadPendingAsync();

        private async Task LoadUsersAsync()
        {
            try
            {
                var sorts = SaveSortDescriptions(UsersGrid);
                var users = await DatabaseService.GetAllUsersAsync();
                UsersGrid.ItemsSource = users;
                if (sorts.Count == 0)
                    sorts.Add(("Id", System.ComponentModel.ListSortDirection.Ascending));
                RestoreSortDescriptions(UsersGrid, sorts);
                UserCountText.Text = $"총 {users.Count}명";
                UserStatusText.Text = "";
            }
            catch (Exception ex)
            {
                UserStatusText.Text = $"오류: {ex.Message}";
                UserStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0x5A, 0x5A));
            }
        }

        private async Task LoadPendingAsync()
        {
            try
            {
                var sorts = SaveSortDescriptions(PendingGrid);
                var pending = await DatabaseService.GetPendingUsersAsync();
                PendingGrid.ItemsSource = pending;
                if (sorts.Count == 0)
                    sorts.Add(("Id", System.ComponentModel.ListSortDirection.Ascending));
                RestoreSortDescriptions(PendingGrid, sorts);
                PendingCountText.Text = $"대기 중 {pending.Count}건";
                PendingStatusText.Text = "";
            }
            catch (Exception ex)
            {
                PendingStatusText.Text = $"오류: {ex.Message}";
                PendingStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0x5A, 0x5A));
            }
        }

        private void PendingGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedPendingUser = PendingGrid.SelectedItem as PendingUser;
            if (SelectedPendingUser != null)
            {
                SelectedUserText.Text = $"{SelectedPendingUser.Username}  ({SelectedPendingUser.DisplayName})";
                bool canProcess = SelectedPendingUser.ProcessedStatus == null;
                ApproveSelectedBtn.IsEnabled = canProcess;
                RejectSelectedBtn.IsEnabled = canProcess;
            }
        }

        private async void ApproveSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPendingUser is not PendingUser user) return;
            ApproveSelectedBtn.IsEnabled = false;
            RejectSelectedBtn.IsEnabled = false;
            try
            {
                await DatabaseService.ApproveUserAsync(user.Id);
                user.ProcessedStatus = "승인처리";
                SetPendingStatus($"'{user.Username}' 승인 완료.", true);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"승인 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                SetPendingStatus($"오류: {ex.Message}", false);
                ApproveSelectedBtn.IsEnabled = true;
                RejectSelectedBtn.IsEnabled = true;
            }
        }

        private async void RejectSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPendingUser is not PendingUser user) return;
            var result = System.Windows.MessageBox.Show(
                $"'{user.Username}' 가입 신청을 거절하시겠습니까?", "확인",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            ApproveSelectedBtn.IsEnabled = false;
            RejectSelectedBtn.IsEnabled = false;
            try
            {
                await DatabaseService.DeleteUserAsync(user.Id);
                user.ProcessedStatus = "거절";
                SetPendingStatus($"'{user.Username}' 거절 완료.", true);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"거절 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                SetPendingStatus($"오류: {ex.Message}", false);
                ApproveSelectedBtn.IsEnabled = true;
                RejectSelectedBtn.IsEnabled = true;
            }
        }

        private static List<(string Column, System.ComponentModel.ListSortDirection Dir)> SaveSortDescriptions(DataGrid grid)
        {
            var result = new List<(string, System.ComponentModel.ListSortDirection)>();
            foreach (var col in grid.Columns)
            {
                if (col.SortDirection.HasValue && col.SortMemberPath is string path && path.Length > 0)
                    result.Add((path, col.SortDirection.Value));
            }
            return result;
        }

        private static void RestoreSortDescriptions(DataGrid grid,
            List<(string Column, System.ComponentModel.ListSortDirection Dir)> sorts)
        {
            if (sorts.Count == 0) return;
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(grid.ItemsSource);
            if (view == null) return;
            using (view.DeferRefresh())
            {
                view.SortDescriptions.Clear();
                foreach (var col in grid.Columns) col.SortDirection = null;
                foreach (var (path, dir) in sorts)
                {
                    view.SortDescriptions.Add(new System.ComponentModel.SortDescription(path, dir));
                    var col = grid.Columns.FirstOrDefault(c => c.SortMemberPath == path);
                    if (col != null) col.SortDirection = dir;
                }
            }
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T target) return target;
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private async void DeleteUserBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            var user = btn.CommandParameter as UserInfo
                       ?? (FindParent<DataGridRow>(btn)?.DataContext as UserInfo);
            if (user == null) { System.Windows.MessageBox.Show("행 데이터를 찾을 수 없습니다."); return; }
            var result = System.Windows.MessageBox.Show(
                $"'{user.Username}' 계정을 삭제하시겠습니까?", "확인",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            try
            {
                await DatabaseService.DeleteUserAsync(user.Id);
                SetUserStatus("삭제 완료.", true);
                await LoadUsersAsync();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"삭제 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                SetUserStatus($"오류: {ex.Message}", false);
            }
        }

        private void SetUserStatus(string msg, bool success)
        {
            UserStatusText.Text = msg;
            UserStatusText.Foreground = new System.Windows.Media.SolidColorBrush(success
                ? System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)
                : System.Windows.Media.Color.FromRgb(0xFF, 0x5A, 0x5A));
        }

        private void SetPendingStatus(string msg, bool success)
        {
            PendingStatusText.Text = msg;
            PendingStatusText.Foreground = new System.Windows.Media.SolidColorBrush(success
                ? System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)
                : System.Windows.Media.Color.FromRgb(0xFF, 0x5A, 0x5A));
        }
    }
}
