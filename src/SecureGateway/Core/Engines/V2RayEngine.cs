using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SecureGateway.Models;

namespace SecureGateway.Core.Engines
{
    public class V2RayEngine : IProxyEngine
    {
        private const int StatsApiPort = 10813;

        private Process _process;
        private readonly string _v2rayPath;
        private readonly string _configDir;
        private bool _disposed;

        public EngineStatus Status { get; private set; } = EngineStatus.Stopped;
        public event EventHandler<EngineStatusChangedEventArgs> StatusChanged;
        public event EventHandler<EngineLogEventArgs> LogReceived;

        public V2RayEngine()
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            _v2rayPath = Path.Combine(appDir, "v2ray-core", "v2ray.exe");
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

            var configPath = Path.Combine(_configDir, "config.json");
            var config = GenerateConfig(profile);
            await File.WriteAllTextAsync(configPath, config);

            Log($"V2Ray config written to {configPath}");

            if (!File.Exists(_v2rayPath))
            {
                SetStatus(EngineStatus.Error,
                    "v2ray-core not found. Please download v2ray-core and place it in the v2ray-core directory.");
                return;
            }

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
                    if (!string.IsNullOrEmpty(e.Data))
                        Log(e.Data);
                };

                _process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log(e.Data, "Error");
                };

                _process.Exited += (s, e) =>
                {
                    if (Status == EngineStatus.Running)
                        SetStatus(EngineStatus.Error, "V2Ray process exited unexpectedly.");
                };

                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                await Task.Delay(1500);

                if (_process.HasExited)
                {
                    SetStatus(EngineStatus.Error, $"V2Ray failed to start (exit code: {_process.ExitCode}).");
                    return;
                }

                SetStatus(EngineStatus.Running,
                    $"Connected to {profile.Address}:{profile.Port} via V2Ray ({profile.V2RayTransport})");
                Log($"V2Ray started. SOCKS5: 127.0.0.1:{profile.LocalSocksPort}, HTTP: 127.0.0.1:{profile.LocalHttpPort}");
            }
            catch (Exception ex)
            {
                SetStatus(EngineStatus.Error, $"Failed to start V2Ray: {ex.Message}");
            }
        }

        public async Task StopAsync()
        {
            SetStatus(EngineStatus.Stopping, "Stopping V2Ray...");

            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }
                catch (Exception ex)
                {
                    Log($"Error stopping V2Ray: {ex.Message}", "Warning");
                }
                finally
                {
                    _process.Dispose();
                    _process = null;
                }
            }

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
                    using var proxyClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

                    var sw = Stopwatch.StartNew();
                    // Use cp.cloudflare.com (works globally) with google as fallback
                    try
                    {
                        await proxyClient.GetAsync("http://cp.cloudflare.com/");
                    }
                    catch
                    {
                        await proxyClient.GetAsync("https://www.google.com/generate_204");
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
                        Arguments = $"api stats --server=127.0.0.1:{StatsApiPort} -json",
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

                var json = JObject.Parse(output);
                long uplink = 0, downlink = 0;

                var stats = json["stat"] as JArray;
                if (stats != null)
                {
                    foreach (var stat in stats)
                    {
                        var name = stat["name"]?.ToString() ?? "";
                        var val = long.TryParse(stat["value"]?.ToString(), out var v) ? v : 0;
                        if (name.Contains("uplink"))
                            uplink = val;
                        else if (name.Contains("downlink"))
                            downlink = val;
                    }
                }

                return (uplink, downlink);
            }
            catch
            {
                return (0, 0);
            }
        }

        private string GenerateConfig(ServerProfile profile)
        {
            var config = new JObject
            {
                ["log"] = new JObject
                {
                    ["loglevel"] = "warning"
                },
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
                        ["settings"] = new JObject
                        {
                            ["auth"] = "noauth",
                            ["udp"] = true
                        },
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
                        ["settings"] = new JObject
                        {
                            ["allowTransparent"] = false
                        }
                    },
                    new JObject
                    {
                        ["tag"] = "api-in",
                        ["port"] = StatsApiPort,
                        ["listen"] = "127.0.0.1",
                        ["protocol"] = "dokodemo-door",
                        ["settings"] = new JObject
                        {
                            ["address"] = "127.0.0.1"
                        }
                    }
                },
                ["outbounds"] = new JArray
                {
                    BuildV2RayOutbound(profile),
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
                        ["settings"] = new JObject
                        {
                            ["response"] = new JObject { ["type"] = "http" }
                        }
                    }
                },
                ["dns"] = new JObject
                {
                    ["servers"] = new JArray(
                        new JObject
                        {
                            ["address"] = "https+local://dns.google/dns-query",
                            ["domains"] = new JArray("geosite:geolocation-!cn")
                        },
                        "8.8.8.8",
                        "1.1.1.1",
                        "localhost"
                    )
                },
                ["routing"] = new JObject
                {
                    ["domainStrategy"] = "IPIfNonMatch",
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

        private JObject BuildV2RayOutbound(ServerProfile profile)
        {
            var vnext = new JObject
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
            };

            var streamSettings = new JObject
            {
                ["network"] = profile.V2RayTransport.ToString().ToLower()
            };

            switch (profile.V2RayTransport)
            {
                case V2RayTransport.WebSocket:
                    streamSettings["wsSettings"] = new JObject
                    {
                        ["path"] = profile.V2RayPath,
                        ["headers"] = new JObject
                        {
                            ["Host"] = string.IsNullOrEmpty(profile.V2RayHost)
                                ? profile.Address
                                : profile.V2RayHost
                        }
                    };
                    break;

                case V2RayTransport.HTTP2:
                    streamSettings["httpSettings"] = new JObject
                    {
                        ["path"] = profile.V2RayPath,
                        ["host"] = new JArray(
                            string.IsNullOrEmpty(profile.V2RayHost)
                                ? profile.Address
                                : profile.V2RayHost)
                    };
                    break;

                case V2RayTransport.GRPC:
                    streamSettings["grpcSettings"] = new JObject
                    {
                        ["serviceName"] = profile.V2RayPath.TrimStart('/'),
                        ["multiMode"] = false
                    };
                    break;

                case V2RayTransport.TCP:
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
                    ["serverName"] = string.IsNullOrEmpty(profile.V2RaySni)
                        ? profile.Address
                        : profile.V2RaySni,
                    ["allowInsecure"] = false
                };
            }

            return new JObject
            {
                ["tag"] = "proxy",
                ["protocol"] = "vmess",
                ["settings"] = new JObject
                {
                    ["vnext"] = new JArray { vnext }
                },
                ["streamSettings"] = streamSettings,
                ["mux"] = new JObject
                {
                    ["enabled"] = true,
                    ["concurrency"] = 8
                }
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

            if (_process != null && !_process.HasExited)
            {
                try { _process.Kill(entireProcessTree: true); } catch { }
                _process.Dispose();
            }
        }
    }
}
