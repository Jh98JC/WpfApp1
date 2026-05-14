using System.Linq;
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
        private System.Collections.Generic.List<System.DateTime> _refetchDates = new();
        private System.Windows.Controls.Primitives.Popup? _refetchPopup;
        private System.Windows.Controls.Calendar? _refetchCal;

        public PosQueryWindow()
        {
            InitializeComponent();
            DaejinPosService.StatusChanged += OnPosStatus;
            Closed += (_, _) => DaejinPosService.StatusChanged -= OnPosStatus;
            Loaded += async (_, _) =>
            {
                await LoadSkippedStoresAsync();
                UpdateMappingHint();
            };

            _refetchDates.Add(DaejinPosService.GetAutoTargetDate());
            UpdateDateDisplay();
        }

        private void UpdateDateDisplay()
        {
            if (_refetchDates.Count == 0)
                RefetchDateText.Text = "날짜 선택";
            else if (_refetchDates.Count == 1)
                RefetchDateText.Text = _refetchDates[0].ToString("yy-MM-dd");
            else
            {
                var min = _refetchDates.Min();
                var max = _refetchDates.Max();
                RefetchDateText.Text = $"{min:M/d}~{max:M/d} ({_refetchDates.Count}일)";
            }
        }

        private void RefetchDateButton_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EnsureRefetchPopup();
            _refetchPopup!.IsOpen = !_refetchPopup.IsOpen;
        }

        private void EnsureRefetchPopup()
        {
            if (_refetchPopup != null) return;

            var darkBg  = new SWM.SolidColorBrush(SWM.Color.FromRgb(0x15, 0x15, 0x1E));
            var darkBg2 = new SWM.SolidColorBrush(SWM.Color.FromRgb(0x25, 0x25, 0x3A));
            var fg      = new SWM.SolidColorBrush(SWM.Color.FromRgb(0xE0, 0xE0, 0xF0));
            var dim     = new SWM.SolidColorBrush(SWM.Color.FromRgb(0x3A, 0x3A, 0x55));
            var hov     = new SWM.SolidColorBrush(SWM.Color.FromRgb(0x32, 0x32, 0x55));

            _refetchCal = new SWC.Calendar
            {
                DisplayMode   = SWC.CalendarMode.Month,
                SelectionMode = SWC.CalendarSelectionMode.SingleRange,
                Background    = darkBg,
                Foreground    = fg,
                BorderBrush   = dim,
                BorderThickness = new Thickness(1)
            };

            if (_refetchDates.Count > 0)
            {
                var sorted = _refetchDates.OrderBy(d => d).ToList();
                _refetchCal.SelectedDates.AddRange(sorted.First(), sorted.Last());
            }

            var calItemStyle = new Style(typeof(System.Windows.Controls.Primitives.CalendarItem));
            calItemStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.CalendarItem.BackgroundProperty, darkBg));
            calItemStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.CalendarItem.ForegroundProperty, fg));
            _refetchCal.CalendarItemStyle = calItemStyle;

            var dayStyle = new Style(typeof(System.Windows.Controls.Primitives.CalendarDayButton));
            dayStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.CalendarDayButton.ForegroundProperty, fg));
            dayStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.CalendarDayButton.BackgroundProperty, SWM.Brushes.Transparent));
            var hoverTrig = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrig.Setters.Add(new Setter(System.Windows.Controls.Primitives.CalendarDayButton.BackgroundProperty, hov));
            dayStyle.Triggers.Add(hoverTrig);
            _refetchCal.CalendarDayButtonStyle = dayStyle;

            var calBtnStyle = new Style(typeof(System.Windows.Controls.Primitives.CalendarButton));
            calBtnStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.CalendarButton.ForegroundProperty, fg));
            calBtnStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.CalendarButton.BackgroundProperty, darkBg));
            _refetchCal.CalendarButtonStyle = calBtnStyle;

            _refetchCal.SelectedDatesChanged += (s, a) =>
            {
                _refetchDates = _refetchCal.SelectedDates.ToList();
                UpdateDateDisplay();
            };

            // 프리셋 버튼 패널
            var presetPanel = new WrapPanel { Margin = new Thickness(2, 4, 2, 2) };

            ControlTemplate MakePresetTemplate()
            {
                var f = new FrameworkElementFactory(typeof(Border));
                f.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
                f.SetValue(Border.BackgroundProperty, darkBg2);
                f.SetValue(Border.BorderBrushProperty, dim);
                f.SetValue(Border.BorderThicknessProperty, new Thickness(1));
                var cp = new FrameworkElementFactory(typeof(ContentPresenter));
                cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
                cp.SetValue(ContentPresenter.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
                f.AppendChild(cp);
                return new ControlTemplate(typeof(SWC.Button)) { VisualTree = f };
            }

            var presetTpl = MakePresetTemplate();

            void AddPreset(string label, System.DateTime from, System.DateTime to)
            {
                var btn = new SWC.Button
                {
                    Content  = label,
                    Width    = 52,
                    Height   = 22,
                    FontSize = 11,
                    Foreground = fg,
                    Margin   = new Thickness(2),
                    Cursor   = System.Windows.Input.Cursors.Hand,
                    Template = presetTpl
                };
                btn.Click += (_, _) =>
                {
                    _refetchCal!.SelectedDates.Clear();
                    _refetchCal.SelectedDates.AddRange(from, to);
                    _refetchPopup!.IsOpen = false;
                };
                presetPanel.Children.Add(btn);
            }

            var yest = System.DateTime.Today.AddDays(-1);
            var dow = (int)yest.DayOfWeek;
            var daysFromMon = dow == 0 ? 6 : dow - 1;
            var thisMon = yest.AddDays(-daysFromMon);

            AddPreset("어제",   yest, yest);
            AddPreset("3일",    yest.AddDays(-2), yest);
            AddPreset("7일",    yest.AddDays(-6), yest);
            AddPreset("이번주", thisMon, yest);
            AddPreset("저번주", thisMon.AddDays(-7), thisMon.AddDays(-1));

            var outerStack = new StackPanel();
            outerStack.Children.Add(presetPanel);
            outerStack.Children.Add(_refetchCal);

            var popupBorder = new Border
            {
                Background      = darkBg,
                BorderBrush     = dim,
                BorderThickness = new Thickness(1),
                Padding         = new Thickness(2),
                Child           = outerStack
            };

            _refetchPopup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget  = RefetchDateButton,
                Placement        = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                AllowsTransparency = true,
                StaysOpen        = false,
                Child            = popupBorder
            };
        }

        private void UpdateMappingHint()
        {
            try
            {
                var map = StoreMappingService.Load();
                if (map.Stores.Count == 0)
                {
                    HeaderText.Text = "매장-계정 매핑이 없습니다. 한 번 '날짜 재추출(어제)'을 실행해 매핑을 만든 뒤 사용하세요.";
                    HeaderText.Foreground = new SWM.SolidColorBrush(SWM.Color.FromRgb(0xFF, 0xB3, 0x47));
                }
            }
            catch { }
        }

        private async void RefetchBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_refetchDates.Count == 0)
            {
                System.Windows.MessageBox.Show("재추출할 날짜를 선택하세요.", "알림",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            var sorted = _refetchDates.Select(d => d.Date).Distinct().OrderBy(d => d).ToList();
            string rangeText = sorted.Count == 1
                ? $"{sorted[0]:yyyy-MM-dd}"
                : $"{sorted.First():yyyy-MM-dd} ~ {sorted.Last():yyyy-MM-dd} ({sorted.Count}일)";

            var result = System.Windows.MessageBox.Show(
                $"{rangeText} 날짜의 기존 데이터를 모두 삭제하고 다시 수집합니다.\n계속하시겠습니까?",
                "재추출 확인",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (result != System.Windows.MessageBoxResult.Yes) return;

            RefetchBtn.IsEnabled = false;
            RefetchBtn.Content = "...";
            try
            {
                foreach (var date in sorted)
                    await DaejinPosService.ForceRunAsync(date);
            }
            finally
            {
                RefetchBtn.Content = "재추출";
                RefetchBtn.IsEnabled = true;
                await LoadSkippedStoresAsync();
            }
        }

        private void OnPosStatus(string status)
        {
            Dispatcher.Invoke(async () =>
            {
                if (!string.IsNullOrEmpty(status))
                {
                    RunningText.Text = status;
                    RunningText.Visibility = Visibility.Visible;
                }
                else
                {
                    RunningText.Visibility = Visibility.Collapsed;
                    await LoadSkippedStoresAsync();
                }
            });
        }

        public async Task RefreshAsync() => await LoadSkippedStoresAsync();

        private async Task LoadSkippedStoresAsync()
        {
            SkippedList.Children.Clear();
            _rowCheckboxes.Clear();

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

        private readonly System.Collections.Generic.Dictionary<SWC.CheckBox, SkippedStoreEntry> _rowCheckboxes
            = new System.Collections.Generic.Dictionary<SWC.CheckBox, SkippedStoreEntry>();

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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var check = new SWC.CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Foreground = new SWM.SolidColorBrush(SWM.Color.FromRgb(0xA0, 0xA8, 0xC8))
            };
            Grid.SetColumn(check, 0);
            _rowCheckboxes[check] = entry;

            var textBlock = new TextBlock
            {
                Text = entry.DisplayText,
                FontSize = 12,
                Foreground = new SWM.SolidColorBrush(SWM.Color.FromRgb(0xA0, 0xA8, 0xC8)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(textBlock, 1);

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
            Grid.SetColumn(collectBtn, 2);

            grid.Children.Add(check);
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
            btn.Foreground = new SWM.SolidColorBrush(SWM.Color.FromRgb(0xFF, 0xB3, 0x47));
            btn.ToolTip = null;

            bool ok = await DaejinPosService.RunForStoreAsync(entry.StoreName, entry.CollectionDate);

            if (ok)
            {
                HeaderText.Text = $"{entry.StoreName} 재취합 완료";
                HeaderText.Foreground = new SWM.SolidColorBrush(SWM.Color.FromRgb(0x70, 0xC0, 0x70));
                await LoadSkippedStoresAsync();
            }
            else
            {
                string reason = string.IsNullOrEmpty(DaejinPosService.LastError)
                    ? "실패 (상세 사유 없음)"
                    : DaejinPosService.LastError;

                btn.Content = "실패";
                btn.Foreground = new SWM.SolidColorBrush(SWM.Color.FromRgb(0xFF, 0x55, 0x55));
                btn.ToolTip = reason;
                btn.IsEnabled = true;

                HeaderText.Text = $"{entry.StoreName} 실패: {reason}";
                HeaderText.Foreground = new SWM.SolidColorBrush(SWM.Color.FromRgb(0xFF, 0x80, 0x80));
            }
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await LoadSkippedStoresAsync();
        }

        private async void DeleteSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = new System.Collections.Generic.List<SkippedStoreEntry>();
            foreach (var kv in _rowCheckboxes)
                if (kv.Key.IsChecked == true) selected.Add(kv.Value);

            if (selected.Count == 0)
            {
                System.Windows.MessageBox.Show("선택된 항목이 없습니다.", "알림",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            var ok = System.Windows.MessageBox.Show(
                $"선택한 {selected.Count}건을 누락목록에서 삭제하시겠습니까?",
                "선택 삭제",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (ok != System.Windows.MessageBoxResult.Yes) return;

            DeleteSelectedBtn.IsEnabled = false;
            int failed = 0;
            foreach (var entry in selected)
            {
                try { await DatabaseService.DeleteSkippedStoreByIdAsync(entry.Id); }
                catch { failed++; }
            }
            DeleteSelectedBtn.IsEnabled = true;

            if (failed > 0)
            {
                System.Windows.MessageBox.Show($"{selected.Count - failed}건 삭제 완료, {failed}건 실패",
                    "결과", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }

            await LoadSkippedStoresAsync();
        }

        private async void DeleteAllBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_rowCheckboxes.Count == 0)
            {
                System.Windows.MessageBox.Show("삭제할 항목이 없습니다.", "알림",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            var ok = System.Windows.MessageBox.Show(
                $"누락목록 전체({_rowCheckboxes.Count}건)를 삭제하시겠습니까?",
                "전체 삭제",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (ok != System.Windows.MessageBoxResult.Yes) return;

            DeleteAllBtn.IsEnabled = false;
            try
            {
                await DatabaseService.DeleteAllSkippedStoresAsync();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"삭제 실패: {ex.Message}",
                    "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            DeleteAllBtn.IsEnabled = true;

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
