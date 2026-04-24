using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace WpfApp2
{
    public static class ThemeManager
    {
        public enum Theme { Purple, Black, White }

        public static Theme CurrentTheme { get; private set; } = Theme.Purple;

        private static readonly string ThemeFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WpfApp2", "theme.txt");

        public static event EventHandler? ThemeChanged;

        public static void Apply(Theme theme)
        {
            CurrentTheme = theme;
            var r = System.Windows.Application.Current.Resources;
            switch (theme)
            {
                case Theme.Purple: ApplyPurple(r); break;
                case Theme.Black:  ApplyBlack(r);  break;
                case Theme.White:  ApplyWhite(r);  break;
            }
            ThemeChanged?.Invoke(null, EventArgs.Empty);
            try { File.WriteAllText(ThemeFile, theme.ToString()); } catch { }
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
            Apply(Theme.Purple);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        static LinearGradientBrush LGB(string top, string bot) =>
            new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(C(top), 0),
                    new GradientStop(C(bot), 1)
                },
                new System.Windows.Point(0, 0),
                new System.Windows.Point(0, 1));

        static SolidColorBrush SB(string hex) =>
            new SolidColorBrush(C(hex));

        static System.Windows.Media.Color C(string hex) =>
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);

        // ── PURPLE ────────────────────────────────────────────────────────────

        static void ApplyPurple(ResourceDictionary r)
        {
            r["WindowBackgroundBrush"]    = SB("#1A1A22");
            r["WindowBackgroundColor"]    = C("#1A1A22");
            r["ForegroundBrush"]          = SB("#F1F1F1");
            r["ForegroundColor"]          = C("#F1F1F1");
            r["StatusBarBackgroundBrush"] = SB("#13131B");
            r["StatusBarBackgroundColor"] = C("#13131B");
            r["StatusBarBorderBrush"]     = SB("#5575EE");
            r["StatusBarBorderColor"]     = C("#5575EE");
            r["TitleBarButtonHoverBrush"] = SB("#1E1E2A");
            r["TitleBarButtonHoverColor"] = C("#1E1E2A");

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
        }

        // ── BLACK ─────────────────────────────────────────────────────────────

        static void ApplyBlack(ResourceDictionary r)
        {
            r["WindowBackgroundBrush"]    = SB("#0D0D0D");
            r["WindowBackgroundColor"]    = C("#0D0D0D");
            r["ForegroundBrush"]          = SB("#DEDEDE");
            r["ForegroundColor"]          = C("#DEDEDE");
            r["StatusBarBackgroundBrush"] = SB("#070707");
            r["StatusBarBackgroundColor"] = C("#070707");
            r["StatusBarBorderBrush"]     = SB("#2C2C2C");
            r["StatusBarBorderColor"]     = C("#2C2C2C");
            r["TitleBarButtonHoverBrush"] = SB("#181818");
            r["TitleBarButtonHoverColor"] = C("#181818");

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
        }

        // ── WHITE ─────────────────────────────────────────────────────────────

        static void ApplyWhite(ResourceDictionary r)
        {
            r["WindowBackgroundBrush"]    = SB("#F0F0F6");
            r["WindowBackgroundColor"]    = C("#F0F0F6");
            r["ForegroundBrush"]          = SB("#1A1A2A");
            r["ForegroundColor"]          = C("#1A1A2A");
            r["StatusBarBackgroundBrush"] = SB("#E4E4F0");
            r["StatusBarBackgroundColor"] = C("#E4E4F0");
            r["StatusBarBorderBrush"]     = SB("#5575EE");
            r["StatusBarBorderColor"]     = C("#5575EE");
            r["TitleBarButtonHoverBrush"] = SB("#D8D8EA");
            r["TitleBarButtonHoverColor"] = C("#D8D8EA");

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
        }
    }
}
