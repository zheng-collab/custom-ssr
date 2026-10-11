using System;
using System.Collections.Generic;
using System.Linq;

namespace SecureGateway.Models
{
    /// <summary>
    /// What the local engine routes through the tunnel versus directly. Built from AppConfig
    /// at connect time; compared to decide whether a routing change needs an engine restart.
    /// </summary>
    public sealed class RoutingOptions : IEquatable<RoutingOptions>
    {
        /// <summary>Mainland-China sites (geosite:cn / geoip:cn) connect directly.</summary>
        public bool ChinaDirect { get; init; }

        /// <summary>User-listed sites that always connect directly (domains, IPs or CIDRs).</summary>
        public IReadOnlyList<string> DirectDomains { get; init; } = Array.Empty<string>();

        public static RoutingOptions From(AppConfig config) => new()
        {
            ChinaDirect = config.ProxyMode == ProxyMode.Rule,
            DirectDomains = NormalizeList(config.DirectDomains)
        };

        /// <summary>Trims, drops blanks/comments and duplicates; keeps the user's order.</summary>
        public static List<string> NormalizeList(IEnumerable<string> entries)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in entries ?? Enumerable.Empty<string>())
            {
                var e = (raw ?? "").Trim();
                if (e.Length == 0 || e.StartsWith('#')) continue;
                if (seen.Add(e)) result.Add(e);
            }
            return result;
        }

        public bool Equals(RoutingOptions other) =>
            other != null && ChinaDirect == other.ChinaDirect &&
            DirectDomains.SequenceEqual(other.DirectDomains, StringComparer.OrdinalIgnoreCase);

        public override bool Equals(object obj) => Equals(obj as RoutingOptions);
        public override int GetHashCode() => HashCode.Combine(ChinaDirect, DirectDomains.Count);
    }
}
