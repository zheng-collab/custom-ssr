using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SecureGateway.Core.Config;
using SecureGateway.Core.Engines;
using SecureGateway.Models;
using SecureGateway.Utils;

namespace SecureGateway.Services
{
    /// <summary>
    /// "Connect first, then sign in": brings a tunnel up before authentication so the login
    /// server can be reached from networks that block it. The system proxy is NOT touched;
    /// only the app's own Supabase traffic goes through the tunnel until sign-in succeeds,
    /// after which the running engine is handed over to the main window (Detach) or torn
    /// down (AbandonAsync) if the user gives up.
    /// </summary>
    public sealed class BootstrapConnector
    {
        public IProxyEngine Engine { get; private set; }
        /// <summary>Profile actually running (local ports may differ from Original).</summary>
        public ServerProfile RuntimeProfile { get; private set; }
        /// <summary>Profile as the user knows it (for adding to the list / persisting).</summary>
        public ServerProfile Original { get; private set; }
        public bool IsRunning => Engine?.Status == EngineStatus.Running;

        /// <summary>
        /// Tries the pasted link first (if any), then servers this device already knows
        /// (cached shared servers, then local ones). Returns the connector holding the first
        /// server through which the login server answered, or an error message.
        /// </summary>
        public static async Task<(BootstrapConnector connector, string error)> TryConnectAsync(
            string shareLink, AuthService auth, Action<string> progress = null)
        {
            var candidates = new List<ServerProfile>();

            if (!string.IsNullOrWhiteSpace(shareLink))
            {
                if (!ShareLinkParser.IsSupported(shareLink))
                    return (null, "That is not a supported server link (expected ss:// or vmess://).");
                var parsed = ShareLinkParser.Parse(shareLink);
                if (parsed == null)
                    return (null, "The server link could not be parsed. Copy the whole line from your administrator.");
                candidates.Add(parsed);
            }
            else
            {
                candidates.AddRange(SharedServerService.LoadCache());
                try { candidates.AddRange(new ConfigManager().Config.Servers); } catch { }
                if (candidates.Count == 0)
                    return (null, "No server is saved on this device yet. Paste the server link from your administrator.");
            }

            string lastError = null;
            foreach (var server in candidates)
            {
                progress?.Invoke($"Connecting through {server.Name}...");
                var connector = new BootstrapConnector();
                var (ok, error) = await connector.StartAsync(server, auth);
                if (ok) return (connector, null);
                lastError = $"{server.Name}: {error}";
            }

            return (null, lastError ?? "Could not connect through any saved server.");
        }

        private async Task<(bool ok, string error)> StartAsync(ServerProfile server, AuthService auth)
        {
            var runtime = ConnectionService.WithFreeLocalPorts(server, out _);
            var engine = new V2RayEngine();
            string lastStatus = "";
            engine.StatusChanged += (_, e) => lastStatus = e.Message;

            try
            {
                await engine.StartAsync(runtime);
                if (engine.Status != EngineStatus.Running)
                {
                    engine.Dispose();
                    return (false, string.IsNullOrEmpty(lastStatus) ? "engine did not start" : lastStatus);
                }

                HttpClients.UseProxy(runtime.LocalHttpPort);
                await auth.ReinitializeClientAsync();

                if (!await auth.PingAsync())
                {
                    HttpClients.UseProxy(null);
                    await auth.ReinitializeClientAsync();
                    await engine.StopAsync();
                    engine.Dispose();
                    return (false, "tunnel is up but the login server still did not answer through it");
                }

                Engine = engine;
                RuntimeProfile = runtime;
                Original = server;
                return (true, null);
            }
            catch (Exception ex)
            {
                try { engine.Dispose(); } catch { }
                HttpClients.UseProxy(null);
                return (false, ex.Message);
            }
        }

        /// <summary>Hands the running engine to the caller (main window). This connector is emptied.</summary>
        public (IProxyEngine engine, ServerProfile runtime, ServerProfile original) Detach()
        {
            var result = (Engine, RuntimeProfile, Original);
            Engine = null; RuntimeProfile = null; Original = null;
            return result;
        }

        /// <summary>Stops the tunnel and restores direct HTTP (login abandoned).</summary>
        public async Task AbandonAsync()
        {
            HttpClients.UseProxy(null);
            if (Engine == null) return;
            try { await Engine.StopAsync(); } catch { }
            Engine.Dispose();
            Engine = null; RuntimeProfile = null; Original = null;
        }
    }
}
