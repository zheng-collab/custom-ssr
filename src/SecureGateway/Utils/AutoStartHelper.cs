using System;
using System.IO;
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
                    // Assembly.Location is empty in a single-file publish; ProcessPath is reliable there.
                    var exePath = Environment.ProcessPath
                        ?? Path.Combine(AppContext.BaseDirectory, "SecureGateway.exe");
                    if (!File.Exists(exePath)) return;
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
}
