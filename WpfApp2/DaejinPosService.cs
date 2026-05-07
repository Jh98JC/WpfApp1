using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
                        if (ok) success++;
                        else { skipped++; try { await DatabaseService.AddSkippedStoreAsync(date, store.StoreName, "쿼리 실패"); } catch { } }
                    }
                }
                else
                {
                    // 자동 모드: DataConnectionString의 매출데이터 테이블에서 매장 목록 파악
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
                            if (ok) success++;
                            else { skipped++; try { await DatabaseService.AddSkippedStoreAsync(date, storeName, "해당일 데이터 없음"); } catch { } }
                        }
                        catch (Exception storeEx)
                        {
                            skipped++;
                            string reason = storeEx.Message.Length > 200 ? storeEx.Message[..200] : storeEx.Message;
                            try { await DatabaseService.AddSkippedStoreAsync(date, storeName, $"오류: {reason}"); } catch { }
                        }
                    }
                }

                if (success > 0)
                    try { await DatabaseService.AddCollectionLogAsync(date, success + skipped, success, skipped); } catch { }

                LastRunTime = DateTime.Now;
                if (success == 0 && skipped == 0)
                    LastRunInfo = $"[{DateTime.Now:HH:mm}] {date:yyyy-MM-dd} — 매장 데이터 없음";
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
    }
}
