using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace 대진포스_쿼리
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
        public Dictionary<string, StoreMappingEntry> Stores { get; set; } = new Dictionary<string, StoreMappingEntry>();
    }

    // WpfApp2와 공유되는 매장 → (계정, 행 인덱스) 매핑 파일.
    // 위치: %AppData%\대진포스쿼리\store_mapping.json
    public static class StoreMappingService
    {
        public static readonly string MappingFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "대진포스쿼리", "store_mapping.json");

        private static readonly JsonSerializerOptions JsonOpt = new JsonSerializerOptions
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
                Directory.CreateDirectory(Path.GetDirectoryName(MappingFile));
                File.WriteAllText(MappingFile, JsonSerializer.Serialize(data, JsonOpt));
            }
            catch { }
        }

        public static StoreMappingEntry Find(string storeName)
        {
            if (string.IsNullOrEmpty(storeName)) return null;
            var data = Load();
            return data.Stores.TryGetValue(storeName, out var entry) ? entry : null;
        }

        // 한 계정의 수집 결과에서 매장 → 행 매핑을 추출해 저장한다.
        // accountStoreNames: 그 계정 데이터에서 등장한 매장명을 등장 순서대로 나열한 리스트
        public static void UpdateFromAccount(string accountId, DateTime date, IList<string> accountStoreNames)
        {
            if (string.IsNullOrEmpty(accountId) || accountStoreNames == null) return;

            var data = Load();
            string dateStr = date.ToString("yyyy-MM-dd");
            for (int i = 0; i < accountStoreNames.Count; i++)
            {
                var store = (accountStoreNames[i] ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(store)) continue;
                data.Stores[store] = new StoreMappingEntry
                {
                    AccountId    = accountId,
                    RowIndex     = i,
                    LastSeenDate = dateStr
                };
            }
            Save(data);
        }

        // 한 계정의 outList(List<List<string>>: 탭 구분 TSV 행 묶음)에서
        // 등장한 매장명을 등장 순서대로 (중복 제거) 추출한다.
        // DbSaver.Save와 동일한 규칙: cols.Length >= 11 이면 cols[0]이 매장명.
        public static List<string> ExtractStoreNamesFromAccountData(List<List<string>> accountData)
        {
            var seen = new HashSet<string>();
            var result = new List<string>();
            if (accountData == null) return result;

            foreach (var storeData in accountData)
            {
                if (storeData == null) continue;
                foreach (var line in storeData)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("매장명\t") || line.StartsWith("중분류\t")) continue;
                    if (line.Contains("[합계]") || line.Contains("[소 계]")) continue;

                    bool allEq = true;
                    foreach (var c in line) { if (c != '=') { allEq = false; break; } }
                    if (allEq) continue;

                    var cols = line.Split('\t');
                    if (cols.Length >= 11)
                    {
                        var name = (cols[0] ?? string.Empty).Trim();
                        if (!string.IsNullOrEmpty(name) && seen.Add(name))
                            result.Add(name);
                    }
                }
            }
            return result;
        }
    }
}
