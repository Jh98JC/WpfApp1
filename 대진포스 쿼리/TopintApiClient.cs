using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace 대진포스_쿼리
{
    public class StoreInfo
    {
        public string Branch { get; set; }      // 매장 코드 (예: CSU188510021)
        public string BranchName { get; set; }  // 매장명 (예: (법)대구성서)
    }

    // 매장매출현황 페이지의 jqGrid AJAX 엔드포인트를 직접 호출하는 클라이언트.
    // WebView2 로그인 → 쿠키 추출 → 본 클라이언트에 전달하여 사용.
    public class TopintApiClient : IDisposable
    {
        const string Base    = "https://asp.topint.co.kr";
        const string Referer = Base + "/asp_office/sale/menuAnal.asp?frameID=fra_menuAnal";

        private readonly HttpClient _client;
        private readonly HttpClientHandler _handler;

        public TopintApiClient(IEnumerable<(string Name, string Value, string Domain, string Path)> cookies)
        {
            var container = new CookieContainer();
            foreach (var c in cookies)
            {
                try
                {
                    string domain = string.IsNullOrEmpty(c.Domain) ? "asp.topint.co.kr" : c.Domain.TrimStart('.');
                    string path = string.IsNullOrEmpty(c.Path) ? "/" : c.Path;
                    var ck = new Cookie(c.Name, c.Value ?? string.Empty, path, domain);
                    container.Add(ck);
                }
                catch { /* 잘못된 쿠키는 스킵 */ }
            }

            _handler = new HttpClientHandler
            {
                CookieContainer = container,
                UseCookies = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _client = new HttpClient(_handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _client.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/javascript, */*; q=0.01");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ko-KR,ko;q=0.9,en-US;q=0.8,en;q=0.7");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/147.0.0.0 Safari/537.36");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", Referer);
        }

        // 매장 목록 (Sch01): branch=빈값으로 전체 매장 받기
        public async Task<string> FetchStoreListJsonAsync(DateTime start, DateTime end)
        {
            long nd = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string url = Base + "/asp_office/sale/menuAnal_Sch01.asp" +
                $"?opendate_s={start:yyyyMMdd}&opendate_e={end:yyyyMMdd}" +
                "&brandcode=&branch=&gcode=&mcode=&mname=&tamount=" +
                "&okURL=.%2FmenuAnal_Sch01.asp&page=1&_search=false" +
                $"&nd={nd}" +
                "&rows=10000&sidx=&sord=asc";
            return await _client.GetStringAsync(url);
        }

        // 특정 매장 상세 (Sch02)
        public async Task<string> FetchStoreDetailJsonAsync(string branch, DateTime start, DateTime end)
        {
            long nd = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string url = Base + "/asp_office/sale/menuAnal_Sch02.asp" +
                $"?opendate_s={start:yyyyMMdd}&opendate_e={end:yyyyMMdd}" +
                $"&brandcode=&branch={Uri.EscapeDataString(branch ?? string.Empty)}&gcode=&mcode=&mname=&tamount=" +
                "&okURL=.%2FmenuAnal_Sch02.asp&page=1&_search=false" +
                $"&nd={nd}" +
                "&rows=100000&sidx=&sord=asc";
            return await _client.GetStringAsync(url);
        }

        // Sch01 응답 → 매장 목록 파싱
        public static List<StoreInfo> ParseStoreList(string json)
        {
            var list = new List<StoreInfo>();
            if (string.IsNullOrWhiteSpace(json)) return list;

            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    if (!doc.RootElement.TryGetProperty("rows", out var rows) ||
                        rows.ValueKind != JsonValueKind.Array)
                        return list;

                    foreach (var row in rows.EnumerateArray())
                    {
                        string branch = TryGetString(row, "branch");
                        if (string.IsNullOrEmpty(branch)) continue;
                        string branch2 = TryGetString(row, "branch2");
                        list.Add(new StoreInfo { Branch = branch, BranchName = branch2 });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParseStoreList] {ex.Message}");
            }
            return list;
        }

        // Sch02 응답 → TSV 행 리스트 (DbSaver 기대 포맷)
        // 형식: 매장명\t중분류\t메뉴명\t메뉴코드\t판매수량\t서비스수량\t총수량\t총매출액\t총할인액\t평균매출액\t매출비율
        public static List<string> ParseStoreDetailToTsv(string json, string storeName)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    if (!doc.RootElement.TryGetProperty("rows", out var rows) ||
                        rows.ValueKind != JsonValueKind.Array)
                        return result;

                    foreach (var row in rows.EnumerateArray())
                    {
                        // 합계/소계 행 스킵
                        string gubun = TryGetString(row, "gubun");
                        if (string.Equals(gubun, "T", StringComparison.OrdinalIgnoreCase)) continue;

                        string mname = TryGetString(row, "mname");
                        if (string.IsNullOrWhiteSpace(mname)) continue;

                        string gname = TryGetString(row, "gname");
                        string mcode = TryGetString(row, "mcode");
                        string sqty = TryGetNumeric(row, "sqty");
                        string svc  = TryGetNumeric(row, "chk_svc");
                        string tqty = TryGetNumeric(row, "tqty");
                        string tamt = TryGetNumeric(row, "tamount");
                        if (string.IsNullOrEmpty(tamt)) tamt = TryGetNumeric(row, "salesamt");
                        string dcamt = TryGetNumeric(row, "dcamount");
                        string aveAmt = TryGetNumeric(row, "aveTamount");
                        if (string.IsNullOrEmpty(aveAmt)) aveAmt = TryGetNumeric(row, "avetamount");
                        string pct = TryGetNumeric(row, "amountPercent");
                        if (string.IsNullOrEmpty(pct)) pct = TryGetNumeric(row, "amountpercent");

                        string line = string.IsNullOrEmpty(storeName)
                            ? $"{gname}\t{mname}\t{mcode}\t{sqty}\t{svc}\t{tqty}\t{tamt}\t{dcamt}\t{aveAmt}\t{pct}"
                            : $"{storeName}\t{gname}\t{mname}\t{mcode}\t{sqty}\t{svc}\t{tqty}\t{tamt}\t{dcamt}\t{aveAmt}\t{pct}";

                        result.Add(line);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParseStoreDetail] {ex.Message}");
            }
            return result;
        }

        private static string TryGetString(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var v)) return string.Empty;
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? string.Empty;
            if (v.ValueKind == JsonValueKind.Null) return string.Empty;
            return v.ToString();
        }

        private static string TryGetNumeric(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var v)) return string.Empty;
            if (v.ValueKind == JsonValueKind.Number) return v.ToString();
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? string.Empty;
            return string.Empty;
        }

        public void Dispose()
        {
            _client?.Dispose();
            _handler?.Dispose();
        }
    }
}
