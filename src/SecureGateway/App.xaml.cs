using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SecureGateway.Services;
using SecureGateway.UI.Views;

namespace SecureGateway
{
    public partial class App : Application
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private static readonly string CrashLog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SecureGateway", "logs", "crash.log");

        private System.Windows.Forms.NotifyIcon _trayIcon;
        private MainWindow _mainWindow;
        private Mutex _mutex;
        private AuthService _authService;

        protected override async void OnStartup(StartupEventArgs e)
        {
            RegisterGlobalExceptionHandlers();

            // "Local\" scopes the mutex to the current Windows logon session, so different
            // Windows user accounts on the same PC can each run their own instance.
            _mutex = new Mutex(true, @"Local\SecureGateway_SingleInstance_Mutex", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("SecureGateway is already running.", "SecureGateway",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);

            bool startMinimized = Array.IndexOf(e.Args, "--minimized") >= 0;

            _authService = new AuthService(new DpapiCredentialStore());
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

            if (!_authService.IsAuthenticated)
            {
                var loginWindow = new LoginWindow(_authService);
                if (loginWindow.ShowDialog() != true || !loginWindow.IsAuthenticated)
                {
                    Shutdown();
                    return;
                }
            }

            _mainWindow = new MainWindow(startMinimized, _authService);
            MainWindow = _mainWindow;

            SetupTrayIcon();

            if (!startMinimized)
                _mainWindow.Show();
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            // Windows is logging off / shutting down: restore the system proxy now.
            _mainWindow?.ShutdownCleanup();
            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mainWindow?.ShutdownCleanup();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            _mutex?.Dispose();
            base.OnExit(e);
        }

        private void RegisterGlobalExceptionHandlers()
        {
            DispatcherUnhandledException += (s, args) =>
            {
                WriteCrashLog(args.Exception);
                MessageBox.Show($"An unexpected error occurred:\n{args.Exception.Message}",
                    "SecureGateway", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                WriteCrashLog(args.Exception);
                args.SetObserved();
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                WriteCrashLog(args.ExceptionObject as Exception);
        }

        private static void WriteCrashLog(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CrashLog)!);
                File.AppendAllText(CrashLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { }
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "SecureGateway VPN",
                Visible = true
            };

            using (var bitmap = new Bitmap(16, 16))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    using var brush = new SolidBrush(Color.FromArgb(76, 175, 80));
                    g.FillPolygon(brush, new System.Drawing.Point[]
                    {
                        new(8, 1), new(14, 4), new(14, 9), new(8, 15), new(2, 9), new(2, 4)
                    });
                }

                var hIcon = bitmap.GetHicon();
                using (var tempIcon = Icon.FromHandle(hIcon))
                {
                    _trayIcon.Icon = (Icon)tempIcon.Clone();
                }
                DestroyIcon(hIcon);
            }

            var menu = new System.Windows.Forms.ContextMenuStrip();

            var showItem = new System.Windows.Forms.ToolStripMenuItem("Show SecureGateway");
            showItem.Click += (s, e) => _mainWindow?.ShowFromTray();
            showItem.Font = new Font(showItem.Font, System.Drawing.FontStyle.Bold);
            menu.Items.Add(showItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var connectItem = new System.Windows.Forms.ToolStripMenuItem("Connect");
            connectItem.Click += (s, e) => _mainWindow?.ViewModel.ToggleConnectionCommand.Execute(null);
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

            menu.Opening += (s, e) =>
                connectItem.Text = _mainWindow?.ViewModel.IsConnected == true ? "Disconnect" : "Connect";

            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, e) => _mainWindow?.ShowFromTray();
        }
    }
}
