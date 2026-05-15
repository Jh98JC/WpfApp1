using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace 대진포스_쿼리
{
    // WpfApp2.ThemeManager의 색상 정의를 동기화한 미러.
    // %APPDATA%\WpfApp2\theme.txt 를 읽어 부모 앱과 같은 테마를 적용한다.
    public static class ThemeManager
    {
        public enum Theme { Purple, Black, White }

        public static Theme CurrentTheme { get; private set; } = Theme.Purple;

        private static readonly string ThemeFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WpfApp2", "theme.txt");

        private static FileSystemWatcher _watcher;

        public static void Apply(Theme theme)
        {
            CurrentTheme = theme;
            var app = System.Windows.Application.Current;
            if (app == null) return;
            var r = app.Resources;
            switch (theme)
            {
                case Theme.Purple: ApplyPurple(r); break;
                case Theme.Black:  ApplyBlack(r);  break;
                case Theme.White:  ApplyWhite(r);  break;
            }
        }

        public static void LoadSaved()
        {
            try
            {
                if (File.Exists(ThemeFile) &&
                    Enum.TryParse<Theme>(File.ReadAllText(ThemeFile).Trim(), out var t))
                {
                    Apply(t);
                    return;
                }
            }
            catch { }
            Apply(Theme.White);
        }

        // 부모 앱이 테마를 변경하면 theme.txt가 갱신된다 — 그것을 감지해 자동 반영.
        public static void StartWatching()
        {
            try
            {
                string dir = Path.GetDirectoryName(ThemeFile);
                if (string.IsNullOrEmpty(dir)) return;
                Directory.CreateDirectory(dir);
                _watcher = new FileSystemWatcher(dir, Path.GetFileName(ThemeFile))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };
                FileSystemEventHandler handler = (_, __) =>
                {
                    try
                    {
                        System.Threading.Thread.Sleep(80); // 잠금 회피
                        var app = System.Windows.Application.Current;
                        app?.Dispatcher.Invoke(new Action(LoadSaved));
                    }
                    catch { }
                };
                _watcher.Changed += handler;
                _watcher.Created += handler;
            }
            catch { }
        }

        static LinearGradientBrush LGB(string top, string bot) =>
            new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(C(top), 0),
                    new GradientStop(C(bot), 1)
                },
                new System.Windows.Point(0, 0),
                new System.Windows.Point(0, 1));

        static SolidColorBrush SB(string hex) => new SolidColorBrush(C(hex));

        static System.Windows.Media.Color C(string hex) =>
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);

        static void ApplyPurple(ResourceDictionary r)
        {
            r["WindowBackgroundBrush"]    = SB("#1A1A22");
            r["ForegroundBrush"]          = SB("#F1F1F1");
            r["StatusBarBackgroundBrush"] = SB("#13131B");
            r["StatusBarBorderBrush"]     = SB("#5575EE");
            r["TitleBarButtonHoverBrush"] = SB("#1E1E2A");

            r["ContextMenuBackgroundBrush"]  = SB("#1E1E28");
            r["ContextMenuBorderBrush"]      = SB("#2E2E3E");
            r["ContextMenuForegroundBrush"]  = SB("#F0F0F0");
            r["ContextMenuItemHoverBrush"]   = SB("#2A2A40");
            r["ContextMenuItemPressedBrush"] = SB("#353550");

            r["TabNormalBrush"]       = LGB("#1D1D26", "#141419");
            r["TabHoverBrush"]        = LGB("#252532", "#1A1A25");
            r["TabSelectedBrush"]     = LGB("#2A2A3E", "#1C1C2E");
            r["TabAccentBrush"]       = LGB("#8BAAFF", "#5575EE");
            r["TabTextNormalBrush"]   = SB("#8888A0");
            r["TabTextHoverBrush"]    = SB("#C8C8DC");
            r["TabTextSelectedBrush"] = SB("#FFFFFF");

            r["AccentBrush"]   = SB("#FFB347");
            r["DangerBrush"]   = SB("#FF8080");
            r["SuccessBrush"]  = SB("#70C070");
        }

        static void ApplyBlack(ResourceDictionary r)
        {
            r["WindowBackgroundBrush"]    = SB("#0D0D0D");
            r["ForegroundBrush"]          = SB("#DEDEDE");
            r["StatusBarBackgroundBrush"] = SB("#070707");
            r["StatusBarBorderBrush"]     = SB("#2C2C2C");
            r["TitleBarButtonHoverBrush"] = SB("#181818");

            r["ContextMenuBackgroundBrush"]  = SB("#111111");
            r["ContextMenuBorderBrush"]      = SB("#282828");
            r["ContextMenuForegroundBrush"]  = SB("#DEDEDE");
            r["ContextMenuItemHoverBrush"]   = SB("#1E1E1E");
            r["ContextMenuItemPressedBrush"] = SB("#2A2A2A");

            r["TabNormalBrush"]       = LGB("#111111", "#080808");
            r["TabHoverBrush"]        = LGB("#1C1C1C", "#141414");
            r["TabSelectedBrush"]     = LGB("#282828", "#1C1C1C");
            r["TabAccentBrush"]       = LGB("#707070", "#505050");
            r["TabTextNormalBrush"]   = SB("#505060");
            r["TabTextHoverBrush"]    = SB("#909090");
            r["TabTextSelectedBrush"] = SB("#D0D0D0");

            r["AccentBrush"]   = SB("#FFB347");
            r["DangerBrush"]   = SB("#FF8080");
            r["SuccessBrush"]  = SB("#70C070");
        }

        static void ApplyWhite(ResourceDictionary r)
        {
            r["WindowBackgroundBrush"]    = SB("#F0F0F6");
            r["ForegroundBrush"]          = SB("#1A1A2A");
            r["StatusBarBackgroundBrush"] = SB("#E4E4F0");
            r["StatusBarBorderBrush"]     = SB("#5575EE");
            r["TitleBarButtonHoverBrush"] = SB("#D8D8EA");

            r["ContextMenuBackgroundBrush"]  = SB("#F5F5FC");
            r["ContextMenuBorderBrush"]      = SB("#DCDCE8");
            r["ContextMenuForegroundBrush"]  = SB("#1A1A2A");
            r["ContextMenuItemHoverBrush"]   = SB("#E0E0F0");
            r["ContextMenuItemPressedBrush"] = SB("#D0D0E4");

            r["TabNormalBrush"]       = LGB("#E6E6F2", "#DCDCE8");
            r["TabHoverBrush"]        = LGB("#D8D8EC", "#CECED8");
            r["TabSelectedBrush"]     = LGB("#C8C8E4", "#B8B8D4");
            r["TabAccentBrush"]       = LGB("#8BAAFF", "#5575EE");
            r["TabTextNormalBrush"]   = SB("#8080A0");
            r["TabTextHoverBrush"]    = SB("#404060");
            r["TabTextSelectedBrush"] = SB("#1A1A2A");

            r["AccentBrush"]   = SB("#E08020");
            r["DangerBrush"]   = SB("#D03030");
            r["SuccessBrush"]  = SB("#208840");
        }
    }
}
