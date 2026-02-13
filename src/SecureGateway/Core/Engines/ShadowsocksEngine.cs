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
    public class ShadowsocksEngine : IProxyEngine
    {
        private Process _ssProcess;
        private Process _httpProxyProcess;
        private readonly string _configDir;
        private bool _disposed;

        public EngineStatus Status { get; private set; } = EngineStatus.Stopped;
        public event EventHandler<EngineStatusChangedEventArgs> StatusChanged;
        public event EventHandler<EngineLogEventArgs> LogReceived;

        public ShadowsocksEngine()
        {
            _configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SecureGateway", "shadowsocks");
            Directory.CreateDirectory(_configDir);
        }

        public async Task StartAsync(ServerProfile profile)
        {
            if (Status == EngineStatus.Running)
                await StopAsync();

            SetStatus(EngineStatus.Starting, "Generating Shadowsocks configuration...");

            var configPath = Path.Combine(_configDir, "config.json");
            var config = GenerateConfig(profile);
            await File.WriteAllTextAsync(configPath, config);

            Log($"Shadowsocks config written to {configPath}");

            // Try to find ss-local in common locations
            var ssLocalPath = FindExecutable("ss-local.exe",
                "shadowsocks-libev", "shadowsocks-rust", "shadowsocks");

            if (ssLocalPath == null)
            {
                // Fall back to using V2Ray with Shadowsocks outbound
                Log("ss-local not found, falling back to V2Ray engine with Shadowsocks protocol...");
                await StartWithV2RayFallback(profile);
                return;
            }

            try
            {
                _ssProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ssLocalPath,
                        Arguments = $"-c \"{configPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    },
                    EnableRaisingEvents = true
                };

                _ssProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log(e.Data);
                };

                _ssProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log(e.Data, "Error");
                };

                _ssProcess.Exited += (s, e) =>
                {
                    if (Status == EngineStatus.Running)
                        SetStatus(EngineStatus.Error, "Shadowsocks process exited unexpectedly.");
                };

                _ssProcess.Start();
                _ssProcess.BeginOutputReadLine();
                _ssProcess.BeginErrorReadLine();

                await Task.Delay(1000);

                if (_ssProcess.HasExited)
                {
                    SetStatus(EngineStatus.Error, $"Shadowsocks failed to start (exit code: {_ssProcess.ExitCode}).");
                    return;
                }

                SetStatus(EngineStatus.Running,
                    $"Connected to {profile.Address}:{profile.Port} via Shadowsocks ({profile.SsEncryption})");
                Log($"Shadowsocks started. SOCKS5: 127.0.0.1:{profile.LocalSocksPort}");
            }
            catch (Exception ex)
            {
                SetStatus(EngineStatus.Error, $"Failed to start Shadowsocks: {ex.Message}");
            }
        }

        private async Task StartWithV2RayFallback(ServerProfile profile)
        {
            var v2rayPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "v2ray-core", "v2ray.exe");

            if (!File.Exists(v2rayPath))
            {
                SetStatus(EngineStatus.Error,
                    "Neither ss-local nor v2ray-core found. Please install one of them.");
                return;
            }

            var configPath = Path.Combine(_configDir, "v2ray-ss-config.json");
            var config = GenerateV2RayFallbackConfig(profile);
            await File.WriteAllTextAsync(configPath, config);

            try
            {
                _ssProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = v2rayPath,
                        Arguments = $"run -config \"{configPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = Path.GetDirectoryName(v2rayPath)
                    },
                    EnableRaisingEvents = true
                };

                _ssProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log(e.Data);
                };

                _ssProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log(e.Data, "Error");
                };

                _ssProcess.Exited += (s, e) =>
                {
                    if (Status == EngineStatus.Running)
                        SetStatus(EngineStatus.Error, "V2Ray/SS process exited unexpectedly.");
                };

                _ssProcess.Start();
                _ssProcess.BeginOutputReadLine();
                _ssProcess.BeginErrorReadLine();

                await Task.Delay(1000);

                if (_ssProcess.HasExited)
                {
                    SetStatus(EngineStatus.Error, "V2Ray/SS failed to start.");
                    return;
                }

                SetStatus(EngineStatus.Running,
                    $"Connected to {profile.Address}:{profile.Port} via Shadowsocks (V2Ray fallback)");
            }
            catch (Exception ex)
            {
                SetStatus(EngineStatus.Error, $"Failed to start V2Ray/SS: {ex.Message}");
            }
        }

        public async Task StopAsync()
        {
            SetStatus(EngineStatus.Stopping, "Stopping Shadowsocks...");

            await KillProcess(_ssProcess);
            _ssProcess = null;

            await KillProcess(_httpProxyProcess);
            _httpProxyProcess = null;

            SetStatus(EngineStatus.Stopped, "Shadowsocks stopped.");
        }

        public async Task<double> TestLatencyAsync(ServerProfile profile)
        {
            try
            {
                if (Status == EngineStatus.Running)
                {
                    var proxy = new System.Net.WebProxy($"http://127.0.0.1:{profile.LocalHttpPort}");
                    var handler = new HttpClientHandler { Proxy = proxy };
                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

                    var sw = Stopwatch.StartNew();
                    await client.GetAsync("https://www.google.com/generate_204");
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

        private string GenerateConfig(ServerProfile profile)
        {
            var encryption = profile.SsEncryption switch
            {
                ShadowsocksEncryption.Aes128Gcm => "aes-128-gcm",
                ShadowsocksEncryption.Aes256Gcm => "aes-256-gcm",
                ShadowsocksEncryption.ChaCha20IetfPoly1305 => "chacha20-ietf-poly1305",
                ShadowsocksEncryption.XChaCha20IetfPoly1305 => "xchacha20-ietf-poly1305",
                _ => "aes-256-gcm"
            };

            var config = new JObject
            {
                ["server"] = profile.Address,
                ["server_port"] = profile.Port,
                ["local_address"] = "127.0.0.1",
                ["local_port"] = profile.LocalSocksPort,
                ["password"] = profile.SsPassword,
                ["method"] = encryption,
                ["timeout"] = 300,
                ["fast_open"] = false,
                ["mode"] = "tcp_and_udp"
            };

            if (!string.IsNullOrEmpty(profile.SsPlugin))
            {
                config["plugin"] = profile.SsPlugin;
                config["plugin_opts"] = profile.SsPluginOptions;
            }

            return config.ToString(Formatting.Indented);
        }

        private string GenerateV2RayFallbackConfig(ServerProfile profile)
        {
            var encryption = profile.SsEncryption switch
            {
                ShadowsocksEncryption.Aes128Gcm => "aes-128-gcm",
                ShadowsocksEncryption.Aes256Gcm => "aes-256-gcm",
                ShadowsocksEncryption.ChaCha20IetfPoly1305 => "chacha20-ietf-poly1305",
                ShadowsocksEncryption.XChaCha20IetfPoly1305 => "xchacha20-ietf-poly1305",
                _ => "aes-256-gcm"
            };

            var config = new JObject
            {
                ["log"] = new JObject { ["loglevel"] = "warning" },
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
                        }
                    },
                    new JObject
                    {
                        ["tag"] = "http-in",
                        ["port"] = profile.LocalHttpPort,
                        ["listen"] = "127.0.0.1",
                        ["protocol"] = "http"
                    }
                },
                ["outbounds"] = new JArray
                {
                    new JObject
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
                                    ["method"] = encryption,
                                    ["password"] = profile.SsPassword
                                }
                            }
                        }
                    },
                    new JObject
                    {
                        ["tag"] = "direct",
                        ["protocol"] = "freedom"
                    }
                }
            };

            return config.ToString(Formatting.Indented);
        }

        private string FindExecutable(string name, params string[] searchDirs)
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;

            // Check app directory first
            var direct = Path.Combine(appDir, name);
            if (File.Exists(direct)) return direct;

            foreach (var dir in searchDirs)
            {
                var path = Path.Combine(appDir, dir, name);
                if (File.Exists(path)) return path;
            }

            // Check PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path)) return path;
            }

            return null;
        }

        private async Task KillProcess(Process process)
        {
            if (process == null || process.HasExited) return;
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                Log($"Error killing process: {ex.Message}", "Warning");
            }
            finally
            {
                process.Dispose();
            }
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

            if (_ssProcess != null && !_ssProcess.HasExited)
            {
                try { _ssProcess.Kill(entireProcessTree: true); } catch { }
                _ssProcess.Dispose();
            }

            if (_httpProxyProcess != null && !_httpProxyProcess.HasExited)
            {
                try { _httpProxyProcess.Kill(entireProcessTree: true); } catch { }
                _httpProxyProcess.Dispose();
            }
        }
    }
}
