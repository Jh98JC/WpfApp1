using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Web.WebView2.Core;

namespace 대진포스_쿼리
{
    public class WebScraper
    {
        private HttpClient _httpClient;
        private CookieContainer _cookieContainer;
        private string _customSelector = null;

        public WebScraper()
        {
            _cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler
            {
                CookieContainer = _cookieContainer,
                UseCookies = true,
                AllowAutoRedirect = true
            };
            _httpClient = new HttpClient(handler);
        }

        /// <summary>
        /// 사용자가 선택한 CSS 선택자 설정
        /// </summary>
        public void SetSelector(string selector)
        {
            _customSelector = selector;
        }

        /// <summary>
        /// 로그인 수행
        /// </summary>
        public async Task<bool> LoginAsync(string userId, string password)
        {
            try
            {
                // 로그인 페이지에서 필요한 폼 데이터 확인 필요
                var loginData = new Dictionary<string, string>
                {
                    // 실제 폼 필드명은 개발자 도구(F12)로 확인 필요
                    { "mb_id", userId },      // 예시: 실제 필드명으로 변경 필요
                    { "mb_password", password } // 예시: 실제 필드명으로 변경 필요
                };

                var content = new FormUrlEncodedContent(loginData);
                var response = await _httpClient.PostAsync("https://topint.co.kr/member/login", content);

                // 로그인 성공 확인 (리다이렉트 또는 쿠키 확인)
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                // 에러 처리
                System.Diagnostics.Debug.WriteLine($"Login failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// WebView2에서 가져온 쿠키를 HttpClient에 설정
        /// </summary>
        public void SetCookies(System.Collections.Generic.IReadOnlyList<CoreWebView2Cookie> cookies)
        {
            foreach (var cookie in cookies)
            {
                var netCookie = new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain);

                // 쿠키를 CookieContainer에 추가
                try
                {
                    _cookieContainer.Add(new Uri("https://asp.topint.co.kr"), netCookie);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Cookie add failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 메인 페이지 HTML 가져오기
        /// </summary>
        public async Task<string> GetMainPageAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync("https://asp.topint.co.kr/asp_office/asp_main/main.asp");
                return response;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Get page failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// HTML 파싱하여 원하는 데이터 추출
        /// </summary>
        /// <param name="html">파싱할 HTML</param>
        /// <param name="storeName">매장명 (옵션 - 자동수집 시 각 행에 추가됨)</param>
        public List<string> ParseData(string html, string storeName = null)
        {
            var result = new List<string>();

            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // jqGrid 테이블에서 데이터 추출
                var tableRows = doc.DocumentNode.SelectNodes("//table[@id='jqGrid02']//tr[@role='row' and not(contains(@class,'jqgfirstrow'))]");

                if (tableRows != null && tableRows.Count > 0)
                {
                    // 헤더 추가 (매장명 포함)
                    if (!string.IsNullOrWhiteSpace(storeName))
                    {
                        result.Add("매장명\t중분류\t메뉴명\t메뉴코드\t판매수량\t서비스수량\t총수량\t총매출액\t총할인액\t평균매출액\t매출비율");
                    }
                    else
                    {
                        result.Add("중분류\t메뉴명\t메뉴코드\t판매수량\t서비스수량\t총수량\t총매출액\t총할인액\t평균매출액\t매출비율");
                    }
                    result.Add("".PadRight(120, '='));

                    foreach (var row in tableRows)
                    {
                        // footrow는 합계 행이므로 제외
                        if (row.GetAttributeValue("class", "").Contains("footrow"))
                            continue;

                        var cells = row.SelectNodes(".//td[@role='gridcell']");
                        if (cells == null || cells.Count < 5)
                            continue;

                        try
                        {
                            // 순서대로 셀 추출 (aria-describedby와 순서 기반)
                            string 중분류 = "";
                            string 메뉴명 = "";
                            string 메뉴코드 = "";
                            string 판매수량 = "";
                            string 서비스수량 = "";
                            string 총수량 = "";
                            string 총매출액 = "";
                            string 총할인액 = "";
                            string 평균매출액 = "";
                            string 매출비율 = "";

                            // 방법 1: aria-describedby 속성으로 매칭 시도
                            bool foundByAria = false;
                            int cellIndex = 0;
                            foreach (var cell in cells)
                            {
                                var ariaDesc = cell.GetAttributeValue("aria-describedby", "").ToLower();
                                if (!string.IsNullOrEmpty(ariaDesc))
                                {
                                    foundByAria = true;

                                    // 실제 값 추출 (title 속성 우선, 없으면 innerText)
                                    var cellTitle = cell.GetAttributeValue("title", "");
                                    var cellText = !string.IsNullOrWhiteSpace(cellTitle) ? cellTitle : cell.InnerText.Trim();

                                    // 디버깅: 각 셀의 aria-describedby와 값 출력
                                    System.Diagnostics.Debug.WriteLine($"  [Cell {cellIndex}] aria-describedby='{ariaDesc}', value='{cellText.Substring(0, Math.Min(20, cellText.Length))}'");

                                    // 메뉴코드는 콤마 제거하지 않고 원본 유지
                                    bool isMenuCode = ariaDesc.Contains("mcode");

                                    // 숫자 포맷 제거 (콤마 등) - 메뉴코드 제외
                                    if (!isMenuCode)
                                    {
                                        cellText = cellText.Replace(",", "").Trim();
                                    }
                                    else
                                    {
                                        cellText = cellText.Trim();
                                    }

                                    // 정확한 aria-describedby 속성 이름으로 매칭
                                    // jqGrid02_gname = 중분류
                                    // jqGrid02_mname = 메뉴명
                                    // jqGrid02_mcode = 메뉴코드
                                    // jqGrid02_sqty = 판매수량
                                    // jqGrid02_chk_svc = 서비스수량
                                    // jqGrid02_tqty = 총수량
                                    // jqGrid02_salesamt = 총매출액
                                    // jqGrid02_dcamount = 총할인액
                                    // jqGrid02_aveTamount = 평균매출액
                                    // jqGrid02_amountPercent = 매출비율
                                    if (ariaDesc.Contains("gname"))
                                        중분류 = cellText;
                                    else if (ariaDesc.Contains("mname"))
                                        메뉴명 = cellText;
                                    else if (ariaDesc.Contains("mcode"))
                                        메뉴코드 = cellText; // 문자열로 유지
                                    else if (ariaDesc.Contains("sqty"))
                                        판매수량 = cellText;
                                    else if (ariaDesc.Contains("chk_svc"))
                                        서비스수량 = cellText;
                                    else if (ariaDesc.Contains("tqty"))
                                        총수량 = cellText;
                                    else if (ariaDesc.Contains("salesamt"))
                                        총매출액 = cellText;
                                    else if (ariaDesc.Contains("dcamount"))
                                        총할인액 = cellText;
                                    else if (ariaDesc.Contains("avetamount"))
                                        평균매출액 = cellText;
                                    else if (ariaDesc.Contains("amountpercent"))
                                        매출비율 = cellText;
                                }
                                cellIndex++;
                            }

                            // 방법 2: aria-describedby가 없으면 순서로 추출
                            if (!foundByAria && cells.Count >= 7)
                            {
                                // 일반적인 순서: 중분류(0), 메뉴명(1), 메뉴코드(2), 판매수량(3), 서비스수량(4), 총수량(5), 총매출액(6), 총할인액(7), 평균매출액(8), 매출비율(9)
                                중분류 = cells.Count > 0 ? GetCellValue(cells[0]) : "";
                                메뉴명 = cells.Count > 1 ? GetCellValue(cells[1]) : "";
                                메뉴코드 = cells.Count > 2 ? GetCellValue(cells[2]) : "";
                                판매수량 = cells.Count > 3 ? GetCellValue(cells[3]) : "";
                                서비스수량 = cells.Count > 4 ? GetCellValue(cells[4]) : "";
                                총수량 = cells.Count > 5 ? GetCellValue(cells[5]) : "";
                                총매출액 = cells.Count > 6 ? GetCellValue(cells[6]) : "";
                                총할인액 = cells.Count > 7 ? GetCellValue(cells[7]) : "";
                                평균매출액 = cells.Count > 8 ? GetCellValue(cells[8]) : "";
                                매출비율 = cells.Count > 9 ? GetCellValue(cells[9]) : "";
                            }

                            // 디버깅: 상세 추출 정보 기록
                            System.Diagnostics.Debug.WriteLine($"[ParseData] foundByAria={foundByAria}, cells.Count={cells.Count}");
                            System.Diagnostics.Debug.WriteLine($"[ParseData] 중분류={중분류}, 메뉴명={메뉴명}, 메뉴코드={메뉴코드}");
                            System.Diagnostics.Debug.WriteLine($"[ParseData] 판매수량={판매수량}, 서비스수량={서비스수량}, 총수량={총수량}");
                            System.Diagnostics.Debug.WriteLine($"[ParseData] 총매출액={총매출액}, 총할인액={총할인액}, 평균매출액={평균매출액}, 매출비율={매출비율}");

                            // 메뉴명이 있는 행만 추가
                            if (!string.IsNullOrWhiteSpace(메뉴명))
                            {
                                if (!string.IsNullOrWhiteSpace(storeName))
                                {
                                    // 매장명 포함
                                    result.Add($"{storeName}\t{중분류}\t{메뉴명}\t{메뉴코드}\t{판매수량}\t{서비스수량}\t{총수량}\t{총매출액}\t{총할인액}\t{평균매출액}\t{매출비율}");
                                }
                                else
                                {
                                    // 매장명 제외
                                    result.Add($"{중분류}\t{메뉴명}\t{메뉴코드}\t{판매수량}\t{서비스수량}\t{총수량}\t{총매출액}\t{총할인액}\t{평균매출액}\t{매출비율}");
                                }
                                System.Diagnostics.Debug.WriteLine($"[ParseData] ✅ 행 추가됨");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[ParseData] ⚠️ 행 건너뜀 (메뉴명 없음)");
                            }
                        }
                        catch
                        {
                            // 개별 행 파싱 실패 시 계속 진행
                            continue;
                        }
                    }

                    // 합계 행 추가
                    var footerRow = doc.DocumentNode.SelectSingleNode("//table[@id='jqGrid02']//tr[contains(@class,'footrow')]");
                    if (footerRow != null)
                    {
                        var footerCells = footerRow.SelectNodes(".//td[@role='gridcell']");
                        if (footerCells != null)
                        {
                            string 판매수량합계 = "";
                            string 서비스수량합계 = "";
                            string 총수량합계 = "";
                            string 총매출액합계 = "";
                            string 총할인액합계 = "";
                            string 평균매출액합계 = "";
                            string 매출비율합계 = "";

                            // 방법 1: aria-describedby 속성으로 매칭 시도
                            bool foundByAria = false;
                            foreach (var cell in footerCells)
                            {
                                var ariaDesc = cell.GetAttributeValue("aria-describedby", "").ToLower();
                                if (!string.IsNullOrEmpty(ariaDesc))
                                {
                                    foundByAria = true;
                                    var cellText = GetCellValue(cell);

                                    if (ariaDesc.Contains("sqty"))
                                        판매수량합계 = cellText;
                                    else if (ariaDesc.Contains("chk_svc"))
                                        서비스수량합계 = cellText;
                                    else if (ariaDesc.Contains("tqty"))
                                        총수량합계 = cellText;
                                    else if (ariaDesc.Contains("salesamt"))
                                        총매출액합계 = cellText;
                                    else if (ariaDesc.Contains("dcamount"))
                                        총할인액합계 = cellText;
                                    else if (ariaDesc.Contains("avetamount"))
                                        평균매출액합계 = cellText;
                                    else if (ariaDesc.Contains("amountpercent"))
                                        매출비율합계 = cellText;
                                }
                            }

                            // 방법 2: aria-describedby가 없으면 순서로 추출
                            if (!foundByAria && footerCells.Count >= 7)
                            {
                                // 합계 행은 중분류, 메뉴명, 메뉴코드가 없으므로 3번째부터 시작
                                판매수량합계 = footerCells.Count > 3 ? GetCellValue(footerCells[3]) : "";
                                서비스수량합계 = footerCells.Count > 4 ? GetCellValue(footerCells[4]) : "";
                                총수량합계 = footerCells.Count > 5 ? GetCellValue(footerCells[5]) : "";
                                총매출액합계 = footerCells.Count > 6 ? GetCellValue(footerCells[6]) : "";
                                총할인액합계 = footerCells.Count > 7 ? GetCellValue(footerCells[7]) : "";
                                평균매출액합계 = footerCells.Count > 8 ? GetCellValue(footerCells[8]) : "";
                                매출비율합계 = footerCells.Count > 9 ? GetCellValue(footerCells[9]) : "";
                            }

                            result.Add("".PadRight(120, '='));
                            if (!string.IsNullOrWhiteSpace(storeName))
                            {
                                result.Add($"{storeName}\t[합계]\t\t\t{판매수량합계}\t{서비스수량합계}\t{총수량합계}\t{총매출액합계}\t{총할인액합계}\t{평균매출액합계}\t{매출비율합계}");
                            }
                            else
                            {
                                result.Add($"[합계]\t\t\t{판매수량합계}\t{서비스수량합계}\t{총수량합계}\t{총매출액합계}\t{총할인액합계}\t{평균매출액합계}\t{매출비율합계}");
                            }
                        }
                    }
                }
                else
                {
                    // 사용자가 선택한 선택자 사용 (기존 코드)
                    if (!string.IsNullOrEmpty(_customSelector))
                    {
                        string xpath;

                        if (_customSelector.StartsWith("#"))
                        {
                            string id = _customSelector.Substring(1);
                            xpath = $"//*[@id='{id}']";
                        }
                        else if (_customSelector.Contains("."))
                        {
                            var parts = _customSelector.Split('.');
                            string tag = parts[0];
                            string className = parts[1];
                            xpath = $"//{tag}[contains(@class,'{className}')]";
                        }
                        else
                        {
                            xpath = $"//{_customSelector}";
                        }

                        var nodes = doc.DocumentNode.SelectNodes(xpath);

                        if (nodes != null)
                        {
                            foreach (var node in nodes)
                            {
                                string text = node.InnerText.Trim();
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    result.Add(text);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Parse failed: {ex.Message}");
                result.Add($"파싱 오류: {ex.Message}");
            }

            return result;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        /// <summary>
        /// 셀에서 값 추출 (title 속성 우선, 없으면 innerText)
        /// </summary>
        private string GetCellValue(HtmlNode cell)
        {
            if (cell == null) return "";

            // title 속성에 실제 값이 있는 경우가 많음
            var cellTitle = cell.GetAttributeValue("title", "");
            var cellText = !string.IsNullOrWhiteSpace(cellTitle) ? cellTitle : cell.InnerText.Trim();

            // HTML 태그 제거
            cellText = System.Text.RegularExpressions.Regex.Replace(cellText, @"<[^>]+>", "");

            // HTML 엔티티 디코드
            cellText = System.Net.WebUtility.HtmlDecode(cellText);

            // 숫자 포맷 제거 (콤마 등)
            cellText = cellText.Replace(",", "").Trim();

            return cellText;
        }
    }
}
