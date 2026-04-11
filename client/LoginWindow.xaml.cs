using System.Windows;
using System.Windows.Input;

namespace CodeExplainer
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                CodeTextBox.Focus();
                UpdatePlaceholder();
            };
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

        private void CodeTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (ErrorText.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(CodeTextBox.Text))
            {
                SetError(string.Empty);
            }

            UpdatePlaceholder();
        }

        private void UpdatePlaceholder()
        {
            PlaceholderText.Visibility = string.IsNullOrWhiteSpace(CodeTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void CodeInputHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CodeTextBox.Focus();
            CodeTextBox.Select(CodeTextBox.Text.Length, 0);
        }
    }
}
