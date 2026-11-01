using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using SecureGateway.Models;
using SecureGateway.Platform;

namespace SecureGateway.Mac.Platform
{
    /// <summary>
    /// Configures the macOS system proxy for the active network service with
    /// `networksetup`. If that is refused (non-admin user), the same commands are
    /// re-run through an administrator-privileges prompt via osascript.
    /// </summary>
    public class MacSystemProxyService : ISystemProxyService
    {
        private static readonly string[] BypassDomains = { "*.local", "169.254/16", "localhost", "127.0.0.1", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" };
        private List<string> _configuredServices = new();

        public void ConfigureForMode(ProxyMode mode, int httpPort, int socksPort)
        {
            if (mode == ProxyMode.Direct) { DisableProxy(); return; }

            var services = GetTargetServices();
            if (services.Count == 0) return;

            var sb = new StringBuilder();
            foreach (var svc in services)
            {
                var q = Quote(svc);
                sb.AppendLine($"networksetup -setwebproxy {q} 127.0.0.1 {httpPort}");
                sb.AppendLine($"networksetup -setsecurewebproxy {q} 127.0.0.1 {httpPort}");
                sb.AppendLine($"networksetup -setsocksfirewallproxy {q} 127.0.0.1 {socksPort}");
                sb.AppendLine($"networksetup -setproxybypassdomains {q} {string.Join(" ", BypassDomains.Select(Quote))}");
                sb.AppendLine($"networksetup -setwebproxystate {q} on");
                sb.AppendLine($"networksetup -setsecurewebproxystate {q} on");
                sb.AppendLine($"networksetup -setsocksfirewallproxystate {q} on");
            }

            if (RunShell(sb.ToString()))
                _configuredServices = services;
        }

        public void DisableProxy()
        {
            var services = _configuredServices.Count > 0 ? _configuredServices : GetTargetServices();
            if (services.Count == 0) return;

            var sb = new StringBuilder();
            foreach (var svc in services)
            {
                var q = Quote(svc);
                sb.AppendLine($"networksetup -setwebproxystate {q} off");
                sb.AppendLine($"networksetup -setsecurewebproxystate {q} off");
                sb.AppendLine($"networksetup -setsocksfirewallproxystate {q} off");
            }

            if (RunShell(sb.ToString()))
                _configuredServices = new List<string>();
        }

        public bool RepairLeftover()
        {
            if (!OperatingSystem.IsMacOS()) return false;

            // Every service, not just the active one: the leftover may be on Wi-Fi while the Mac
            // is now on Ethernet.
            var services = Exec("/usr/sbin/networksetup", "-listallnetworkservices")
                .Split('\n').Skip(1).Select(l => l.Trim().TrimStart('*').Trim()).Where(l => l.Length > 0).ToList();

            var sb = new StringBuilder();
            foreach (var svc in services)
            {
                foreach (var (get, set) in new[] { ("-getwebproxy", "-setwebproxystate"), ("-getsecurewebproxy", "-setsecurewebproxystate"),
                                                   ("-getsocksfirewallproxy", "-setsocksfirewallproxystate") })
                {
                    var info = ParseProxyInfo(ExecArgs("/usr/sbin/networksetup", get, svc));
                    if (info.enabled && ProxyLeftover.IsStale($"{info.server}:{info.port}", p => SecureGateway.Utils.PortHelper.IsListening(p, 300)))
                        sb.AppendLine($"networksetup {set} {Quote(svc)} off");
                }
            }
            return sb.Length > 0 && RunShell(sb.ToString());
        }

        /// <summary>Parses `networksetup -getwebproxy` output ("Enabled: Yes", "Server: …", "Port: …").</summary>
        public static (bool enabled, string server, string port) ParseProxyInfo(string output)
        {
            string Field(string name) => output.Split('\n').Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))?.Split(':', 2)[1].Trim() ?? "";
            return (Field("Enabled").Equals("Yes", StringComparison.OrdinalIgnoreCase), Field("Server"), Field("Port"));
        }

        private static string ExecArgs(string file, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return output;
            }
            catch { return ""; }
        }

        // ---- service discovery ---------------------------------------------------------------
        private static List<string> GetTargetServices()
        {
            if (!OperatingSystem.IsMacOS()) return new List<string>();

            // Prefer the service that owns the default route (e.g. "Wi-Fi" for en0).
            var defaultIf = Exec("/sbin/route", "-n get default")
                .Split('\n').Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("interface:"))?.Split(':', 2)[1].Trim();

            var order = Exec("/usr/sbin/networksetup", "-listnetworkserviceorder");
            var services = new List<(string name, string device)>();
            string pending = null;
            foreach (var raw in order.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("(") && line.Contains(") ") && !line.StartsWith("(Hardware"))
                    pending = line.Substring(line.IndexOf(") ") + 2).Trim();
                else if (line.StartsWith("(Hardware Port:") && pending != null)
                {
                    var dev = line.Split("Device:", 2).Skip(1).FirstOrDefault()?.Trim().TrimEnd(')') ?? "";
                    services.Add((pending, dev));
                    pending = null;
                }
            }

            if (!string.IsNullOrEmpty(defaultIf))
            {
                var match = services.Where(s => s.device == defaultIf).Select(s => s.name).ToList();
                if (match.Count > 0) return match;
            }

            // Fallback: every enabled service (a leading '*' marks disabled ones).
            return Exec("/usr/sbin/networksetup", "-listallnetworkservices")
                .Split('\n').Skip(1).Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("*")).ToList();
        }

        // ---- process helpers -----------------------------------------------------------------
        private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

        private static string Exec(string file, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return output;
            }
            catch { return ""; }
        }

        /// <summary>Runs the script with /bin/sh; on a permission failure retries with an admin prompt.</summary>
        private static bool RunShell(string script)
        {
            if (!OperatingSystem.IsMacOS()) return false;

            var (code, err) = RunSh(script);
            if (code == 0) return true;

            var needsAdmin = err.Contains("requires admin", StringComparison.OrdinalIgnoreCase)
                          || err.Contains("not permitted", StringComparison.OrdinalIgnoreCase)
                          || err.Contains("permission", StringComparison.OrdinalIgnoreCase);
            if (!needsAdmin) return false;

            // osascript "do shell script ... with administrator privileges" shows the standard
            // macOS password dialog once for the whole batch.
            var escaped = script.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "; ");
            var apple = $"do shell script \"{escaped}\" with administrator privileges";
            var (code2, _) = RunProcess("/usr/bin/osascript", new[] { "-e", apple });
            return code2 == 0;
        }

        private static (int code, string stderr) RunSh(string script) =>
            RunProcess("/bin/sh", new[] { "-c", script });

        private static (int code, string stderr) RunProcess(string file, string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(file)
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
                };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                var err = p.StandardError.ReadToEnd();
                p.WaitForExit(60000);
                return (p.ExitCode, err);
            }
            catch (Exception ex) { return (-1, ex.Message); }
        }
    }
}
