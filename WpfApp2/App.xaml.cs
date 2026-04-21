using System.Configuration;
using System.Data;
using System.Windows;
using AutoUpdaterDotNET;

namespace WpfApp2
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 자동 업데이트 설정
            AutoUpdater.ShowSkipButton = false; // 건너뛰기 버튼 숨기기
            AutoUpdater.ShowRemindLaterButton = false; // 나중에 알림 버튼 숨기기
            AutoUpdater.Mandatory = true; // 필수 업데이트로 설정
            AutoUpdater.UpdateMode = Mode.Forced; // 강제 업데이트 모드

            // GitHub Releases를 사용한 자동 업데이트
            // TODO: 'YOUR-USERNAME'과 'YOUR-REPO-NAME'을 실제 GitHub 저장소 정보로 변경하세요
            string githubUser = "Jh98JC";        // 예: 
            string githubRepo = "WpfApp1";       // 예: 
            string updateUrl = $"https://raw.githubusercontent.com/{githubUser}/{githubRepo}/main/updates/update.xml";

            AutoUpdater.Start(updateUrl);
        }
    }

}
