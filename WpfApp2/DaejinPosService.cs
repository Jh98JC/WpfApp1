using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WpfApp2
{
    public class PosStoreConfig
    {
        public string StoreName { get; set; } = string.Empty;
        // 별도 POS DB가 있는 매장은 ConnectionString/SalesQuery 지정, 없으면 DataConnectionString의 매출데이터 테이블 사용
        public string ConnectionString { get; set; } = string.Empty;
        public string SalesQuery { get; set; } = string.Empty;
    }

    public static class DaejinPosService
    {
        private static readonly string StoresConfigFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WpfApp2", "pos_stores.json");

        public static bool IsRunning { get; private set; }
        public static string? LastError { get; private set; }
        public static string LastRunInfo { get; private set; } = string.Empty;
        public static DateTime? LastRunTime { get; private set; }

        // 한 세션 내에서 같은 날짜 스크래퍼를 반복 실행하지 않도록 추적
        private static readonly HashSet<DateTime> _scrapedDatesThisSession = new();

        public static event Action<string>? StatusChanged;

        public static async Task RunAsync(DateTime? targetDate = null)
        {
            if (IsRunning) return;
            if (!DatabaseService.IsDataConfigured)
            {
                LastError = "데이터 DB 미연결";
                LastRunInfo = $"[{DateTime.Now:HH:mm}] DB 미연결 — 데이터 연결 설정을 확인하세요.";
                LastRunTime = DateTime.Now;
                StatusChanged?.Invoke(string.Empty);
                return;
            }

            var date = (targetDate ?? DateTime.Today.AddDays(-1)).Date;

            try
            {
                await DatabaseService.InitializePosTablesAsync();
                if (await DatabaseService.HasCollectionForDateAsync(date))
                {
                    LastRunInfo = $"[{DateTime.Now:HH:mm}] {date:yyyy-MM-dd} 자료 이미 취합됨";
                    LastRunTime = DateTime.Now;
                    StatusChanged?.Invoke(string.Empty);
                    return;
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                LastRunInfo = $"[{DateTime.Now:HH:mm}] 초기화 오류 — {ex.Message}";
                LastRunTime = DateTime.Now;
                StatusChanged?.Invoke(string.Empty);
                return;
            }

            IsRunning = true;
            LastError = null;
            StatusChanged?.Invoke("매장현황 취합중...");

            try
            {
                // pos_stores.json에 별도 연결 설정이 있으면 사용, 없으면 매출데이터 테이블에서 자동 파악
                var configuredStores = LoadStoreConfigs();
                bool useCustom = configuredStores.Count > 0 &&
                                 configuredStores.Exists(s => !string.IsNullOrWhiteSpace(s.ConnectionString));

                int success = 0, skipped = 0;

                if (useCustom)
                {
                    // 외부 POS DB 모드: 각 매장 연결로 쿼리
                    foreach (var store in configuredStores)
                    {
                        bool ok = await TryCollectFromExternalAsync(store, date);
                        if (ok)
                        {
                            success++;
                            try { await DatabaseService.DeleteSkippedStoreByNameAndDateAsync(date, store.StoreName); } catch { }
                        }
                        else { skipped++; try { await DatabaseService.AddSkippedStoreAsync(date, store.StoreName, "쿼리 실패"); } catch { } }
                    }
                }
                else
                {
                    // 자동 모드: 매출데이터 테이블이 해당일 데이터를 갖고 있어야 한다.
                    // 데이터가 아직 들어오지 않은 경우(전체 0건)와 일부 매장만 누락된 경우를 구분한다.
                    int rowsForDate = await DatabaseService.GetRowCountForDateAsync(date);
                    if (rowsForDate == 0)
                    {
                        // 외부 스크래퍼(대진포스 쿼리.exe)를 한 번 시도해서 매출데이터를 채워본다.
                        bool alreadyTried;
                        lock (_scrapedDatesThisSession)
                            alreadyTried = !_scrapedDatesThisSession.Add(date);

                        if (!alreadyTried)
                        {
                            StatusChanged?.Invoke($"대진포스 쿼리 자동 수집중... ({date:yyyy-MM-dd})");
                            var (launched, ok, msg) = await TryRunExternalScraperAsync(date);
                            if (launched && ok)
                            {
                                rowsForDate = await DatabaseService.GetRowCountForDateAsync(date);
                                StatusChanged?.Invoke("매장현황 취합중...");
                            }
                            else if (launched && !ok)
                            {
                                LastError = msg;
                                LastRunInfo = $"[{DateTime.Now:HH:mm}] {date:yyyy-MM-dd} 외부 수집기 실패 — {msg}";
                                LastRunTime = DateTime.Now;
                                return;
                            }
                            // launched == false : exe 못 찾음 → 아래 "미도착" 메시지로 자연스럽게 진행
                        }

                        if (rowsForDate == 0)
                        {
                            // 어제 데이터 자체가 아직 미도착 → 누락 처리하지 않고 로그도 남기지 않음(다음 실행 때 재시도)
                            try { await DatabaseService.DeleteSkippedStoresForDateAsync(date); } catch { }
                            var latest = await DatabaseService.GetLatestDataDateAsync();
                            LastRunTime = DateTime.Now;
                            LastRunInfo = latest.HasValue
                                ? $"[{DateTime.Now:HH:mm}] {date:yyyy-MM-dd} 데이터 미도착 (최신: {latest:yyyy-MM-dd})"
                                : $"[{DateTime.Now:HH:mm}] {date:yyyy-MM-dd} 매출데이터 비어있음";
                            return;
                        }
                    }

                    List<string> storeNames;
                    if (configuredStores.Count > 0)
                        // pos_stores.json에 매장명만 지정된 경우
                        storeNames = configuredStores.ConvertAll(s => s.StoreName);
                    else
                        // 완전 자동: 최근 7일 매출데이터에서 매장 목록 추출
                        storeNames = await DatabaseService.GetRecentStoreNamesAsync(7);

                    foreach (var storeName in storeNames)
                    {
                        try
                        {
                            bool ok = await DatabaseService.CheckAndRecordStoreSalesAsync(date, storeName);
                            if (ok)
                            {
                                success++;
                                try { await DatabaseService.DeleteSkippedStoreByNameAndDateAsync(date, storeName); } catch { }
                            }
                            else { skipped++; try { await DatabaseService.AddSkippedStoreAsync(date, storeName, "해당일 매장 매출 0건"); } catch { } }
                        }
                        catch (Exception storeEx)
                        {
                            skipped++;
                            string reason = storeEx.Message.Length > 200 ? storeEx.Message[..200] : storeEx.Message;
                            try { await DatabaseService.AddSkippedStoreAsync(date, storeName, $"오류: {reason}"); } catch { }
                        }
                    }
                }

                if (success + skipped > 0)
                    try { await DatabaseService.AddCollectionLogAsync(date, success + skipped, success, skipped); } catch { }

                LastRunTime = DateTime.Now;
                if (success == 0 && skipped == 0)
                    LastRunInfo = $"[{DateTime.Now:HH:mm}] {date:yyyy-MM-dd} — 매장 목록 없음";
                else if (skipped == 0)
                    LastRunInfo = $"[{DateTime.Now:HH:mm}] {date:yyyy-MM-dd} 취합완료 ({success}개 매장)";
                else
                    LastRunInfo = $"[{DateTime.Now:HH:mm}] {date:yyyy-MM-dd} 취합완료 (성공 {success} / 누락 {skipped})";
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                LastRunInfo = $"[{DateTime.Now:HH:mm}] 오류 — {ex.Message}";
                LastRunTime = DateTime.Now;
            }
            finally
            {
                IsRunning = false;
                StatusChanged?.Invoke(string.Empty);
            }
        }

        public static async Task<bool> RunForStoreAsync(string storeName, DateTime date)
        {
            if (!DatabaseService.IsDataConfigured) return false;

            try { await DatabaseService.InitializePosTablesAsync(); } catch { return false; }

            var configuredStores = LoadStoreConfigs();
            var store = configuredStores.Find(s => s.StoreName == storeName);

            bool ok = false;
            try
            {
                if (store != null && !string.IsNullOrWhiteSpace(store.ConnectionString))
                    ok = await TryCollectFromExternalAsync(store, date);
                else
                    ok = await DatabaseService.CheckAndRecordStoreSalesAsync(date, storeName);
            }
            catch { return false; }

            if (ok)
                try { await DatabaseService.DeleteSkippedStoreByNameAndDateAsync(date, storeName); } catch { }

            return ok;
        }

        private static async Task<bool> TryCollectFromExternalAsync(PosStoreConfig store, DateTime date)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(store.ConnectionString) || string.IsNullOrWhiteSpace(store.SalesQuery))
                    return false;

                decimal total = 0;
                await using var conn = new Microsoft.Data.SqlClient.SqlConnection(store.ConnectionString);
                await conn.OpenAsync();
                await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(store.SalesQuery, conn);
                cmd.Parameters.AddWithValue("@date", date.Date);
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != System.DBNull.Value)
                    total = Convert.ToDecimal(result);

                await DatabaseService.SaveSalesDataAsync(date, store.StoreName, total);
                return true;
            }
            catch { return false; }
        }

        public static List<PosStoreConfig> LoadStoreConfigs()
        {
            try
            {
                if (!File.Exists(StoresConfigFile)) return new List<PosStoreConfig>();
                var data = File.ReadAllText(StoresConfigFile);
                return JsonSerializer.Deserialize<List<PosStoreConfig>>(data) ?? new List<PosStoreConfig>();
            }
            catch { return new List<PosStoreConfig>(); }
        }

        // ── 외부 대진포스 쿼리.exe 자동 호출 ─────────────────────────────────
        private static async Task<(bool launched, bool success, string message)> TryRunExternalScraperAsync(DateTime date)
        {
            string? exePath = FindScraperExe();
            if (exePath == null)
                return (false, false, "대진포스 쿼리.exe를 찾을 수 없음");

            string statusFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WpfApp2", $"pos_scrape_{date:yyyyMMdd}_{Guid.NewGuid():N}.txt");

            try { Directory.CreateDirectory(Path.GetDirectoryName(statusFile)!); } catch { }
            try { if (File.Exists(statusFile)) File.Delete(statusFile); } catch { }

            System.Diagnostics.Process? process = null;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exePath)
                {
                    Arguments = $"--auto --date={date:yyyy-MM-dd} --status=\"{statusFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
                };
                process = System.Diagnostics.Process.Start(psi);
                if (process == null)
                    return (true, false, "Process.Start 실패");

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch { }
                    return (true, false, "스크래퍼 타임아웃 (15분)");
                }

                if (!File.Exists(statusFile))
                    return (true, false, $"스크래퍼 종료(코드 {process.ExitCode}) 그러나 상태파일 없음");

                var lines = await File.ReadAllLinesAsync(statusFile);
                bool ok = lines.Any(l => l.StartsWith("success=true", StringComparison.OrdinalIgnoreCase));
                string msg = lines.FirstOrDefault(l => l.StartsWith("message=", StringComparison.OrdinalIgnoreCase))?.Substring(8) ?? "";
                return (true, ok, ok ? (string.IsNullOrEmpty(msg) ? "수집 완료" : msg) : (string.IsNullOrEmpty(msg) ? $"실패(코드 {process.ExitCode})" : msg));
            }
            catch (Exception ex)
            {
                return (true, false, $"실행 오류: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(statusFile)) File.Delete(statusFile); } catch { }
                process?.Dispose();
            }
        }

        private static string? FindScraperExe()
        {
            const string ExeName = "대진포스 쿼리.exe";

            var candidates = new List<string>
            {
                // WpfApp2.exe와 같은 폴더에 배포한 경우
                Path.Combine(AppContext.BaseDirectory, ExeName),
                // 개발환경: WpfApp2\bin\Debug\net8.0-windows → ..\..\..\..\대진포스 쿼리\bin\Release|Debug
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "대진포스 쿼리", "bin", "Release", ExeName),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "대진포스 쿼리", "bin", "Debug", ExeName),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "대진포스 쿼리", "bin", "Release", ExeName),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "대진포스 쿼리", "bin", "Debug", ExeName),
                // 절대경로 폴백 (이 머신 한정)
                @"C:\권지훈\1. 개인자료\Visual Studio\WpfApp1\대진포스 쿼리\bin\Release\" + ExeName,
                @"C:\권지훈\1. 개인자료\Visual Studio\WpfApp1\대진포스 쿼리\bin\Debug\" + ExeName,
            };

            foreach (var c in candidates)
            {
                try
                {
                    var full = Path.GetFullPath(c);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
            return null;
        }
    }
}
