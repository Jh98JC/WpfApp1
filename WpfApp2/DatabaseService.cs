using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace WpfApp2
{
    public class UserInfo
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class PendingUser : System.ComponentModel.INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; }

        private string? _processedStatus;
        public string? ProcessedStatus
        {
            get => _processedStatus;
            set
            {
                _processedStatus = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ProcessedStatus)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    public class CollectionLogEntry
    {
        public DateTime CollectionDate { get; set; }
        public DateTime CollectedAt { get; set; }
        public int TotalStores { get; set; }
        public int SuccessCount { get; set; }
        public int SkippedCount { get; set; }
        public string DisplayText => SkippedCount > 0
            ? $"{CollectionDate:yyyy-MM-dd}  매장매출자료 취합완료  (누락 {SkippedCount}건)"
            : $"{CollectionDate:yyyy-MM-dd}  매장매출자료 취합완료";
    }

    public class SkippedStoreEntry
    {
        public int Id { get; set; }
        public DateTime CollectionDate { get; set; }
        public string StoreName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string DisplayText => $"{CollectionDate:yyyy-MM-dd}  {StoreName}";
    }

    public static class DatabaseService
    {
        private static readonly string ConfigFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WpfApp2");
        private static readonly string ConfigFile = System.IO.Path.Combine(ConfigFolder, "db_config.dat");
        private static readonly string LegacyConfigFile = System.IO.Path.Combine(ConfigFolder, "db_config.json");

        public static string ConnectionString { get; private set; } = string.Empty;
        public static string DataConnectionString { get; private set; } = string.Empty;

        public static bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
        public static bool IsDataConfigured => !string.IsNullOrWhiteSpace(DataConnectionString);

        public static void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var encrypted = File.ReadAllBytes(ConfigFile);
                    var json = Encoding.UTF8.GetString(
                        ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
                    var doc = JsonDocument.Parse(json);
                    ConnectionString = doc.RootElement.GetProperty("connectionString").GetString() ?? string.Empty;
                    if (doc.RootElement.TryGetProperty("dataConnectionString", out var dataProp))
                        DataConnectionString = dataProp.GetString() ?? string.Empty;
                }
                else if (File.Exists(LegacyConfigFile))
                {
                    // 기존 평문 파일 → 암호화 마이그레이션
                    var json = File.ReadAllText(LegacyConfigFile);
                    var doc = JsonDocument.Parse(json);
                    ConnectionString = doc.RootElement.GetProperty("connectionString").GetString() ?? string.Empty;
                    if (doc.RootElement.TryGetProperty("dataConnectionString", out var dataProp))
                        DataConnectionString = dataProp.GetString() ?? string.Empty;
                    SaveAll();
                    File.Delete(LegacyConfigFile);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[DatabaseService.LoadConfig] {ex.Message}"); }
        }

        public static void SaveConnectionString(string connectionString)
        {
            ConnectionString = SanitizeConnectionString(connectionString);
            SaveAll();
        }

        public static void SaveDataConnectionString(string connectionString)
        {
            DataConnectionString = SanitizeConnectionString(connectionString);
            SaveAll();
        }

        private static void SaveAll()
        {
            Directory.CreateDirectory(ConfigFolder);
            var json = JsonSerializer.Serialize(new { connectionString = ConnectionString, dataConnectionString = DataConnectionString });
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(ConfigFile, encrypted);
        }

        public static async Task<bool> TestConnectionAsync(string connectionString)
        {
            try
            {
                await using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static async Task InitializeDatabaseAsync()
        {
            const string sql = """
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Users' AND xtype='U')
                CREATE TABLE Users (
                    Id          INT IDENTITY(1,1) PRIMARY KEY,
                    Username    NVARCHAR(50)  NOT NULL UNIQUE,
                    PasswordHash NVARCHAR(256) NOT NULL,
                    DisplayName NVARCHAR(100) NOT NULL DEFAULT '',
                    Role        NVARCHAR(20)  NOT NULL DEFAULT 'user',
                    IsApproved  BIT           NOT NULL DEFAULT 0,
                    CreatedAt   DATETIME2     NOT NULL DEFAULT GETDATE()
                );
                """;
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        public static async Task<bool> HasAnyUserAsync()
        {
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT COUNT(*) FROM Users", conn);
            return (int)(await cmd.ExecuteScalarAsync())! > 0;
        }

        public static async Task CreateMasterAsync(string username, string displayName, string password)
        {
            string hash = HashPassword(password);
            const string sql = """
                INSERT INTO Users (Username, PasswordHash, DisplayName, Role, IsApproved)
                VALUES (@u, @p, @d, 'master', 1)
                """;
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@p", hash);
            cmd.Parameters.AddWithValue("@d", displayName);
            await cmd.ExecuteNonQueryAsync();
        }

        public static async Task<(bool Success, string Role, int UserId, string DisplayName)> AuthenticateAsync(
            string username, string password)
        {
            string hash = HashPassword(password);
            const string sql = """
                SELECT Id, Role, DisplayName
                FROM Users
                WHERE Username = @u AND PasswordHash = @p AND IsApproved = 1
                """;
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@p", hash);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                int id = reader.GetInt32(0);
                string role = reader.GetString(1);
                string displayName = reader.GetString(2);
                return (true, role, id, displayName);
            }
            return (false, string.Empty, 0, string.Empty);
        }

        public static async Task<List<UserInfo>> GetAllUsersAsync()
        {
            const string sql = """
                SELECT Id, Username, DisplayName, Role, CreatedAt
                FROM Users WHERE IsApproved = 1
                ORDER BY CreatedAt DESC
                """;
            var list = new List<UserInfo>();
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new UserInfo
                {
                    Id = reader.GetInt32(0),
                    Username = reader.GetString(1),
                    DisplayName = reader.GetString(2),
                    Role = reader.GetString(3),
                    CreatedAt = reader.GetDateTime(4)
                });
            }
            return list;
        }

        public static async Task<List<PendingUser>> GetPendingUsersAsync()
        {
            const string sql = """
                SELECT Id, Username, DisplayName, CreatedAt
                FROM Users WHERE IsApproved = 0
                ORDER BY CreatedAt ASC
                """;
            var list = new List<PendingUser>();
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new PendingUser
                {
                    Id = reader.GetInt32(0),
                    Username = reader.GetString(1),
                    DisplayName = reader.GetString(2),
                    RequestedAt = reader.GetDateTime(3)
                });
            }
            return list;
        }

        public static async Task ApproveUserAsync(int userId)
        {
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("UPDATE Users SET IsApproved = 1 WHERE Id = @id", conn);
            cmd.Parameters.AddWithValue("@id", userId);
            await cmd.ExecuteNonQueryAsync();
        }

        public static async Task DeleteUserAsync(int userId)
        {
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("DELETE FROM Users WHERE Id = @id AND Role <> 'master'", conn);
            cmd.Parameters.AddWithValue("@id", userId);
            await cmd.ExecuteNonQueryAsync();
        }

        public static async Task<bool> RegisterRequestAsync(string username, string displayName, string password)
        {
            string hash = HashPassword(password);
            const string sql = """
                IF NOT EXISTS (SELECT 1 FROM Users WHERE Username = @u)
                BEGIN
                    INSERT INTO Users (Username, PasswordHash, DisplayName, Role, IsApproved)
                    VALUES (@u, @p, @d, 'user', 0)
                    SELECT 1
                END
                ELSE SELECT 0
                """;
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@p", hash);
            cmd.Parameters.AddWithValue("@d", displayName);
            var result = await cmd.ExecuteScalarAsync();
            return result != null && (int)result == 1;
        }

        private static string SanitizeConnectionString(string cs)
        {
            // Azure Portal 복사 시 따옴표가 포함될 수 있음 → 각 값의 따옴표 제거
            // 예: Authentication="Active Directory Default" → Authentication=Active Directory Default
            var result = new System.Text.StringBuilder();
            bool inKey = true;
            foreach (char c in cs)
            {
                if (c == ';') inKey = true;
                if (c == '=') inKey = false;
                if (!inKey && c == '"') continue;
                result.Append(c);
            }
            return result.ToString().Trim();
        }

        private static string HashPassword(string password)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            return Convert.ToHexString(bytes);
        }

        // ── POS 취합 테이블 ──────────────────────────────────────────────────

        public static async Task InitializePosTablesAsync()
        {
            const string sql = """
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='CollectionLogs' AND xtype='U')
                CREATE TABLE CollectionLogs (
                    Id             INT IDENTITY(1,1) PRIMARY KEY,
                    CollectionDate DATE      NOT NULL,
                    CollectedAt    DATETIME2 NOT NULL DEFAULT GETDATE(),
                    TotalStores    INT       NOT NULL DEFAULT 0,
                    SuccessCount   INT       NOT NULL DEFAULT 0,
                    SkippedCount   INT       NOT NULL DEFAULT 0
                );
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SkippedStores' AND xtype='U')
                CREATE TABLE SkippedStores (
                    Id             INT IDENTITY(1,1) PRIMARY KEY,
                    CollectionDate DATE           NOT NULL,
                    StoreName      NVARCHAR(100)  NOT NULL,
                    Reason         NVARCHAR(500)  NULL,
                    CreatedAt      DATETIME2      NOT NULL DEFAULT GETDATE()
                );
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='StoreSales' AND xtype='U')
                CREATE TABLE StoreSales (
                    Id          INT IDENTITY(1,1) PRIMARY KEY,
                    SaleDate    DATE           NOT NULL,
                    StoreName   NVARCHAR(100)  NOT NULL,
                    TotalAmount DECIMAL(18,2)  NOT NULL DEFAULT 0,
                    CreatedAt   DATETIME2      NOT NULL DEFAULT GETDATE()
                );
                """;
            await using var conn = new SqlConnection(DataConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        public static async Task<bool> HasCollectionForDateAsync(DateTime date)
        {
            try
            {
                // 성공 건수가 1개 이상인 로그가 있을 때만 "이미 취합됨"으로 판단
                const string sql = "SELECT COUNT(*) FROM CollectionLogs WHERE CollectionDate = @date AND SuccessCount > 0";
                await using var conn = new SqlConnection(DataConnectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@date", date.Date);
                return (int)(await cmd.ExecuteScalarAsync())! > 0;
            }
            catch { return false; }
        }

        public static async Task SaveSalesDataAsync(DateTime date, string storeName, decimal totalAmount)
        {
            const string sql = """
                IF NOT EXISTS (SELECT 1 FROM StoreSales WHERE SaleDate = @date AND StoreName = @store)
                    INSERT INTO StoreSales (SaleDate, StoreName, TotalAmount) VALUES (@date, @store, @amount)
                ELSE
                    UPDATE StoreSales SET TotalAmount = @amount WHERE SaleDate = @date AND StoreName = @store
                """;
            await using var conn = new SqlConnection(DataConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@date", date.Date);
            cmd.Parameters.AddWithValue("@store", storeName);
            cmd.Parameters.AddWithValue("@amount", totalAmount);
            await cmd.ExecuteNonQueryAsync();
        }

        public static async Task AddCollectionLogAsync(DateTime date, int total, int success, int skipped)
        {
            const string sql = """
                INSERT INTO CollectionLogs (CollectionDate, TotalStores, SuccessCount, SkippedCount)
                VALUES (@date, @total, @success, @skipped)
                """;
            await using var conn = new SqlConnection(DataConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@date", date.Date);
            cmd.Parameters.AddWithValue("@total", total);
            cmd.Parameters.AddWithValue("@success", success);
            cmd.Parameters.AddWithValue("@skipped", skipped);
            await cmd.ExecuteNonQueryAsync();
        }

        public static async Task<List<CollectionLogEntry>> GetCollectionLogsAsync()
        {
            const string sql = """
                SELECT CollectionDate, CollectedAt, TotalStores, SuccessCount, SkippedCount
                FROM CollectionLogs ORDER BY CollectionDate DESC
                """;
            var list = new List<CollectionLogEntry>();
            await using var conn = new SqlConnection(DataConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new CollectionLogEntry
                {
                    CollectionDate = reader.GetDateTime(0),
                    CollectedAt    = reader.GetDateTime(1),
                    TotalStores    = reader.GetInt32(2),
                    SuccessCount   = reader.GetInt32(3),
                    SkippedCount   = reader.GetInt32(4)
                });
            }
            return list;
        }

        public static async Task AddSkippedStoreAsync(DateTime date, string storeName, string reason)
        {
            const string sql = """
                IF NOT EXISTS (SELECT 1 FROM SkippedStores WHERE CollectionDate = @date AND StoreName = @store)
                    INSERT INTO SkippedStores (CollectionDate, StoreName, Reason) VALUES (@date, @store, @reason)
                """;
            await using var conn = new SqlConnection(DataConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@date", date.Date);
            cmd.Parameters.AddWithValue("@store", storeName);
            cmd.Parameters.AddWithValue("@reason", reason);
            await cmd.ExecuteNonQueryAsync();
        }

        public static async Task<List<SkippedStoreEntry>> GetSkippedStoresAsync()
        {
            const string sql = """
                SELECT Id, CollectionDate, StoreName, Reason, CreatedAt
                FROM SkippedStores ORDER BY CollectionDate DESC, StoreName
                """;
            var list = new List<SkippedStoreEntry>();
            await using var conn = new SqlConnection(DataConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new SkippedStoreEntry
                {
                    Id             = reader.GetInt32(0),
                    CollectionDate = reader.GetDateTime(1),
                    StoreName      = reader.GetString(2),
                    Reason         = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    CreatedAt      = reader.GetDateTime(4)
                });
            }
            return list;
        }

        public static async Task DeleteSkippedStoreByNameAndDateAsync(DateTime date, string storeName)
        {
            const string sql = "DELETE FROM SkippedStores WHERE CollectionDate = @date AND StoreName = @store";
            await using var conn = new SqlConnection(DataConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@date", date.Date);
            cmd.Parameters.AddWithValue("@store", storeName);
            await cmd.ExecuteNonQueryAsync();
        }

        // ── 자동 모드: 매출데이터 테이블에서 직접 취합 ─────────────────────

        public static async Task<List<string>> GetRecentStoreNamesAsync(int days = 7)
        {
            var list = new List<string>();
            try
            {
                var cutoff = DateTime.Today.AddDays(-days);
                const string sql = """
                    SELECT DISTINCT 매장명 FROM 매출데이터
                    WHERE 날짜 >= @cutoff AND 매장명 IS NOT NULL AND 매장명 <> ''
                    ORDER BY 매장명
                    """;
                await using var conn = new SqlConnection(DataConnectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@cutoff", cutoff.Date);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    list.Add(reader.GetString(0));
            }
            catch { }
            return list;
        }

        public static async Task<bool> CheckAndRecordStoreSalesAsync(DateTime date, string storeName)
        {
            const string sql = """
                SELECT ISNULL(SUM(총매출액), 0), COUNT(*)
                FROM 매출데이터
                WHERE CAST(날짜 AS DATE) = @date AND 매장명 = @store
                """;
            int count;
            decimal total;
            await using (var conn = new SqlConnection(DataConnectionString))
            {
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@date", date.Date);
                cmd.Parameters.AddWithValue("@store", storeName);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return false;
                count = reader.GetInt32(1);
                if (count == 0) return false;
                total = Convert.ToDecimal(reader.GetValue(0));
            }
            await SaveSalesDataAsync(date, storeName, total);
            return true;
        }
    }
}
