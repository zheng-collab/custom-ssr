using SecureGateway.Models;

namespace SecureGateway.Platform
{
    /// <summary>
    /// Points the operating system's proxy settings at the local v2ray inbounds
    /// (Windows: Internet Settings registry + WinINET; macOS: networksetup).
    /// </summary>
    public interface ISystemProxyService
    {
        void ConfigureForMode(ProxyMode mode, int httpPort, int socksPort);
        void DisableProxy();

        /// <summary>
        /// Undoes a proxy setting SecureGateway left behind (crash, killed process, power cut):
        /// restores the user's own settings if they were saved, otherwise switches off a proxy that
        /// points at a local port nothing listens on. Leaves any working proxy alone.
        /// Returns true when it changed something.
        /// </summary>
        bool RepairLeftover();
    }
}
