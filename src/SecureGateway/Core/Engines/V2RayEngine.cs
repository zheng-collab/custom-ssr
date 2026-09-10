using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SecureGateway.Models;
using SecureGateway.Utils;

namespace SecureGateway.Core.Engines
{
    /// <summary>
    /// Drives a local v2ray-core process. Builds a VMess or Shadowsocks outbound
    /// depending on the profile, so one engine serves every server type.
    /// </summary>
    public class V2RayEngine : IProxyEngine
    {
        private const int DefaultStatsApiPort = 10813;
        private const int StartupTimeoutMs = 8000;
        private const string StatsPrefix = "outbound>>>proxy>>>traffic>>>";

        private readonly string _v2rayPath;
        private readonly string _configDir;
        private Process _process;
        private int _statsApiPort = DefaultStatsApiPort;
        private bool _disposed;

        public EngineStatus Status { get; private set; } = EngineStatus.Stopped;
        public event EventHandler<EngineStatusChangedEventArgs> StatusChanged;
        public event EventHandler<EngineLogEventArgs> LogReceived;

        public V2RayEngine()
        {
            _v2rayPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "v2ray-core", "v2ray.exe");
            _configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SecureGateway", "v2ray");
            Directory.CreateDirectory(_configDir);
        }

        public async Task StartAsync(ServerProfile profile)
        {
            if (Status == EngineStatus.Running)
                await StopAsync();

            SetStatus(EngineStatus.Starting, "Generating V2Ray configuration...");

            if (!File.Exists(_v2rayPath))
            {
                SetStatus(EngineStatus.Error,
                    "v2ray-core not found. Reinstall the app or place v2ray.exe in the v2ray-core folder.");
                return;
            }

            if (profile.Protocol == ProxyProtocol.Shadowsocks && !string.IsNullOrEmpty(profile.SsPlugin))
                Log($"Shadowsocks plugin '{profile.SsPlugin}' is not supported and will be ignored.", "Warning");

            _statsApiPort = PortHelper.FindFreePort(DefaultStatsApiPort, profile.LocalSocksPort, profile.LocalHttpPort);

            var configPath = Path.Combine(_configDir, "config.json");
            await File.WriteAllTextAsync(configPath, GenerateConfig(profile));
            Log($"V2Ray config written to {configPath}");

            try
            {
                _process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _v2rayPath,
                        Arguments = $"run -config \"{configPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = Path.GetDirectoryName(_v2rayPath)
                    },
                    EnableRaisingEvents = true
                };

                _process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data)) Log(e.Data);
                };
                _process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data)) Log(e.Data, "Error");
                };
                _process.Exited += (s, e) =>
                {
                    if (Status == EngineStatus.Running)
                        SetStatus(EngineStatus.Error, "V2Ray process exited unexpectedly.");
                };

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                if (!await WaitForListeningAsync(profile.LocalSocksPort))
                {
                    var reason = _process != null && _process.HasExited
                        ? $"V2Ray exited with code {_process.ExitCode}. See the Log tab for details."
                        : $"V2Ray did not open local port {profile.LocalSocksPort} in time.";
                    await KillProcessAsync();
                    SetStatus(EngineStatus.Error, reason);
                    return;
                }

                SetStatus(EngineStatus.Running,
                    $"Connected to {profile.Address}:{profile.Port} via {DescribeProtocol(profile)}");
                Log($"Local proxy ready. SOCKS5: 127.0.0.1:{profile.LocalSocksPort}, HTTP: 127.0.0.1:{profile.LocalHttpPort}");
            }
            catch (Exception ex)
            {
                await KillProcessAsync();
                SetStatus(EngineStatus.Error, $"Failed to start V2Ray: {ex.Message}");
            }
        }

        public async Task StopAsync()
        {
            SetStatus(EngineStatus.Stopping, "Stopping V2Ray...");
            await KillProcessAsync();
            SetStatus(EngineStatus.Stopped, "V2Ray stopped.");
        }

        public async Task<double> TestLatencyAsync(ServerProfile profile)
        {
            try
            {
                if (Status == EngineStatus.Running)
                {
                    var proxy = new System.Net.WebProxy($"http://127.0.0.1:{profile.LocalHttpPort}");
                    using var handler = new HttpClientHandler { Proxy = proxy };
                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

                    var sw = Stopwatch.StartNew();
                    try
                    {
                        await client.GetAsync("http://cp.cloudflare.com/");
                    }
                    catch
                    {
                        await client.GetAsync("https://www.google.com/generate_204");
                    }
                    sw.Stop();
                    return sw.Elapsed.TotalMilliseconds;
                }

                using var tcp = new System.Net.Sockets.TcpClient();
                var sw2 = Stopwatch.StartNew();
                await tcp.ConnectAsync(profile.Address, profile.Port);
                sw2.Stop();
                return sw2.Elapsed.TotalMilliseconds;
            }
            catch
            {
                return -1;
            }
        }

        public async Task<(long uplink, long downlink)> QueryTrafficStatsAsync()
        {
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _v2rayPath,
                        Arguments = $"api stats --server=127.0.0.1:{_statsApiPort} -json",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = Path.GetDirectoryName(_v2rayPath)
                    }
                };

                proc.Start();
                var outputTask = proc.StandardOutput.ReadToEndAsync();

                if (!proc.WaitForExit(3000))
                {
                    try { proc.Kill(); } catch { }
                    return (0, 0);
                }

                var output = await outputTask;
                if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                    return (0, 0);

                long uplink = 0, downlink = 0;
                if (JObject.Parse(output)["stat"] is JArray stats)
                {
                    foreach (var stat in stats)
                    {
                        var name = stat["name"]?.ToString() ?? "";
                        if (!name.StartsWith(StatsPrefix)) continue;

                        var value = long.TryParse(stat["value"]?.ToString(), out var v) ? v : 0;
                        if (name.EndsWith("uplink")) uplink = value;
                        else if (name.EndsWith("downlink")) downlink = value;
                    }
                }

                return (uplink, downlink);
            }
            catch
            {
                return (0, 0);
            }
        }

        private async Task<bool> WaitForListeningAsync(int port)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < StartupTimeoutMs)
            {
                var proc = _process;
                if (proc == null || proc.HasExited) return false;
                if (await PortHelper.IsListeningAsync(port)) return true;
                await Task.Delay(200);
            }
            return false;
        }

        private async Task KillProcessAsync()
        {
            var proc = _process;
            _process = null;
            if (proc == null) return;

            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    await proc.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                Log($"Error stopping V2Ray: {ex.Message}", "Warning");
            }
            finally
            {
                proc.Dispose();
            }
        }

        private static string DescribeProtocol(ServerProfile profile) =>
            profile.Protocol == ProxyProtocol.Shadowsocks
                ? $"Shadowsocks ({profile.SsEncryption})"
                : $"V2Ray ({profile.V2RayTransport})";

        private string GenerateConfig(ServerProfile profile)
        {
            var config = new JObject
            {
                ["log"] = new JObject { ["loglevel"] = "warning" },
                ["stats"] = new JObject(),
                ["api"] = new JObject
                {
                    ["tag"] = "api",
                    ["services"] = new JArray("StatsService")
                },
                ["policy"] = new JObject
                {
                    ["system"] = new JObject
                    {
                        ["statsOutboundUplink"] = true,
                        ["statsOutboundDownlink"] = true
                    }
                },
                ["inbounds"] = new JArray
                {
                    new JObject
                    {
                        ["tag"] = "socks-in",
                        ["port"] = profile.LocalSocksPort,
                        ["listen"] = "127.0.0.1",
                        ["protocol"] = "socks",
                        ["settings"] = new JObject { ["auth"] = "noauth", ["udp"] = true },
                        ["sniffing"] = new JObject
                        {
                            ["enabled"] = true,
                            ["destOverride"] = new JArray("http", "tls")
                        }
                    },
                    new JObject
                    {
                        ["tag"] = "http-in",
                        ["port"] = profile.LocalHttpPort,
                        ["listen"] = "127.0.0.1",
                        ["protocol"] = "http",
                        ["settings"] = new JObject { ["allowTransparent"] = false }
                    },
                    new JObject
                    {
                        ["tag"] = "api-in",
                        ["port"] = _statsApiPort,
                        ["listen"] = "127.0.0.1",
                        ["protocol"] = "dokodemo-door",
                        ["settings"] = new JObject { ["address"] = "127.0.0.1" }
                    }
                },
                ["outbounds"] = new JArray
                {
                    BuildProxyOutbound(profile),
                    new JObject
                    {
                        ["tag"] = "direct",
                        ["protocol"] = "freedom",
                        ["settings"] = new JObject()
                    },
                    new JObject
                    {
                        ["tag"] = "block",
                        ["protocol"] = "blackhole",
                        ["settings"] = new JObject { ["response"] = new JObject { ["type"] = "http" } }
                    }
                },
                ["routing"] = new JObject
                {
                    // AsIs: never resolve domains locally. Local DNS is unreliable in
                    // restricted networks and would stall every new connection; the
                    // remote server resolves instead.
                    ["domainStrategy"] = "AsIs",
                    ["rules"] = new JArray
                    {
                        new JObject
                        {
                            ["type"] = "field",
                            ["inboundTag"] = new JArray("api-in"),
                            ["outboundTag"] = "api"
                        },
                        new JObject
                        {
                            ["type"] = "field",
                            ["outboundTag"] = "block",
                            ["domain"] = new JArray("geosite:category-ads-all")
                        },
                        new JObject
                        {
                            ["type"] = "field",
                            ["outboundTag"] = "direct",
                            ["domain"] = new JArray("geosite:private")
                        },
                        new JObject
                        {
                            ["type"] = "field",
                            ["outboundTag"] = "direct",
                            ["ip"] = new JArray("geoip:private")
                        }
                    }
                }
            };

            return config.ToString(Formatting.Indented);
        }

        private static JObject BuildProxyOutbound(ServerProfile profile)
        {
            return profile.Protocol == ProxyProtocol.Shadowsocks
                ? BuildShadowsocksOutbound(profile)
                : BuildVmessOutbound(profile);
        }

        private static JObject BuildShadowsocksOutbound(ServerProfile profile)
        {
            var method = profile.SsEncryption switch
            {
                ShadowsocksEncryption.Aes128Gcm => "aes-128-gcm",
                ShadowsocksEncryption.Aes256Gcm => "aes-256-gcm",
                ShadowsocksEncryption.ChaCha20IetfPoly1305 => "chacha20-ietf-poly1305",
                ShadowsocksEncryption.XChaCha20IetfPoly1305 => "xchacha20-ietf-poly1305",
                _ => "aes-256-gcm"
            };

            return new JObject
            {
                ["tag"] = "proxy",
                ["protocol"] = "shadowsocks",
                ["settings"] = new JObject
                {
                    ["servers"] = new JArray
                    {
                        new JObject
                        {
                            ["address"] = profile.Address,
                            ["port"] = profile.Port,
                            ["method"] = method,
                            ["password"] = profile.SsPassword
                        }
                    }
                },
                ["streamSettings"] = new JObject { ["network"] = "tcp" }
            };
        }

        private static JObject BuildVmessOutbound(ServerProfile profile)
        {
            var host = string.IsNullOrEmpty(profile.V2RayHost) ? profile.Address : profile.V2RayHost;

            var streamSettings = new JObject
            {
                ["network"] = profile.V2RayTransport switch
                {
                    V2RayTransport.WebSocket => "ws",
                    V2RayTransport.HTTP2 => "h2",
                    V2RayTransport.GRPC => "grpc",
                    V2RayTransport.QUIC => "quic",
                    _ => "tcp"
                }
            };

            switch (profile.V2RayTransport)
            {
                case V2RayTransport.WebSocket:
                    streamSettings["wsSettings"] = new JObject
                    {
                        ["path"] = profile.V2RayPath,
                        ["headers"] = new JObject { ["Host"] = host }
                    };
                    break;

                case V2RayTransport.HTTP2:
                    streamSettings["httpSettings"] = new JObject
                    {
                        ["path"] = profile.V2RayPath,
                        ["host"] = new JArray(host)
                    };
                    break;

                case V2RayTransport.GRPC:
                    streamSettings["grpcSettings"] = new JObject
                    {
                        ["serviceName"] = profile.V2RayPath.TrimStart('/'),
                        ["multiMode"] = false
                    };
                    break;

                case V2RayTransport.QUIC:
                    streamSettings["quicSettings"] = new JObject
                    {
                        ["security"] = "none",
                        ["key"] = "",
                        ["header"] = new JObject { ["type"] = "none" }
                    };
                    break;

                default:
                    streamSettings["tcpSettings"] = new JObject
                    {
                        ["header"] = new JObject { ["type"] = "none" }
                    };
                    break;
            }

            if (profile.V2RayTls)
            {
                streamSettings["security"] = "tls";
                streamSettings["tlsSettings"] = new JObject
                {
                    ["serverName"] = string.IsNullOrEmpty(profile.V2RaySni) ? host : profile.V2RaySni,
                    ["allowInsecure"] = false
                };
            }

            return new JObject
            {
                ["tag"] = "proxy",
                ["protocol"] = "vmess",
                ["settings"] = new JObject
                {
                    ["vnext"] = new JArray
                    {
                        new JObject
                        {
                            ["address"] = profile.Address,
                            ["port"] = profile.Port,
                            ["users"] = new JArray
                            {
                                new JObject
                                {
                                    ["id"] = profile.V2RayUserId,
                                    ["alterId"] = profile.V2RayAlterId,
                                    ["security"] = profile.V2RaySecurity
                                }
                            }
                        }
                    }
                },
                ["streamSettings"] = streamSettings
            };
        }

        private void SetStatus(EngineStatus status, string message)
        {
            Status = status;
            StatusChanged?.Invoke(this, new EngineStatusChangedEventArgs(status, message));
            Log($"[Status: {status}] {message}");
        }

        private void Log(string message, string level = "Info")
        {
            LogReceived?.Invoke(this, new EngineLogEventArgs(message, level));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            var proc = _process;
            _process = null;
            if (proc == null) return;

            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            proc.Dispose();
        }
    }
}
