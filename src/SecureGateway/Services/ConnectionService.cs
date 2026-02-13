using System;
using System.Threading;
using System.Threading.Tasks;
using SecureGateway.Core.Config;
using SecureGateway.Core.Engines;
using SecureGateway.Core.Logging;
using SecureGateway.Core.Routing;
using SecureGateway.Models;

namespace SecureGateway.Services
{
    public class ConnectionService : IDisposable
    {
        private readonly ConfigManager _configManager;
        private readonly SystemProxyService _systemProxy;
        private readonly RoutingManager _routingManager;
        private readonly AppLogger _logger;

        private IProxyEngine _currentEngine;
        private Timer _statsTimer;
        private bool _disposed;

        public ConnectionStats Stats { get; private set; } = new();
        public bool IsConnected => _currentEngine?.Status == EngineStatus.Running;
        public EngineStatus EngineStatus => _currentEngine?.Status ?? EngineStatus.Stopped;

        public event EventHandler<EngineStatusChangedEventArgs> StatusChanged;
        public event EventHandler<ConnectionStats> StatsUpdated;
        public event EventHandler<EngineLogEventArgs> LogReceived;

        public ConnectionService(ConfigManager configManager, AppLogger logger)
        {
            _configManager = configManager;
            _logger = logger;
            _systemProxy = new SystemProxyService();
            _routingManager = new RoutingManager();
        }

        public async Task ConnectAsync()
        {
            var server = _configManager.GetActiveServer();
            if (server == null)
            {
                _logger.Warning("No active server configured.");
                StatusChanged?.Invoke(this,
                    new EngineStatusChangedEventArgs(EngineStatus.Error, "No server configured. Please add a server first."));
                return;
            }

            await ConnectAsync(server);
        }

        public async Task ConnectAsync(ServerProfile server)
        {
            _logger.Info($"Connecting to {server.Name} ({server.Address}:{server.Port})...");

            // Create appropriate engine
            _currentEngine?.Dispose();
            _currentEngine = server.Protocol switch
            {
                ProxyProtocol.V2Ray => new V2RayEngine(),
                ProxyProtocol.Shadowsocks => new ShadowsocksEngine(),
                _ => new V2RayEngine()
            };

            _currentEngine.StatusChanged += OnEngineStatusChanged;
            _currentEngine.LogReceived += OnEngineLogReceived;

            try
            {
                await _currentEngine.StartAsync(server);

                if (_currentEngine.Status == EngineStatus.Running)
                {
                    // Configure system proxy
                    _systemProxy.ConfigureForMode(
                        _configManager.Config.ProxyMode,
                        server.LocalHttpPort);

                    // Start stats collection
                    Stats = new ConnectionStats { ConnectedSince = DateTime.UtcNow };
                    _statsTimer = new Timer(UpdateStats, server, 1000, 2000);

                    _configManager.SetActiveServer(server.Id);
                    _logger.Info($"Connected to {server.Name} successfully.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Connection failed: {ex.Message}");
                StatusChanged?.Invoke(this,
                    new EngineStatusChangedEventArgs(EngineStatus.Error, $"Connection failed: {ex.Message}"));
            }
        }

        public async Task DisconnectAsync()
        {
            _logger.Info("Disconnecting...");

            _statsTimer?.Dispose();
            _statsTimer = null;

            if (_currentEngine != null)
            {
                _currentEngine.StatusChanged -= OnEngineStatusChanged;
                _currentEngine.LogReceived -= OnEngineLogReceived;
                await _currentEngine.StopAsync();
                _currentEngine.Dispose();
                _currentEngine = null;
            }

            // Restore system proxy settings
            _systemProxy.DisableProxy();

            Stats = new ConnectionStats();
            StatsUpdated?.Invoke(this, Stats);
            _logger.Info("Disconnected.");
        }

        public async Task ReconnectAsync()
        {
            await DisconnectAsync();
            await Task.Delay(500);
            await ConnectAsync();
        }

        public async Task<double> TestServerLatencyAsync(ServerProfile server)
        {
            using var engine = server.Protocol switch
            {
                ProxyProtocol.V2Ray => (IProxyEngine)new V2RayEngine(),
                ProxyProtocol.Shadowsocks => new ShadowsocksEngine(),
                _ => new V2RayEngine()
            };

            return await engine.TestLatencyAsync(server);
        }

        public void ChangeProxyMode(ProxyMode mode)
        {
            _configManager.Config.ProxyMode = mode;
            _configManager.Save();

            if (IsConnected)
            {
                var server = _configManager.GetActiveServer();
                if (server != null)
                {
                    _systemProxy.ConfigureForMode(mode, server.LocalHttpPort);
                    _logger.Info($"Proxy mode changed to {mode}.");
                }
            }
        }

        private void OnEngineStatusChanged(object sender, EngineStatusChangedEventArgs e)
        {
            StatusChanged?.Invoke(this, e);
        }

        private void OnEngineLogReceived(object sender, EngineLogEventArgs e)
        {
            _logger.Log(e.Message, e.Level, "Engine");
            LogReceived?.Invoke(this, e);
        }

        private async void UpdateStats(object state)
        {
            if (!IsConnected) return;

            var server = state as ServerProfile;
            if (server == null) return;

            try
            {
                var latency = await _currentEngine.TestLatencyAsync(server);
                Stats.LatencyMs = latency;
                StatsUpdated?.Invoke(this, Stats);
            }
            catch
            {
                // Ignore stats update failures
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _statsTimer?.Dispose();
            _systemProxy.DisableProxy();
            _currentEngine?.Dispose();
        }
    }
}
