using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SecureGateway.Utils
{
    public static class PortHelper
    {
        public static bool IsPortFree(int port)
        {
            if (port <= 0 || port > 65535) return false;

            try
            {
                var props = IPGlobalProperties.GetIPGlobalProperties();

                foreach (var listener in props.GetActiveTcpListeners())
                    if (listener.Port == port) return false;

                foreach (var listener in props.GetActiveUdpListeners())
                    if (listener.Port == port) return false;

                // Lingering TIME_WAIT sockets from a previous session don't block a new listener.
                foreach (var conn in props.GetActiveTcpConnections())
                    if (conn.LocalEndPoint.Port == port
                        && conn.State != TcpState.TimeWait
                        && conn.State != TcpState.CloseWait)
                        return false;

                return true;
            }
            catch
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Loopback, port);
                    listener.Start();
                    listener.Stop();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Returns <paramref name="preferred"/> if it is free, otherwise the next free port above it.
        /// Ports listed in <paramref name="exclude"/> are skipped even if free.
        /// </summary>
        public static int FindFreePort(int preferred, params int[] exclude)
        {
            var start = preferred is > 0 and <= 65535 ? preferred : 10800;

            for (int port = start; port <= 65535; port++)
            {
                if (Array.IndexOf(exclude, port) >= 0) continue;
                if (IsPortFree(port)) return port;
            }

            throw new InvalidOperationException("No free local port available.");
        }

        public static async Task<bool> IsListeningAsync(int port, int timeoutMs = 500)
        {
            try
            {
                using var tcp = new TcpClient();
                var connect = tcp.ConnectAsync(IPAddress.Loopback, port);
                var finished = await Task.WhenAny(connect, Task.Delay(timeoutMs));
                return finished == connect && connect.IsCompletedSuccessfully && tcp.Connected;
            }
            catch
            {
                return false;
            }
        }
    }
}
