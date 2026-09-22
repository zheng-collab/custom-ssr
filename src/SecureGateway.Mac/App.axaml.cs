using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using SecureGateway.Mac.Platform;
using SecureGateway.Mac.Views;
using SecureGateway.Platform;
using SecureGateway.Services;

namespace SecureGateway.Mac
{
    public partial class App : Application
    {
        private static readonly string CrashLog = Path.Combine(AppPaths.LogDir, "crash.log");

        private IClassicDesktopStyleApplicationLifetime _desktop;
        private AuthService _authService;
        private MainWindow _mainWindow;
        private TrayIcon _tray;
        private NativeMenuItem _connectItem;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _desktop = desktop;
                // Hiding to the menu bar must not quit the app.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                desktop.Exit += (_, _) => _mainWindow?.ShutdownCleanup();

                TaskScheduler.UnobservedTaskException += (_, e) => { WriteCrashLog(e.Exception); e.SetObserved(); };
                AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject as Exception);

                // macOS: clicking the Dock icon while the window is hidden should bring it back.
                if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
                    activatable.Activated += (_, e) => { if (e.Kind == ActivationKind.Reopen) ShowMain(); };

                _ = StartAsync(desktop.Args ?? Array.Empty<string>());
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async Task StartAsync(string[] args)
        {
            bool startMinimized = args.Contains("--minimized");

            _authService = new AuthService(new MacCredentialStore());
            try
            {
                await _authService.InitializeAsync();
            }
            catch (Exception ex)
            {
                await MessageWindow.ShowAsync(null, "SecureGateway",
                    $"Failed to initialize authentication:\n{ex.Message}", MessageWindow.Kind.Error);
                _desktop.Shutdown();
                return;
            }

            if (!_authService.IsAuthenticated)
            {
                var login = new LoginWindow(_authService);
                _desktop.MainWindow = login;
                login.Show();
                if (!await login.WaitForResultAsync())
                {
                    _desktop.Shutdown();
                    return;
                }
            }

            _mainWindow = new MainWindow(_authService, this);
            _desktop.MainWindow = _mainWindow;
            SetupTrayIcon();

            if (!startMinimized || !_mainWindow.ViewModel.MinimizeToTray)
                _mainWindow.Show();
        }

        // ---- menu bar (tray) icon -------------------------------------------------------------
        private void SetupTrayIcon()
        {
            _tray = new TrayIcon
            {
                ToolTipText = "SecureGateway VPN",
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SecureGateway/Assets/tray.png"))),
                IsVisible = true
            };

            var menu = new NativeMenu();

            var show = new NativeMenuItem("Show SecureGateway");
            show.Click += (_, _) => ShowMain();
            menu.Add(show);
            menu.Add(new NativeMenuItemSeparator());

            _connectItem = new NativeMenuItem("Connect");
            _connectItem.Click += (_, _) => _mainWindow?.ViewModel.ToggleConnectionCommand.Execute(null);
            menu.Add(_connectItem);
            menu.Add(new NativeMenuItemSeparator());

            var quit = new NativeMenuItem("Quit SecureGateway");
            quit.Click += (_, _) => Quit();
            menu.Add(quit);

            _tray.Menu = menu;
            _tray.Clicked += (_, _) => ShowMain();

            if (_mainWindow != null)
                _mainWindow.ViewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(_mainWindow.ViewModel.IsConnected))
                        Dispatcher.UIThread.Post(() =>
                            _connectItem.Header = _mainWindow.ViewModel.IsConnected ? "Disconnect" : "Connect");
                };

            TrayIcon.SetIcons(this, new TrayIcons { _tray });
        }

        public void ShowMain()
        {
            if (_mainWindow == null) return;
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        public void Quit()
        {
            if (_tray != null) _tray.IsVisible = false;
            _mainWindow?.ShutdownCleanup();
            _desktop?.Shutdown();
        }

        /// <summary>Sign out from the main window: back to the login screen.</summary>
        public async Task SignOutAsync()
        {
            if (_mainWindow == null) return;

            await _mainWindow.ViewModel.DisconnectAsync();
            await _authService.SignOutAsync();
            _mainWindow.ViewModel.ClearSharedServers();
            _mainWindow.Hide();

            var login = new LoginWindow(_authService);
            login.Show();
            if (await login.WaitForResultAsync())
            {
                _mainWindow.ViewModel.SetAuthService(_authService);
                ShowMain();
            }
            else
            {
                Quit();
            }
        }

        public static void WriteCrashLog(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CrashLog)!);
                File.AppendAllText(CrashLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
