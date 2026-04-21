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
            AutoUpdater.ShowSkipButton = false;
            AutoUpdater.ShowRemindLaterButton = false;
            AutoUpdater.Mandatory = true;
            AutoUpdater.UpdateMode = Mode.Forced;

            // GitHub Releases를 사용한 자동 업데이트
            string githubUser = "Jh98JC";
            string githubRepo = "WpfApp1";
            string updateUrl = $"https://raw.githubusercontent.com/{githubUser}/{githubRepo}/main/updates/update.xml";

            AutoUpdater.Start(updateUrl);
        }
    }

}
