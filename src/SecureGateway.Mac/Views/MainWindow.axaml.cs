using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SecureGateway.Mac.ViewModels;
using SecureGateway.Services;

namespace SecureGateway.Mac.Views
{
    public partial class MainWindow : Window
    {
        private readonly App _app;
        private bool _forceClose;
        private bool _cleanedUp;

        public MainViewModel ViewModel { get; }

        public MainWindow(AuthService authService, App app)
        {
            InitializeComponent();
            _app = app;

            ViewModel = new MainViewModel { Owner = this };
            DataContext = ViewModel;

            ViewModel.LogEntries.CollectionChanged += (_, _) =>
            {
                if (LogListBox.ItemCount > 0)
                    LogListBox.ScrollIntoView(LogListBox.ItemCount - 1);
            };

            ViewModel.SetAuthService(authService);

            if (ViewModel.AutoConnect)
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    await Task.Delay(1000);
                    if (!ViewModel.IsConnected && !ViewModel.IsConnecting)
                        ViewModel.ToggleConnectionCommand.Execute(null);
                });
            }
        }

        private async void OnSignOut(object sender, RoutedEventArgs e)
        {
            if (await MessageWindow.ConfirmAsync(this, "Sign Out", "Sign out and return to the login screen?"))
                await _app.SignOutAsync();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!_forceClose && ViewModel.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            ShutdownCleanup();
            base.OnClosing(e);
        }

        public void ForceClose()
        {
            _forceClose = true;
            Close();
        }

        /// <summary>Stops the engine and restores the system proxy. Safe to call more than once.</summary>
        public void ShutdownCleanup()
        {
            if (_cleanedUp) return;
            _cleanedUp = true;
            _forceClose = true;
            ViewModel.Dispose();
        }
    }
}
