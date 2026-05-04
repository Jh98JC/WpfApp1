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
    }
}
