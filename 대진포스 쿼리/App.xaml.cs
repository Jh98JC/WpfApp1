using System;
using System.IO;
using System.Windows;

namespace 대진포스_쿼리
{
    public partial class App : Application
    {
        public static bool AutoMode { get; private set; }
        public static DateTime AutoModeDate { get; private set; } = DateTime.Today.AddDays(-1);
        public static string AutoModeStatusFile { get; private set; } = string.Empty;
        // --store=매장명 인자: 특정 매장만 재취합하는 모드
        public static string StoreFilter { get; private set; } = string.Empty;

        // 부모(WpfApp2)가 전달한 초기 위치/크기 — 메인윈도우 우측하단에 1/4 크기로 표시되도록 함.
        public static double? StartX { get; private set; }
        public static double? StartY { get; private set; }
        public static double? StartW { get; private set; }
        public static double? StartH { get; private set; }

        // 부모 윈도우 HWND — 자식이 GWL_HWNDPARENT로 소유 관계를 설정하면
        // 부모가 최소화/숨김 될 때 자식도 같이 가려지게 된다.
        public static IntPtr ParentHwnd { get; private set; } = IntPtr.Zero;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            foreach (var arg in e.Args)
            {
                if (string.Equals(arg, "--auto", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "/auto", StringComparison.OrdinalIgnoreCase))
                {
                    AutoMode = true;
                }
                else if (StartsWithEq(arg, "--date") || StartsWithEq(arg, "/date"))
                {
                    var raw = ValueAfterEq(arg);
                    if (DateTime.TryParse(raw, out var d))
                    {
                        AutoModeDate = d.Date;
                        AutoMode = true;
                    }
                }
                else if (StartsWithEq(arg, "--status") || StartsWithEq(arg, "/status"))
                {
                    AutoModeStatusFile = ValueAfterEq(arg).Trim('"');
                }
                else if (StartsWithEq(arg, "--store") || StartsWithEq(arg, "/store"))
                {
                    StoreFilter = ValueAfterEq(arg).Trim('"');
                    AutoMode = true; // 매장 필터는 자동 모드 전제
                }
                else if (StartsWithEq(arg, "--x")) StartX = ParseDouble(arg);
                else if (StartsWithEq(arg, "--y")) StartY = ParseDouble(arg);
                else if (StartsWithEq(arg, "--width"))  StartW = ParseDouble(arg);
                else if (StartsWithEq(arg, "--height")) StartH = ParseDouble(arg);
                else if (StartsWithEq(arg, "--parent"))
                {
                    if (long.TryParse(ValueAfterEq(arg), System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var h))
                        ParentHwnd = new IntPtr(h);
                }
            }

            // 부모 앱과 같은 테마 적용 + 변경 감시 시작
            ThemeManager.LoadSaved();
            ThemeManager.StartWatching();
        }

        private static bool StartsWithEq(string arg, string prefix)
        {
            return arg.StartsWith(prefix + "=", StringComparison.OrdinalIgnoreCase) ||
                   arg.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase);
        }

        private static string ValueAfterEq(string arg)
        {
            int i = arg.IndexOfAny(new[] { '=', ':' });
            return i < 0 ? string.Empty : arg.Substring(i + 1);
        }

        private static double? ParseDouble(string arg)
        {
            var v = ValueAfterEq(arg);
            return double.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? (double?)d : null;
        }

        public static void WriteStatus(bool success, string message, int savedRows)
        {
            if (string.IsNullOrWhiteSpace(AutoModeStatusFile)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AutoModeStatusFile) ?? ".");
                string content =
                    $"success={(success ? "true" : "false")}\n" +
                    $"message={message?.Replace("\r", " ").Replace("\n", " ")}\n" +
                    $"date={AutoModeDate:yyyy-MM-dd}\n" +
                    $"savedRows={savedRows}\n" +
                    $"finishedAt={DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";
                File.WriteAllText(AutoModeStatusFile, content);
            }
            catch { }
        }
    }
}
