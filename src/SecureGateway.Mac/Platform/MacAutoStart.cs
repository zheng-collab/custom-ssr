using System;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace SecureGateway.Mac.Platform
{
    /// <summary>Start-at-login via a per-user LaunchAgent (~/Library/LaunchAgents).</summary>
    public static class MacAutoStart
    {
        private const string Label = "com.fourthzodiac.securegateway";

        private static string PlistPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents", Label + ".plist");

        public static bool IsEnabled() => File.Exists(PlistPath);

        public static void Set(bool enable)
        {
            if (!OperatingSystem.IsMacOS()) return;

            try
            {
                if (!enable)
                {
                    Run("/bin/launchctl", "unload", PlistPath);
                    if (File.Exists(PlistPath)) File.Delete(PlistPath);
                    return;
                }

                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;

                Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
                File.WriteAllText(PlistPath, $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key><string>{Label}</string>
    <key>ProgramArguments</key>
    <array>
        <string>{SecurityElement.Escape(exe)}</string>
        <string>--minimized</string>
    </array>
    <key>RunAtLoad</key><true/>
    <key>ProcessType</key><string>Interactive</string>
</dict>
</plist>
");
                Run("/bin/launchctl", "load", PlistPath);
            }
            catch { }
        }

        private static void Run(string file, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true,
                                                      RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                p.WaitForExit(5000);
            }
            catch { }
        }
    }
}
