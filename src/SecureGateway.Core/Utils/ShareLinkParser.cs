using System;
using System.Text;
using Newtonsoft.Json.Linq;
using SecureGateway.Models;

namespace SecureGateway.Utils
{
    /// <summary>Parses vmess:// and ss:// share links into server profiles.</summary>
    public static class ShareLinkParser
    {
        public static bool IsSupported(string link)
        {
            var l = (link ?? "").Trim();
            return l.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase)
                || l.StartsWith("ss://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Returns null if the link is not a supported/valid share link.</summary>
        public static ServerProfile Parse(string link)
        {
            var l = (link ?? "").Trim();
            if (l.StartsWith("ss://", StringComparison.OrdinalIgnoreCase)) return ParseShadowsocks(l);
            if (l.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase)) return ParseVmess(l);
            return null;
        }

        private static ServerProfile ParseShadowsocks(string link)
        {
            // SIP002: ss://base64(method:password)@host:port#name
            var uri = link.Substring(5);
            var name = "Imported SS Server";

            var nameIndex = uri.IndexOf('#');
            if (nameIndex > 0)
            {
                name = Uri.UnescapeDataString(uri.Substring(nameIndex + 1));
                uri = uri.Substring(0, nameIndex);
            }

            var atIndex = uri.LastIndexOf('@');
            if (atIndex <= 0) return null;

            var userInfo = uri.Substring(0, atIndex);
            var hostPort = uri.Substring(atIndex + 1);

            try { userInfo = Encoding.UTF8.GetString(DecodeBase64(userInfo)); }
            catch { /* already plain "method:password" (legacy) */ }

            var colonIndex = userInfo.IndexOf(':');
            if (colonIndex <= 0) return null;

            var method = userInfo.Substring(0, colonIndex);
            var password = userInfo.Substring(colonIndex + 1);

            var lastColon = hostPort.LastIndexOf(':');
            if (lastColon <= 0) return null;
            var host = hostPort.Substring(0, lastColon).Trim('[', ']');
            if (!int.TryParse(hostPort.Substring(lastColon + 1), out var port)) return null;

            var encryption = method.ToLowerInvariant() switch
            {
                "aes-128-gcm" => ShadowsocksEncryption.Aes128Gcm,
                "aes-256-gcm" => ShadowsocksEncryption.Aes256Gcm,
                "chacha20-ietf-poly1305" => ShadowsocksEncryption.ChaCha20IetfPoly1305,
                "xchacha20-ietf-poly1305" => ShadowsocksEncryption.XChaCha20IetfPoly1305,
                _ => ShadowsocksEncryption.Aes256Gcm
            };

            return new ServerProfile
            {
                Name = name,
                Address = host,
                Port = port,
                Protocol = ProxyProtocol.Shadowsocks,
                SsPassword = password,
                SsEncryption = encryption
            };
        }

        private static ServerProfile ParseVmess(string link)
        {
            // vmess://base64(json)
            try
            {
                var json = Encoding.UTF8.GetString(DecodeBase64(link.Substring(8)));
                var obj = JObject.Parse(json);

                var transport = (obj["net"]?.ToString() ?? "tcp").ToLowerInvariant() switch
                {
                    "ws" => V2RayTransport.WebSocket,
                    "h2" => V2RayTransport.HTTP2,
                    "grpc" => V2RayTransport.GRPC,
                    "quic" => V2RayTransport.QUIC,
                    _ => V2RayTransport.TCP
                };

                return new ServerProfile
                {
                    Name = obj["ps"]?.ToString() ?? "Imported V2Ray Server",
                    Address = obj["add"]?.ToString() ?? "",
                    Port = int.TryParse(obj["port"]?.ToString(), out var p) ? p : 443,
                    Protocol = ProxyProtocol.V2Ray,
                    V2RayUserId = obj["id"]?.ToString() ?? "",
                    V2RayAlterId = int.TryParse(obj["aid"]?.ToString(), out var a) ? a : 0,
                    V2RaySecurity = obj["scy"]?.ToString() ?? "auto",
                    V2RayTransport = transport,
                    V2RayPath = obj["path"]?.ToString() ?? "/",
                    V2RayHost = obj["host"]?.ToString() ?? "",
                    V2RayTls = obj["tls"]?.ToString() == "tls",
                    V2RaySni = obj["sni"]?.ToString() ?? ""
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Base64 with tolerance for url-safe alphabet and missing padding.</summary>
        private static byte[] DecodeBase64(string s)
        {
            s = s.Trim().Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }
    }
}
