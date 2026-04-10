using System.Windows;

namespace CodeExplainer
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => CodeTextBox.Focus();
        }

        public string RedeemCode => CodeTextBox.Text.Trim();

        public void SetError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        public void SetBusy(bool isBusy)
        {
            BusyText.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            SignInButton.IsEnabled = !isBusy;
            CancelButton.IsEnabled = !isBusy;
            CodeTextBox.IsEnabled = !isBusy;
        }

        private void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RedeemCode))
            {
                SetError("Enter the redeem code you received.");
                return;
            }

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
