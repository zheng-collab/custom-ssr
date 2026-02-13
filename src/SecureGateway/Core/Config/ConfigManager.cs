using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SecureGateway.Models;

namespace SecureGateway.Core.Config
{
    public class ConfigManager
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SecureGateway");

        private static readonly string ConfigFile = Path.Combine(ConfigDir, "settings.json");

        private AppConfig _config;
        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public AppConfig Config
        {
            get
            {
                if (_config == null)
                    Load();
                return _config;
            }
        }

        public void Load()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);

                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    _config = JsonConvert.DeserializeObject<AppConfig>(json, JsonSettings) ?? new AppConfig();
                }
                else
                {
                    _config = CreateDefaultConfig();
                    Save();
                }
            }
            catch (Exception)
            {
                _config = CreateDefaultConfig();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var json = JsonConvert.SerializeObject(_config, JsonSettings);
                File.WriteAllText(ConfigFile, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save configuration: {ex.Message}", ex);
            }
        }

        public async Task SaveAsync()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var json = JsonConvert.SerializeObject(_config, JsonSettings);
                await File.WriteAllTextAsync(ConfigFile, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save configuration: {ex.Message}", ex);
            }
        }

        public void AddServer(ServerProfile server)
        {
            Config.Servers.Add(server);
            if (string.IsNullOrEmpty(Config.ActiveServerId))
                Config.ActiveServerId = server.Id;
            Save();
        }

        public void RemoveServer(string serverId)
        {
            Config.Servers.RemoveAll(s => s.Id == serverId);
            if (Config.ActiveServerId == serverId)
                Config.ActiveServerId = Config.Servers.Count > 0 ? Config.Servers[0].Id : "";
            Save();
        }

        public void UpdateServer(ServerProfile server)
        {
            var index = Config.Servers.FindIndex(s => s.Id == server.Id);
            if (index >= 0)
            {
                Config.Servers[index] = server;
                Save();
            }
        }

        public ServerProfile GetActiveServer()
        {
            return Config.Servers.Find(s => s.Id == Config.ActiveServerId);
        }

        public void SetActiveServer(string serverId)
        {
            Config.ActiveServerId = serverId;
            Save();
        }

        private static AppConfig CreateDefaultConfig()
        {
            return new AppConfig
            {
                Servers = new(),
                ProxyMode = ProxyMode.Rule,
                AutoStart = false,
                AutoConnect = false,
                MinimizeToTray = true,
                EnableLogging = true,
                DnsServer = "8.8.8.8",
                EnableDnsOverHttps = true,
                DirectDomains = new()
                {
                    "localhost",
                    "*.local",
                    "*.lan"
                },
                BlockedDomains = new()
                {
                    "*.ads.example.com"
                }
            };
        }

        public static string GetLogDirectory()
        {
            var logDir = Path.Combine(ConfigDir, "logs");
            Directory.CreateDirectory(logDir);
            return logDir;
        }

        public static string GetConfigDirectory() => ConfigDir;
    }
}
