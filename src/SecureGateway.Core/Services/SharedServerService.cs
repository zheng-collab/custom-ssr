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
    public class SharedServerService
    {
        private static readonly HttpClient Http = new();

        private readonly string _supabaseUrl;
        private readonly string _supabaseAnonKey;
        private readonly Func<string> _getAccessToken;

        public SharedServerService(string supabaseUrl, string supabaseAnonKey, Func<string> getAccessToken)
        {
            _supabaseUrl = supabaseUrl;
            _supabaseAnonKey = supabaseAnonKey;
            _getAccessToken = getAccessToken;
        }

        public async Task<List<ServerProfile>> FetchSharedServersAsync()
        {
            var servers = new List<ServerProfile>();

            try
            {
                var request = CreateRequest(HttpMethod.Get,
                    $"{_supabaseUrl}/rest/v1/shared_servers?enabled=eq.true&select=*");
                var response = await Http.SendAsync(request);

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
            catch { }

            return servers;
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

                var request = CreateRequest(HttpMethod.Post,
                    $"{_supabaseUrl}/rest/v1/shared_servers");
                request.Headers.Add("Prefer", "return=minimal");
                request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");

                var response = await Http.SendAsync(request);
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
                    $"{_supabaseUrl}/rest/v1/shared_servers?id=eq.{sharedId}");
                var response = await Http.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
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
