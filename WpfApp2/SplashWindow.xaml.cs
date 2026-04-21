using System.Windows;

namespace WpfApp2
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        public void UpdateStatus(string message)
        {
            StatusText.Text = message;
        }
    }
}
