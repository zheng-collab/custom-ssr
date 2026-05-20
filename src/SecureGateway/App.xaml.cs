using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using SecureGateway.Services;
using SecureGateway.UI.Views;

namespace SecureGateway
{
    public partial class App : Application
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private System.Windows.Forms.NotifyIcon _trayIcon;
        private MainWindow _mainWindow;
        private Mutex _mutex;
        private AuthService _authService;

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Single instance check
            const string mutexName = "SecureGateway_SingleInstance_Mutex";
            _mutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("SecureGateway is already running.", "SecureGateway",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);

            bool startMinimized = false;
            foreach (var arg in e.Args)
            {
                if (arg == "--minimized")
                    startMinimized = true;
            }

            // Initialize auth service
            _authService = new AuthService();
            try
            {
                await _authService.InitializeAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize authentication:\n{ex.Message}",
                    "SecureGateway", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // If not already authenticated (no saved session), show login
            if (!_authService.IsAuthenticated)
            {
                var loginWindow = new LoginWindow(_authService);
                var result = loginWindow.ShowDialog();

                if (result != true || !loginWindow.IsAuthenticated)
                {
                    Shutdown();
                    return;
                }
            }

            // Authenticated - launch main window
            _mainWindow = new MainWindow(startMinimized, _authService);
            MainWindow = _mainWindow;

            SetupTrayIcon();

            if (!startMinimized)
                _mainWindow.Show();
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "SecureGateway VPN",
                Visible = true
            };

            using var bitmap = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                using var brush = new SolidBrush(Color.FromArgb(76, 175, 80));
                var points = new System.Drawing.Point[]
                {
                    new(8, 1), new(14, 4), new(14, 9), new(8, 15), new(2, 9), new(2, 4)
                };
                g.FillPolygon(brush, points);
            }
            var hIcon = bitmap.GetHicon();
            using (var tempIcon = System.Drawing.Icon.FromHandle(hIcon))
            {
                _trayIcon.Icon = (System.Drawing.Icon)tempIcon.Clone();
            }
            DestroyIcon(hIcon);

            // Context menu
            var menu = new System.Windows.Forms.ContextMenuStrip();

            var showItem = new System.Windows.Forms.ToolStripMenuItem("Show SecureGateway");
            showItem.Click += (s, e) => _mainWindow?.ShowFromTray();
            showItem.Font = new Font(showItem.Font, System.Drawing.FontStyle.Bold);
            menu.Items.Add(showItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var connectItem = new System.Windows.Forms.ToolStripMenuItem("Connect");
            connectItem.Click += (s, e) =>
            {
                var vm = _mainWindow?.DataContext as UI.ViewModels.MainViewModel;
                vm?.ToggleConnectionCommand.Execute(null);
            };
            menu.Items.Add(connectItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) =>
            {
                _trayIcon.Visible = false;
                _mainWindow?.ForceClose();
                Shutdown();
            };
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) => _mainWindow?.ShowFromTray();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
