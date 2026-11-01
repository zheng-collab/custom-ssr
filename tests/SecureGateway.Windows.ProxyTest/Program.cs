using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Microsoft.Win32;
using SecureGateway.Models;
using SecureGateway.Platform;
using SecureGateway.Services;

// Windows proxy behaviour that matters for "browser has no internet": leftovers are repaired,
// the user's own settings come back exactly, and a proxy that is not ours is never touched.
static class Program
{
    const string Path_ = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    static int fails;
    static void Expect(bool ok, string what) { Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}"); if (!ok) fails++; }

    static RegistryKey Key() => Registry.CurrentUser.CreateSubKey(Path_, true);
    static (int enable, string server, string over, string pac) Read()
    {
        using var k = Key();
        return (k.GetValue("ProxyEnable") is int i ? i : 0, k.GetValue("ProxyServer") as string ?? "",
                k.GetValue("ProxyOverride") as string ?? "", k.GetValue("AutoConfigURL") as string ?? "");
    }
    static void Write(int enable, string server, string over, string pac)
    {
        using var k = Key();
        k.SetValue("ProxyEnable", enable);
        if (server == "") k.DeleteValue("ProxyServer", false); else k.SetValue("ProxyServer", server);
        if (over == "") k.DeleteValue("ProxyOverride", false); else k.SetValue("ProxyOverride", over);
        if (pac == "") k.DeleteValue("AutoConfigURL", false); else k.SetValue("AutoConfigURL", pac);
    }
    static string Backup => Path.Combine(AppPaths.DataDir, "system-proxy-backup.json");

    static int Main()
    {
        var original = Read();
        try
        {
            File.Delete(Backup);
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int livePort = ((IPEndPoint)listener.LocalEndpoint).Port;

            // 1. Company proxy before connecting: connect, crash, next start restores it exactly.
            Write(1, "proxy.company.example:8080", "*.company.example;<local>", "");
            new SystemProxyService().ConfigureForMode(ProxyMode.Rule, livePort, livePort + 1);
            var during = Read();
            Expect(during.enable == 1 && during.server == $"127.0.0.1:{livePort}", "connected: Windows points at the local engine");
            Expect(File.Exists(Backup), "user's settings written to the backup file before changing them");
            listener.Stop();   // the engine is gone (crash / killed / power cut)
            bool repaired = new SystemProxyService().RepairLeftover();   // a fresh instance = next app start
            var after = Read();
            Expect(repaired && after.enable == 1 && after.server == "proxy.company.example:8080" && after.over == "*.company.example;<local>",
                   $"next start restores the company proxy and bypass list exactly ({after.server} / {after.over})");
            Expect(!File.Exists(Backup), "backup file removed once restored");

            // 2. No backup file (older version crashed): a dead 127.0.0.1 proxy is switched off.
            Write(1, "127.0.0.1:10809", "", "");
            Expect(new SystemProxyService().RepairLeftover() && Read().enable == 0 && Read().server == "", "leftover without backup: switched off");

            // 3. Repair never touches a working setup.
            Write(1, "proxy.company.example:8080", "", "");
            Expect(!new SystemProxyService().RepairLeftover() && Read().server == "proxy.company.example:8080", "company proxy left alone by repair");
            var other = new TcpListener(IPAddress.Loopback, 0); other.Start();
            int otherPort = ((IPEndPoint)other.LocalEndpoint).Port;
            Write(1, $"127.0.0.1:{otherPort}", "", "");
            Expect(!new SystemProxyService().RepairLeftover() && Read().server == $"127.0.0.1:{otherPort}", "another local proxy that still answers is left alone");
            other.Stop();

            // 4. Normal connect + disconnect, starting from "no proxy" with a PAC script.
            Write(0, "", "", "http://wpad.company.example/proxy.pac");
            var svc = new SystemProxyService();
            svc.ConfigureForMode(ProxyMode.Global, 10809, 10808);
            Expect(Read().pac == "", "PAC script set aside while connected (it would bypass the VPN)");
            svc.DisableProxy();
            var restored = Read();
            Expect(restored.enable == 0 && restored.server == "" && restored.pac == "http://wpad.company.example/proxy.pac", "disconnect restores no-proxy + PAC");

            // 5. Disconnect without a backup never deletes a proxy that is not ours.
            File.Delete(Backup);
            Write(1, "proxy.company.example:8080", "", "");
            new SystemProxyService().DisableProxy();
            Expect(Read().enable == 1 && Read().server == "proxy.company.example:8080", "disconnect with no backup leaves the company proxy");

            // 6. Crash, then connect again before any repair: the leftover must not become "the original".
            File.Delete(Backup);
            Write(1, "127.0.0.1:10809", "", "");
            var s2 = new SystemProxyService();
            s2.ConfigureForMode(ProxyMode.Rule, 10809, 10808);
            s2.DisableProxy();
            Expect(Read().enable == 0, "own leftover is not saved as the user's setting");
        }
        finally
        {
            Write(original.enable, original.server, original.over, original.pac);
            try { File.Delete(Backup); } catch { }
        }
        Console.WriteLine(fails == 0 ? "WINDOWS PROXY TEST OK" : $"WINDOWS PROXY TEST FAILED ({fails})");
        return fails;
    }
}
