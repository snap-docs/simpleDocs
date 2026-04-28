using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace CodeExplainer
{
    public partial class AccountMethodsWindow : Window
    {
        public AuthWindowSubmission? Submission { get; private set; }

        public AccountMethodsWindow(AuthStateResponse authState)
        {
            InitializeComponent();
            LoadAuthState(authState);
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

        private void LoadAuthState(AuthStateResponse authState)
        {
            string displayName = authState.User?.DisplayName?.Trim() ?? string.Empty;
            string email = authState.User?.Email?.Trim() ?? string.Empty;

            AccountNameText.Text = string.IsNullOrWhiteSpace(displayName)
                ? "Current account"
                : displayName;
            AccountEmailText.Text = string.IsNullOrWhiteSpace(email)
                ? "Signed-in identity"
                : email;

            LinkedMethodsList.ItemsSource = authState.Methods.Count == 0
                ? new[] { "No linked methods found." }
                : authState.Methods.Select(FormatMethod).ToArray();
        }

        private static string FormatMethod(AuthMethodSummary method)
        {
            string label = string.IsNullOrWhiteSpace(method.Label)
                ? method.Provider
                : method.Label;
            return $"{method.Provider}: {label}";
        }

        private void GoogleLinkButton_Click(object sender, RoutedEventArgs e)
        {
            Submit(new AuthWindowSubmission
            {
                Kind = AuthWindowSubmissionKind.GoogleLink
            });
        }

        private void RedeemCodeLinkButton_Click(object sender, RoutedEventArgs e)
        {
            string code = RedeemCodeTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                SetError("Enter a redeem code to link it to this account.");
                ProviderTabs.SelectedIndex = 1;
                return;
            }

            Submit(new AuthWindowSubmission
            {
                Kind = AuthWindowSubmissionKind.RedeemCodeLink,
                Code = code
            });
        }

        private void EmailLinkButton_Click(object sender, RoutedEventArgs e)
        {
            string email = EmailTextBox.Text.Trim();
            string password = PasswordTextBox.Password;
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                SetError("Enter an email address and password to link this method.");
                ProviderTabs.SelectedIndex = 2;
                return;
            }

            Submit(new AuthWindowSubmission
            {
                Kind = AuthWindowSubmissionKind.EmailPasswordLink,
                Email = email,
                Password = password,
                DisplayName = EmailDisplayNameTextBox.Text.Trim()
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
