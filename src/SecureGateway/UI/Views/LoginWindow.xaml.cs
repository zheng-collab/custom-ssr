using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Newtonsoft.Json;
using SecureGateway.Services;

namespace SecureGateway.UI.Views
{
    public partial class LoginWindow : Window
    {
        private const int MaxAttempts = 10;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

        private static readonly string LockoutFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SecureGateway", "lockout.json");

        private readonly AuthService _authService;
        private bool _isSignUpMode;
        private int _failedAttempts;
        private DateTime? _lockoutUntil;
        private DispatcherTimer _countdownTimer;

        public bool IsAuthenticated { get; private set; }

        public LoginWindow(AuthService authService)
        {
            InitializeComponent();
            _authService = authService;

            LoadLockoutState();
            UpdateLockoutUI();

            if (_lockoutUntil.HasValue && _lockoutUntil.Value > DateTime.UtcNow)
                StartCountdownTimer();

            TxtEmail.Focus();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Only allow closing if authenticated — otherwise block the close
            if (!IsAuthenticated)
            {
                e.Cancel = true;
                // Shut down the entire app instead
                Application.Current.Shutdown();
            }

            base.OnClosing(e);
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
            UpdateLockoutUI();
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
            UpdateLockoutUI();
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                OnSubmitClick(sender, e);
        }

        private async void OnSubmitClick(object sender, RoutedEventArgs e)
        {
            // Check lockout
            if (IsLockedOut())
            {
                ShowLockoutMessage();
                return;
            }

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
                    OnSignInTabClick(sender, e);
                }
                else
                {
                    // Successful login — reset lockout state
                    _failedAttempts = 0;
                    _lockoutUntil = null;
                    SaveLockoutState();
                    StopCountdownTimer();

                    IsAuthenticated = true;
                    DialogResult = true;
                    Close();
                }
            }
            else
            {
                // Count failed sign-in attempts (not sign-up failures)
                if (!_isSignUpMode)
                {
                    _failedAttempts++;
                    SaveLockoutState();

                    if (_failedAttempts >= MaxAttempts)
                    {
                        _lockoutUntil = DateTime.UtcNow.Add(LockoutDuration);
                        SaveLockoutState();
                        StartCountdownTimer();
                        ShowLockoutMessage();
                        UpdateLockoutUI();
                        return;
                    }

                    int remaining = MaxAttempts - _failedAttempts;
                    ShowError($"{result.Message}\n{remaining} attempt{(remaining == 1 ? "" : "s")} remaining before lockout.");
                }
                else
                {
                    ShowError(result.Message);
                }

                UpdateLockoutUI();
            }
        }

        private bool IsLockedOut()
        {
            if (!_lockoutUntil.HasValue)
                return false;

            if (DateTime.UtcNow >= _lockoutUntil.Value)
            {
                // Lockout expired — reset
                _failedAttempts = 0;
                _lockoutUntil = null;
                SaveLockoutState();
                StopCountdownTimer();
                UpdateLockoutUI();
                return false;
            }

            return true;
        }

        private void ShowLockoutMessage()
        {
            if (_lockoutUntil.HasValue)
            {
                var remaining = _lockoutUntil.Value - DateTime.UtcNow;
                if (remaining.TotalSeconds > 0)
                {
                    int mins = (int)remaining.TotalMinutes;
                    int secs = remaining.Seconds;
                    ShowError($"Too many failed attempts. Account locked.\nTry again in {mins}:{secs:D2}.");
                }
            }
        }

        private void UpdateLockoutUI()
        {
            bool locked = IsLockedOut();

            BtnSubmit.IsEnabled = !locked;
            TxtEmail.IsEnabled = !locked;
            TxtPassword.IsEnabled = !locked;
            TxtConfirmPassword.IsEnabled = !locked;

            if (locked)
            {
                ShowLockoutMessage();
                TxtLockoutBar.Visibility = Visibility.Visible;
            }
            else
            {
                TxtLockoutBar.Visibility = Visibility.Collapsed;

                // Show attempt counter if there have been failures
                if (_failedAttempts > 0 && !_isSignUpMode)
                {
                    int remaining = MaxAttempts - _failedAttempts;
                    TxtLockoutBar.Text = $"{remaining} attempt{(remaining == 1 ? "" : "s")} remaining";
                    TxtLockoutBar.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#FAB387")!;
                    TxtLockoutBar.Visibility = Visibility.Visible;
                }
            }
        }

        private void StartCountdownTimer()
        {
            StopCountdownTimer();
            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += (s, e) =>
            {
                if (!IsLockedOut())
                {
                    StopCountdownTimer();
                    TxtStatus.Text = "";
                    UpdateLockoutUI();
                    ShowInfo("Lockout expired. You may try again.");
                }
                else
                {
                    ShowLockoutMessage();
                }
            };
            _countdownTimer.Start();
        }

        private void StopCountdownTimer()
        {
            _countdownTimer?.Stop();
            _countdownTimer = null;
        }

        private void LoadLockoutState()
        {
            try
            {
                if (!File.Exists(LockoutFile)) return;

                var json = File.ReadAllText(LockoutFile);
                var state = JsonConvert.DeserializeObject<LockoutState>(json);
                if (state == null) return;

                _failedAttempts = state.FailedAttempts;

                if (!string.IsNullOrEmpty(state.LockoutUntilUtc))
                {
                    if (DateTime.TryParse(state.LockoutUntilUtc, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                    {
                        _lockoutUntil = parsed;
                    }
                }

                // If lockout has expired, reset
                if (_lockoutUntil.HasValue && DateTime.UtcNow >= _lockoutUntil.Value)
                {
                    _failedAttempts = 0;
                    _lockoutUntil = null;
                    SaveLockoutState();
                }
            }
            catch
            {
                _failedAttempts = 0;
                _lockoutUntil = null;
            }
        }

        private void SaveLockoutState()
        {
            try
            {
                var dir = Path.GetDirectoryName(LockoutFile);
                if (dir != null) Directory.CreateDirectory(dir);

                var state = new LockoutState
                {
                    FailedAttempts = _failedAttempts,
                    LockoutUntilUtc = _lockoutUntil?.ToString("o") ?? ""
                };

                File.WriteAllText(LockoutFile, JsonConvert.SerializeObject(state));
            }
            catch { }
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

        private class LockoutState
        {
            public int FailedAttempts { get; set; }
            public string LockoutUntilUtc { get; set; } = "";
        }
    }
}
