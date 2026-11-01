using System;
using System.Text.RegularExpressions;

namespace SecureGateway.Platform
{
    /// <summary>
    /// Decides whether a system proxy setting is one SecureGateway left behind: it points at this
    /// computer (127.0.0.1 / localhost) and nothing is listening on that port any more. A local
    /// proxy that is still answering belongs to something that is running (another tool, or our
    /// own live session) and is left alone.
    /// </summary>
    public static class ProxyLeftover
    {
        private static readonly Regex Local = new(@"^(?:http=)?(?:127\.0\.0\.1|localhost):(\d{1,5})(?:;.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The local port the proxy setting points at, or null if it does not point at this computer.</summary>
        public static int? LocalPort(string proxyServer)
        {
            var m = Local.Match((proxyServer ?? "").Trim());
            return m.Success && int.TryParse(m.Groups[1].Value, out var p) && p is > 0 and < 65536 ? p : null;
        }

        public static bool IsStale(string proxyServer, Func<int, bool> isListening)
        {
            var port = LocalPort(proxyServer);
            return port.HasValue && !isListening(port.Value);
        }
    }
}
