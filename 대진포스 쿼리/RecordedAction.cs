using System;

namespace 대진포스_쿼리
{
    /// <summary>
    /// 녹화된 사용자 액션
    /// </summary>
    public class RecordedAction
    {
        public int Step { get; set; }
        public DateTime Timestamp { get; set; }
        public string ActionType { get; set; } // Click, Navigate, etc.
        public string ElementSelector { get; set; }
        public string ElementText { get; set; }
        public string ElementTag { get; set; }
        public string Url { get; set; }
        public string ScreenshotPath { get; set; }
        public string HtmlSnapshotPath { get; set; }

        public override string ToString()
        {
            return $"[단계 {Step}] {Timestamp:HH:mm:ss} - {ActionType}: {ElementSelector} ({ElementText})";
        }
    }
}
