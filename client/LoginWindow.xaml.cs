using System.Windows;
using System.Windows.Input;

namespace CodeExplainer
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

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
        }

        private void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
