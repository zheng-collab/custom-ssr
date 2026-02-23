using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SecureGateway.Core.Config;
using SecureGateway.Core.Engines;
using SecureGateway.Core.Logging;
using SecureGateway.Models;
using SecureGateway.Services;
using SecureGateway.Utils;

namespace SecureGateway.UI.ViewModels
{
    public class MainViewModel : BaseViewModel, IDisposable
    {
        private readonly ConfigManager _configManager;
        private readonly ConnectionService _connectionService;
        private readonly AppLogger _logger;

        // Connection state
        private bool _isConnected;
        private bool _isConnecting;
        private string _statusText = "Disconnected";
        private string _connectionInfo = "Not connected to any server";

        // Server selection
        private ServerProfile _selectedServer;

        // Stats
        private string _uploadSpeed = "0 B";
        private string _downloadSpeed = "0 B";
        private string _latency = "N/A";
        private string _connectionDuration = "00:00:00";

        // Proxy mode
        private ProxyMode _proxyMode;

        // Settings
        private bool _autoStart;
        private bool _autoConnect;
        private bool _minimizeToTray;

        // Auth
        private string _userEmail = "";

        public ObservableCollection<ServerProfile> Servers { get; } = new();
        public ObservableCollection<string> LogEntries { get; } = new();

        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        public bool IsConnecting
        {
            get => _isConnecting;
            set => SetProperty(ref _isConnecting, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string ConnectionInfo
        {
            get => _connectionInfo;
            set => SetProperty(ref _connectionInfo, value);
        }

        public ServerProfile SelectedServer
        {
            get => _selectedServer;
            set
            {
                if (SetProperty(ref _selectedServer, value) && value != null)
                    _configManager.SetActiveServer(value.Id);
            }
        }

        public string UploadSpeed
        {
            get => _uploadSpeed;
            set => SetProperty(ref _uploadSpeed, value);
        }

        public string DownloadSpeed
        {
            get => _downloadSpeed;
            set => SetProperty(ref _downloadSpeed, value);
        }

        public string Latency
        {
            get => _latency;
            set => SetProperty(ref _latency, value);
        }

        public string ConnectionDuration
        {
            get => _connectionDuration;
            set => SetProperty(ref _connectionDuration, value);
        }

        public string UserEmail
        {
            get => _userEmail;
            set => SetProperty(ref _userEmail, value);
        }

        public ProxyMode ProxyMode
        {
            get => _proxyMode;
            set
            {
                if (SetProperty(ref _proxyMode, value))
                    _connectionService.ChangeProxyMode(value);
            }
        }

        public bool AutoStart
        {
            get => _autoStart;
            set
            {
                if (SetProperty(ref _autoStart, value))
                {
                    AutoStartHelper.SetAutoStart(value);
                    _configManager.Config.AutoStart = value;
                    _configManager.Save();
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
                    _configManager.Config.AutoConnect = value;
                    _configManager.Save();
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
                    _configManager.Config.MinimizeToTray = value;
                    _configManager.Save();
                }
            }
        }

        // Commands
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
        public ICommand ExportConfigCommand { get; }

        public MainViewModel()
        {
            _configManager = new ConfigManager();
            _logger = new AppLogger(ConfigManager.GetLogDirectory());
            _connectionService = new ConnectionService(_configManager, _logger);

            // Wire up events
            _connectionService.StatusChanged += OnStatusChanged;
            _connectionService.StatsUpdated += OnStatsUpdated;
            _logger.LogAdded += OnLogAdded;

            // Create commands
            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected && !IsConnecting);
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
            ToggleConnectionCommand = new AsyncRelayCommand(ToggleConnectionAsync);
            AddServerCommand = new RelayCommand(AddServer);
            EditServerCommand = new RelayCommand(EditServer, _ => SelectedServer != null);
            DeleteServerCommand = new RelayCommand(DeleteServer, _ => SelectedServer != null);
            DuplicateServerCommand = new RelayCommand(DuplicateServer, _ => SelectedServer != null);
            TestLatencyCommand = new AsyncRelayCommand(TestLatencyAsync, _ => SelectedServer != null);
            TestAllLatencyCommand = new AsyncRelayCommand(TestAllLatencyAsync);
            ClearLogCommand = new RelayCommand(ClearLog);
            ImportFromClipboardCommand = new RelayCommand(ImportFromClipboard);

            // Load config
            LoadConfig();
        }

        private void LoadConfig()
        {
            var config = _configManager.Config;

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

            _logger.Info("Configuration loaded.");
        }

        private async Task ConnectAsync()
        {
            if (SelectedServer == null)
            {
                StatusText = "No server selected";
                return;
            }

            IsConnecting = true;
            StatusText = "Connecting...";
            await _connectionService.ConnectAsync(SelectedServer);
            IsConnecting = false;
        }

        private async Task DisconnectAsync()
        {
            await _connectionService.DisconnectAsync();
            IsConnected = false;
            StatusText = "Disconnected";
            ConnectionInfo = "Not connected to any server";
        }

        private async Task ToggleConnectionAsync()
        {
            if (IsConnected)
                await DisconnectAsync();
            else
                await ConnectAsync();
        }

        private void AddServer(object parameter)
        {
            var server = new ServerProfile
            {
                Name = $"Vultr Server {Servers.Count + 1}",
                Protocol = ProxyProtocol.V2Ray,
                V2RayTransport = V2RayTransport.WebSocket,
                V2RayTls = true,
                Port = 443
            };

            var dialog = new Views.ServerEditWindow(server) { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() == true)
            {
                _configManager.AddServer(server);
                Servers.Add(server);
                SelectedServer = server;
                _logger.Info($"Server added: {server.Name}");
            }
        }

        private void EditServer(object parameter)
        {
            if (SelectedServer == null) return;

            var dialog = new Views.ServerEditWindow(SelectedServer) { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() == true)
            {
                _configManager.UpdateServer(SelectedServer);
                _logger.Info($"Server updated: {SelectedServer.Name}");

                // Refresh the list
                var index = Servers.IndexOf(SelectedServer);
                if (index >= 0)
                {
                    var server = SelectedServer;
                    Servers.RemoveAt(index);
                    Servers.Insert(index, server);
                    SelectedServer = server;
                }
            }
        }

        private void DeleteServer(object parameter)
        {
            if (SelectedServer == null) return;

            var result = MessageBox.Show(
                $"Delete server '{SelectedServer.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var name = SelectedServer.Name;
                _configManager.RemoveServer(SelectedServer.Id);
                Servers.Remove(SelectedServer);
                SelectedServer = Servers.FirstOrDefault();
                _logger.Info($"Server deleted: {name}");
            }
        }

        private void DuplicateServer(object parameter)
        {
            if (SelectedServer == null) return;

            var clone = SelectedServer.Clone();
            clone.Name = $"{SelectedServer.Name} (Copy)";
            _configManager.AddServer(clone);
            Servers.Add(clone);
            SelectedServer = clone;
        }

        private async Task TestLatencyAsync(object parameter)
        {
            if (SelectedServer == null) return;

            _logger.Info($"Testing latency for {SelectedServer.Name}...");
            var latency = await _connectionService.TestServerLatencyAsync(SelectedServer);
            Latency = latency > 0 ? $"{latency:F0} ms" : "Timeout";
            _logger.Info($"Latency for {SelectedServer.Name}: {Latency}");
        }

        private async Task TestAllLatencyAsync(object parameter)
        {
            _logger.Info("Testing latency for all servers...");
            foreach (var server in Servers)
            {
                var latency = await _connectionService.TestServerLatencyAsync(server);
                var result = latency > 0 ? $"{latency:F0} ms" : "Timeout";
                _logger.Info($"  {server.Name}: {result}");
            }
        }

        private void ClearLog(object parameter)
        {
            _logger.Clear();
            LogEntries.Clear();
        }

        private void ImportFromClipboard(object parameter)
        {
            try
            {
                var text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text))
                {
                    MessageBox.Show("No text found in clipboard.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var linkType = ClipboardHelper.ParseShareLink(text);
                if (linkType == null)
                {
                    MessageBox.Show(
                        "Unsupported share link format. Supported: vmess://, ss://, vless://",
                        "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var server = ParseShareLink(text, linkType);
                if (server != null)
                {
                    _configManager.AddServer(server);
                    Servers.Add(server);
                    SelectedServer = server;
                    _logger.Info($"Server imported from clipboard: {server.Name}");
                    MessageBox.Show($"Server '{server.Name}' imported successfully.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private ServerProfile ParseShareLink(string link, string type)
        {
            if (type == "shadowsocks" && link.StartsWith("ss://"))
            {
                return ParseShadowsocksLink(link);
            }

            if (type == "v2ray" && link.StartsWith("vmess://"))
            {
                return ParseVmessLink(link);
            }

            return null;
        }

        private ServerProfile ParseShadowsocksLink(string link)
        {
            // ss://base64(method:password)@host:port#name
            var uri = link.Substring(5); // remove "ss://"
            var nameIndex = uri.IndexOf('#');
            var name = "Imported SS Server";
            if (nameIndex > 0)
            {
                name = Uri.UnescapeDataString(uri.Substring(nameIndex + 1));
                uri = uri.Substring(0, nameIndex);
            }

            // Try SIP002 format: base64(method:password)@host:port
            var atIndex = uri.IndexOf('@');
            if (atIndex > 0)
            {
                var userInfo = uri.Substring(0, atIndex);
                var hostPort = uri.Substring(atIndex + 1);

                try
                {
                    userInfo = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(userInfo));
                }
                catch { }

                var colonIndex = userInfo.IndexOf(':');
                if (colonIndex <= 0) return null;

                var method = userInfo.Substring(0, colonIndex);
                var password = userInfo.Substring(colonIndex + 1);

                var lastColon = hostPort.LastIndexOf(':');
                var host = hostPort.Substring(0, lastColon);
                var port = int.Parse(hostPort.Substring(lastColon + 1));

                var encryption = method switch
                {
                    "aes-128-gcm" => ShadowsocksEncryption.Aes128Gcm,
                    "aes-256-gcm" => ShadowsocksEncryption.Aes256Gcm,
                    "chacha20-ietf-poly1305" => ShadowsocksEncryption.ChaCha20IetfPoly1305,
                    _ => ShadowsocksEncryption.Aes256Gcm
                };

                return new ServerProfile
                {
                    Name = name,
                    Address = host,
                    Port = port,
                    Protocol = ProxyProtocol.Shadowsocks,
                    SsPassword = password,
                    SsEncryption = encryption
                };
            }

            return null;
        }

        private ServerProfile ParseVmessLink(string link)
        {
            // vmess://base64(json)
            var base64 = link.Substring(8); // remove "vmess://"
            try
            {
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                var obj = Newtonsoft.Json.Linq.JObject.Parse(json);

                var transport = (obj["net"]?.ToString() ?? "tcp") switch
                {
                    "ws" => V2RayTransport.WebSocket,
                    "h2" => V2RayTransport.HTTP2,
                    "grpc" => V2RayTransport.GRPC,
                    "quic" => V2RayTransport.QUIC,
                    _ => V2RayTransport.TCP
                };

                return new ServerProfile
                {
                    Name = obj["ps"]?.ToString() ?? "Imported V2Ray Server",
                    Address = obj["add"]?.ToString() ?? "",
                    Port = int.TryParse(obj["port"]?.ToString(), out var p) ? p : 443,
                    Protocol = ProxyProtocol.V2Ray,
                    V2RayUserId = obj["id"]?.ToString() ?? "",
                    V2RayAlterId = int.TryParse(obj["aid"]?.ToString(), out var a) ? a : 0,
                    V2RaySecurity = obj["scy"]?.ToString() ?? "auto",
                    V2RayTransport = transport,
                    V2RayPath = obj["path"]?.ToString() ?? "/",
                    V2RayHost = obj["host"]?.ToString() ?? "",
                    V2RayTls = obj["tls"]?.ToString() == "tls",
                    V2RaySni = obj["sni"]?.ToString() ?? ""
                };
            }
            catch
            {
                return null;
            }
        }

        private void OnStatusChanged(object sender, EngineStatusChangedEventArgs e)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                StatusText = e.Message;
                IsConnected = e.Status == EngineStatus.Running;
                IsConnecting = e.Status == EngineStatus.Starting;

                if (IsConnected && SelectedServer != null)
                    ConnectionInfo = $"{SelectedServer.Protocol} | {SelectedServer.Address}:{SelectedServer.Port} | {SelectedServer.V2RayTransport}";
                else if (!IsConnected)
                    ConnectionInfo = "Not connected to any server";
            });
        }

        private void OnStatsUpdated(object sender, ConnectionStats stats)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                UploadSpeed = stats.FormattedUpload;
                DownloadSpeed = stats.FormattedDownload;
                Latency = stats.FormattedLatency;
                ConnectionDuration = stats.FormattedDuration;
            });
        }

        private void OnLogAdded(object sender, LogEntry entry)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                LogEntries.Add(entry.ToString());

                // Keep UI log manageable
                while (LogEntries.Count > 1000)
                    LogEntries.RemoveAt(0);
            });
        }

        public void Dispose()
        {
            _connectionService?.Dispose();
            _logger?.Dispose();
        }
    }
}
