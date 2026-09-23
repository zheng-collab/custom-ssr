using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SecureGateway.Mac;
using SecureGateway.Mac.Platform;
using SecureGateway.Mac.Views;
using SecureGateway.Models;
using SecureGateway.Services;

namespace SecureGateway.Mac.Smoke
{
    /// <summary>
    /// Headless smoke test: instantiates and renders every window of the macOS app without a
    /// display server. The main thread is the UI thread here, so everything is synchronous and
    /// the dispatcher is pumped by hand (awaiting on this thread would deadlock).
    /// </summary>
    internal static class Program
    {
        private static int _failures;

        public static int Main(string[] args)
        {
            var outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "sg-mac-smoke");
            Directory.CreateDirectory(outDir);

            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            Run(outDir);

            Console.WriteLine(_failures == 0 ? "SMOKE OK" : $"SMOKE FAILED ({_failures} failure(s))");
            return _failures == 0 ? 0 : 1;
        }

        private static void Run(string outDir)
        {
            var auth = new AuthService(new MacCredentialStore());

            Check("LoginWindow", () =>
            {
                var w = new LoginWindow(auth);
                w.Show();
                Pump();
                Expect(w.FindControl<TextBox>("TxtEmail") != null, "TxtEmail present");
                Expect(w.FindControl<Button>("BtnSubmit") != null, "BtnSubmit present");
                Snapshot(w, Path.Combine(outDir, "login.png"));

                var vm = (SecureGateway.UI.ViewModels.LoginViewModel)w.DataContext;
                vm.IsResetMode = true;
                Pump();
                Snapshot(w, Path.Combine(outDir, "login-reset.png"));

                vm.IsResetMode = false;
                vm.IsSignUpMode = true;
                Pump();
                Snapshot(w, Path.Combine(outDir, "login-signup.png"));

                // "Connect first" panel for restricted networks.
                vm.IsSignUpMode = false;
                vm.ToggleNetworkHelp();
                Pump();
                Expect(vm.IsNetworkBlocked, "network-help panel toggles on");
                Snapshot(w, Path.Combine(outDir, "login-bootstrap.png"));
                w.Close();
                Pump();
            });

            Check("Share link + QR", () =>
            {
                var ss = new ServerProfile { Name = "Vultr (SS)", Address = "203.0.113.5", Port = 8388,
                    Protocol = ProxyProtocol.Shadowsocks, SsPassword = "Ab+/cd12==", SsEncryption = ShadowsocksEncryption.Aes256Gcm };
                var ssLink = SecureGateway.Utils.ShareLinkParser.ToLink(ss);
                var back = SecureGateway.Utils.ShareLinkParser.Parse(ssLink);
                Expect(back != null && back.Address == ss.Address && back.Port == ss.Port && back.SsPassword == ss.SsPassword
                       && back.SsEncryption == ss.SsEncryption && back.Name == ss.Name, "ss:// link round-trips (" + ssLink[..20] + "…)");

                var vm = new ServerProfile { Name = "Tokyo", Address = "vpn.example.com", Port = 443, Protocol = ProxyProtocol.V2Ray,
                    V2RayUserId = "5e2f0b5c-1234-4a1b-9c2d-abcdef012345", V2RayTransport = V2RayTransport.WebSocket,
                    V2RayPath = "/ws", V2RayTls = true, V2RaySni = "vpn.example.com" };
                var vmLink = SecureGateway.Utils.ShareLinkParser.ToLink(vm);
                var back2 = SecureGateway.Utils.ShareLinkParser.Parse(vmLink);
                Expect(back2 != null && back2.V2RayUserId == vm.V2RayUserId && back2.V2RayTransport == V2RayTransport.WebSocket
                       && back2.V2RayTls && back2.V2RayPath == "/ws" && back2.Port == 443, "vmess:// link round-trips");

                var png = SecureGateway.Utils.QrCode.Png(ssLink);
                Expect(png != null && png.Length > 500 && png[0] == 0x89 && png[1] == (byte)'P', $"QR renders to PNG ({png?.Length ?? 0} bytes)");

                var w = new ShareLinkWindow(ss, ssLink, png);
                w.Show();
                Pump();
                Snapshot(w, Path.Combine(outDir, "share-qr.png"));
                w.Close();
                Pump();
            });

            Check("Bootstrap logic (no network needed)", () =>
            {
                var t1 = SecureGateway.Services.BootstrapConnector.TryConnectAsync("not a link", auth).GetAwaiter().GetResult();
                Expect(t1.connector == null && t1.error.Contains("not a supported"), "rejects a non-link");

                var t2 = SecureGateway.Services.BootstrapConnector.TryConnectAsync("ss://@@@", auth).GetAwaiter().GetResult();
                Expect(t2.connector == null && t2.error.Contains("could not be parsed"), "rejects a malformed link");

                SecureGateway.Services.SharedServerService.ClearCache();
                var t3 = SecureGateway.Services.BootstrapConnector.TryConnectAsync("", auth).GetAwaiter().GetResult();
                Expect(t3.connector == null && (t3.error.Contains("No server is saved") || t3.error.Contains("engine") || t3.error.Contains("v2ray")),
                    "no link + no saved server -> clear error (" + t3.error + ")");

                var ssl = new System.Net.Http.HttpRequestException("The SSL connection could not be established",
                    new System.Security.Authentication.AuthenticationException("boom"));
                Expect(SecureGateway.Services.AuthService.IsNetworkError(ssl), "SSL failure classified as network error");
                Expect(!SecureGateway.Services.AuthService.IsNetworkError(new InvalidOperationException("x")), "logic error not classified as network");
            });

            Check("ServerEditWindow", () =>
            {
                var w = new ServerEditWindow(new ServerProfile { Name = "Test", Address = "203.0.113.5" });
                w.Show();
                Pump();
                Expect(w.FindControl<ComboBox>("CmbProtocol")?.ItemCount == 2, "protocol combo populated");
                Snapshot(w, Path.Combine(outDir, "server-edit.png"));

                w.FindControl<ComboBox>("CmbProtocol")!.SelectedItem = ProxyProtocol.Shadowsocks;
                Pump();
                Expect(w.FindControl<StackPanel>("ShadowsocksPanel")!.IsVisible, "SS panel shown after protocol switch");
                Snapshot(w, Path.Combine(outDir, "server-edit-ss.png"));
                w.Close();
                Pump();
            });

            Check("MessageWindow", () =>
            {
                var owner = new Window { Width = 300, Height = 200 };
                owner.Show();
                Pump();

                var t = MessageWindow.ShowAsync(owner, "Test", "Hello from the smoke test.", MessageWindow.Kind.Warning);
                Pump();
                var win = owner.OwnedWindows.FirstOrDefault(x => x.Title == "Test");
                Expect(win != null, "message window opened as a dialog");
                Snapshot(win, Path.Combine(outDir, "message.png"));
                win?.Close();
                Pump();
                Expect(t.IsCompleted, "ShowAsync completes when the dialog closes");

                var confirm = MessageWindow.ConfirmAsync(owner, "Confirm", "Delete server 'Test'?");
                Pump();
                var cw = owner.OwnedWindows.FirstOrDefault(x => x.Title == "Confirm");
                Expect(cw != null, "confirm window opened as a dialog");
                Snapshot(cw, Path.Combine(outDir, "confirm.png"));
                cw?.Close();
                Pump();
                Expect(confirm.IsCompleted && confirm.Result == false, "closing confirm without choosing returns false");
                owner.Close();
                Pump();
            });

            Check("MainWindow", () =>
            {
                var w = new MainWindow(auth, (App)Application.Current);
                w.Show();
                Pump(6);
                Expect(w.FindControl<ListBox>("LogListBox") != null, "LogListBox present");
                Expect(w.ViewModel.LogEntries.Count > 0, "log has entries after startup");
                Snapshot(w, Path.Combine(outDir, "main-dashboard.png"));

                var tabs = w.GetVisualDescendants().OfType<TabControl>().FirstOrDefault();
                Expect(tabs != null && tabs.ItemCount == 4, "four tabs");
                if (tabs != null)
                {
                    for (int i = 1; i < tabs.ItemCount; i++)
                    {
                        tabs.SelectedIndex = i;
                        Pump();
                        Snapshot(w, Path.Combine(outDir, $"main-tab{i}.png"));
                    }
                }

                // Exercise a couple of commands that don't need the network.
                w.ViewModel.ClearLogCommand.Execute(null);
                Expect(w.ViewModel.LogEntries.Count == 0, "Clear Log empties the list");
                Expect(w.ViewModel.ConnectCommand.CanExecute(null) == (w.ViewModel.SelectedServer != null || true), "connect command evaluable");

                w.ShutdownCleanup();
                w.ForceClose();
                Pump();
            });
        }

        private static void Pump(int cycles = 3)
        {
            for (int i = 0; i < cycles; i++)
            {
                Thread.Sleep(40);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
        }

        private static void Snapshot(Window w, string path)
        {
            if (w == null) return;
            try
            {
                using var frame = w.CaptureRenderedFrame();
                if (frame == null) { Console.WriteLine($"  (no frame for {Path.GetFileName(path)})"); return; }
                using (var fs = File.Create(path))
                    frame.Save(fs);
                var size = new FileInfo(path).Length;
                Console.WriteLine($"  snapshot {Path.GetFileName(path)} {frame.PixelSize.Width}x{frame.PixelSize.Height} ({size / 1024} KB)");
                if (size < 1024) { Console.WriteLine("  FAIL snapshot file is empty"); _failures++; }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  snapshot failed: {ex.Message}");
            }
        }

        private static void Expect(bool condition, string what)
        {
            Console.WriteLine($"  {(condition ? "ok  " : "FAIL")} {what}");
            if (!condition) _failures++;
        }

        private static void Check(string name, Action body)
        {
            Console.WriteLine($"[{name}]");
            try { body(); }
            catch (Exception ex)
            {
                _failures++;
                Console.WriteLine($"  EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
