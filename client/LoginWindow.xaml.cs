using System.Windows;
using System.Windows.Input;

namespace CodeExplainer
{
    public partial class LoginWindow : Window
    {
        public AuthWindowSubmission? Submission { get; private set; }

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

        public void SetBusy(bool isBusy, string? message = null)
        {
            BusyText.Text = isBusy ? (message ?? "Working...") : string.Empty;
            BusyText.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            FormContentGrid.IsEnabled = !isBusy;
            CancelButton.IsEnabled = !isBusy;
        }

        // Panel toggles

        private void ShowSignInPanel_Click(object sender, RoutedEventArgs e)
        {
            SignInPanel.Visibility = Visibility.Visible;
            RegisterPanel.Visibility = Visibility.Collapsed;
            SetError(string.Empty);
        }

        private void ShowRegisterPanel_Click(object sender, RoutedEventArgs e)
        {
            SignInPanel.Visibility = Visibility.Collapsed;
            RegisterPanel.Visibility = Visibility.Visible;
            SetError(string.Empty);
        }

        private void RedeemCodeToggle_Click(object sender, RoutedEventArgs e)
        {
            bool isVisible = RedeemCodePanel.Visibility == Visibility.Visible;
            RedeemCodePanel.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            RedeemToggleButton.Content = isVisible ? "Have a support code?" : "Hide support code ↑";
        }

        // Keyboard Enter navigation

        private void SignInEmailField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                EmailSignInPasswordBox.Focus();
                e.Handled = true;
            }
        }

        private void SignInPasswordField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                EmailSignInButton_Click(sender, e);
                e.Handled = true;
            }
        }

        private void RegisterEmailField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                EmailRegisterPasswordBox.Focus();
                e.Handled = true;
            }
        }

        private void RegisterPasswordField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                EmailRegisterButton_Click(sender, e);
                e.Handled = true;
            }
        }

        private void RedeemCodeField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                RedeemCodeContinueButton_Click(sender, e);
                e.Handled = true;
            }
        }

        // Submit handlers

        private void GoogleContinueButton_Click(object sender, RoutedEventArgs e)
        {
            Submit(new AuthWindowSubmission
            {
                Kind = AuthWindowSubmissionKind.GoogleLogin
            });
        }

        private void EmailSignInButton_Click(object sender, RoutedEventArgs e)
        {
            string email = EmailSignInEmailTextBox.Text.Trim();
            string password = EmailSignInPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                SetError("Enter both your email and password to continue.");
                return;
            }

            Submit(new AuthWindowSubmission
            {
                Kind = AuthWindowSubmissionKind.EmailPasswordLogin,
                Email = email,
                Password = password
            });
        }

        private void EmailRegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string email = EmailRegisterEmailTextBox.Text.Trim();
            string password = EmailRegisterPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                SetError("Enter an email and password to create your account.");
                return;
            }

            Submit(new AuthWindowSubmission
            {
                Kind = AuthWindowSubmissionKind.EmailPasswordRegister,
                Email = email,
                Password = password,
                DisplayName = EmailRegisterDisplayNameTextBox.Text.Trim()
            });
        }

        private void RedeemCodeContinueButton_Click(object sender, RoutedEventArgs e)
        {
            string code = RedeemCodeTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(code))
            {
                SetError("Enter your support code to continue.");
                return;
            }

            Submit(new AuthWindowSubmission
            {
                Kind = AuthWindowSubmissionKind.RedeemCodeLogin,
                Code = code
            });
        }

        private void Submit(AuthWindowSubmission submission)
        {
            Submission = submission;
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
                DragMove();
        }
    }
}
