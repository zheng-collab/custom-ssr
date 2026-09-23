using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace SecureGateway.Services
{
    /// <summary>
    /// The HttpClient used for every Supabase call (auth, permissions, shared servers).
    /// On restricted networks (e.g. mainland China) Supabase itself is blocked, so once a
    /// tunnel is up all of this traffic is routed through the local v2ray HTTP inbound.
    /// </summary>
    public static class HttpClients
    {
        private static readonly IWebProxy SystemDefaultProxy = HttpClient.DefaultProxy;
        private static HttpClient _shared = Create(null);

        public static HttpClient Shared => _shared;

        /// <summary>Local HTTP proxy port currently in use, or null for the system default.</summary>
        public static int? ProxyPort { get; private set; }

        /// <summary>Raised after the proxy changed; consumers holding their own clients should rebuild them.</summary>
        public static event Action ProxyChanged;

        public static void UseProxy(int? httpPort)
        {
            if (ProxyPort == httpPort) return;
            ProxyPort = httpPort;

            IWebProxy proxy = httpPort.HasValue ? new WebProxy($"http://127.0.0.1:{httpPort.Value}") : null;

            // Third-party clients (the Supabase SDK) resolve HttpClient.DefaultProxy when their
            // handler first sends; they are re-created after this call so they pick it up.
            HttpClient.DefaultProxy = proxy ?? SystemDefaultProxy;

            var old = _shared;
            _shared = Create(proxy);
            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => old.Dispose());

            ProxyChanged?.Invoke();
        }

        private static HttpClient Create(IWebProxy proxy)
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectTimeout = TimeSpan.FromSeconds(12)
            };
            if (proxy != null)
            {
                handler.Proxy = proxy;
                handler.UseProxy = true;
            }
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        }
    }
}
