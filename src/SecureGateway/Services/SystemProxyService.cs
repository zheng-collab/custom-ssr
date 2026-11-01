using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Newtonsoft.Json;
using SecureGateway.Models;
using SecureGateway.Platform;
using SecureGateway.Utils;

namespace SecureGateway.Services
{
    /// <summary>
    /// Points the Windows (WinINET) proxy at the local engine and puts the user's own settings
    /// back afterwards.
    ///
    /// The user's settings are written to a backup file *before* anything is changed, so they
    /// survive a crash, a killed process or a power cut: the next start (RepairLeftover) restores
    /// them. Without that file a leftover is recognised as "127.0.0.1:port with nothing
    /// listening" and switched off. A proxy that is not ours is never touched.
    /// </summary>
    public class SystemProxyService : ISystemProxyService
    {
        private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private const string Bypass = "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>";

        private static readonly string BackupFile = Path.Combine(AppPaths.DataDir, "system-proxy-backup.json");

        [DllImport("wininet.dll")]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        private class Snapshot
        {
            public int ProxyEnable { get; set; }
            public string ProxyServer { get; set; } = "";
            public string ProxyOverride { get; set; } = "";
            public string AutoConfigURL { get; set; } = "";
        }

        public void ConfigureForMode(ProxyMode mode, int httpPort, int socksPort)
        {
            switch (mode)
            {
                case ProxyMode.Global:
                case ProxyMode.Rule:
                    // Rule-based routing is done inside v2ray; the system proxy points at it in both modes.
                    EnableGlobalProxy(httpPort);
                    break;
                case ProxyMode.Direct:
                    DisableProxy();
                    break;
            }
        }

        private void EnableGlobalProxy(int httpPort)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (key == null) return;

            // Back up the user's settings once per session (the file persists across a crash).
            if (!File.Exists(BackupFile))
            {
                var current = Read(key);
                // Our own setting from a session that died: the user's real "before" is "no proxy".
                if (current.ProxyEnable == 1 && ProxyLeftover.LocalPort(current.ProxyServer) == httpPort)
                    current = new Snapshot();
                WriteBackup(current);
            }

            key.SetValue("ProxyServer", $"127.0.0.1:{httpPort}");
            key.SetValue("ProxyEnable", 1);
            key.SetValue("ProxyOverride", Bypass);
            key.DeleteValue("AutoConfigURL", false);   // a PAC file would take precedence over our proxy
            Refresh();
        }

        public void DisableProxy()
        {
            if (RestoreBackup()) return;

            // No backup: only remove a setting that points at this computer (ours), never the user's.
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (key == null) return;
            if (ProxyLeftover.LocalPort(key.GetValue("ProxyServer") as string) != null)
            {
                key.SetValue("ProxyEnable", 0);
                key.DeleteValue("ProxyServer", false);
                Refresh();
            }
        }

        public bool RepairLeftover()
        {
            // Called at startup (before any session of ours exists) and from "Fix internet"
            // (after disconnecting): a backup file means a session never restored the settings.
            if (RestoreBackup()) return true;

            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (key == null) return false;
            var current = Read(key);
            if (current.ProxyEnable != 1 || !ProxyLeftover.IsStale(current.ProxyServer, p => PortHelper.IsListening(p, 300)))
                return false;

            key.SetValue("ProxyEnable", 0);
            key.DeleteValue("ProxyServer", false);
            Refresh();
            return true;
        }

        // ---- backup file --------------------------------------------------------------------------
        private static bool RestoreBackup()
        {
            Snapshot saved;
            try
            {
                if (!File.Exists(BackupFile)) return false;
                saved = JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(BackupFile)) ?? new Snapshot();
            }
            catch
            {
                saved = new Snapshot();   // unreadable backup: fall back to "no proxy"
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
                if (key == null) return false;
                key.SetValue("ProxyEnable", saved.ProxyEnable);
                SetOrDelete(key, "ProxyServer", saved.ProxyServer);
                SetOrDelete(key, "ProxyOverride", saved.ProxyOverride);
                SetOrDelete(key, "AutoConfigURL", saved.AutoConfigURL);
                Refresh();
            }
            catch
            {
                return false;   // keep the backup so the next attempt can still restore it
            }

            try { File.Delete(BackupFile); } catch { }
            return true;
        }

        private static void WriteBackup(Snapshot s)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BackupFile)!);
                File.WriteAllText(BackupFile, JsonConvert.SerializeObject(s));
            }
            catch { }
        }

        private static Snapshot Read(RegistryKey key) => new()
        {
            ProxyEnable = key.GetValue("ProxyEnable") is int i ? i : 0,
            ProxyServer = key.GetValue("ProxyServer") as string ?? "",
            ProxyOverride = key.GetValue("ProxyOverride") as string ?? "",
            AutoConfigURL = key.GetValue("AutoConfigURL") as string ?? "",
        };

        private static void SetOrDelete(RegistryKey key, string name, string value)
        {
            if (string.IsNullOrEmpty(value)) key.DeleteValue(name, false);
            else key.SetValue(name, value);
        }

        private static void Refresh()
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
    }
}
