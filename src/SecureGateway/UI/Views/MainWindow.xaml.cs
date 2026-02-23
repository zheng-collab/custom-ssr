using System;
using System.ComponentModel;
using System.Windows;
using SecureGateway.Services;
using SecureGateway.UI.ViewModels;

namespace SecureGateway.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly AuthService _authService;
        private bool _forceClose;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            _viewModel.LogEntries.CollectionChanged += (s, e) =>
            {
                if (LogListBox.Items.Count > 0)
                    LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            };
        }

        public MainWindow(bool startMinimized, AuthService authService) : this()
        {
            _authService = authService;
            _viewModel.UserEmail = authService.UserEmail;

            if (startMinimized)
            {
                WindowState = WindowState.Minimized;
                if (_viewModel.MinimizeToTray)
                    Hide();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            if (_viewModel.AutoConnect)
            {
                Dispatcher.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(500);
                    _viewModel.ToggleConnectionCommand.Execute(null);
                });
            }
        }

        private async void OnLogoutClick(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Sign out and return to the login screen?",
                "Sign Out",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // Disconnect VPN first
            if (_viewModel.IsConnected)
            {
                _viewModel.ToggleConnectionCommand.Execute(null);
                await System.Threading.Tasks.Task.Delay(500);
            }

            if (_authService != null)
                await _authService.SignOutAsync();

            _viewModel.Dispose();

            // Show login window again
            var loginWindow = new LoginWindow(_authService ?? new AuthService());
            var loginResult = loginWindow.ShowDialog();

            if (loginResult == true && loginWindow.IsAuthenticated)
            {
                // Re-authenticated, update user info
                _viewModel.UserEmail = _authService?.UserEmail ?? "";
            }
            else
            {
                // User cancelled login, exit app
                _forceClose = true;
                Application.Current.Shutdown();
            }
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (!_forceClose && _viewModel.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            _viewModel.Dispose();
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && _viewModel.MinimizeToTray)
                Hide();
        }

        public void ForceClose()
        {
            _forceClose = true;
            Close();
        }

        public void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
    }
}
