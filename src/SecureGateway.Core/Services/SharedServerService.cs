using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SecureGateway.Models;
using SecureGateway.Platform;

namespace SecureGateway.Services
{
    public class SharedServerService
    {
        private static readonly string CacheFile = Path.Combine(AppPaths.DataDir, "shared_servers_cache.json");

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
        /// Fetches all enabled shared servers. Returns null when the request failed (offline,
        /// blocked, not authorised) so callers can fall back to LoadCache(); on success the
        /// result is also written to the cache for the next offline start.
        /// </summary>
        public async Task<List<ServerProfile>> FetchSharedServersAsync()
        {
            try
            {
                var request = CreateRequest(HttpMethod.Get,
                    $"{_supabaseUrl}/rest/v1/shared_servers?enabled=eq.true&select=*");
                using var response = await HttpClients.Shared.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    return null;

                var body = await response.Content.ReadAsStringAsync();
                var servers = new List<ServerProfile>();
                foreach (var row in JArray.Parse(body))
                {
                    var server = MapRowToServerProfile(row);
                    if (server != null)
                        servers.Add(server);
                }

                SaveCache(servers);
                return servers;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> PublishServerAsync(ServerProfile server)
        {
            try
            {
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

                var request = CreateRequest(HttpMethod.Post, $"{_supabaseUrl}/rest/v1/shared_servers");
                request.Headers.Add("Prefer", "return=minimal");
                request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");

                using var response = await HttpClients.Shared.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> UnpublishServerAsync(string sharedId)
        {
            try
            {
                var request = CreateRequest(HttpMethod.Delete,
                    $"{_supabaseUrl}/rest/v1/shared_servers?id=eq.{Uri.EscapeDataString(sharedId)}");
                using var response = await HttpClients.Shared.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // ---- offline cache --------------------------------------------------------------------
        private class CachedShared
        {
            public ServerProfile Profile { get; set; }
            public string SharedId { get; set; } = "";
            public string SharedBy { get; set; } = "";
        }

        private static void SaveCache(List<ServerProfile> servers)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                var items = new List<CachedShared>();
                foreach (var s in servers)
                    items.Add(new CachedShared { Profile = s, SharedId = s.SharedId, SharedBy = s.SharedBy });
                File.WriteAllText(CacheFile, JsonConvert.SerializeObject(items));
            }
            catch { }
        }

        /// <summary>Shared servers from the last successful sync on this device (empty if none).</summary>
        public static List<ServerProfile> LoadCache()
        {
            var result = new List<ServerProfile>();
            try
            {
                if (!File.Exists(CacheFile)) return result;
                var items = JsonConvert.DeserializeObject<List<CachedShared>>(File.ReadAllText(CacheFile));
                if (items == null) return result;
                foreach (var c in items)
                {
                    if (c?.Profile == null) continue;
                    c.Profile.IsShared = true;
                    c.Profile.SharedId = c.SharedId ?? "";
                    c.Profile.SharedBy = c.SharedBy ?? "";
                    result.Add(c.Profile);
                }
            }
            catch { }
            return result;
        }

        public static void ClearCache()
        {
            try { if (File.Exists(CacheFile)) File.Delete(CacheFile); } catch { }
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Add("apikey", _supabaseAnonKey);

            var accessToken = _getAccessToken();
            if (!string.IsNullOrEmpty(accessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            return request;
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
