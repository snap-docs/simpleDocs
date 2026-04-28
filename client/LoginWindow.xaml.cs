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

        public void SetBusy(bool isBusy)
        {
            BusyText.Text = isBusy ? "Working..." : string.Empty;
            BusyText.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            FormContentGrid.IsEnabled = !isBusy;
            CancelButton.IsEnabled = !isBusy;
        }

        private void RedeemCodeContinueButton_Click(object sender, RoutedEventArgs e)
        {
            string code = RedeemCodeTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                SetError("Enter your redeem code to continue.");
                ProviderTabs.SelectedIndex = 0;
                return;
            }

            Submit(new AuthWindowSubmission
            {
                Kind = AuthWindowSubmissionKind.RedeemCodeLogin,
                Code = code
            });
        }

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
                ProviderTabs.SelectedIndex = 2;
                EmailModeTabs.SelectedIndex = 0;
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
                SetError("Enter an email address and password to create your account.");
                ProviderTabs.SelectedIndex = 2;
                EmailModeTabs.SelectedIndex = 1;
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
            {
                DragMove();
            }
        }
    }
}
