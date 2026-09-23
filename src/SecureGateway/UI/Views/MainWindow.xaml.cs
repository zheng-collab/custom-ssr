using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using SecureGateway.Services;
using SecureGateway.UI.ViewModels;

namespace SecureGateway.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly AuthService _authService;
        private bool _forceClose;
        private bool _cleanedUp;

        public MainViewModel ViewModel { get; }

        public MainWindow(bool startMinimized, AuthService authService)
        {
            InitializeComponent();

            _authService = authService;
            ViewModel = new MainViewModel();
            DataContext = ViewModel;

            ViewModel.LogEntries.CollectionChanged += (s, e) =>
            {
                if (LogListBox.Items.Count > 0)
                    LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            };

            ViewModel.SetAuthService(authService);

            if (startMinimized)
            {
                WindowState = WindowState.Minimized;
                if (ViewModel.MinimizeToTray)
                    Hide();
            }

            // Runs even when the window starts hidden in the tray, unlike Loaded/SourceInitialized.
            if (ViewModel.AutoConnect)
            {
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(1000);
                    if (!ViewModel.IsConnected && !ViewModel.IsConnecting)
                        ViewModel.ToggleConnectionCommand.Execute(null);
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

            await ViewModel.DisconnectAsync();
            await _authService.SignOutAsync();
            ViewModel.ClearSharedServers();
            Hide();

            var loginWindow = new LoginWindow(_authService);
            if (loginWindow.ShowDialog() == true && loginWindow.IsAuthenticated)
            {
                ViewModel.SetAuthService(_authService);
                Show();
                Activate();
                if (loginWindow.Bootstrap != null)
                    await ViewModel.AdoptBootstrapAsync(loginWindow.Bootstrap);
            }
            else
            {
                _forceClose = true;
                Application.Current.Shutdown();
            }
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (!_forceClose && ViewModel.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            ShutdownCleanup();
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && ViewModel.MinimizeToTray)
                Hide();
        }

        public void ForceClose()
        {
            _forceClose = true;
            Close();
        }

        /// <summary>Stops the proxy engine and restores the system proxy. Safe to call more than once.</summary>
        public void ShutdownCleanup()
        {
            if (_cleanedUp) return;
            _cleanedUp = true;
            _forceClose = true;
            ViewModel.Dispose();
        }

        public void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
    }
}
