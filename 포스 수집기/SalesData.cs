using System;

namespace 대진포스_쿼리
{
    /// <summary>
    /// 판매 데이터 모델
    /// </summary>
    public class SalesData
    {
        public string 매장명 { get; set; }
        public string 총매출액 { get; set; }
        public string 판매수량 { get; set; }
        public string 서비스수량 { get; set; }
        public string 총수량 { get; set; }
        public string 공급가 { get; set; }
        public string 부가세 { get; set; }

        public override string ToString()
        {
            return $"{매장명}\t{총매출액}\t{판매수량}\t{서비스수량}\t{총수량}\t{공급가}\t{부가세}";
        }
    }
}
