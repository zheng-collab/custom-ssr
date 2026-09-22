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
                w.Close();
                Pump();
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
