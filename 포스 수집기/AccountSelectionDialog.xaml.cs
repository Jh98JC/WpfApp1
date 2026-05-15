using System.Windows;

namespace 대진포스_쿼리
{
    public partial class AccountSelectionDialog : Window
    {
        public string SelectedAccount { get; private set; }

        public AccountSelectionDialog()
        {
            InitializeComponent();
        }

        private void JuncoButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedAccount = "junco";
            DialogResult = true;
            Close();
        }

        private void Junco3Button_Click(object sender, RoutedEventArgs e)
        {
            SelectedAccount = "junco3";
            DialogResult = true;
            Close();
        }
    }
}
