using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SecureGateway.Models;

namespace SecureGateway.Services
{
    public class SystemProxyService
    {
        private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        [DllImport("wininet.dll")]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        private string _previousProxyServer;
        private int _previousProxyEnable;
        private string _previousAutoConfigUrl;
        private bool _hasSavedState;

        public void EnableGlobalProxy(int httpPort)
        {
            SaveCurrentState();

            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (key == null) return;

            key.SetValue("ProxyServer", $"127.0.0.1:{httpPort}");
            key.SetValue("ProxyEnable", 1);
            // Bypass local addresses
            key.SetValue("ProxyOverride", "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>");

            RefreshSystemProxy();
        }

        public void EnablePacProxy(string pacUrl)
        {
            SaveCurrentState();

            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (key == null) return;

            key.SetValue("AutoConfigURL", pacUrl);
            key.SetValue("ProxyEnable", 0);

            RefreshSystemProxy();
        }

        public void DisableProxy()
        {
            if (_hasSavedState)
            {
                RestorePreviousState();
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
                if (key == null) return;

                key.SetValue("ProxyEnable", 0);
                key.DeleteValue("ProxyServer", false);
                key.DeleteValue("AutoConfigURL", false);
            }

            RefreshSystemProxy();
        }

        public void ConfigureForMode(ProxyMode mode, int httpPort, string pacUrl = null)
        {
            switch (mode)
            {
                case ProxyMode.Global:
                    EnableGlobalProxy(httpPort);
                    break;
                case ProxyMode.Rule:
                    if (!string.IsNullOrEmpty(pacUrl))
                        EnablePacProxy(pacUrl);
                    else
                        EnableGlobalProxy(httpPort);
                    break;
                case ProxyMode.Direct:
                    DisableProxy();
                    break;
            }
        }

        private void SaveCurrentState()
        {
            if (_hasSavedState) return;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key == null) return;

                _previousProxyServer = key.GetValue("ProxyServer") as string ?? "";
                _previousProxyEnable = (int)(key.GetValue("ProxyEnable") ?? 0);
                _previousAutoConfigUrl = key.GetValue("AutoConfigURL") as string ?? "";
                _hasSavedState = true;
            }
            catch
            {
                // Best effort - if we can't read, we'll just disable on cleanup
            }
        }

        private void RestorePreviousState()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
                if (key == null) return;

                if (!string.IsNullOrEmpty(_previousProxyServer))
                    key.SetValue("ProxyServer", _previousProxyServer);
                else
                    key.DeleteValue("ProxyServer", false);

                key.SetValue("ProxyEnable", _previousProxyEnable);

                if (!string.IsNullOrEmpty(_previousAutoConfigUrl))
                    key.SetValue("AutoConfigURL", _previousAutoConfigUrl);
                else
                    key.DeleteValue("AutoConfigURL", false);

                _hasSavedState = false;
            }
            catch
            {
                // Best effort
            }
        }

        private static void RefreshSystemProxy()
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
    }
}
