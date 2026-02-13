using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SecureGateway.Models;

namespace SecureGateway.Core.Routing
{
    public class RoutingManager
    {
        private readonly string _pacDir;
        private int _pacPort = 10810;

        public RoutingManager()
        {
            _pacDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SecureGateway", "pac");
            Directory.CreateDirectory(_pacDir);
        }

        public string GeneratePacFile(AppConfig config, int socksPort)
        {
            var pacPath = Path.Combine(_pacDir, "proxy.pac");
            var pacContent = BuildPacScript(config, socksPort);
            File.WriteAllText(pacPath, pacContent);
            return pacPath;
        }

        public string GetPacUrl()
        {
            return $"http://127.0.0.1:{_pacPort}/proxy.pac";
        }

        private string BuildPacScript(AppConfig config, int socksPort)
        {
            var sb = new StringBuilder();
            sb.AppendLine("function FindProxyForURL(url, host) {");
            sb.AppendLine("    var PROXY = \"SOCKS5 127.0.0.1:" + socksPort + "; SOCKS 127.0.0.1:" + socksPort + "; DIRECT\";");
            sb.AppendLine("    var DIRECT = \"DIRECT\";");
            sb.AppendLine();

            // Always direct for local addresses
            sb.AppendLine("    // Local network - direct");
            sb.AppendLine("    if (isPlainHostName(host) ||");
            sb.AppendLine("        shExpMatch(host, \"*.local\") ||");
            sb.AppendLine("        isInNet(dnsResolve(host), \"10.0.0.0\", \"255.0.0.0\") ||");
            sb.AppendLine("        isInNet(dnsResolve(host), \"172.16.0.0\", \"255.240.0.0\") ||");
            sb.AppendLine("        isInNet(dnsResolve(host), \"192.168.0.0\", \"255.255.0.0\") ||");
            sb.AppendLine("        isInNet(dnsResolve(host), \"127.0.0.0\", \"255.255.255.0\")) {");
            sb.AppendLine("        return DIRECT;");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Blocked domains
            if (config.BlockedDomains.Count > 0)
            {
                sb.AppendLine("    // Blocked domains");
                foreach (var domain in config.BlockedDomains)
                {
                    var pattern = domain.Replace(".", "\\.").Replace("*", ".*");
                    sb.AppendLine($"    if (shExpMatch(host, \"{domain}\")) return \"PROXY 127.0.0.1:1\";");
                }
                sb.AppendLine();
            }

            // Direct domains
            if (config.DirectDomains.Count > 0)
            {
                sb.AppendLine("    // Direct domains");
                foreach (var domain in config.DirectDomains)
                {
                    sb.AppendLine($"    if (shExpMatch(host, \"{domain}\")) return DIRECT;");
                }
                sb.AppendLine();
            }

            // Proxy domains
            if (config.ProxyDomains.Count > 0)
            {
                sb.AppendLine("    // Force proxy domains");
                foreach (var domain in config.ProxyDomains)
                {
                    sb.AppendLine($"    if (shExpMatch(host, \"{domain}\")) return PROXY;");
                }
                sb.AppendLine();
            }

            // Default: proxy everything else
            sb.AppendLine("    // Default: proxy all other traffic");
            sb.AppendLine("    return PROXY;");
            sb.AppendLine("}");

            return sb.ToString();
        }

        public List<RoutingRule> GetDefaultRules()
        {
            return new List<RoutingRule>
            {
                new() { Pattern = "*.local", Action = RoutingAction.Direct, Description = "Local network" },
                new() { Pattern = "localhost", Action = RoutingAction.Direct, Description = "Localhost" },
                new() { Pattern = "*.lan", Action = RoutingAction.Direct, Description = "LAN domains" },
                new() { Pattern = "10.*", Action = RoutingAction.Direct, Description = "Private network (10.x)" },
                new() { Pattern = "192.168.*", Action = RoutingAction.Direct, Description = "Private network (192.168.x)" },
                new() { Pattern = "172.16.*", Action = RoutingAction.Direct, Description = "Private network (172.16.x)" }
            };
        }
    }

    public enum RoutingAction
    {
        Proxy,
        Direct,
        Block
    }

    public class RoutingRule
    {
        public string Pattern { get; set; } = "";
        public RoutingAction Action { get; set; } = RoutingAction.Proxy;
        public string Description { get; set; } = "";
        public bool Enabled { get; set; } = true;
    }
}
