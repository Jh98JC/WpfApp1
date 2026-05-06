using System.Windows;

namespace WpfApp2.Updater
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string installerPath = "";
            int waitPid = -1;
            string silentArgs = "/S";
            string version = "";

            var args = e.Args;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--install" when i + 1 < args.Length:
                        installerPath = args[++i]; break;
                    case "--pid" when i + 1 < args.Length:
                        int.TryParse(args[++i], out waitPid); break;
                    case "--args" when i + 1 < args.Length:
                        silentArgs = args[++i]; break;
                    case "--version" when i + 1 < args.Length:
                        version = args[++i]; break;
                }
            }

            if (string.IsNullOrEmpty(installerPath))
            {
                Shutdown();
                return;
            }

            var window = new UpdaterWindow(installerPath, waitPid, silentArgs, version);
            MainWindow = window;
            window.Show();
        }
    }
}
