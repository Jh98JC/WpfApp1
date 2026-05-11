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
                else if (arg.StartsWith("--date=", StringComparison.OrdinalIgnoreCase) ||
                         arg.StartsWith("/date=", StringComparison.OrdinalIgnoreCase) ||
                         arg.StartsWith("/date:", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = arg.Substring(arg.IndexOfAny(new[] { '=', ':' }) + 1);
                    if (DateTime.TryParse(raw, out var d))
                    {
                        AutoModeDate = d.Date;
                        AutoMode = true;
                    }
                }
                else if (arg.StartsWith("--status=", StringComparison.OrdinalIgnoreCase) ||
                         arg.StartsWith("/status=", StringComparison.OrdinalIgnoreCase) ||
                         arg.StartsWith("/status:", StringComparison.OrdinalIgnoreCase))
                {
                    AutoModeStatusFile = arg.Substring(arg.IndexOfAny(new[] { '=', ':' }) + 1).Trim('"');
                }
                else if (arg.StartsWith("--store=", StringComparison.OrdinalIgnoreCase) ||
                         arg.StartsWith("/store=", StringComparison.OrdinalIgnoreCase) ||
                         arg.StartsWith("/store:", StringComparison.OrdinalIgnoreCase))
                {
                    StoreFilter = arg.Substring(arg.IndexOfAny(new[] { '=', ':' }) + 1).Trim('"');
                    AutoMode = true; // 매장 필터는 자동 모드 전제
                }
            }
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
