using System;

namespace SecureGateway.Models
{
    public class ConnectionStats
    {
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
        public DateTime ConnectedSince { get; set; }
        public int ActiveConnections { get; set; }
        public double LatencyMs { get; set; }

        public string FormattedUpload => FormatBytes(BytesSent);
        public string FormattedDownload => FormatBytes(BytesReceived);

        public string FormattedDuration
        {
            get
            {
                var duration = DateTime.UtcNow - ConnectedSince;
                if (duration.TotalHours >= 1)
                    return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
                if (duration.TotalMinutes >= 1)
                    return $"{duration.Minutes}m {duration.Seconds}s";
                return $"{duration.Seconds}s";
            }
        }

        public string FormattedLatency => LatencyMs > 0 ? $"{LatencyMs:F0} ms" : "N/A";

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:F2} {suffixes[order]}";
        }
    }
}
