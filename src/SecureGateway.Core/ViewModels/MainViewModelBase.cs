using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using SecureGateway.Core.Config;
using SecureGateway.Core.Engines;
using SecureGateway.Core.Logging;
using SecureGateway.Models;
using SecureGateway.Platform;
using SecureGateway.Services;
using SecureGateway.Utils;

namespace SecureGateway.UI.ViewModels
{
    public enum MessageKind { Info, Warning, Error }

    /// <summary>
    /// All main-window behaviour, shared by the Windows (WPF) and macOS (Avalonia) apps.
    /// Platform projects supply dialogs, UI-thread marshalling, clipboard, the server
    /// editor and auto-start via the abstract members.
    /// </summary>
    public abstract class MainViewModelBase : BaseViewModel, IDisposable
    {
        protected readonly ConfigManager ConfigManager;
        protected readonly ConnectionService ConnectionService;
        protected readonly AppLogger Logger;
        private SharedServerService _sharedServerService;
        private AuthService _auth;

        private bool _isConnected;
        private bool _isConnecting;
        private string _statusText = "Disconnected";
        private string _connectionInfo = "Not connected to any server";
        private ServerProfile _selectedServer;
        private string _uploadSpeed = "0 B";
        private string _downloadSpeed = "0 B";
        private string _latency = "N/A";
        private string _connectionDuration = "00:00:00";
        private ProxyMode _proxyMode;
        private bool _autoStart;
        private bool _autoConnect;
        private bool _minimizeToTray;
        private string _userEmail = "";
        private bool _isSyncing;

        public ObservableCollection<ServerProfile> Servers { get; } = new();
        public ObservableCollection<string> LogEntries { get; } = new();

        // ---- platform hooks -------------------------------------------------------------
        protected abstract void RunOnUi(Action action);
        protected abstract Task ShowMessageAsync(string title, string message, MessageKind kind);
        protected abstract Task<bool> ConfirmAsync(string title, string message);
        /// <summary>Opens the server editor; returns true if the user saved.</summary>
        protected abstract Task<bool> EditServerAsync(ServerProfile server);
        protected abstract Task<string> GetClipboardTextAsync();
        protected abstract void ApplyAutoStart(bool enable);

        protected MainViewModelBase(ISystemProxyService systemProxy)
        {
            ConfigManager = new ConfigManager();
            Logger = new AppLogger(ConfigManager.GetLogDirectory());
            ConnectionService = new ConnectionService(ConfigManager, Logger, systemProxy);

            ConnectionService.StatusChanged += OnStatusChanged;
            ConnectionService.StatsUpdated += OnStatsUpdated;
            Logger.LogAdded += OnLogAdded;

            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected && !IsConnecting);
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
            ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync);
            AddServerCommand = new AsyncRelayCommand(AddServerAsync);
            EditServerCommand = new AsyncRelayCommand(EditSelectedServerAsync, () => SelectedServer != null);
            DeleteServerCommand = new AsyncRelayCommand(DeleteServerAsync, () => SelectedServer != null);
            DuplicateServerCommand = new RelayCommand(DuplicateServer, () => SelectedServer != null);
            TestLatencyCommand = new AsyncRelayCommand(TestLatencyAsync, () => SelectedServer != null);
            TestAllLatencyCommand = new AsyncRelayCommand(TestAllLatencyAsync);
            ClearLogCommand = new RelayCommand(ClearLog);
            ImportFromClipboardCommand = new AsyncRelayCommand(ImportFromClipboardAsync);
            SyncSharedServersCommand = new AsyncRelayCommand(SyncSharedServersAsync);
            ShareServerCommand = new AsyncRelayCommand(ShareServerAsync, () => SelectedServer != null && !SelectedServer.IsShared);
            UnshareServerCommand = new AsyncRelayCommand(UnshareServerAsync, () => SelectedServer != null && SelectedServer.IsShared);

            LoadConfig();
        }

        // ---- bindable state -----------------------------------------------------------------
        public bool IsConnected
        {
            get => _isConnected;
            set { if (SetProperty(ref _isConnected, value)) RefreshCommands(); }
        }

        public bool IsConnecting
        {
            get => _isConnecting;
            set { if (SetProperty(ref _isConnecting, value)) RefreshCommands(); }
        }

        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
        public string ConnectionInfo { get => _connectionInfo; set => SetProperty(ref _connectionInfo, value); }
        public string UploadSpeed { get => _uploadSpeed; set => SetProperty(ref _uploadSpeed, value); }
        public string DownloadSpeed { get => _downloadSpeed; set => SetProperty(ref _downloadSpeed, value); }
        public string Latency { get => _latency; set => SetProperty(ref _latency, value); }
        public string ConnectionDuration { get => _connectionDuration; set => SetProperty(ref _connectionDuration, value); }
        public string UserEmail { get => _userEmail; set => SetProperty(ref _userEmail, value); }
        public bool IsSyncing { get => _isSyncing; set => SetProperty(ref _isSyncing, value); }

        public ServerProfile SelectedServer
        {
            get => _selectedServer;
            set
            {
                if (SetProperty(ref _selectedServer, value))
                {
                    if (value != null && !value.IsShared)
                        ConfigManager.SetActiveServer(value.Id);
                    OnPropertyChanged(nameof(IsSelectedServerShared));
                    RefreshCommands();
                }
            }
        }

        public bool IsSelectedServerShared => SelectedServer?.IsShared ?? false;

        public ProxyMode ProxyMode
        {
            get => _proxyMode;
            set { if (SetProperty(ref _proxyMode, value)) ConnectionService.ChangeProxyMode(value); }
        }

        public bool AutoStart
        {
            get => _autoStart;
            set
            {
                if (SetProperty(ref _autoStart, value))
                {
                    ApplyAutoStart(value);
                    ConfigManager.Config.AutoStart = value;
                    ConfigManager.Save();
                }
            }
        }

        public bool AutoConnect
        {
            get => _autoConnect;
            set
            {
                if (SetProperty(ref _autoConnect, value))
                {
                    ConfigManager.Config.AutoConnect = value;
                    ConfigManager.Save();
                }
            }
        }

        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set
            {
                if (SetProperty(ref _minimizeToTray, value))
                {
                    ConfigManager.Config.MinimizeToTray = value;
                    ConfigManager.Save();
                }
            }
        }

        // ---- commands -------------------------------------------------------------------------
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand ToggleConnectionCommand { get; }
        public ICommand AddServerCommand { get; }
        public ICommand EditServerCommand { get; }
        public ICommand DeleteServerCommand { get; }
        public ICommand DuplicateServerCommand { get; }
        public ICommand TestLatencyCommand { get; }
        public ICommand TestAllLatencyCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand ImportFromClipboardCommand { get; }
        public ICommand SyncSharedServersCommand { get; }
        public ICommand ShareServerCommand { get; }
        public ICommand UnshareServerCommand { get; }

        protected void RefreshCommands()
        {
            foreach (var c in new ICommand[]
                     {
                         ConnectCommand, DisconnectCommand, EditServerCommand, DeleteServerCommand,
                         DuplicateServerCommand, TestLatencyCommand, ShareServerCommand, UnshareServerCommand
                     })
            {
                switch (c)
                {
                    case AsyncRelayCommand a: a.RaiseCanExecuteChanged(); break;
                    case RelayCommand r: r.RaiseCanExecuteChanged(); break;
                }
            }
        }

        // ---- auth / shared servers ------------------------------------------------------
        public void SetAuthService(AuthService authService)
        {
            if (authService == null) return;
            _auth = authService;
            _sharedServerService = authService.CreateSharedServerService();
            UserEmail = authService.UserEmail;

            if (authService.IsAuthenticated)
            {
                _ = SyncSharedServersAsync();
            }
            else
            {
                // Offline session (login server unreachable): show what we knew last time so the
                // user can connect; the real sync runs once the tunnel is up.
                MergeSharedServers(SharedServerService.LoadCache());
                if (Servers.Count > 0)
                    Logger.Info($"Login server unreachable; showing {Servers.Count(s => s.IsShared)} cached shared server(s). Connect to sync.");
            }
        }

        /// <summary>
        /// Adopts a tunnel that was started on the login screen ("connect first") so the user
        /// is connected the moment the main window appears.
        /// </summary>
        public async Task AdoptBootstrapAsync(BootstrapConnector bootstrap)
        {
            if (bootstrap == null || !bootstrap.IsRunning) return;

            var (engine, runtime, original) = bootstrap.Detach();

            // Make sure the server is in the list (a pasted link is new; persist it).
            var existing = Servers.FirstOrDefault(s => s.Id == original.Id)
                        ?? Servers.FirstOrDefault(s => s.Address == original.Address && s.Port == original.Port && s.Protocol == original.Protocol);
            if (existing == null)
            {
                if (!original.IsShared)
                    ConfigManager.AddServer(original);
                Servers.Add(original);
                existing = original;
                Logger.Info($"Server added from sign-in link: {original.Name}");
            }
            SelectedServer = existing;

            IsConnecting = true;
            await ConnectionService.AdoptRunningEngineAsync(engine, runtime, existing);
            IsConnecting = false;
        }

        private void MergeSharedServers(System.Collections.Generic.IList<ServerProfile> shared)
        {
            foreach (var s in Servers.Where(s => s.IsShared).ToList())
                Servers.Remove(s);

            for (int i = 0; i < shared.Count; i++)
                Servers.Insert(i, shared[i]);

            if (SelectedServer == null && Servers.Count > 0)
                SelectedServer = Servers.FirstOrDefault();
        }

        private bool _postConnectRunning;

        /// <summary>After the tunnel is up: verify an offline session and sync shared servers through it.</summary>
        private async Task OnTunnelUpAsync()
        {
            if (_auth == null || _postConnectRunning) return;
            _postConnectRunning = true;
            try
            {
                if (!_auth.IsAuthenticated && _auth.HasOfflineSession)
                {
                    Logger.Info("Verifying saved sign-in through the tunnel...");
                    if (await _auth.EnsureSessionAsync())
                    {
                        UserEmail = _auth.UserEmail;
                        Logger.Info("Sign-in verified.");
                    }
                    else if (!_auth.HasOfflineSession)
                    {
                        Logger.Warning("Saved sign-in was rejected by the server. Please sign out and sign in again.");
                        await ShowMessageAsync("Sign-in expired",
                            "Your saved sign-in is no longer valid. Use Sign Out and sign in again.", MessageKind.Warning);
                        return;
                    }
                }

                if (_auth.IsAuthenticated)
                    await SyncSharedServersAsync();
            }
            finally
            {
                _postConnectRunning = false;
            }
        }

        public void ClearSharedServers()
        {
            foreach (var s in Servers.Where(s => s.IsShared).ToList())
                Servers.Remove(s);

            if (SelectedServer != null && SelectedServer.IsShared)
                SelectedServer = Servers.FirstOrDefault();

            UserEmail = "";
        }

        private void LoadConfig()
        {
            var config = ConfigManager.Config;

            Servers.Clear();
            foreach (var server in config.Servers)
                Servers.Add(server);

            SelectedServer = Servers.FirstOrDefault(s => s.Id == config.ActiveServerId) ?? Servers.FirstOrDefault();
            _proxyMode = config.ProxyMode;
            _autoStart = config.AutoStart;
            _autoConnect = config.AutoConnect;
            _minimizeToTray = config.MinimizeToTray;

            OnPropertyChanged(nameof(ProxyMode));
            OnPropertyChanged(nameof(AutoStart));
            OnPropertyChanged(nameof(AutoConnect));
            OnPropertyChanged(nameof(MinimizeToTray));

            Logger.Info("Configuration loaded.");
        }

        // ---- connection -----------------------------------------------------------------------
        private async Task ConnectAsync()
        {
            if (SelectedServer == null)
            {
                StatusText = "No server selected";
                return;
            }

            IsConnecting = true;
            StatusText = "Connecting...";
            await ConnectionService.ConnectAsync(SelectedServer);
            IsConnecting = false;
        }

        public async Task DisconnectAsync()
        {
            await ConnectionService.DisconnectAsync();
            IsConnected = false;
            IsConnecting = false;
            StatusText = "Disconnected";
            ConnectionInfo = "Not connected to any server";
        }

        private async Task ToggleConnectionAsync()
        {
            if (IsConnected) await DisconnectAsync();
            else await ConnectAsync();
        }

        // ---- server management ------------------------------------------------------------
        private async Task AddServerAsync()
        {
            var server = new ServerProfile
            {
                Name = $"Vultr Server {Servers.Count + 1}",
                Protocol = ProxyProtocol.V2Ray,
                V2RayTransport = V2RayTransport.WebSocket,
                V2RayTls = true,
                Port = 443
            };

            if (await EditServerAsync(server))
            {
                ConfigManager.AddServer(server);
                Servers.Add(server);
                SelectedServer = server;
                Logger.Info($"Server added: {server.Name}");
            }
        }

        private async Task EditSelectedServerAsync()
        {
            if (SelectedServer == null) return;

            if (SelectedServer.IsShared)
            {
                await ShowMessageAsync("Shared Server",
                    "Shared servers cannot be edited. They are managed centrally for all users.", MessageKind.Info);
                return;
            }

            var server = SelectedServer;
            if (await EditServerAsync(server))
            {
                ConfigManager.UpdateServer(server);
                Logger.Info($"Server updated: {server.Name}");

                // Re-insert so list item templates refresh (ServerProfile is a plain POCO).
                var index = Servers.IndexOf(server);
                if (index >= 0)
                {
                    Servers.RemoveAt(index);
                    Servers.Insert(index, server);
                    SelectedServer = server;
                }
            }
        }

        private async Task DeleteServerAsync()
        {
            if (SelectedServer == null) return;

            if (SelectedServer.IsShared)
            {
                await ShowMessageAsync("Shared Server",
                    "Shared servers cannot be deleted locally. Use 'Unshare' to remove from the shared pool.", MessageKind.Info);
                return;
            }

            if (!await ConfirmAsync("Confirm Delete", $"Delete server '{SelectedServer.Name}'?"))
                return;

            var name = SelectedServer.Name;
            ConfigManager.RemoveServer(SelectedServer.Id);
            Servers.Remove(SelectedServer);
            SelectedServer = Servers.FirstOrDefault();
            Logger.Info($"Server deleted: {name}");
        }

        private void DuplicateServer()
        {
            if (SelectedServer == null) return;

            var clone = SelectedServer.Clone();
            clone.Name = $"{SelectedServer.Name} (Copy)";
            ConfigManager.AddServer(clone);
            Servers.Add(clone);
            SelectedServer = clone;
        }

        private async Task TestLatencyAsync()
        {
            if (SelectedServer == null) return;

            Logger.Info($"Testing latency for {SelectedServer.Name}...");
            var latency = await ConnectionService.TestServerLatencyAsync(SelectedServer);
            Latency = latency > 0 ? $"{latency:F0} ms" : "Timeout";
            Logger.Info($"Latency for {SelectedServer.Name}: {Latency}");
        }

        private async Task TestAllLatencyAsync()
        {
            Logger.Info("Testing latency for all servers...");
            foreach (var server in Servers.ToList())
            {
                var latency = await ConnectionService.TestServerLatencyAsync(server);
                Logger.Info($"  {server.Name}: {(latency > 0 ? $"{latency:F0} ms" : "Timeout")}");
            }
        }

        private void ClearLog()
        {
            Logger.Clear();
            LogEntries.Clear();
        }

        private async Task ImportFromClipboardAsync()
        {
            try
            {
                var text = await GetClipboardTextAsync();
                if (string.IsNullOrWhiteSpace(text))
                {
                    await ShowMessageAsync("Import", "No text found in clipboard.", MessageKind.Info);
                    return;
                }

                if (!ShareLinkParser.IsSupported(text))
                {
                    await ShowMessageAsync("Import",
                        "Unsupported share link format. Supported: vmess://, ss://", MessageKind.Warning);
                    return;
                }

                var server = ShareLinkParser.Parse(text);
                if (server == null)
                {
                    await ShowMessageAsync("Import", "The share link could not be parsed.", MessageKind.Warning);
                    return;
                }

                ConfigManager.AddServer(server);
                Servers.Add(server);
                SelectedServer = server;
                Logger.Info($"Server imported from clipboard: {server.Name}");
                await ShowMessageAsync("Import", $"Server '{server.Name}' imported successfully.", MessageKind.Info);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Import Error", $"Failed to import: {ex.Message}", MessageKind.Error);
            }
        }

        // ---- shared servers (Supabase) ----------------------------------------------------
        private async Task SyncSharedServersAsync()
        {
            if (_sharedServerService == null || IsSyncing) return;

            IsSyncing = true;
            Logger.Info("Syncing shared servers...");

            try
            {
                var sharedServers = await _sharedServerService.FetchSharedServersAsync();
                bool fromCache = sharedServers == null;
                if (fromCache)
                    sharedServers = SharedServerService.LoadCache();

                RunOnUi(() =>
                {
                    MergeSharedServers(sharedServers);
                    Logger.Info(fromCache
                        ? $"Shared server list unavailable (offline or not authorised); using {sharedServers.Count} cached server(s)."
                        : $"Synced {sharedServers.Count} shared server(s).");
                });
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to sync shared servers: {ex.Message}");
            }
            finally
            {
                IsSyncing = false;
            }
        }

        private async Task ShareServerAsync()
        {
            if (SelectedServer == null || SelectedServer.IsShared || _sharedServerService == null) return;

            if (!await ConfirmAsync("Share Server",
                    $"Share server '{SelectedServer.Name}' with all users?\n\n" +
                    "All authorized users will see this server and can connect to it simultaneously."))
                return;

            Logger.Info($"Publishing server '{SelectedServer.Name}' to shared pool...");
            var success = await _sharedServerService.PublishServerAsync(SelectedServer);

            if (success)
            {
                Logger.Info($"Server '{SelectedServer.Name}' shared successfully.");
                await ShowMessageAsync("Server Shared",
                    $"Server '{SelectedServer.Name}' is now shared with all users.", MessageKind.Info);
                await SyncSharedServersAsync();
            }
            else
            {
                Logger.Warning("Failed to share server. Check your permissions.");
                await ShowMessageAsync("Share Failed",
                    "Failed to share server. You may not have permission to publish shared servers.", MessageKind.Warning);
            }
        }

        private async Task UnshareServerAsync()
        {
            if (SelectedServer == null || !SelectedServer.IsShared || _sharedServerService == null) return;

            if (!await ConfirmAsync("Unshare Server",
                    $"Remove shared server '{SelectedServer.Name}' from the pool?\n\n" +
                    "Other users will no longer see this server after their next sync."))
                return;

            Logger.Info($"Removing shared server '{SelectedServer.Name}'...");
            var success = await _sharedServerService.UnpublishServerAsync(SelectedServer.SharedId);

            if (success)
            {
                Logger.Info($"Server '{SelectedServer.Name}' removed from shared pool.");
                Servers.Remove(SelectedServer);
                SelectedServer = Servers.FirstOrDefault();
            }
            else
            {
                Logger.Warning("Failed to remove shared server. Check your permissions.");
                await ShowMessageAsync("Unshare Failed",
                    "Failed to remove shared server. You may not have permission.", MessageKind.Warning);
            }
        }

        // ---- events from the connection layer (arrive on worker threads) ----------------
        private void OnStatusChanged(object sender, EngineStatusChangedEventArgs e)
        {
            RunOnUi(() =>
            {
                bool wasConnected = IsConnected;
                StatusText = e.Message;
                IsConnected = e.Status == EngineStatus.Running;
                IsConnecting = e.Status == EngineStatus.Starting;

                if (IsConnected && SelectedServer != null)
                    ConnectionInfo = $"{SelectedServer.Protocol} | {SelectedServer.Address}:{SelectedServer.Port} | {SelectedServer.V2RayTransport}";
                else if (!IsConnected)
                    ConnectionInfo = "Not connected to any server";

                if (IsConnected && !wasConnected)
                    _ = OnTunnelUpAsync();
            });
        }

        private void OnStatsUpdated(object sender, ConnectionStats stats)
        {
            RunOnUi(() =>
            {
                UploadSpeed = stats.FormattedUpload;
                DownloadSpeed = stats.FormattedDownload;
                Latency = stats.FormattedLatency;
                ConnectionDuration = stats.FormattedDuration;
            });
        }

        private void OnLogAdded(object sender, LogEntry entry)
        {
            RunOnUi(() =>
            {
                LogEntries.Add(entry.ToString());
                while (LogEntries.Count > 1000)
                    LogEntries.RemoveAt(0);
            });
        }

        public virtual void Dispose()
        {
            ConnectionService?.Dispose();
            Logger?.Dispose();
        }
    }
}
