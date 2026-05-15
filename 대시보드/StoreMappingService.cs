using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfApp2
{
    public class StoreMappingEntry
    {
        [JsonPropertyName("accountId")] public string AccountId { get; set; } = string.Empty;
        [JsonPropertyName("rowIndex")]  public int    RowIndex  { get; set; }
        [JsonPropertyName("lastSeenDate")] public string LastSeenDate { get; set; } = string.Empty;
    }

    public class StoreMappingFile
    {
        [JsonPropertyName("stores")]
        public Dictionary<string, StoreMappingEntry> Stores { get; set; } = new();
    }

    // 매장명 → (계정, 행 인덱스) 매핑 — 대진포스 쿼리와 공유되는 파일.
    // 위치: %AppData%\대진포스쿼리\store_mapping.json
    public static class StoreMappingService
    {
        public static readonly string MappingFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "대진포스쿼리", "store_mapping.json");

        private static readonly JsonSerializerOptions JsonOpt = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static StoreMappingFile Load()
        {
            try
            {
                if (!File.Exists(MappingFile)) return new StoreMappingFile();
                var json = File.ReadAllText(MappingFile);
                return JsonSerializer.Deserialize<StoreMappingFile>(json) ?? new StoreMappingFile();
            }
            catch { return new StoreMappingFile(); }
        }

        public static void Save(StoreMappingFile data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MappingFile)!);
                File.WriteAllText(MappingFile, JsonSerializer.Serialize(data, JsonOpt));
            }
            catch { }
        }

        public static StoreMappingEntry? Find(string storeName)
        {
            var data = Load();
            return data.Stores.TryGetValue(storeName, out var entry) ? entry : null;
        }

        public static List<string> FindAccountsForStores(IEnumerable<string> storeNames)
        {
            var data = Load();
            var accounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in storeNames)
                if (data.Stores.TryGetValue(n, out var e) && !string.IsNullOrEmpty(e.AccountId))
                    accounts.Add(e.AccountId);
            return accounts.ToList();
        }
    }
}
