using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SecureGateway.Platform;
using SecureGateway.Services;

namespace SecureGateway.UI.ViewModels
{
    /// <summary>
    /// Sign-in / sign-up / password-reset logic with the 10-attempt, 5-minute lockout,
    /// persisted to disk so restarting the app doesn't reset the counter.
    /// Platform views bind to this; they only need a 1-second timer to call Tick().
    /// </summary>
    public class LoginViewModel : BaseViewModel
    {
        public const int MaxAttempts = 10;
        public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
        private static readonly string LockoutFile = Path.Combine(AppPaths.DataDir, "lockout.json");

        private readonly AuthService _auth;
        private int _failedAttempts;
        private DateTime? _lockoutUntil;

        private string _email = "";
        private string _password = "";
        private string _confirmPassword = "";
        private string _resetCode = "";
        private string _newPassword = "";
        private bool _rememberMe;
        private bool _isSignUpMode;
        private bool _isResetMode;
        private bool _isBusy;
        private string _busyText = "Authenticating...";
        private string _statusMessage = "";
        private bool _statusIsError;
        private string _lockoutText = "";

        /// <summary>Raised on the calling thread when sign-in succeeds.</summary>
        public event EventHandler Authenticated;

        public LoginViewModel(AuthService auth)
        {
            _auth = auth;
            LoadLockoutState();

            var saved = auth.LoadCredentials();
            if (saved != null)
            {
                _email = saved.Email;
                _password = saved.Password;
                _rememberMe = true;
            }

            UpdateLockoutText();
        }

        public string Email { get => _email; set => SetProperty(ref _email, value); }
        public string Password { get => _password; set => SetProperty(ref _password, value); }
        public string ConfirmPassword { get => _confirmPassword; set => SetProperty(ref _confirmPassword, value); }
        public string ResetCode { get => _resetCode; set => SetProperty(ref _resetCode, value); }
        public string NewPassword { get => _newPassword; set => SetProperty(ref _newPassword, value); }
        public bool RememberMe { get => _rememberMe; set => SetProperty(ref _rememberMe, value); }

        public bool IsSignUpMode
        {
            get => _isSignUpMode;
            set
            {
                if (SetProperty(ref _isSignUpMode, value))
                {
                    if (value) IsResetMode = false;
                    StatusMessage = "";
                    OnPropertyChanged(nameof(SubmitText));
                    OnPropertyChanged(nameof(IsSignInMode));
                }
            }
        }

        public bool IsResetMode
        {
            get => _isResetMode;
            set
            {
                if (SetProperty(ref _isResetMode, value))
                {
                    if (value) { ResetCode = ""; NewPassword = ""; }
                    StatusMessage = "";
                    OnPropertyChanged(nameof(IsMainMode));
                    OnPropertyChanged(nameof(IsSignInMode));
                }
            }
        }

        public bool IsMainMode => !IsResetMode;
        public bool IsSignInMode => !IsSignUpMode && !IsResetMode;
        public string SubmitText => IsSignUpMode ? "Create Account" : "Sign In";

        public bool IsBusy
        {
            get => _isBusy;
            private set { if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(CanInteract)); }
        }

        public string BusyText { get => _busyText; private set => SetProperty(ref _busyText, value); }
        public bool CanInteract => !IsBusy && !IsLockedOut;
        public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
        public bool StatusIsError { get => _statusIsError; private set => SetProperty(ref _statusIsError, value); }
        public string LockoutText { get => _lockoutText; private set => SetProperty(ref _lockoutText, value); }
        public bool HasLockoutText => !string.IsNullOrEmpty(LockoutText);

        public bool IsLockedOut
        {
            get
            {
                if (!_lockoutUntil.HasValue) return false;
                if (DateTime.UtcNow < _lockoutUntil.Value) return true;
                ResetLockout();
                return false;
            }
        }

        /// <summary>Call once per second while a lockout is active to refresh the countdown.</summary>
        public void Tick()
        {
            if (_lockoutUntil.HasValue && DateTime.UtcNow >= _lockoutUntil.Value)
            {
                ResetLockout();
                ShowInfo("Lockout expired. You may try again.");
            }
            UpdateLockoutText();
        }

        // ---- actions --------------------------------------------------------------------------
        /// <summary>Status text after "Forgot password?" opened (or failed to open) the website.</summary>
        public void ShowWebResetHint(bool browserOpened)
        {
            if (browserOpened)
                ShowInfo($"Opened {AuthService.PasswordResetUrl} in your browser.\nReset your password there, then sign in here.");
            else
                ShowError($"Could not open the browser. Visit {AuthService.PasswordResetUrl} to reset your password.");
        }

        public async Task SubmitAsync()
        {
            if (IsLockedOut) { UpdateLockoutText(); return; }

            var email = Email.Trim();
            if (string.IsNullOrEmpty(email)) { ShowError("Please enter your email address."); return; }
            if (string.IsNullOrEmpty(Password)) { ShowError("Please enter your password."); return; }
            if (Password.Length < 6) { ShowError("Password must be at least 6 characters."); return; }
            if (IsSignUpMode && Password != ConfirmPassword) { ShowError("Passwords do not match."); return; }

            SetBusy(true, "Authenticating...");
            var result = IsSignUpMode
                ? await _auth.SignUpAsync(email, Password)
                : await _auth.SignInAsync(email, Password, RememberMe);
            SetBusy(false);

            if (result.IsSuccess)
            {
                if (result.RequiresConfirmation)
                {
                    ShowInfo(result.Message);
                    IsSignUpMode = false;
                    return;
                }

                ResetLockout();
                Authenticated?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (IsSignUpMode)
            {
                ShowError(result.Message);
                return;
            }

            _failedAttempts++;
            SaveLockoutState();

            if (_failedAttempts >= MaxAttempts)
            {
                _lockoutUntil = DateTime.UtcNow.Add(LockoutDuration);
                SaveLockoutState();
                OnPropertyChanged(nameof(IsLockedOut));
                OnPropertyChanged(nameof(CanInteract));
                UpdateLockoutText();
                return;
            }

            var remaining = MaxAttempts - _failedAttempts;
            ShowError($"{result.Message}\n{remaining} attempt{(remaining == 1 ? "" : "s")} remaining before lockout.");
            UpdateLockoutText();
        }

        public async Task SendResetCodeAsync()
        {
            var email = Email.Trim();
            if (string.IsNullOrEmpty(email) || !email.Contains('@'))
            {
                ShowError("Enter your account e-mail address above first.");
                return;
            }

            SetBusy(true, "Sending reset code...");
            var result = await _auth.RequestPasswordResetAsync(email);
            SetBusy(false);

            if (result.IsSuccess)
                ShowInfo(result.Message + "\nEnter the code (or paste the link) below along with your new password.");
            else
                ShowError(result.Message);
        }

        public async Task ApplyResetAsync()
        {
            var email = Email.Trim();
            if (string.IsNullOrEmpty(email)) { ShowError("Enter your account e-mail address above."); return; }
            if (string.IsNullOrWhiteSpace(ResetCode)) { ShowError("Enter the reset code, or paste the reset link from the e-mail."); return; }
            if (NewPassword.Length < 6) { ShowError("New password must be at least 6 characters."); return; }

            SetBusy(true, "Updating password...");
            var result = await _auth.ResetPasswordWithCodeAsync(email, ResetCode, NewPassword);
            SetBusy(false);

            if (!result.IsSuccess) { ShowError(result.Message); return; }

            ResetLockout();
            IsResetMode = false;
            Password = "";
            RememberMe = false;
            ShowInfo(result.Message);
        }

        // ---- helpers ----------------------------------------------------------------------------
        private void SetBusy(bool busy, string text = "Authenticating...")
        {
            BusyText = text;
            IsBusy = busy;
            if (busy) StatusMessage = "";
        }

        private void ShowError(string message) { StatusIsError = true; StatusMessage = message; }
        private void ShowInfo(string message) { StatusIsError = false; StatusMessage = message; }

        private void ResetLockout()
        {
            _failedAttempts = 0;
            _lockoutUntil = null;
            SaveLockoutState();
            OnPropertyChanged(nameof(IsLockedOut));
            OnPropertyChanged(nameof(CanInteract));
            UpdateLockoutText();
        }

        private void UpdateLockoutText()
        {
            if (_lockoutUntil.HasValue && DateTime.UtcNow < _lockoutUntil.Value)
            {
                var remaining = _lockoutUntil.Value - DateTime.UtcNow;
                LockoutText = $"Too many failed attempts. Try again in {(int)remaining.TotalMinutes}:{remaining.Seconds:D2}.";
            }
            else if (_failedAttempts > 0 && !IsSignUpMode)
            {
                var remaining = MaxAttempts - _failedAttempts;
                LockoutText = $"{remaining} attempt{(remaining == 1 ? "" : "s")} remaining";
            }
            else
            {
                LockoutText = "";
            }
            OnPropertyChanged(nameof(HasLockoutText));
        }

        private class LockoutState
        {
            public int FailedAttempts { get; set; }
            public string LockoutUntilUtc { get; set; } = "";
        }

        private void LoadLockoutState()
        {
            try
            {
                if (!File.Exists(LockoutFile)) return;
                var state = JsonConvert.DeserializeObject<LockoutState>(File.ReadAllText(LockoutFile));
                if (state == null) return;

                _failedAttempts = state.FailedAttempts;
                if (DateTime.TryParse(state.LockoutUntilUtc, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var until))
                    _lockoutUntil = until;

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
                Directory.CreateDirectory(AppPaths.DataDir);
                File.WriteAllText(LockoutFile, JsonConvert.SerializeObject(new LockoutState
                {
                    FailedAttempts = _failedAttempts,
                    LockoutUntilUtc = _lockoutUntil?.ToString("o") ?? ""
                }));
            }
            catch { }
        }
    }
}
