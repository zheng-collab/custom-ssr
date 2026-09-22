using System;
using System.IO;

namespace SecureGateway.Platform
{
    /// <summary>Per-platform locations for user data and the bundled v2ray-core binary.</summary>
    public static class AppPaths
    {
        public const string AppName = "SecureGateway";

        /// <summary>
        /// Windows: %APPDATA%\SecureGateway · macOS: ~/Library/Application Support/SecureGateway ·
        /// Linux: ~/.config/SecureGateway
        /// </summary>
        public static string DataDir { get; } = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           "Library", "Application Support", AppName)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

        public static string LogDir => Path.Combine(DataDir, "logs");

        /// <summary>Folder next to the executable that holds v2ray plus the geoip/geosite data.</summary>
        public static string V2RayDir { get; } = Path.Combine(AppContext.BaseDirectory, "v2ray-core");

        public static string V2RayExecutable { get; } =
            Path.Combine(V2RayDir, OperatingSystem.IsWindows() ? "v2ray.exe" : "v2ray");

        public static string EnsureDataDir()
        {
            Directory.CreateDirectory(DataDir);
            return DataDir;
        }
    }
}
