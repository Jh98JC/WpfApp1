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

        // 현재 백그라운드로 실행 중인 대진포스 쿼리.exe의 PID — 보기 버튼에서 사용
        public static int? RunningProcessId { get; private set; }

        // 자동 수집 시 사용할 기준 날짜 — 한국 시간 기준 오전 8시 이전이면 그저께, 이후면 어제
        // 새벽 시간대(0~7시)에 어제 데이터가 아직 POS에 도착 안 한 경우를 회피하기 위함.
        public static DateTime GetAutoTargetDate()
        {
            TimeZoneInfo kst;
            try { kst = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time"); }
            catch { kst = TimeZoneInfo.Local; }

            var nowKst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, kst);
            int daysBack = nowKst.Hour < 8 ? 2 : 1;
            return nowKst.Date.AddDays(-daysBack);
        }

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

            var date = (targetDate ?? GetAutoTargetDate()).Date;

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
                    {
                        // pos_stores.json에 매장명만 지정된 경우
                        storeNames = configuredStores.ConvertAll(s => s.StoreName);
                    }
                    else
                    {
                        // 1차: store_mapping.json에 'lastSeenDate=오늘 날짜'인 매장만 추출 (= 윗표 Sch01에 등장한 활성 매장)
                        // 윗표에 없는 매장(폐업/이관 등)은 누락 후보에서 제외.
                        var dateStr = date.ToString("yyyy-MM-dd");
                        var mapping = StoreMappingService.Load();
                        storeNames = mapping.Stores
                            .Where(kv => string.Equals(kv.Value.LastSeenDate, dateStr, StringComparison.Ordinal))
                            .Select(kv => kv.Key)
                            .ToList();

                        // 매핑이 비어있거나 그 날짜 학습이 안 된 경우 → 폴백: 최근 7일 매출데이터 기반
                        if (storeNames.Count == 0)
                            storeNames = await DatabaseService.GetRecentStoreNamesAsync(7);
                    }

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

        // 재추출: 해당 날짜의 데이터를 모두 삭제하고 다시 수집한다.
        public static async Task ForceRunAsync(DateTime date)
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

            var d = date.Date;

            // 세션 캐시에서 이 날짜 제거 (이미 스크랩 시도했더라도 재시도되도록)
            lock (_scrapedDatesThisSession) _scrapedDatesThisSession.Remove(d);

            StatusChanged?.Invoke($"{d:yyyy-MM-dd} 데이터 정리 중...");
            try
            {
                await DatabaseService.InitializePosTablesAsync();
                int deleted = await DatabaseService.DeleteSalesDataForDateAsync(d);
                await DatabaseService.DeleteCollectionLogForDateAsync(d);
                await DatabaseService.DeleteSkippedStoresForDateAsync(d);
                LastRunInfo = $"[{DateTime.Now:HH:mm}] {d:yyyy-MM-dd} 기존 {deleted}건 삭제, 재수집 시작";
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                LastRunInfo = $"[{DateTime.Now:HH:mm}] {d:yyyy-MM-dd} 재추출 준비 오류 — {ex.Message}";
                LastRunTime = DateTime.Now;
                StatusChanged?.Invoke(string.Empty);
                return;
            }

            await RunAsync(d);
        }

        public static async Task<bool> RunForStoreAsync(string storeName, DateTime date)
        {
            if (!DatabaseService.IsDataConfigured) return false;

            try { await DatabaseService.InitializePosTablesAsync(); } catch { return false; }

            // 1) 별도 POS DB 연결이 정의되어 있으면 그쪽으로 직접 쿼리
            var configuredStores = LoadStoreConfigs();
            var store = configuredStores.Find(s => s.StoreName == storeName);
            if (store != null && !string.IsNullOrWhiteSpace(store.ConnectionString))
            {
                bool extOk = false;
                try { extOk = await TryCollectFromExternalAsync(store, date); } catch { return false; }
                if (extOk)
                    try { await DatabaseService.DeleteSkippedStoreByNameAndDateAsync(date, storeName); } catch { }
                return extOk;
            }

            // 2) 매장 → 계정 매핑이 있으면 → 대진포스 쿼리.exe를 단일 매장 모드로 재실행
            var mapping = StoreMappingService.Find(storeName);
            if (mapping != null && !string.IsNullOrEmpty(mapping.AccountId))
            {
                if (IsRunning)
                {
                    LastError = "다른 작업 진행 중";
                    return false;
                }
                IsRunning = true;
                StatusChanged?.Invoke($"{storeName} 재취합 중... ({mapping.AccountId} 계정)");
                try
                {
                    // 매장의 잔여 데이터가 잘못된 경우 대비해 먼저 삭제
                    try { await DatabaseService.DeleteSalesDataForStoreAsync(date, storeName); } catch { }

                    var (launched, success, msg) = await TryRunExternalScraperAsync(date, storeName);
                    if (!launched)
                    {
                        LastError = msg;
                        LastRunInfo = $"[{DateTime.Now:HH:mm}] {storeName} 재취합 실패 — {msg}";
                        try { await DatabaseService.UpdateSkippedStoreReasonAsync(date, storeName, $"재취합 실패: {msg}"); } catch { }
                        return false;
                    }
                    if (!success)
                    {
                        LastError = msg;
                        LastRunInfo = $"[{DateTime.Now:HH:mm}] {storeName} 재취합 실패 — {msg}";
                        try { await DatabaseService.UpdateSkippedStoreReasonAsync(date, storeName, $"재취합 실패: {msg}"); } catch { }
                        return false;
                    }
                }
                finally
                {
                    IsRunning = false;
                    StatusChanged?.Invoke(string.Empty);
                }

                // 3) 재수집 후 매출데이터에서 해당 매장 행이 들어왔는지 확인하고 StoreSales에 합계 기록
                bool ok = false;
                try { ok = await DatabaseService.CheckAndRecordStoreSalesAsync(date, storeName); } catch { return false; }
                if (ok)
                {
                    LastRunInfo = $"[{DateTime.Now:HH:mm}] {storeName} 재취합 완료";
                    try { await DatabaseService.DeleteSkippedStoreByNameAndDateAsync(date, storeName); } catch { }
                }
                else
                {
                    LastError = "스크래퍼 종료 후에도 매출데이터에 행이 없음 (클릭 실패 또는 로딩 타임아웃)";
                    LastRunInfo = $"[{DateTime.Now:HH:mm}] {storeName} 재취합 실패 — 데이터 미도착";
                    try { await DatabaseService.UpdateSkippedStoreReasonAsync(date, storeName, "재취합 실패: 스크래퍼 종료 후 데이터 미도착"); } catch { }
                }
                return ok;
            }

            // 3) 매핑이 없으면 fallback — 매출데이터에 이미 있는지만 확인
            bool fallbackOk = false;
            try { fallbackOk = await DatabaseService.CheckAndRecordStoreSalesAsync(date, storeName); } catch { return false; }
            if (fallbackOk)
                try { await DatabaseService.DeleteSkippedStoreByNameAndDateAsync(date, storeName); } catch { }
            else
                LastError = "매핑 없음 — 전체 수집을 한 번 실행해 매핑을 만든 뒤 재시도하세요";

            return fallbackOk;
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

        // ── 대진포스 쿼리.exe를 사용자 모드로 실행 (--auto 없이) ────────────
        public static (bool launched, string message) LaunchInteractive()
        {
            string? exePath = FindScraperExe();
            if (exePath == null)
                return (false, "대진포스 쿼리.exe를 찾을 수 없음");

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exePath)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
                };
                var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null)
                    return (false, "Process.Start 실패");
                ChildProcessTracker.AddProcess(proc);
                return (true, exePath);
            }
            catch (Exception ex)
            {
                return (false, $"실행 오류: {ex.Message}");
            }
        }

        // ── 외부 대진포스 쿼리.exe 자동 호출 ─────────────────────────────────
        private static Task<(bool launched, bool success, string message)> TryRunExternalScraperAsync(DateTime date)
            => TryRunExternalScraperAsync(date, storeName: null);

        private static async Task<(bool launched, bool success, string message)> TryRunExternalScraperAsync(DateTime date, string? storeName)
        {
            string? exePath = FindScraperExe();
            if (exePath == null)
                return (false, false, "대진포스 쿼리.exe를 찾을 수 없음");

            string statusFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WpfApp2", $"pos_scrape_{date:yyyyMMdd}_{Guid.NewGuid():N}.txt");

            try { Directory.CreateDirectory(Path.GetDirectoryName(statusFile)!); } catch { }
            try { if (File.Exists(statusFile)) File.Delete(statusFile); } catch { }

            string args = $"--auto --date={date:yyyy-MM-dd} --status=\"{statusFile}\"";
            if (!string.IsNullOrEmpty(storeName))
                args += $" --store=\"{storeName}\"";

            System.Diagnostics.Process? process = null;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exePath)
                {
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
                };
                process = System.Diagnostics.Process.Start(psi);
                if (process == null)
                    return (true, false, "Process.Start 실패");
                ChildProcessTracker.AddProcess(process);
                RunningProcessId = process.Id;

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
                RunningProcessId = null;
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
