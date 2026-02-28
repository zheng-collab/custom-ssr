using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SecureGateway.Models
{
    public enum ProxyProtocol
    {
        V2Ray,
        Shadowsocks
    }

    public enum V2RayTransport
    {
        TCP,
        WebSocket,
        HTTP2,
        GRPC,
        QUIC
    }

    public enum ShadowsocksEncryption
    {
        Aes128Gcm,
        Aes256Gcm,
        ChaCha20IetfPoly1305,
        XChaCha20IetfPoly1305
    }

    public class ServerProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "My Server";
        public string Address { get; set; } = "";
        public int Port { get; set; } = 443;

        [JsonConverter(typeof(StringEnumConverter))]
        public ProxyProtocol Protocol { get; set; } = ProxyProtocol.V2Ray;

        // V2Ray settings
        public string V2RayUserId { get; set; } = Guid.NewGuid().ToString();
        public int V2RayAlterId { get; set; } = 0;
        public string V2RaySecurity { get; set; } = "auto";

        [JsonConverter(typeof(StringEnumConverter))]
        public V2RayTransport V2RayTransport { get; set; } = V2RayTransport.WebSocket;

        public string V2RayPath { get; set; } = "/ws";
        public string V2RayHost { get; set; } = "";
        public bool V2RayTls { get; set; } = true;
        public string V2RaySni { get; set; } = "";

        // Shadowsocks settings
        public string SsPassword { get; set; } = "";

        [JsonConverter(typeof(StringEnumConverter))]
        public ShadowsocksEncryption SsEncryption { get; set; } = ShadowsocksEncryption.Aes256Gcm;

        public string SsPlugin { get; set; } = "";
        public string SsPluginOptions { get; set; } = "";

        // Connection settings
        public int LocalSocksPort { get; set; } = 10808;
        public int LocalHttpPort { get; set; } = 10809;
        public string Remarks { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Shared server support — servers synced from Supabase for multi-user access
        [JsonIgnore]
        public bool IsShared { get; set; } = false;
        [JsonIgnore]
        public string SharedId { get; set; } = "";
        [JsonIgnore]
        public string SharedBy { get; set; } = "";

        public ServerProfile Clone()
        {
            var json = JsonConvert.SerializeObject(this);
            var clone = JsonConvert.DeserializeObject<ServerProfile>(json)!;
            clone.Id = Guid.NewGuid().ToString();
            return clone;
        }

        public override string ToString() => $"{Name} ({Address}:{Port})";
    }
}
