using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SecureGateway.Models
{
    public enum ProxyMode
    {
        Global,
        Rule,
        Direct
    }

    public class AppConfig
    {
        public List<ServerProfile> Servers { get; set; } = new();
        public string ActiveServerId { get; set; } = "";

        [JsonConverter(typeof(StringEnumConverter))]
        public ProxyMode ProxyMode { get; set; } = ProxyMode.Rule;

        public bool AutoStart { get; set; } = false;
        public bool AutoConnect { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool EnableLogging { get; set; } = true;
        public string LogLevel { get; set; } = "Info";

        // DNS settings
        public string DnsServer { get; set; } = "8.8.8.8";
        public bool EnableDnsOverHttps { get; set; } = true;

        // Routing rules
        public List<string> DirectDomains { get; set; } = new();
        public List<string> ProxyDomains { get; set; } = new();
        public List<string> BlockedDomains { get; set; } = new();

        // PAC
        public string CustomPacUrl { get; set; } = "";
        public bool UseCustomPac { get; set; } = false;

        // UI
        public double WindowWidth { get; set; } = 900;
        public double WindowHeight { get; set; } = 600;
    }
}
