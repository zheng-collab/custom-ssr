using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SecureGateway.Services;
using SecureGateway.UI.ViewModels;

namespace SecureGateway.Mac.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _vm;
        private readonly TaskCompletionSource<bool> _result = new();
        private readonly DispatcherTimer _timer;
        private bool _authenticated;

        public LoginWindow(AuthService auth)
        {
            InitializeComponent();

            _vm = new LoginViewModel(auth);
            DataContext = _vm;
            _vm.Authenticated += (_, _) =>
            {
                _authenticated = true;
                _result.TrySetResult(true);
                Close();
            };
            _vm.PropertyChanged += OnVmPropertyChanged;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => _vm.Tick();
            _timer.Start();

            UpdateTabs();
            Opened += (_, _) => (string.IsNullOrEmpty(_vm.Email) ? TxtEmail : TxtPassword).Focus();
        }

        /// <summary>True when the user signed in; false when the window was closed without signing in.</summary>
        public Task<bool> WaitForResultAsync() => _result.Task;

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _timer.Stop();
            if (!_authenticated) _result.TrySetResult(false);
            base.OnClosing(e);
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LoginViewModel.StatusIsError))
                TxtStatus.Foreground = new SolidColorBrush(Color.Parse(_vm.StatusIsError ? "#F38BA8" : "#A6E3A1"));
            if (e.PropertyName == nameof(LoginViewModel.IsSignUpMode))
                UpdateTabs();
        }

        private void UpdateTabs()
        {
            BtnSignInTab.Classes.Set("accent", !_vm.IsSignUpMode);
            BtnSignUpTab.Classes.Set("accent", _vm.IsSignUpMode);
        }

        private void OnSignInTab(object sender, RoutedEventArgs e) => _vm.IsSignUpMode = false;
        private void OnSignUpTab(object sender, RoutedEventArgs e) => _vm.IsSignUpMode = true;
        private void OnForgotPassword(object sender, RoutedEventArgs e)
        {
            if (AuthService.UsesWebPasswordReset)
            {
                _vm.ShowWebResetHint(AuthService.OpenPasswordResetPage());
                return;
            }
            _vm.IsResetMode = true;
        }
        private void OnBackToSignIn(object sender, RoutedEventArgs e) => _vm.IsResetMode = false;

        private void OnTogglePassword(object sender, RoutedEventArgs e)
            => TxtPassword.RevealPassword = !TxtPassword.RevealPassword;

        private async void OnSubmit(object sender, RoutedEventArgs e) => await _vm.SubmitAsync();
        private async void OnSendReset(object sender, RoutedEventArgs e) => await _vm.SendResetCodeAsync();
        private async void OnApplyReset(object sender, RoutedEventArgs e) => await _vm.ApplyResetAsync();

        private async void OnEnter(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && _vm.IsMainMode) await _vm.SubmitAsync();
        }

        private async void OnResetEnter(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) await _vm.ApplyResetAsync();
        }
    }
}
