using System;
using System.Threading;
using System.Threading.Tasks;
using SecureGateway.Core.Config;
using SecureGateway.Core.Engines;
using SecureGateway.Core.Logging;
using SecureGateway.Models;
using SecureGateway.Platform;
using SecureGateway.Utils;

namespace SecureGateway.Services
{
    public class ConnectionService : IDisposable
    {
        private readonly ConfigManager _configManager;
        private readonly ISystemProxyService _systemProxy;
        private readonly AppLogger _logger;

        private IProxyEngine _currentEngine;
        private ServerProfile _activeServer;
        private Timer _statsTimer;
        private bool _disposed;

        public ConnectionStats Stats { get; private set; } = new();
        public bool IsConnected => _currentEngine?.Status == EngineStatus.Running;
        public EngineStatus EngineStatus => _currentEngine?.Status ?? EngineStatus.Stopped;

        public event EventHandler<EngineStatusChangedEventArgs> StatusChanged;
        public event EventHandler<ConnectionStats> StatsUpdated;
        public event EventHandler<EngineLogEventArgs> LogReceived;

        public ConnectionService(ConfigManager configManager, AppLogger logger, ISystemProxyService systemProxy)
        {
            _configManager = configManager;
            _logger = logger;
            _systemProxy = systemProxy ?? throw new ArgumentNullException(nameof(systemProxy));
        }

        public async Task ConnectAsync(ServerProfile server)
        {
            if (_currentEngine != null)
                await DisconnectAsync();

            _logger.Info($"Connecting to {server.Name} ({server.Address}:{server.Port})...");

            ServerProfile runtime;
            try
            {
                runtime = ResolveLocalPorts(server);
            }
            catch (Exception ex)
            {
                _logger.Error(ex.Message);
                StatusChanged?.Invoke(this, new EngineStatusChangedEventArgs(EngineStatus.Error, ex.Message));
                return;
            }

            _activeServer = runtime;
            _currentEngine = new V2RayEngine();
            _currentEngine.StatusChanged += OnEngineStatusChanged;
            _currentEngine.LogReceived += OnEngineLogReceived;

            try
            {
                await _currentEngine.StartAsync(runtime);

                if (_currentEngine.Status != EngineStatus.Running)
                {
                    TearDownEngine();
                    return;
                }

                _systemProxy.ConfigureForMode(_configManager.Config.ProxyMode, runtime.LocalHttpPort, runtime.LocalSocksPort);

                Stats = new ConnectionStats { ConnectedSince = DateTime.UtcNow };
                _statsTimer = new Timer(UpdateStats, runtime, 5000, 15000);

                if (!server.IsShared)
                    _configManager.SetActiveServer(server.Id);

                _logger.Info($"Connected to {server.Name} successfully.");
            }
            catch (Exception ex)
            {
                TearDownEngine();
                _logger.Error($"Connection failed: {ex.Message}");
                StatusChanged?.Invoke(this,
                    new EngineStatusChangedEventArgs(EngineStatus.Error, $"Connection failed: {ex.Message}"));
            }
        }

        public async Task DisconnectAsync()
        {
            if (_currentEngine == null) return;

            _logger.Info("Disconnecting...");

            _statsTimer?.Dispose();
            _statsTimer = null;

            var engine = _currentEngine;
            engine.StatusChanged -= OnEngineStatusChanged;
            engine.LogReceived -= OnEngineLogReceived;

            try { await engine.StopAsync(); }
            catch (Exception ex) { _logger.Warning($"Error while stopping engine: {ex.Message}"); }

            engine.Dispose();
            _currentEngine = null;
            _activeServer = null;

            _systemProxy.DisableProxy();

            Stats = new ConnectionStats();
            StatsUpdated?.Invoke(this, Stats);
            _logger.Info("Disconnected.");
        }

        public async Task<double> TestServerLatencyAsync(ServerProfile server)
        {
            if (IsConnected && _activeServer != null && _activeServer.Id == server.Id)
                return await _currentEngine.TestLatencyAsync(_activeServer);

            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await tcp.ConnectAsync(server.Address, server.Port);
                sw.Stop();
                return sw.Elapsed.TotalMilliseconds;
            }
            catch
            {
                return -1;
            }
        }

        public void ChangeProxyMode(ProxyMode mode)
        {
            _configManager.Config.ProxyMode = mode;
            _configManager.Save();

            if (IsConnected && _activeServer != null)
            {
                _systemProxy.ConfigureForMode(mode, _activeServer.LocalHttpPort, _activeServer.LocalSocksPort);
                _logger.Info($"Proxy mode changed to {mode}.");
            }
        }

        /// <summary>
        /// Returns a profile whose local ports are guaranteed free. Multiple instances
        /// (other Windows users, other proxy tools) may already hold the defaults.
        /// </summary>
        private ServerProfile ResolveLocalPorts(ServerProfile server)
        {
            var socks = PortHelper.FindFreePort(server.LocalSocksPort);
            var http = PortHelper.FindFreePort(server.LocalHttpPort, socks);

            if (socks == server.LocalSocksPort && http == server.LocalHttpPort)
                return server;

            _logger.Warning(
                $"Local port(s) {server.LocalSocksPort}/{server.LocalHttpPort} are in use — " +
                $"using {socks}/{http} instead.");

            var runtime = server.Clone();
            runtime.Id = server.Id;
            runtime.IsShared = server.IsShared;
            runtime.SharedId = server.SharedId;
            runtime.SharedBy = server.SharedBy;
            runtime.LocalSocksPort = socks;
            runtime.LocalHttpPort = http;
            return runtime;
        }

        private void TearDownEngine()
        {
            _statsTimer?.Dispose();
            _statsTimer = null;

            if (_currentEngine != null)
            {
                _currentEngine.StatusChanged -= OnEngineStatusChanged;
                _currentEngine.LogReceived -= OnEngineLogReceived;
                _currentEngine.Dispose();
                _currentEngine = null;
            }

            _activeServer = null;
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
            var engine = _currentEngine;
            if (engine == null || engine.Status != EngineStatus.Running) return;
            if (state is not ServerProfile server) return;

            try
            {
                var latencyTask = engine.TestLatencyAsync(server);
                var trafficTask = engine.QueryTrafficStatsAsync();
                await Task.WhenAll(latencyTask, trafficTask);

                Stats.LatencyMs = latencyTask.Result;
                Stats.BytesSent = trafficTask.Result.uplink;
                Stats.BytesReceived = trafficTask.Result.downlink;
                StatsUpdated?.Invoke(this, Stats);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _statsTimer?.Dispose();
            _currentEngine?.Dispose();
            _currentEngine = null;
            _systemProxy.DisableProxy();
        }
    }
}
