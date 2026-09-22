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
    }
}
