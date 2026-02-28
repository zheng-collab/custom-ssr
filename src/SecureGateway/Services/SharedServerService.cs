using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SecureGateway.Models;

namespace SecureGateway.Services
{
    /// <summary>
    /// Syncs shared server profiles from the Supabase "shared_servers" table
    /// so that multiple users can access the same server configurations.
    ///
    /// Supabase table schema (shared_servers):
    ///   id           uuid   PRIMARY KEY DEFAULT gen_random_uuid()
    ///   name         text   NOT NULL
    ///   address      text   NOT NULL
    ///   port         int    NOT NULL DEFAULT 443
    ///   protocol     text   NOT NULL DEFAULT 'V2Ray'
    ///   v2ray_user_id       text
    ///   v2ray_alter_id      int    DEFAULT 0
    ///   v2ray_security      text   DEFAULT 'auto'
    ///   v2ray_transport     text   DEFAULT 'WebSocket'
    ///   v2ray_path          text   DEFAULT '/ws'
    ///   v2ray_host          text
    ///   v2ray_tls           bool   DEFAULT true
    ///   v2ray_sni           text
    ///   ss_password         text
    ///   ss_encryption       text   DEFAULT 'Aes256Gcm'
    ///   ss_plugin           text
    ///   ss_plugin_options   text
    ///   remarks      text
    ///   created_by   text
    ///   created_at   timestamptz DEFAULT now()
    ///   enabled      bool   DEFAULT true
    ///
    /// RLS policy: SELECT allowed for authenticated users with gateway.access.
    /// </summary>
    public class SharedServerService
    {
        private readonly string _supabaseUrl;
        private readonly string _supabaseAnonKey;
        private readonly Func<string> _getAccessToken;

        public SharedServerService(string supabaseUrl, string supabaseAnonKey, Func<string> getAccessToken)
        {
            _supabaseUrl = supabaseUrl;
            _supabaseAnonKey = supabaseAnonKey;
            _getAccessToken = getAccessToken;
        }

        /// <summary>
        /// Fetches all enabled shared servers from Supabase.
        /// </summary>
        public async Task<List<ServerProfile>> FetchSharedServersAsync()
        {
            var servers = new List<ServerProfile>();

            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("apikey", _supabaseAnonKey);

                var accessToken = _getAccessToken();
                if (!string.IsNullOrEmpty(accessToken))
                    http.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", accessToken);

                var url = $"{_supabaseUrl}/rest/v1/shared_servers?enabled=eq.true&select=*";
                var response = await http.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return servers;

                var body = await response.Content.ReadAsStringAsync();
                var rows = JArray.Parse(body);

                foreach (var row in rows)
                {
                    var server = MapRowToServerProfile(row);
                    if (server != null)
                        servers.Add(server);
                }
            }
            catch
            {
                // Network or parse failure — return empty list, local servers still work
            }

            return servers;
        }

        /// <summary>
        /// Publishes a local server profile to the shared_servers table (admin action).
        /// </summary>
        public async Task<bool> PublishServerAsync(ServerProfile server)
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("apikey", _supabaseAnonKey);

                var accessToken = _getAccessToken();
                if (!string.IsNullOrEmpty(accessToken))
                    http.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", accessToken);

                var payload = new JObject
                {
                    ["name"] = server.Name,
                    ["address"] = server.Address,
                    ["port"] = server.Port,
                    ["protocol"] = server.Protocol.ToString(),
                    ["v2ray_user_id"] = server.V2RayUserId,
                    ["v2ray_alter_id"] = server.V2RayAlterId,
                    ["v2ray_security"] = server.V2RaySecurity,
                    ["v2ray_transport"] = server.V2RayTransport.ToString(),
                    ["v2ray_path"] = server.V2RayPath,
                    ["v2ray_host"] = server.V2RayHost,
                    ["v2ray_tls"] = server.V2RayTls,
                    ["v2ray_sni"] = server.V2RaySni,
                    ["ss_password"] = server.SsPassword,
                    ["ss_encryption"] = server.SsEncryption.ToString(),
                    ["ss_plugin"] = server.SsPlugin,
                    ["ss_plugin_options"] = server.SsPluginOptions,
                    ["remarks"] = server.Remarks,
                    ["enabled"] = true
                };

                var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                http.DefaultRequestHeaders.Add("Prefer", "return=minimal");

                var url = $"{_supabaseUrl}/rest/v1/shared_servers";
                var response = await http.PostAsync(url, content);

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Removes a shared server from the table (admin action).
        /// </summary>
        public async Task<bool> UnpublishServerAsync(string sharedId)
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("apikey", _supabaseAnonKey);

                var accessToken = _getAccessToken();
                if (!string.IsNullOrEmpty(accessToken))
                    http.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", accessToken);

                var url = $"{_supabaseUrl}/rest/v1/shared_servers?id=eq.{sharedId}";
                var response = await http.DeleteAsync(url);

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static ServerProfile MapRowToServerProfile(JToken row)
        {
            try
            {
                var protocol = Enum.TryParse<ProxyProtocol>(row["protocol"]?.ToString(), out var p)
                    ? p : ProxyProtocol.V2Ray;

                var transport = Enum.TryParse<V2RayTransport>(row["v2ray_transport"]?.ToString(), out var t)
                    ? t : V2RayTransport.WebSocket;

                var encryption = Enum.TryParse<ShadowsocksEncryption>(row["ss_encryption"]?.ToString(), out var e)
                    ? e : ShadowsocksEncryption.Aes256Gcm;

                var sharedId = row["id"]?.ToString() ?? "";

                return new ServerProfile
                {
                    // Use a deterministic local ID derived from the shared ID so we can
                    // detect duplicates across syncs without re-adding them.
                    Id = $"shared-{sharedId}",
                    Name = row["name"]?.ToString() ?? "Shared Server",
                    Address = row["address"]?.ToString() ?? "",
                    Port = row["port"]?.ToObject<int>() ?? 443,
                    Protocol = protocol,

                    V2RayUserId = row["v2ray_user_id"]?.ToString() ?? "",
                    V2RayAlterId = row["v2ray_alter_id"]?.ToObject<int>() ?? 0,
                    V2RaySecurity = row["v2ray_security"]?.ToString() ?? "auto",
                    V2RayTransport = transport,
                    V2RayPath = row["v2ray_path"]?.ToString() ?? "/ws",
                    V2RayHost = row["v2ray_host"]?.ToString() ?? "",
                    V2RayTls = row["v2ray_tls"]?.ToObject<bool>() ?? true,
                    V2RaySni = row["v2ray_sni"]?.ToString() ?? "",

                    SsPassword = row["ss_password"]?.ToString() ?? "",
                    SsEncryption = encryption,
                    SsPlugin = row["ss_plugin"]?.ToString() ?? "",
                    SsPluginOptions = row["ss_plugin_options"]?.ToString() ?? "",

                    Remarks = row["remarks"]?.ToString() ?? "",

                    // Mark as shared
                    IsShared = true,
                    SharedId = sharedId,
                    SharedBy = row["created_by"]?.ToString() ?? ""
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
