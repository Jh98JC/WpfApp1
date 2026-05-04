using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WpfApp2
{
    public static class AutoLoginService
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WpfApp2", "autologin.dat");

        public static void Save(string username, string password)
        {
            var json = JsonSerializer.Serialize(new { username, password });
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllBytes(FilePath, encrypted);
        }

        public static (string Username, string Password)? Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var encrypted = File.ReadAllBytes(FilePath);
                var json = Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
                var doc = JsonDocument.Parse(json);
                return (
                    doc.RootElement.GetProperty("username").GetString() ?? "",
                    doc.RootElement.GetProperty("password").GetString() ?? ""
                );
            }
            catch { return null; }
        }

        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }

        public static bool Exists => File.Exists(FilePath);
    }
}
