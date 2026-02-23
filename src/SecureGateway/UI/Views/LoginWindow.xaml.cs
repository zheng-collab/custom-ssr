using System.Windows;
using System.Windows.Input;
using SecureGateway.Services;

namespace SecureGateway.UI.Views
{
    public partial class LoginWindow : Window
    {
        private readonly AuthService _authService;
        private bool _isSignUpMode;

        public bool IsAuthenticated { get; private set; }

        public LoginWindow(AuthService authService)
        {
            InitializeComponent();
            _authService = authService;
            TxtEmail.Focus();
        }

        private void OnSignInTabClick(object sender, RoutedEventArgs e)
        {
            _isSignUpMode = false;
            BtnSignInTab.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#89B4FA")!;
            BtnSignInTab.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#1E1E2E")!;
            BtnSignUpTab.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#45475A")!;
            BtnSignUpTab.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#A6ADC8")!;
            BtnSubmit.Content = "Sign In";
            LblConfirmPassword.Visibility = Visibility.Collapsed;
            TxtConfirmPassword.Visibility = Visibility.Collapsed;
            TxtStatus.Text = "";
        }

        private void OnSignUpTabClick(object sender, RoutedEventArgs e)
        {
            _isSignUpMode = true;
            BtnSignUpTab.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#89B4FA")!;
            BtnSignUpTab.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#1E1E2E")!;
            BtnSignInTab.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#45475A")!;
            BtnSignInTab.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#A6ADC8")!;
            BtnSubmit.Content = "Create Account";
            LblConfirmPassword.Visibility = Visibility.Visible;
            TxtConfirmPassword.Visibility = Visibility.Visible;
            TxtStatus.Text = "";
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                OnSubmitClick(sender, e);
        }

        private async void OnSubmitClick(object sender, RoutedEventArgs e)
        {
            var email = TxtEmail.Text.Trim();
            var password = TxtPassword.Password;

            // Validation
            if (string.IsNullOrEmpty(email))
            {
                ShowError("Please enter your email address.");
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                ShowError("Please enter your password.");
                return;
            }

            if (password.Length < 6)
            {
                ShowError("Password must be at least 6 characters.");
                return;
            }

            if (_isSignUpMode)
            {
                var confirmPassword = TxtConfirmPassword.Password;
                if (password != confirmPassword)
                {
                    ShowError("Passwords do not match.");
                    return;
                }
            }

            // Disable UI during auth
            SetLoading(true);
            TxtStatus.Text = "";

            AuthResult result;
            if (_isSignUpMode)
            {
                result = await _authService.SignUpAsync(email, password);
            }
            else
            {
                result = await _authService.SignInAsync(email, password);
            }

            SetLoading(false);

            if (result.IsSuccess)
            {
                if (result.RequiresConfirmation)
                {
                    ShowInfo(result.Message);
                    // Switch to sign-in tab
                    OnSignInTabClick(sender, e);
                }
                else
                {
                    IsAuthenticated = true;
                    DialogResult = true;
                    Close();
                }
            }
            else
            {
                ShowError(result.Message);
            }
        }

        private void ShowError(string message)
        {
            TxtStatus.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#F38BA8")!;
            TxtStatus.Text = message;
        }

        private void ShowInfo(string message)
        {
            TxtStatus.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#A6E3A1")!;
            TxtStatus.Text = message;
        }

        private void SetLoading(bool loading)
        {
            BtnSubmit.IsEnabled = !loading;
            TxtEmail.IsEnabled = !loading;
            TxtPassword.IsEnabled = !loading;
            TxtConfirmPassword.IsEnabled = !loading;
            TxtLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
