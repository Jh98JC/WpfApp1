using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace 대진포스_쿼리
{
    public static class DbSaver
    {
        public const string ConnectionString =
            "Server=localhost\\SQLEXPRESS;Database=대진포스DB;Integrated Security=True;";

        /// <summary>
        /// _allAccountsData (탭 구분 행 목록)를 DB에 저장합니다.
        /// </summary>
        public static int Save(List<List<string>> allData, DateTime targetDate)
        {
            int savedRows = 0;

            using (var conn = new SqlConnection(ConnectionString))
            {
                conn.Open();

                foreach (var storeData in allData)
                {
                    string currentStore = null;

                    foreach (var line in storeData)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (line.StartsWith("매장명\t") || line.StartsWith("중분류\t")) continue;
                        if (line.All(c => c == '=')) continue;
                        if (line.Contains("[합계]") || line.Contains("[소 계]")) continue;

                        var cols = line.Split('\t');

                        // 열이 11개 이상이면 매장명 포함
                        int offset = cols.Length >= 11 ? 1 : 0;

                        if (offset == 1)
                            currentStore = cols[0].Trim();

                        if (cols.Length < offset + 9) continue;

                        string 중분류     = Get(cols, offset + 0);
                        string 메뉴명     = Get(cols, offset + 1);
                        string 메뉴코드   = Get(cols, offset + 2).TrimStart('\'');
                        int?   판매수량   = ParseInt(Get(cols, offset + 3));
                        int?   서비스수량 = ParseInt(Get(cols, offset + 4));
                        int?   총수량     = ParseInt(Get(cols, offset + 5));
                        long?  총매출액   = ParseLong(Get(cols, offset + 6));
                        long?  총할인액   = ParseLong(Get(cols, offset + 7));
                        long?  평균매출액 = ParseLong(Get(cols, offset + 8));
                        double? 매출비율  = ParseDouble(Get(cols, offset + 9).TrimEnd('%'));

                        if (string.IsNullOrWhiteSpace(메뉴명)) continue;

                        using (var cmd = new SqlCommand(@"
                            MERGE INTO 매출데이터 WITH (HOLDLOCK) AS T
                            USING (SELECT @날짜 AS 날짜, @매장명 AS 매장명,
                                          @중분류 AS 중분류, @메뉴명 AS 메뉴명, @메뉴코드 AS 메뉴코드) AS S
                                ON  T.날짜     = S.날짜
                                AND T.매장명   = S.매장명
                                AND ISNULL(T.중분류,  '') = ISNULL(S.중분류,  '')
                                AND ISNULL(T.메뉴명,  '') = ISNULL(S.메뉴명,  '')
                                AND ISNULL(T.메뉴코드,'') = ISNULL(S.메뉴코드,'')
                            WHEN MATCHED THEN
                                UPDATE SET
                                    수집일시    = GETDATE(),
                                    판매수량    = @판매수량,
                                    서비스수량  = @서비스수량,
                                    총수량      = @총수량,
                                    총매출액    = @총매출액,
                                    총할인액    = @총할인액,
                                    평균매출액  = @평균매출액,
                                    매출비율    = @매출비율
                            WHEN NOT MATCHED THEN
                                INSERT (날짜, 매장명, 중분류, 메뉴명, 메뉴코드,
                                        판매수량, 서비스수량, 총수량, 총매출액, 총할인액, 평균매출액, 매출비율)
                                VALUES (@날짜, @매장명, @중분류, @메뉴명, @메뉴코드,
                                        @판매수량, @서비스수량, @총수량, @총매출액, @총할인액, @평균매출액, @매출비율);",
                            conn))
                        {
                            cmd.Parameters.Add("@날짜",      SqlDbType.Date).Value        = (object)targetDate.Date ?? DBNull.Value;
                            cmd.Parameters.Add("@매장명",    SqlDbType.NVarChar, 100).Value = (object)currentStore ?? DBNull.Value;
                            cmd.Parameters.Add("@중분류",    SqlDbType.NVarChar, 100).Value = (object)중분류 ?? DBNull.Value;
                            cmd.Parameters.Add("@메뉴명",    SqlDbType.NVarChar, 200).Value = (object)메뉴명 ?? DBNull.Value;
                            cmd.Parameters.Add("@메뉴코드",  SqlDbType.NVarChar,  50).Value = (object)메뉴코드 ?? DBNull.Value;
                            cmd.Parameters.Add("@판매수량",  SqlDbType.Int).Value           = (object)판매수량  ?? DBNull.Value;
                            cmd.Parameters.Add("@서비스수량",SqlDbType.Int).Value           = (object)서비스수량 ?? DBNull.Value;
                            cmd.Parameters.Add("@총수량",    SqlDbType.Int).Value           = (object)총수량    ?? DBNull.Value;
                            cmd.Parameters.Add("@총매출액",  SqlDbType.BigInt).Value        = (object)총매출액  ?? DBNull.Value;
                            cmd.Parameters.Add("@총할인액",  SqlDbType.BigInt).Value        = (object)총할인액  ?? DBNull.Value;
                            cmd.Parameters.Add("@평균매출액",SqlDbType.BigInt).Value        = (object)평균매출액 ?? DBNull.Value;
                            cmd.Parameters.Add("@매출비율",  SqlDbType.Float).Value         = (object)매출비율  ?? DBNull.Value;

                            cmd.ExecuteNonQuery();
                            savedRows++;
                        }
                    }
                }
            }

            return savedRows;
        }

        /// <summary>연결 테스트</summary>
        public static bool TestConnection(out string errorMessage)
        {
            try
            {
                using (var conn = new SqlConnection(ConnectionString))
                {
                    conn.Open();
                    errorMessage = null;
                    return true;
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static string Get(string[] arr, int idx) =>
            idx < arr.Length ? arr[idx].Trim().Replace(",", "") : "";

        private static int?    ParseInt(string s)    => int.TryParse(s,    out var v) ? v : (int?)null;
        private static long?   ParseLong(string s)   => long.TryParse(s,   out var v) ? v : (long?)null;
        private static double? ParseDouble(string s) => double.TryParse(s, out var v) ? v : (double?)null;
    }
}
