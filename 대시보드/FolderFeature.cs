using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace WpfApp2
{
    // ===== 저장용(JSON 직렬화) 모델 =====
    public class FolderRecord
    {
        public string Name { get; set; } = "새 폴더";
        public double X { get; set; }
        public double Y { get; set; }
        public int CanvasIndex { get; set; }
        public int ColumnsOverride { get; set; }
        public int RowsOverride { get; set; }
        public string SortMode { get; set; } = "None";
        public List<FolderItemRecord> Items { get; set; } = new();
    }

    public class FolderItemRecord
    {
        // 둘 중 하나만 채워짐
        public ButtonState? Button { get; set; }
        public FolderRecord? Folder { get; set; }
        // 폴더 내 셀 좌표 (-1 = 자동 배치). 정렬 모드가 None일 때만 의미가 있음.
        public int CellCol { get; set; } = -1;
        public int CellRow { get; set; } = -1;
    }

    public partial class MainWindow
    {
        // ===== 상수 =====
        private const double FolderCellSize = 50.0;          // 셀(버튼) 한 변
        private const double FolderCellGap = 4.0;            // 셀 사이 간격
        private const double FolderInnerPadding = 6.0;       // 폴더창 안쪽 여백
        private const double FolderHeaderHeight = 22.0;      // 이름 헤더 높이
        private const int FolderAutoMaxColumns = 5;          // 자동 모드 최대 열
        private const int FolderAutoMaxVisibleRows = 4;      // 자동 모드 최대 가시 행

        // AppDataFolder는 다른 partial 파일의 static 필드라 초기화 순서가 보장되지 않으므로
        // readonly 필드 대신 프로퍼티로 지연 평가
        private static string FolderStateFile =>
            System.IO.Path.Combine(AppDataFolder, "folder_states.json");

        // ===== 런타임 모델 =====
        private sealed class FolderMeta
        {
            public string Name { get; set; } = "새 폴더";
            public List<FolderEntry> Entries { get; set; } = new();
            public int ColumnsOverride { get; set; } = 0;   // 0 = 자동(1..5)
            public int RowsOverride { get; set; } = 0;       // 0 = 자동(1..4 가시), >0 = 강제 가시 행 수
            public string SortMode { get; set; } = "None";   // None, NameAsc, NameDesc, FoldersFirst

            public Window? OpenWindow { get; set; }
            public FolderMeta? RuntimeParent { get; set; }
            public System.Windows.Media.ScaleTransform? OpenScale { get; set; }
            public bool IsClosing { get; set; }
            // 열린 폴더창의 내부 캔버스/스크롤뷰어. 항목 추가/삭제 시 창을 다시 열지 않고 in-place 갱신용.
            public System.Windows.Controls.Canvas? OpenInner { get; set; }
            public System.Windows.Controls.ScrollViewer? OpenScrollViewer { get; set; }
        }

        private sealed class FolderEntry
        {
            public ButtonState? Button { get; set; }
            public FolderMeta? SubFolder { get; set; }
            // 폴더 내 셀 좌표. -1이면 자동 배치 (비어 있는 첫 셀에 들어감).
            // 정렬 모드가 None일 때만 이 좌표가 적용됨.
            public int CellCol { get; set; } = -1;
            public int CellRow { get; set; } = -1;
            public bool IsFolder => SubFolder != null;
        }

        // 캔버스 버튼이 Shift 드래그로 폴더 위에 떨궈졌으면 폴더로 이동
        // 기존 AttachDragHandlers의 MouseUp에서 호출됨
        private void TryDropCanvasButtonOnFolder(System.Windows.Controls.Button btn, System.Windows.Controls.Canvas canvas, ButtonMeta meta)
        {
            if (btn.Tag is FolderMeta) return; // 폴더 버튼 자신은 무시 (현재는 캔버스 폴더 버튼은 별도 드래그 핸들러 사용)

            // 버튼의 현재 위치(좌상단 + 중앙)
            double x = System.Windows.Controls.Canvas.GetLeft(btn);
            double y = System.Windows.Controls.Canvas.GetTop(btn);
            double cx = x + btn.ActualWidth / 2;
            double cy = y + btn.ActualHeight / 2;

            // 같은 캔버스의 폴더 버튼들 중 중심이 폴더 영역에 들어간 첫 번째 폴더로 이동
            foreach (UIElement child in canvas.Children)
            {
                if (child is System.Windows.Controls.Button fb && fb != btn && fb.Tag is FolderMeta fm)
                {
                    double fx = System.Windows.Controls.Canvas.GetLeft(fb);
                    double fy = System.Windows.Controls.Canvas.GetTop(fb);
                    double fw = fb.ActualWidth > 0 ? fb.ActualWidth : fb.Width;
                    double fh = fb.ActualHeight > 0 ? fb.ActualHeight : fb.Height;
                    if (cx >= fx && cx <= fx + fw && cy >= fy && cy <= fy + fh)
                    {
                        MoveCanvasButtonIntoFolder(btn, canvas, meta, fm);
                        return;
                    }
                }
            }
        }

        // ===== 일반 버튼에 "폴더로 이동" 메뉴 추가 =====
        private void AddMoveToFolderMenu(System.Windows.Controls.Button btn, System.Windows.Controls.Canvas canvas, ButtonMeta meta)
        {
            if (btn.ContextMenu == null) btn.ContextMenu = new ContextMenu();
            var mi = new MenuItem { Header = "폴더로 이동" };
            mi.SubmenuOpened += (_, __) =>
            {
                mi.Items.Clear();
                var folders = GetCanvasFolders(canvas);
                if (folders.Count == 0)
                {
                    var empty = new MenuItem { Header = "(폴더가 없습니다)", IsEnabled = false };
                    mi.Items.Add(empty);
                    return;
                }
                foreach (var f in folders)
                {
                    var sub = new MenuItem { Header = f.Name };
                    var targetFolder = f;
                    sub.Click += (_, _) => MoveCanvasButtonIntoFolder(btn, canvas, meta, targetFolder);
                    mi.Items.Add(sub);
                }
            };
            // 빈 placeholder (SubmenuOpened가 발생하려면 자식이 필요)
            mi.Items.Add(new MenuItem { Header = "(불러오는 중)" });
            btn.ContextMenu.Items.Add(mi);
        }

        // 캔버스 상의 폴더(FolderMeta) 모두 가져오기
        private List<FolderMeta> GetCanvasFolders(System.Windows.Controls.Canvas canvas)
        {
            var list = new List<FolderMeta>();
            foreach (UIElement child in canvas.Children)
            {
                if (child is System.Windows.Controls.Button b && b.Tag is FolderMeta fm)
                    list.Add(fm);
            }
            return list;
        }

        // 캔버스의 버튼을 폴더로 이동
        private void MoveCanvasButtonIntoFolder(System.Windows.Controls.Button btn, System.Windows.Controls.Canvas canvas, ButtonMeta meta, FolderMeta folder)
        {
            var state = BuildButtonStateFromButton(btn, meta, canvasIndex: GetCanvasIndex(canvas));
            // 폴더 안에서는 크기 고정
            state.Width = FolderCellSize;
            state.Height = FolderCellSize;
            state.X = 0;
            state.Y = 0;

            // 라벨이 폴더창에서는 안쪽 강제
            state.LabelInside = true;

            folder.Entries.Add(new FolderEntry { Button = state });

            // 캔버스에서 제거 (라벨 블록도 같이)
            if (meta.LabelBlock != null && canvas.Children.Contains(meta.LabelBlock))
                canvas.Children.Remove(meta.LabelBlock);
            canvas.Children.Remove(btn);

            // 열려 있으면 갱신
            RefreshOpenFolderWindow(folder);

            SaveAllButtonStates();
            SaveAllFolderStates();
        }

        // 기존 ButtonState 객체에 현재 UI/메타 상태를 다시 덮어쓰기 (폴더 내부 자식 동기화용)
        private void SyncStateFromButton(ButtonState state, System.Windows.Controls.Button btn, ButtonMeta meta)
        {
            var img = GetButtonImageControl(btn);
            state.Content = btn.Content is string s ? s : null;
            state.ImagePath = img != null && img.Source is BitmapImage bmp ? bmp.UriSource?.LocalPath : state.ImagePath;
            state.Path = meta.Path;
            state.IsFolder = meta.IsFolder;
            state.LabelText = meta.LabelText;
            state.ImageWidth = img?.Width ?? state.ImageWidth;
            state.ImageHeight = img?.Height ?? state.ImageHeight;
            state.ImageHAlign = img?.HorizontalAlignment.ToString() ?? state.ImageHAlign;
            state.ImageVAlign = img?.VerticalAlignment.ToString() ?? state.ImageVAlign;
            state.LabelInside = meta.LabelInside;
            state.FontFamily = btn.FontFamily?.Source;
            state.FontSize = btn.FontSize;
            state.FontWeightName = btn.FontWeight.ToString();
            state.Italic = btn.FontStyle == FontStyles.Italic;
            state.FontColor = btn.ReadLocalValue(System.Windows.Controls.Control.ForegroundProperty) != DependencyProperty.UnsetValue
                ? (btn.Foreground as SolidColorBrush)?.Color.ToString() : null;
            state.BgColor = btn.ReadLocalValue(System.Windows.Controls.Control.BackgroundProperty) != DependencyProperty.UnsetValue
                ? ((btn.Background as SolidColorBrush)?.Color.A == 0 ? "transparent" : (btn.Background as SolidColorBrush)?.Color.ToString())
                : null;
            state.CustomFontFamily = btn.ReadLocalValue(System.Windows.Controls.Control.FontFamilyProperty) != DependencyProperty.UnsetValue
                ? btn.FontFamily?.Source : null;
        }

        // 현재 캔버스 버튼의 상태를 ButtonState로 직렬화
        private ButtonState BuildButtonStateFromButton(System.Windows.Controls.Button btn, ButtonMeta meta, int canvasIndex)
        {
            var img = GetButtonImageControl(btn);
            return new ButtonState
            {
                X = System.Windows.Controls.Canvas.GetLeft(btn),
                Y = System.Windows.Controls.Canvas.GetTop(btn),
                Width = btn.Width,
                Height = btn.Height,
                Content = btn.Content is string s ? s : null,
                ImagePath = img != null && img.Source is BitmapImage bmp ? bmp.UriSource?.LocalPath : null,
                CanvasIndex = canvasIndex,
                Path = meta.Path,
                IsFolder = meta.IsFolder,
                LabelText = meta.LabelText,
                ImageWidth = img?.Width ?? 0,
                ImageHeight = img?.Height ?? 0,
                ImageHAlign = img?.HorizontalAlignment.ToString(),
                ImageVAlign = img?.VerticalAlignment.ToString(),
                LabelInside = meta.LabelInside,
                FontFamily = btn.FontFamily?.Source,
                FontSize = btn.FontSize,
                FontWeightName = btn.FontWeight.ToString(),
                Italic = btn.FontStyle == FontStyles.Italic,
                FontColor = (btn.Foreground as SolidColorBrush)?.Color.ToString(),
                BgColor = (btn.Background as SolidColorBrush)?.Color.A == 0
                    ? "transparent"
                    : (btn.Background as SolidColorBrush)?.Color.ToString(),
                CustomFontFamily = btn.FontFamily?.Source,
                BackgroundTransparent = false
            };
        }

        private int GetCanvasIndex(System.Windows.Controls.Canvas canvas)
        {
            if (canvas == null || tabControl == null) return 0;
            for (int i = 0; i < tabControl.Items.Count; i++)
            {
                if (GetCanvasByIndex(i) == canvas) return i;
            }
            return 0;
        }

        // 폴더가 열려있다면 내용 갱신.
        // Why: 기존엔 close→reopen 방식이라 열기/닫기 애니메이션이 모두 발생해 깜빡임이 보였음.
        // How: OpenInner/OpenScrollViewer를 보관해두고 children만 재구성 + 창/캔버스/스크롤뷰 크기만 보정.
        private void RefreshOpenFolderWindow(FolderMeta folder)
        {
            if (folder.OpenWindow == null || !folder.OpenWindow.IsVisible) return;
            if (folder.OpenInner == null)
            {
                // 보관된 inner가 없으면 안전망: 기존 방식대로 close→reopen.
                var w = folder.OpenWindow;
                var l = w.Left; var t = w.Top;
                folder.OpenWindow = null;
                w.Close();
                ShowFolderWindowAt(folder, l, t, folder.RuntimeParent);
                return;
            }

            int columns = ResolveColumns(folder);
            int rowsTotal = Math.Max(1, (int)Math.Ceiling((double)Math.Max(folder.Entries.Count, 1) / columns));
            int rowsVisible = ResolveVisibleRows(folder, rowsTotal);
            double cellsW = columns * FolderCellSize + Math.Max(0, columns - 1) * FolderCellGap;
            double cellsH = rowsVisible * FolderCellSize + Math.Max(0, rowsVisible - 1) * FolderCellGap;
            double winW = cellsW + FolderInnerPadding * 2 + 2;
            double winH = cellsH + FolderInnerPadding * 2 + 2 + FolderHeaderHeight;
            double canvasH = rowsTotal * FolderCellSize + Math.Max(0, rowsTotal - 1) * FolderCellGap;

            folder.OpenInner.Width = cellsW;
            folder.OpenInner.Height = Math.Max(canvasH, cellsH);
            if (folder.OpenScrollViewer != null)
            {
                folder.OpenScrollViewer.Width = cellsW;
                folder.OpenScrollViewer.Height = cellsH;
            }
            folder.OpenWindow.Width = winW;
            folder.OpenWindow.Height = winH;

            ApplyFolderSort(folder);
            LayoutFolderContents(folder, folder.OpenInner, columns);
        }

        // ===== 폴더 생성 (캔버스 우클릭 메뉴 핸들러) =====
        private void CreateFolderInBorder_Click(object sender, RoutedEventArgs e)
        {
            var canvas = GetCanvasFromContextMenuSender(sender) ?? CurrentButtonCanvas;
            if (canvas == null) return;

            var folder = new FolderMeta { Name = "새 폴더" };

            // 우클릭한 마우스 위치를 중심으로 배치. 캔버스 경계로 클램프.
            const double bw = FolderCellSize, bh = FolderCellSize;
            double x, y;
            if (_lastCanvasContextMenuPointValid)
            {
                x = _lastCanvasContextMenuPoint.X - bw / 2;
                y = _lastCanvasContextMenuPoint.Y - bh / 2;
            }
            else { x = 5; y = 5; }
            double maxX = Math.Max(0, canvas.Width - bw);
            double maxY = Math.Max(0, canvas.Height - bh);
            x = Math.Max(0, Math.Min(maxX, x));
            y = Math.Max(0, Math.Min(maxY, y));

            var btn = BuildFolderCanvasButton(canvas, folder);
            System.Windows.Controls.Canvas.SetLeft(btn, x);
            System.Windows.Controls.Canvas.SetTop(btn, y);
            canvas.Children.Add(btn);

            SaveAllFolderStates();
        }

        // ===== 캔버스 위 폴더 버튼 생성 =====
        private System.Windows.Controls.Button BuildFolderCanvasButton(System.Windows.Controls.Canvas canvas, FolderMeta folder)
        {
            var btn = new System.Windows.Controls.Button
            {
                Width = FolderCellSize,
                Height = FolderCellSize,
                Tag = folder,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ContextMenu = new ContextMenu(),
                Style = FindResource("DynamicButtonStyle") as Style,
            };

            ApplyFolderButtonVisual(btn, folder);

            btn.Click += (s, ev) =>
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return; // Shift 드래그 중엔 무시
                var mouse = Forms.Cursor.Position;
                ShowFolderWindow(folder, mouse, parent: null);
            };

            BuildFolderContextMenu(btn, folder, canvas, parent: null);

            AttachCanvasFolderDragHandlers(btn, canvas);

            return btn;
        }

        // 폴더 버튼의 시각적 콘텐츠 (아이콘 + 이름)
        private void ApplyFolderButtonVisual(System.Windows.Controls.Button btn, FolderMeta folder)
        {
            var grid = new Grid();
            var icon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 2,16 L 16,16 L 20,11 L 50,11 L 50,46 L 2,46 Z"),
                Stretch = Stretch.Uniform,
                Margin = new Thickness(8, 4, 8, 14),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            icon.SetResourceReference(System.Windows.Shapes.Path.FillProperty, "TabAccentBrush");

            var nameTb = new TextBlock
            {
                Text = folder.Name,
                FontSize = 9,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(2, 0, 2, 2),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = FolderCellSize - 4,
                IsHitTestVisible = false,
            };
            nameTb.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundBrush");

            grid.Children.Add(icon);
            grid.Children.Add(nameTb);
            btn.Content = grid;
        }

        // 캔버스 폴더 버튼 드래그 (Shift + 좌클릭 드래그)
        private void AttachCanvasFolderDragHandlers(System.Windows.Controls.Button btn, System.Windows.Controls.Canvas canvas)
        {
            bool shiftStart = false;
            bool dragging = false;
            System.Windows.Point startPt = default;
            System.Windows.Point origin = default;

            btn.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { shiftStart = false; return; }
                shiftStart = true;
                startPt = e.GetPosition(canvas);
                origin = new System.Windows.Point(System.Windows.Controls.Canvas.GetLeft(btn), System.Windows.Controls.Canvas.GetTop(btn));
                dragging = false;
                btn.CaptureMouse();
                e.Handled = true;
            };
            btn.PreviewMouseMove += (s, e) =>
            {
                if (!shiftStart || !btn.IsMouseCaptured) return;
                var cur = e.GetPosition(canvas);
                var diff = cur - startPt;
                if (!dragging && diff.Length > 3) dragging = true;
                if (!dragging) return;
                double nx = origin.X + diff.X;
                double ny = origin.Y + diff.Y;
                nx = Math.Round(nx / GridSize) * GridSize;
                ny = Math.Round(ny / GridSize) * GridSize;
                double maxX = canvas.ActualWidth - btn.ActualWidth;
                double maxY = canvas.ActualHeight - btn.ActualHeight;
                if (maxX > 0) nx = Math.Max(0, Math.Min(nx, maxX));
                if (maxY > 0) ny = Math.Max(0, Math.Min(ny, maxY));
                System.Windows.Controls.Canvas.SetLeft(btn, nx);
                System.Windows.Controls.Canvas.SetTop(btn, ny);
                e.Handled = true;
            };
            btn.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (!shiftStart) return;
                if (btn.IsMouseCaptured) btn.ReleaseMouseCapture();
                if (dragging)
                {
                    SaveAllFolderStates();
                    e.Handled = true;
                }
                dragging = false;
                shiftStart = false;
            };
        }

        // ===== 폴더 우클릭 메뉴 =====
        private void BuildFolderContextMenu(System.Windows.Controls.Button btn, FolderMeta folder, System.Windows.Controls.Canvas? hostCanvas, FolderMeta? parent)
        {
            if (btn.ContextMenu == null) btn.ContextMenu = new ContextMenu();
            btn.ContextMenu.Items.Clear();

            var editMi = new MenuItem { Header = "폴더 수정" };
            editMi.Click += (_, _) => ShowFolderEditDialog(folder, btn, hostCanvas, parent);

            var delMi = new MenuItem { Header = "폴더 삭제" };
            delMi.Click += (_, _) => DeleteFolderWithConfirm(folder, btn, hostCanvas, parent);

            btn.ContextMenu.Items.Add(editMi);
            btn.ContextMenu.Items.Add(delMi);
        }

        // ===== 폴더 수정 다이얼로그 (이름/강제 크기/정렬) =====
        private void ShowFolderEditDialog(FolderMeta folder, System.Windows.Controls.Button btn, System.Windows.Controls.Canvas? hostCanvas, FolderMeta? parent)
        {
            var dlg = new Window
            {
                Title = "폴더 수정",
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight
            };
            MakeBorderless(dlg);

            var stack = new StackPanel { Margin = new Thickness(16), MinWidth = 280 };

            stack.Children.Add(new TextBlock { Text = "이름:", Margin = new Thickness(0, 0, 0, 4) });
            var nameBox = new System.Windows.Controls.TextBox { Text = folder.Name, Margin = new Thickness(0, 0, 0, 10) };
            stack.Children.Add(nameBox);

            stack.Children.Add(new TextBlock { Text = "열 수 (격자, 0=자동, 최대 5):", Margin = new Thickness(0, 0, 0, 4) });
            var colBox = new System.Windows.Controls.TextBox { Text = folder.ColumnsOverride.ToString(), Margin = new Thickness(0, 0, 0, 10) };
            stack.Children.Add(colBox);

            stack.Children.Add(new TextBlock { Text = "가시 행 수 (격자, 0=자동/최대 4 후 스크롤):", Margin = new Thickness(0, 0, 0, 4) });
            var rowBox = new System.Windows.Controls.TextBox { Text = folder.RowsOverride.ToString(), Margin = new Thickness(0, 0, 0, 10) };
            stack.Children.Add(rowBox);

            stack.Children.Add(new TextBlock { Text = "정렬:", Margin = new Thickness(0, 0, 0, 4) });
            var sortCombo = new System.Windows.Controls.ComboBox { Margin = new Thickness(0, 0, 0, 12) };
            sortCombo.Items.Add(new ComboBoxItem { Content = "사용 안 함", Tag = "None" });
            sortCombo.Items.Add(new ComboBoxItem { Content = "이름 오름차순", Tag = "NameAsc" });
            sortCombo.Items.Add(new ComboBoxItem { Content = "이름 내림차순", Tag = "NameDesc" });
            sortCombo.Items.Add(new ComboBoxItem { Content = "폴더 먼저", Tag = "FoldersFirst" });
            foreach (ComboBoxItem it in sortCombo.Items)
                if ((string)it.Tag == folder.SortMode) { sortCombo.SelectedItem = it; break; }
            if (sortCombo.SelectedItem == null) sortCombo.SelectedIndex = 0;
            stack.Children.Add(sortCombo);

            var btnPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var okBtn = new System.Windows.Controls.Button { Content = "적용", Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
            var cancelBtn = new System.Windows.Controls.Button { Content = "취소", Width = 80, Height = 30 };
            okBtn.Click += (_, _) =>
            {
                folder.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? folder.Name : nameBox.Text.Trim();
                if (int.TryParse(colBox.Text, out var c) && c >= 0 && c <= FolderAutoMaxColumns) folder.ColumnsOverride = c;
                if (int.TryParse(rowBox.Text, out var r) && r >= 0) folder.RowsOverride = r;
                folder.SortMode = (sortCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "None";

                // 폴더 버튼의 이름 텍스트 갱신
                ApplyFolderButtonVisual(btn, folder);
                RefreshOpenFolderWindow(folder);
                SaveAllFolderStates();
                dlg.Close();
            };
            cancelBtn.Click += (_, _) => dlg.Close();
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            stack.Children.Add(btnPanel);

            dlg.Content = stack;
            ApplyDarkTheme(dlg);
            dlg.ShowDialog();
        }

        // ===== 폴더 삭제 (확인 다이얼로그) =====
        private void DeleteFolderWithConfirm(FolderMeta folder, System.Windows.Controls.Button btn, System.Windows.Controls.Canvas? hostCanvas, FolderMeta? parent)
        {
            var dlg = new Window
            {
                Title = "폴더 삭제 확인",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight
            };
            MakeBorderless(dlg);
            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock { Text = $"폴더 \"{folder.Name}\"와 내부 항목을 모두 삭제하시겠습니까?", Margin = new Thickness(0, 0, 0, 12), FontWeight = FontWeights.Bold });
            var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var yesBtn = new System.Windows.Controls.Button { Content = "예", Margin = new Thickness(0, 0, 8, 0), Width = 80, Height = 30 };
            var noBtn = new System.Windows.Controls.Button { Content = "아니오", Width = 80, Height = 30 };
            yesBtn.Click += (_, _) =>
            {
                dlg.Close();
                // 열려 있으면 닫기 (자식까지 재귀)
                CloseFolderTree(folder);
                if (parent != null)
                {
                    parent.Entries.RemoveAll(en => en.SubFolder == folder);
                    RefreshOpenFolderWindow(parent);
                }
                else if (hostCanvas != null)
                {
                    hostCanvas.Children.Remove(btn);
                }
                SaveAllFolderStates();
            };
            noBtn.Click += (_, _) => dlg.Close();
            panel.Children.Add(yesBtn);
            panel.Children.Add(noBtn);
            stack.Children.Add(panel);
            dlg.Content = stack;
            ApplyDarkTheme(dlg);
            dlg.ShowDialog();
        }

        private void CloseFolderTree(FolderMeta folder)
        {
            foreach (var e in folder.Entries)
                if (e.SubFolder != null) CloseFolderTree(e.SubFolder);
            if (folder.OpenWindow != null) AnimateAndCloseFolderWindow(folder);
        }

        // ===== 폴더 창 열기 =====
        private void ShowFolderWindow(FolderMeta folder, System.Drawing.Point mouseScreenPos, FolderMeta? parent)
        {
            // 위치 계산은 ShowFolderWindowAt에 위임
            int columns = ResolveColumns(folder);
            int rowsTotal = Math.Max(1, (int)Math.Ceiling((double)Math.Max(folder.Entries.Count, 1) / columns));
            int rowsVisible = ResolveVisibleRows(folder, rowsTotal);
            double cellsW = columns * FolderCellSize + Math.Max(0, columns - 1) * FolderCellGap;
            double cellsH = rowsVisible * FolderCellSize + Math.Max(0, rowsVisible - 1) * FolderCellGap;
            double winW = cellsW + FolderInnerPadding * 2 + 2;
            double winH = cellsH + FolderInnerPadding * 2 + 2 + FolderHeaderHeight;

            var screen = Forms.Screen.FromPoint(mouseScreenPos).WorkingArea;
            double left = mouseScreenPos.X + 4;       // 오른쪽
            double top = mouseScreenPos.Y + 4;        // 기본: 아래
            // 화면 밖이면 위로 반전
            if (top + winH > screen.Bottom) top = mouseScreenPos.Y - winH - 4;
            if (left + winW > screen.Right) left = mouseScreenPos.X - winW - 4;
            if (left < screen.Left) left = screen.Left + 2;
            if (top < screen.Top) top = screen.Top + 2;

            ShowFolderWindowAt(folder, left, top, parent);
        }

        private void ShowFolderWindowAt(FolderMeta folder, double left, double top, FolderMeta? parent)
        {
            if (folder.OpenWindow != null && folder.OpenWindow.IsVisible) { folder.OpenWindow.Activate(); return; }

            folder.RuntimeParent = parent;

            int columns = ResolveColumns(folder);
            int rowsTotal = Math.Max(1, (int)Math.Ceiling((double)Math.Max(folder.Entries.Count, 1) / columns));
            int rowsVisible = ResolveVisibleRows(folder, rowsTotal);
            double cellsW = columns * FolderCellSize + Math.Max(0, columns - 1) * FolderCellGap;
            double cellsH = rowsVisible * FolderCellSize + Math.Max(0, rowsVisible - 1) * FolderCellGap;
            double winW = cellsW + FolderInnerPadding * 2 + 2;
            double winH = cellsH + FolderInnerPadding * 2 + 2 + FolderHeaderHeight;

            var win = new Window
            {
                Width = winW,
                Height = winH,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = left,
                Top = top,
                Owner = this,
                // Why no Topmost: Topmost 부모 안에서는 자식 ContextMenu popup이 z-order에서 밀려 폴더창 뒤로 깔림.
                // Owner=this 관계로 이미 MainWindow 위에 표시되며, 다른 앱이 활성화되면 Deactivated에서 자동 닫힘.
                Tag = folder,
                Focusable = false,
            };

            var rootBd = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(FolderInnerPadding),
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            };
            rootBd.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
            rootBd.SetResourceReference(Border.BorderBrushProperty, "StatusBarBorderBrush");
            var openScale = new System.Windows.Media.ScaleTransform(0.82, 0.82);
            rootBd.RenderTransform = openScale;

            var dock = new DockPanel();
            var header = new TextBlock
            {
                Text = folder.Name,
                FontSize = 11,
                Margin = new Thickness(2, 0, 2, 4),
                FontWeight = FontWeights.SemiBold,
                Height = FolderHeaderHeight - 4,
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundBrush");
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);

            double totalRows = Math.Max(1, (int)Math.Ceiling((double)Math.Max(folder.Entries.Count, 1) / columns));
            double canvasH = totalRows * FolderCellSize + Math.Max(0, totalRows - 1) * FolderCellGap;
            var inner = new System.Windows.Controls.Canvas { Width = cellsW, Height = Math.Max(canvasH, cellsH) };

            var sv = new System.Windows.Controls.ScrollViewer
            {
                Content = inner,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                Width = cellsW,
                Height = cellsH,
            };
            // 슬림 스크롤바 적용
            if (System.Windows.Application.Current.Resources["SlimScrollBarStyle"] is Style slim)
                sv.Resources[typeof(ScrollBar)] = slim;

            dock.Children.Add(sv);
            rootBd.Child = dock;
            win.Content = rootBd;

            // 우클릭(빈 영역)으로 폴더창 자체 메뉴 열기
            inner.Background = System.Windows.Media.Brushes.Transparent;
            inner.ContextMenu = new ContextMenu();
            var addBtnMi = new MenuItem { Header = "버튼 추가" };
            addBtnMi.Click += (_, _) => AddNewButtonToFolder(folder);
            var addFolderMi = new MenuItem { Header = "폴더 추가" };
            addFolderMi.Click += (_, _) => AddNewSubFolderToFolder(folder);
            var editFolderMi = new MenuItem { Header = "폴더 수정" };
            editFolderMi.Click += (_, _) => ShowFolderEditDialog(folder, /*btn*/null!, hostCanvas: null, parent: parent);
            inner.ContextMenu.Items.Add(addBtnMi);
            inner.ContextMenu.Items.Add(addFolderMi);
            inner.ContextMenu.Items.Add(editFolderMi);

            ApplyFolderSort(folder);
            LayoutFolderContents(folder, inner, columns);

            folder.OpenWindow = win;
            folder.OpenScale = openScale;
            folder.IsClosing = false;
            folder.OpenInner = inner;
            folder.OpenScrollViewer = sv;

            // 닫힘 처리
            win.MouseLeave += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() => TryCloseFolderWindow(folder)), DispatcherPriority.Background);
            };
            win.Deactivated += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() => TryCloseFolderWindow(folder)), DispatcherPriority.Background);
            };
            win.Closed += (_, _) =>
            {
                folder.OpenWindow = null;
                folder.OpenInner = null;
                folder.OpenScrollViewer = null;
                folder.OpenScale = null;
                // 부모도 자기 영역에 마우스가 없고 자식이 없다면 닫힘 시도
                var p = folder.RuntimeParent;
                if (p?.OpenWindow != null)
                {
                    Dispatcher.BeginInvoke(new Action(() => TryCloseFolderWindow(p)), DispatcherPriority.Background);
                }
            };

            // 폴더창 열기 애니메이션 (페이드인 + 살짝 확대)
            win.Opacity = 0;
            win.Show();
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            var scaleUp = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.82,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            win.BeginAnimation(Window.OpacityProperty, fadeIn);
            openScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleUp);
            openScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleUp);
        }

        // 폴더창 닫기 시도 (자식이 열려있거나 자식 트리 안에 마우스가 있으면 닫지 않음)
        private void TryCloseFolderWindow(FolderMeta folder)
        {
            if (folder.OpenWindow == null) return;
            // 자식 트리에 마우스가 있으면 닫지 않음
            if (IsMouseOverFolderTree(folder)) return;
            // 자식 폴더 창이 살아있으면 닫지 않음
            if (HasOpenDescendantWindow(folder)) return;
            // 컨텍스트 메뉴/팝업이 열려있으면 닫지 않음
            if (HasOpenPopupInside(folder.OpenWindow)) return;
            AnimateAndCloseFolderWindow(folder);
        }

        // 폴더창을 페이드아웃 + 축소 애니메이션 후 닫기
        private void AnimateAndCloseFolderWindow(FolderMeta folder)
        {
            var win = folder.OpenWindow;
            if (win == null) return;
            if (folder.IsClosing) return;
            folder.IsClosing = true;

            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = win.Opacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(140),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };
            fadeOut.Completed += (_, _) =>
            {
                try { win.Close(); } catch { }
            };

            var scale = folder.OpenScale;
            if (scale != null)
            {
                var scaleDown = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = scale.ScaleX,
                    To = 0.82,
                    Duration = TimeSpan.FromMilliseconds(140),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
                };
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleDown);
                scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleDown);
            }

            win.BeginAnimation(Window.OpacityProperty, fadeOut);
        }

        private static bool IsMouseOverFolderTree(FolderMeta folder)
        {
            if (folder.OpenWindow != null && IsMouseOverWindow(folder.OpenWindow)) return true;
            foreach (var e in folder.Entries)
                if (e.SubFolder != null && IsMouseOverFolderTree(e.SubFolder)) return true;
            return false;
        }

        private static bool IsMouseOverWindow(Window w)
        {
            if (w == null || !w.IsVisible) return false;
            var p = Forms.Cursor.Position;
            return p.X >= w.Left && p.X <= w.Left + w.ActualWidth
                && p.Y >= w.Top && p.Y <= w.Top + w.ActualHeight;
        }

        private static bool HasOpenDescendantWindow(FolderMeta folder)
        {
            foreach (var e in folder.Entries)
            {
                if (e.SubFolder == null) continue;
                if (e.SubFolder.OpenWindow != null) return true;
                if (HasOpenDescendantWindow(e.SubFolder)) return true;
            }
            return false;
        }

        private static bool HasOpenPopupInside(Window? w)
        {
            if (w == null) return false;
            // 활성 ContextMenu/Popup 검출: Application.Current.Windows에 떠 있는 ContextMenuRoot/Popup용 Window 존재 여부로 대체
            foreach (Window other in System.Windows.Application.Current.Windows)
            {
                if (other == w) continue;
                if (other.Owner == w) return true;
            }
            return false;
        }

        // ===== 자동 열/행 계산 =====
        private int ResolveColumns(FolderMeta folder)
        {
            if (folder.ColumnsOverride > 0)
                return Math.Min(folder.ColumnsOverride, FolderAutoMaxColumns);
            int items = Math.Max(folder.Entries.Count, 1);
            return Math.Min(items, FolderAutoMaxColumns);
        }

        private int ResolveVisibleRows(FolderMeta folder, int rowsTotal)
        {
            if (folder.RowsOverride > 0) return folder.RowsOverride;
            return Math.Min(Math.Max(rowsTotal, 1), FolderAutoMaxVisibleRows);
        }

        // ===== 정렬 적용 =====
        private void ApplyFolderSort(FolderMeta folder)
        {
            switch (folder.SortMode)
            {
                case "NameAsc":
                    folder.Entries.Sort((a, b) => string.Compare(EntryName(a), EntryName(b), StringComparison.OrdinalIgnoreCase));
                    break;
                case "NameDesc":
                    folder.Entries.Sort((a, b) => string.Compare(EntryName(b), EntryName(a), StringComparison.OrdinalIgnoreCase));
                    break;
                case "FoldersFirst":
                    folder.Entries.Sort((a, b) => b.IsFolder.CompareTo(a.IsFolder));
                    break;
            }
        }

        private static string EntryName(FolderEntry e)
        {
            if (e.SubFolder != null) return e.SubFolder.Name ?? "";
            if (e.Button != null) return e.Button.LabelText ?? e.Button.Content ?? e.Button.Path ?? "";
            return "";
        }

        // ===== 폴더 내부 항목 배치 =====
        private void LayoutFolderContents(FolderMeta folder, System.Windows.Controls.Canvas inner, int columns)
        {
            inner.Children.Clear();

            bool freeMode = folder.SortMode == "None";

            // 셀 점유 맵 (자동 배치를 위해)
            var occupied = new HashSet<(int col, int row)>();
            int maxRow = 0;

            if (freeMode)
            {
                // 좌표가 지정된 항목 먼저 점유 표시
                foreach (var en in folder.Entries)
                {
                    if (en.CellCol >= 0 && en.CellRow >= 0 && en.CellCol < columns)
                    {
                        occupied.Add((en.CellCol, en.CellRow));
                        if (en.CellRow > maxRow) maxRow = en.CellRow;
                    }
                }
            }

            for (int i = 0; i < folder.Entries.Count; i++)
            {
                var entry = folder.Entries[i];
                int col, row;

                if (freeMode && entry.CellCol >= 0 && entry.CellRow >= 0 && entry.CellCol < columns)
                {
                    col = entry.CellCol;
                    row = entry.CellRow;
                }
                else if (freeMode)
                {
                    // 비어 있는 첫 셀 자동 탐색
                    (col, row) = FindFirstEmptyCell(occupied, columns);
                    entry.CellCol = col;
                    entry.CellRow = row;
                    occupied.Add((col, row));
                    if (row > maxRow) maxRow = row;
                }
                else
                {
                    // 정렬 모드: 인덱스 순서 그대로 배치 (좌상단부터 채움)
                    col = i % columns;
                    row = i / columns;
                    if (row > maxRow) maxRow = row;
                }

                double x = col * (FolderCellSize + FolderCellGap);
                double y = row * (FolderCellSize + FolderCellGap);

                System.Windows.Controls.Button btn;
                if (entry.SubFolder != null)
                {
                    btn = BuildSubFolderButton(inner, entry.SubFolder, folder);
                }
                else if (entry.Button != null)
                {
                    btn = BuildChildButtonFromState(entry.Button, folder, inner);
                }
                else continue;

                System.Windows.Controls.Canvas.SetLeft(btn, x);
                System.Windows.Controls.Canvas.SetTop(btn, y);
                AttachFolderInnerDragHandlers(btn, inner, folder, entry);
                inner.Children.Add(btn);
            }

            int totalRows = Math.Max(1, maxRow + 1);
            double cellsH = totalRows * FolderCellSize + Math.Max(0, totalRows - 1) * FolderCellGap;
            int rowsVisible = ResolveVisibleRows(folder, totalRows);
            double visH = rowsVisible * FolderCellSize + Math.Max(0, rowsVisible - 1) * FolderCellGap;
            inner.Height = Math.Max(cellsH, visH);
        }

        // 빈 셀을 좌→우, 위→아래 순서로 탐색
        private static (int col, int row) FindFirstEmptyCell(HashSet<(int, int)> occupied, int columns)
        {
            for (int r = 0; r < 10000; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    if (!occupied.Contains((c, r))) return (c, r);
                }
            }
            return (0, 0);
        }

        // 폴더창 내부의 자식 폴더 버튼
        private System.Windows.Controls.Button BuildSubFolderButton(System.Windows.Controls.Canvas inner, FolderMeta subFolder, FolderMeta parent)
        {
            var btn = new System.Windows.Controls.Button
            {
                Width = FolderCellSize,
                Height = FolderCellSize,
                Tag = subFolder,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ContextMenu = new ContextMenu(),
                Style = FindResource("DynamicButtonStyle") as Style,
            };
            ApplyFolderButtonVisual(btn, subFolder);

            btn.Click += (_, _) =>
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
                var mouse = Forms.Cursor.Position;
                ShowFolderWindow(subFolder, mouse, parent);
            };

            // 우클릭 메뉴: 수정/삭제 + 상위로 빼기
            var editMi = new MenuItem { Header = "폴더 수정" };
            editMi.Click += (_, _) => ShowFolderEditDialog(subFolder, btn, hostCanvas: null, parent: parent);
            var delMi = new MenuItem { Header = "폴더 삭제" };
            delMi.Click += (_, _) =>
            {
                CloseFolderTree(subFolder);
                parent.Entries.RemoveAll(en => en.SubFolder == subFolder);
                RefreshOpenFolderWindow(parent);
                SaveAllFolderStates();
            };
            btn.ContextMenu.Items.Add(editMi);
            btn.ContextMenu.Items.Add(delMi);

            return btn;
        }

        // 폴더창 내부의 자식 일반 버튼
        private System.Windows.Controls.Button BuildChildButtonFromState(ButtonState state, FolderMeta folder, System.Windows.Controls.Canvas hostCanvas)
        {
            // 크기 고정
            state.Width = FolderCellSize;
            state.Height = FolderCellSize;
            // 텍스트가 있는 경우에만 안쪽 라벨 모드. 텍스트 없으면 이미지가 중앙에 오도록 false로.
            state.LabelInside = !string.IsNullOrWhiteSpace(state.LabelText);

            var btn = new System.Windows.Controls.Button
            {
                Width = FolderCellSize,
                Height = FolderCellSize,
                Content = state.Content ?? "",
                ContextMenu = new ContextMenu(),
                Style = FindResource("DynamicButtonStyle") as Style,
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };

            // 폰트/색 복원
            try
            {
                if (!string.IsNullOrWhiteSpace(state.FontFamily))
                    btn.FontFamily = new System.Windows.Media.FontFamily(state.FontFamily);
                if (state.FontSize > 0) btn.FontSize = state.FontSize;
                if (!string.IsNullOrWhiteSpace(state.FontWeightName))
                {
                    try
                    {
                        var fwConv = new FontWeightConverter();
                        var fw = (FontWeight)fwConv.ConvertFromString(state.FontWeightName)!;
                        btn.FontWeight = fw;
                    }
                    catch { }
                }
                btn.FontStyle = state.Italic ? FontStyles.Italic : FontStyles.Normal;
                if (!string.IsNullOrWhiteSpace(state.FontColor))
                {
                    try
                    {
                        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(state.FontColor)!;
                        btn.Foreground = new SolidColorBrush(c);
                    }
                    catch { }
                }
                if (state.BgColor == "transparent")
                    btn.Background = System.Windows.Media.Brushes.Transparent;
                else if (!string.IsNullOrWhiteSpace(state.BgColor))
                {
                    try
                    {
                        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(state.BgColor)!;
                        btn.Background = new SolidColorBrush(c);
                    }
                    catch { }
                }
            }
            catch { }

            // 이미지 복원
            if (!string.IsNullOrEmpty(state.ImagePath) && File.Exists(state.ImagePath))
            {
                try
                {
                    var imgW = state.ImageWidth > 0 ? state.ImageWidth : btn.Width * 0.8;
                    var imgH = state.ImageHeight > 0 ? state.ImageHeight : btn.Height * 0.8;
                    var img = new System.Windows.Controls.Image
                    {
                        Source = TryLoadImageSource(state.ImagePath)!,
                        Stretch = Stretch.Uniform,
                        Width = imgW,
                        Height = imgH,
                    };
                    if (!string.IsNullOrEmpty(state.ImageHAlign) && Enum.TryParse<System.Windows.HorizontalAlignment>(state.ImageHAlign, out var ha)) img.HorizontalAlignment = ha; else img.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                    if (!string.IsNullOrEmpty(state.ImageVAlign) && Enum.TryParse<System.Windows.VerticalAlignment>(state.ImageVAlign, out var va)) img.VerticalAlignment = va; else img.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                    btn.Content = img;
                }
                catch { }
            }

            // 라벨
            var meta = new ButtonMeta
            {
                Path = state.Path,
                IsFolder = state.IsFolder,
                LabelText = state.LabelText,
                LabelInside = state.LabelInside,
                Width = FolderCellSize,
                Height = FolderCellSize,
            };
            btn.Tag = meta;
            if (!string.IsNullOrWhiteSpace(meta.LabelText))
                EnsureOrUpdateInButtonLabel(btn, meta);

            // 클릭 = 경로 실행
            btn.Click += (_, _) =>
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
                if (string.IsNullOrEmpty(meta.Path)) { System.Windows.MessageBox.Show("경로가 설정되지 않았습니다."); return; }
                try
                {
                    if (Uri.TryCreate(meta.Path, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                        Process.Start(new ProcessStartInfo { FileName = meta.Path, UseShellExecute = true });
                    else if (meta.IsFolder && Directory.Exists(meta.Path))
                        Process.Start(new ProcessStartInfo { FileName = meta.Path, UseShellExecute = true });
                    else if (!meta.IsFolder && File.Exists(meta.Path))
                        Process.Start(new ProcessStartInfo { FileName = meta.Path, UseShellExecute = true });
                    else
                        System.Windows.MessageBox.Show("경로가 올바르지 않습니다.");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"경로를 열 수 없습니다.\n{ex.Message}");
                }
            };

            // 우클릭 메뉴 — 캔버스용 버튼수정 다이얼로그를 그대로 사용 (크기 부분만 잠금)
            // 변경 사항은 onChanged에서 ButtonState로 다시 직렬화하여 폴더에 저장
            var editMi = new MenuItem { Header = "버튼수정" };
            editMi.Click += (_, _) => ShowButtonEditDialog(btn, hostCanvas, meta, lockSize: true,
                onChanged: () =>
                {
                    // 변경된 UI/메타를 ButtonState에 반영
                    SyncStateFromButton(state, btn, meta);
                    // 크기는 항상 셀 크기 유지
                    state.Width = FolderCellSize;
                    state.Height = FolderCellSize;
                    btn.Width = FolderCellSize;
                    btn.Height = FolderCellSize;
                    SaveAllFolderStates();
                });
            var delMi = new MenuItem { Header = "버튼삭제" };
            delMi.Click += (_, _) =>
            {
                folder.Entries.RemoveAll(en => en.Button == state);
                RefreshOpenFolderWindow(folder);
                SaveAllFolderStates();
            };
            btn.ContextMenu.Items.Add(editMi);
            btn.ContextMenu.Items.Add(delMi);

            // Ripple (Shift가 아닐 때)
            btn.PreviewMouseLeftButtonDown += Btn_PreviewMouseLeftButtonDown;

            return btn;
        }

        // 폴더 내부 자식 드래그 (격자 단위로만 이동, 폴더 영역 내 제한)
        private void AttachFolderInnerDragHandlers(System.Windows.Controls.Button btn, System.Windows.Controls.Canvas inner, FolderMeta folder, FolderEntry entry)
        {
            bool shiftStart = false;
            bool dragging = false;
            System.Windows.Point start = default;
            System.Windows.Point origin = default;

            btn.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { shiftStart = false; return; }
                shiftStart = true;
                start = e.GetPosition(inner);
                origin = new System.Windows.Point(System.Windows.Controls.Canvas.GetLeft(btn), System.Windows.Controls.Canvas.GetTop(btn));
                dragging = false;
                btn.CaptureMouse();
                e.Handled = true;
            };
            btn.PreviewMouseMove += (s, e) =>
            {
                if (!shiftStart || !btn.IsMouseCaptured) return;
                var cur = e.GetPosition(inner);
                var diff = cur - start;
                if (!dragging && diff.Length > 3) dragging = true;
                if (!dragging) return;
                // 시각적 따라가기 (스냅 없이)
                System.Windows.Controls.Canvas.SetLeft(btn, origin.X + diff.X);
                System.Windows.Controls.Canvas.SetTop(btn, origin.Y + diff.Y);
                e.Handled = true;
            };
            btn.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (!shiftStart) { return; }
                if (btn.IsMouseCaptured) btn.ReleaseMouseCapture();
                if (!dragging) { shiftStart = false; return; }

                // 폴더창 영역 밖으로 빠졌으면 캔버스(또는 부모 폴더)로 이동
                if (folder.OpenWindow != null && !IsMouseOverWindow(folder.OpenWindow))
                {
                    if (TryDropChildOutsideFolder(folder, entry))
                    {
                        dragging = false;
                        shiftStart = false;
                        e.Handled = true;
                        return;
                    }
                }

                // 셀 좌표 계산
                int columns = ResolveColumns(folder);
                double x = System.Windows.Controls.Canvas.GetLeft(btn);
                double y = System.Windows.Controls.Canvas.GetTop(btn);
                int col = (int)Math.Round(x / (FolderCellSize + FolderCellGap));
                int row = (int)Math.Round(y / (FolderCellSize + FolderCellGap));
                col = Math.Max(0, Math.Min(col, columns - 1));
                row = Math.Max(0, row);

                if (folder.SortMode == "None")
                {
                    // 자유 배치 모드: 셀 좌표를 직접 저장. 같은 셀에 다른 항목이 있으면 그 항목과 교환.
                    var occupant = folder.Entries.FirstOrDefault(en => en != entry && en.CellCol == col && en.CellRow == row);
                    if (occupant != null)
                    {
                        occupant.CellCol = entry.CellCol;
                        occupant.CellRow = entry.CellRow;
                    }
                    entry.CellCol = col;
                    entry.CellRow = row;
                }
                else
                {
                    // 정렬 모드(이름순 등): 리스트 순서를 바꿔서 다음 LayoutFolderContents가 인덱스 기반 배치
                    int targetIndex = row * columns + col;
                    int currentIndex = folder.Entries.IndexOf(entry);
                    if (currentIndex < 0) { shiftStart = false; dragging = false; return; }
                    if (targetIndex >= folder.Entries.Count) targetIndex = folder.Entries.Count - 1;
                    if (targetIndex != currentIndex)
                    {
                        folder.Entries.RemoveAt(currentIndex);
                        folder.Entries.Insert(Math.Min(targetIndex, folder.Entries.Count), entry);
                    }
                }

                LayoutFolderContents(folder, inner, columns);
                SaveAllFolderStates();
                dragging = false;
                shiftStart = false;
                e.Handled = true;
            };
        }

        // 폴더창 밖으로 자식이 드래그되었을 때 처리. 성공 시 true.
        // - 부모 폴더창 위에 마우스가 있다면 그 부모 폴더로 이동
        // - 어디에도 안 걸리면 루트 캔버스로 (또는 부모 폴더 없으면 현재 캔버스로) 이동
        private bool TryDropChildOutsideFolder(FolderMeta folder, FolderEntry entry)
        {
            // 부모 폴더창 위면 부모로 이동
            var parent = folder.RuntimeParent;
            if (parent?.OpenWindow != null && IsMouseOverWindow(parent.OpenWindow))
            {
                folder.Entries.Remove(entry);
                parent.Entries.Add(entry);
                RefreshOpenFolderWindow(folder);
                RefreshOpenFolderWindow(parent);
                SaveAllFolderStates();
                return true;
            }

            // 그 외엔 루트 캔버스로 빼기 (자식 폴더 자체는 캔버스로 못 빼게 막을 수도 있지만 일단 허용)
            var rootCanvas = FindCanvasForRootFolder(folder);
            if (rootCanvas == null) return false;

            folder.Entries.Remove(entry);

            if (entry.Button != null)
            {
                // 캔버스에 즉시 표시되는 새 버튼으로 SpawnButtonOnCanvasFromState 대신 라이브로 생성
                SpawnButtonOnCanvasLive(rootCanvas, entry.Button);
            }
            else if (entry.SubFolder != null)
            {
                // 폴더를 캔버스로: 새 폴더 버튼 생성
                var fbtn = BuildFolderCanvasButton(rootCanvas, entry.SubFolder);
                // 마우스 위치에 떨굼
                var mouseScreen = Forms.Cursor.Position;
                var pt = rootCanvas.PointFromScreen(new System.Windows.Point(mouseScreen.X, mouseScreen.Y));
                double nx = Math.Round(pt.X / GridSize) * GridSize;
                double ny = Math.Round(pt.Y / GridSize) * GridSize;
                nx = Math.Max(0, nx);
                ny = Math.Max(0, ny);
                System.Windows.Controls.Canvas.SetLeft(fbtn, nx);
                System.Windows.Controls.Canvas.SetTop(fbtn, ny);
                rootCanvas.Children.Add(fbtn);
            }

            RefreshOpenFolderWindow(folder);
            SaveAllFolderStates();
            SaveAllButtonStates();
            return true;
        }

        // 캔버스에 ButtonState로부터 즉시 보이는 버튼을 만들어 추가 (Restore 전체 흐름을 거치지 않고 라이브 생성)
        private void SpawnButtonOnCanvasLive(System.Windows.Controls.Canvas canvas, ButtonState state)
        {
            // 폴더에 들어가기 전 원래 크기 정보가 없으면 기본 50×50
            double w = state.Width > 0 ? state.Width : 50.0;
            double h = state.Height > 0 ? state.Height : 50.0;

            // 마우스 위치 기준으로 떨굼
            var mouseScreen = Forms.Cursor.Position;
            var pt = canvas.PointFromScreen(new System.Windows.Point(mouseScreen.X, mouseScreen.Y));
            double nx = Math.Round((pt.X - w / 2) / GridSize) * GridSize;
            double ny = Math.Round((pt.Y - h / 2) / GridSize) * GridSize;
            nx = Math.Max(0, nx);
            ny = Math.Max(0, ny);

            // 버튼 인스턴스화 — BuildChildButtonFromState 로직 재사용을 위해 임시 FolderMeta? 없이는 안 됨.
            // 그러므로 직접 RestoreAllButtonStates와 동등한 변환을 간단히 수행.
            var btn = new System.Windows.Controls.Button
            {
                Width = w,
                Height = h,
                Content = state.Content ?? "",
                ContextMenu = new ContextMenu(),
                Style = FindResource("DynamicButtonStyle") as Style,
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            // 폰트/색/이미지 복원
            try
            {
                if (!string.IsNullOrWhiteSpace(state.FontFamily))
                    btn.FontFamily = new System.Windows.Media.FontFamily(state.FontFamily);
                if (state.FontSize > 0) btn.FontSize = state.FontSize;
                if (!string.IsNullOrWhiteSpace(state.FontWeightName))
                {
                    try { btn.FontWeight = (FontWeight)new FontWeightConverter().ConvertFromString(state.FontWeightName)!; } catch { }
                }
                btn.FontStyle = state.Italic ? FontStyles.Italic : FontStyles.Normal;
                if (!string.IsNullOrWhiteSpace(state.FontColor))
                {
                    try
                    {
                        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(state.FontColor)!;
                        btn.Foreground = new SolidColorBrush(c);
                    }
                    catch { }
                }
                if (state.BgColor == "transparent") btn.Background = System.Windows.Media.Brushes.Transparent;
                else if (!string.IsNullOrWhiteSpace(state.BgColor))
                {
                    try
                    {
                        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(state.BgColor)!;
                        btn.Background = new SolidColorBrush(c);
                    }
                    catch { }
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(state.ImagePath) && File.Exists(state.ImagePath))
            {
                try
                {
                    var img = new System.Windows.Controls.Image
                    {
                        Source = TryLoadImageSource(state.ImagePath)!,
                        Stretch = Stretch.Uniform,
                        Width = state.ImageWidth > 0 ? state.ImageWidth : w * 0.8,
                        Height = state.ImageHeight > 0 ? state.ImageHeight : h * 0.8,
                    };
                    if (!string.IsNullOrEmpty(state.ImageHAlign) && Enum.TryParse<System.Windows.HorizontalAlignment>(state.ImageHAlign, out var ha)) img.HorizontalAlignment = ha;
                    if (!string.IsNullOrEmpty(state.ImageVAlign) && Enum.TryParse<System.Windows.VerticalAlignment>(state.ImageVAlign, out var va)) img.VerticalAlignment = va;
                    btn.Content = img;
                }
                catch { }
            }

            var meta = new ButtonMeta
            {
                Path = state.Path,
                IsFolder = state.IsFolder,
                LabelText = state.LabelText,
                LabelInside = state.LabelInside,
                Width = w,
                Height = h,
            };
            btn.Tag = meta;
            if (!string.IsNullOrWhiteSpace(meta.LabelText))
            {
                if (meta.LabelInside) EnsureOrUpdateInButtonLabel(btn, meta);
                else EnsureOrUpdateButtonLabel(canvas, btn, meta);
            }

            // 클릭 = 경로 실행
            btn.Click += (_, _) =>
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
                if (string.IsNullOrEmpty(meta.Path)) { System.Windows.MessageBox.Show("경로가 설정되지 않았습니다."); return; }
                try
                {
                    if (Uri.TryCreate(meta.Path, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                        Process.Start(new ProcessStartInfo { FileName = meta.Path, UseShellExecute = true });
                    else if (meta.IsFolder && Directory.Exists(meta.Path))
                        Process.Start(new ProcessStartInfo { FileName = meta.Path, UseShellExecute = true });
                    else if (!meta.IsFolder && File.Exists(meta.Path))
                        Process.Start(new ProcessStartInfo { FileName = meta.Path, UseShellExecute = true });
                    else System.Windows.MessageBox.Show("경로가 올바르지 않습니다.");
                }
                catch (Exception ex) { System.Windows.MessageBox.Show($"경로를 열 수 없습니다.\n{ex.Message}"); }
            };

            var delMenu = new MenuItem { Header = "버튼삭제" };
            delMenu.Click += (_, _) => DeleteButtonWithConfirm(canvas, btn, meta);
            var editMenu = new MenuItem { Header = "버튼수정" };
            editMenu.Click += (_, _) => ShowButtonEditDialog(btn, canvas, meta);
            btn.ContextMenu.Items.Add(editMenu);
            btn.ContextMenu.Items.Add(delMenu);
            AddMoveToFolderMenu(btn, canvas, meta);

            System.Windows.Controls.Canvas.SetLeft(btn, nx);
            System.Windows.Controls.Canvas.SetTop(btn, ny);
            canvas.Children.Add(btn);

            AttachDragHandlers(btn, canvas, meta);
            btn.PreviewMouseLeftButtonDown += Btn_PreviewMouseLeftButtonDown;

            SaveAllButtonStates();
        }

        // 폴더 내부의 새 빈 버튼 추가
        private void AddNewButtonToFolder(FolderMeta folder)
        {
            var state = new ButtonState
            {
                Width = FolderCellSize,
                Height = FolderCellSize,
                LabelInside = true,
                Content = "",
            };
            folder.Entries.Add(new FolderEntry { Button = state });
            RefreshOpenFolderWindow(folder);
            SaveAllFolderStates();
        }

        // 폴더 내부에 새 하위 폴더 추가
        private void AddNewSubFolderToFolder(FolderMeta folder)
        {
            var sub = new FolderMeta { Name = "새 폴더" };
            folder.Entries.Add(new FolderEntry { SubFolder = sub });
            RefreshOpenFolderWindow(folder);
            SaveAllFolderStates();
        }

        // 폴더 자식을 폴더 밖(상위 폴더 또는 캔버스)으로 빼기
        private void MoveChildOutOfFolder(ButtonState state, FolderMeta folder)
        {
            folder.Entries.RemoveAll(en => en.Button == state);
            if (folder.RuntimeParent != null)
            {
                folder.RuntimeParent.Entries.Add(new FolderEntry { Button = state });
                RefreshOpenFolderWindow(folder.RuntimeParent);
            }
            else
            {
                // 캔버스로 빼기 - 어느 캔버스로? 폴더 버튼이 있는 캔버스
                var hostCanvas = FindCanvasForRootFolder(folder);
                if (hostCanvas != null)
                {
                    // 원래 크기 정보 복원 — 폴더에 들어갈 때 54x54로 바뀌었으므로 여기선 유지
                    SpawnButtonOnCanvasFromState(hostCanvas, state);
                }
            }
            RefreshOpenFolderWindow(folder);
            SaveAllFolderStates();
            SaveAllButtonStates();
        }

        // 루트 폴더가 어느 캔버스에 있는지 검색
        private System.Windows.Controls.Canvas? FindCanvasForRootFolder(FolderMeta folder)
        {
            // 루트까지 거슬러 올라감
            while (folder.RuntimeParent != null) folder = folder.RuntimeParent;
            int tabCount = tabControl?.Items.Count ?? 0;
            for (int i = 0; i < tabCount; i++)
            {
                var c = GetCanvasByIndex(i);
                if (c == null) continue;
                foreach (UIElement ch in c.Children)
                {
                    if (ch is System.Windows.Controls.Button b && b.Tag is FolderMeta fm && fm == folder)
                        return c;
                }
            }
            return null;
        }

        // 캔버스에 ButtonState로부터 새 버튼 생성 (간단한 복원 경로)
        private void SpawnButtonOnCanvasFromState(System.Windows.Controls.Canvas canvas, ButtonState state)
        {
            // 빈 자리 찾기
            const double gap = 5;
            double w = state.Width > 0 ? state.Width : FolderCellSize;
            double h = state.Height > 0 ? state.Height : FolderCellSize;
            double availW = canvas.ActualWidth > 0 ? canvas.ActualWidth : canvas.Width;
            int cols = Math.Max(1, (int)((availW - gap) / (w + gap)));
            int existing = canvas.Children.OfType<System.Windows.Controls.Button>().Count();
            int col = existing % cols;
            int row = existing / cols;
            double x = gap + col * (w + gap);
            double y = gap + row * (h + gap);

            // 임시 ButtonState 리스트로 RestoreAllButtonStates 호출 대신 단일 복원
            // -> 단순화를 위해 한 번 button_states.json에 추가하고 restoring하는 대신
            //    여기서는 즉시 button을 만들어 canvas에 추가하지만, RestoreAllButtonStates 변환 로직이 복잡하므로
            //    저장만 하고 다음 실행 때 보이도록 한다.
            var list = new List<ButtonState>();
            if (File.Exists(ButtonStateFile))
            {
                try { list = JsonSerializer.Deserialize<List<ButtonState>>(File.ReadAllText(ButtonStateFile)) ?? new(); }
                catch { list = new(); }
            }
            state.X = x;
            state.Y = y;
            state.Width = w;
            state.Height = h;
            state.CanvasIndex = GetCanvasIndex(canvas);
            list.Add(state);
            File.WriteAllText(ButtonStateFile, JsonSerializer.Serialize(list));

            // 즉시 시각적으로 추가 — 단순화하여 알림으로 대체
            System.Windows.MessageBox.Show("버튼이 폴더에서 빠져나왔습니다. 캔버스에는 다음 실행 시 표시됩니다.");
        }

        // ===== 저장 / 복원 =====
        private void SaveAllFolderStates()
        {
            try
            {
                var all = new List<FolderRecord>();
                int tabCount = tabControl?.Items.Count ?? 0;
                for (int i = 0; i < tabCount; i++)
                {
                    var canvas = GetCanvasByIndex(i);
                    if (canvas == null) continue;
                    foreach (UIElement ch in canvas.Children)
                    {
                        if (ch is System.Windows.Controls.Button b && b.Tag is FolderMeta fm)
                        {
                            var rec = SerializeFolder(fm);
                            rec.X = System.Windows.Controls.Canvas.GetLeft(b);
                            rec.Y = System.Windows.Controls.Canvas.GetTop(b);
                            rec.CanvasIndex = i;
                            all.Add(rec);
                        }
                    }
                }
                File.WriteAllText(FolderStateFile, JsonSerializer.Serialize(all));
            }
            catch (Exception ex) { Debug.WriteLine("SaveAllFolderStates: " + ex); }
        }

        private FolderRecord SerializeFolder(FolderMeta folder)
        {
            var rec = new FolderRecord
            {
                Name = folder.Name,
                ColumnsOverride = folder.ColumnsOverride,
                RowsOverride = folder.RowsOverride,
                SortMode = folder.SortMode,
            };
            foreach (var entry in folder.Entries)
            {
                if (entry.SubFolder != null)
                    rec.Items.Add(new FolderItemRecord { Folder = SerializeFolder(entry.SubFolder), CellCol = entry.CellCol, CellRow = entry.CellRow });
                else if (entry.Button != null)
                    rec.Items.Add(new FolderItemRecord { Button = entry.Button, CellCol = entry.CellCol, CellRow = entry.CellRow });
            }
            return rec;
        }

        private void RestoreAllFolderStates()
        {
            try
            {
                if (!File.Exists(FolderStateFile)) return;
                var json = File.ReadAllText(FolderStateFile);
                var all = JsonSerializer.Deserialize<List<FolderRecord>>(json);
                if (all == null) return;
                foreach (var rec in all)
                {
                    var canvas = GetCanvasByIndex(rec.CanvasIndex);
                    if (canvas == null) continue;
                    var folder = DeserializeFolder(rec);
                    var btn = BuildFolderCanvasButton(canvas, folder);
                    System.Windows.Controls.Canvas.SetLeft(btn, rec.X);
                    System.Windows.Controls.Canvas.SetTop(btn, rec.Y);
                    canvas.Children.Add(btn);
                }
            }
            catch (Exception ex) { Debug.WriteLine("RestoreAllFolderStates: " + ex); }
        }

        private FolderMeta DeserializeFolder(FolderRecord rec)
        {
            var folder = new FolderMeta
            {
                Name = rec.Name ?? "새 폴더",
                ColumnsOverride = rec.ColumnsOverride,
                RowsOverride = rec.RowsOverride,
                SortMode = string.IsNullOrEmpty(rec.SortMode) ? "None" : rec.SortMode,
            };
            foreach (var it in rec.Items ?? new())
            {
                if (it.Folder != null)
                    folder.Entries.Add(new FolderEntry { SubFolder = DeserializeFolder(it.Folder), CellCol = it.CellCol, CellRow = it.CellRow });
                else if (it.Button != null)
                    folder.Entries.Add(new FolderEntry { Button = it.Button, CellCol = it.CellCol, CellRow = it.CellRow });
            }
            return folder;
        }
    }
}
