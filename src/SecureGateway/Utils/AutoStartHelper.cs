using System;
using System.Reflection;
using Microsoft.Win32;

namespace SecureGateway.Utils
{
    public static class AutoStartHelper
    {
        private const string AppName = "SecureGateway";
        private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        public static void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
                if (key == null) return;

                if (enable)
                {
                    var exePath = Environment.ProcessPath
                        ?? Assembly.GetExecutingAssembly().Location;
                    key.SetValue(AppName, $"\"{exePath}\" --minimized");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch
            {
                // Requires elevated permissions in some cases
            }
        }
    }

    public static class ClipboardHelper
    {
        public static string ParseShareLink(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
                return null;

            link = link.Trim();

            if (link.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
                return "v2ray";

            if (link.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
                return "shadowsocks";

            if (link.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
                return "v2ray";

            return null;
        }
    }
}
