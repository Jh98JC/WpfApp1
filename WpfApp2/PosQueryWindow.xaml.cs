using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SWM = System.Windows.Media;
using SWC = System.Windows.Controls;

namespace WpfApp2
{
    public partial class PosQueryWindow : Window
    {
        public PosQueryWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) => await LoadSkippedStoresAsync();
        }

        private async Task LoadSkippedStoresAsync()
        {
            SkippedList.Children.Clear();

            if (!DatabaseService.IsDataConfigured)
            {
                EmptyText.Text = "데이터 DB가 연결되지 않았습니다.";
                EmptyText.Visibility = Visibility.Visible;
                return;
            }

            System.Collections.Generic.List<SkippedStoreEntry> entries;
            try
            {
                await DatabaseService.InitializePosTablesAsync();
                entries = await DatabaseService.GetSkippedStoresAsync();
            }
            catch
            {
                EmptyText.Text = "불러오기 실패";
                EmptyText.Visibility = Visibility.Visible;
                return;
            }

            if (entries.Count == 0)
            {
                EmptyText.Visibility = Visibility.Visible;
                return;
            }

            EmptyText.Visibility = Visibility.Collapsed;

            foreach (var entry in entries)
            {
                SkippedList.Children.Add(BuildRow(entry));
            }
        }

        private Border BuildRow(SkippedStoreEntry entry)
        {
            var row = new Border
            {
                Background = SWM.Brushes.Transparent,
                BorderBrush = new SWM.SolidColorBrush(SWM.Color.FromRgb(0x1E, 0x1E, 0x2E)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 8, 12, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textBlock = new TextBlock
            {
                Text = entry.DisplayText,
                FontSize = 12,
                Foreground = new SWM.SolidColorBrush(SWM.Color.FromRgb(0xA0, 0xA8, 0xC8)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(textBlock, 0);

            var collectBtn = new SWC.Button
            {
                Content = "취합",
                Width = 52,
                Height = 24,
                FontSize = 11,
                Foreground = new SWM.SolidColorBrush(SWM.Color.FromRgb(0xFF, 0xB3, 0x47)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = entry
            };
            collectBtn.Template = BuildBtnTemplate();
            collectBtn.Click += async (s, e) => await OnCollectClick(collectBtn, entry);
            Grid.SetColumn(collectBtn, 1);

            grid.Children.Add(textBlock);
            grid.Children.Add(collectBtn);
            row.Child = grid;

            row.MouseEnter += (_, _) => row.Background =
                new SWM.SolidColorBrush(SWM.Color.FromArgb(30, 0x55, 0x55, 0x88));
            row.MouseLeave += (_, _) => row.Background = SWM.Brushes.Transparent;

            return row;
        }

        private static ControlTemplate BuildBtnTemplate()
        {
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            factory.SetValue(Border.BackgroundProperty,
                new SWM.SolidColorBrush(SWM.Color.FromRgb(0x25, 0x25, 0x38)));
            factory.SetValue(Border.BorderBrushProperty,
                new SWM.SolidColorBrush(SWM.Color.FromRgb(0x3A, 0x3A, 0x55)));
            factory.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            factory.AppendChild(presenter);

            return new ControlTemplate(typeof(SWC.Button)) { VisualTree = factory };
        }

        private async Task OnCollectClick(SWC.Button btn, SkippedStoreEntry entry)
        {
            btn.IsEnabled = false;
            btn.Content = "...";

            bool ok = await DaejinPosService.RunForStoreAsync(entry.StoreName, entry.CollectionDate);

            if (ok)
            {
                await LoadSkippedStoresAsync();
            }
            else
            {
                btn.Content = "실패";
                btn.Foreground = new SWM.SolidColorBrush(SWM.Color.FromRgb(0xFF, 0x55, 0x55));
                btn.IsEnabled = true;
            }
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await LoadSkippedStoresAsync();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
