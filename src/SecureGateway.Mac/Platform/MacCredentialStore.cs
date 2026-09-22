using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using SecureGateway.Platform;

namespace SecureGateway.Mac.Platform
{
    /// <summary>
    /// "Remember me" credentials in the macOS login Keychain (via the `security` tool).
    /// On non-macOS (development on Linux) falls back to a 0600 file in the data folder.
    /// </summary>
    public class MacCredentialStore : ICredentialStore
    {
        private const string Service = "com.fourthzodiac.securegateway";
        private const string Account = "SecureGateway";
        private static readonly string FallbackFile = Path.Combine(AppPaths.DataDir, "saved_credentials.json");

        private class Payload
        {
            public string Email { get; set; } = "";
            public string Password { get; set; } = "";
        }

        public void Save(string email, string password)
        {
            var json = JsonConvert.SerializeObject(new Payload { Email = email, Password = password });
            var blob = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

            if (OperatingSystem.IsMacOS())
            {
                Run("/usr/bin/security", "add-generic-password", "-U", "-a", Account, "-s", Service, "-w", blob);
                return;
            }

            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.WriteAllText(FallbackFile, json);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(FallbackFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch { }
        }

        public (string email, string password)? Load()
        {
            try
            {
                string json;
                if (OperatingSystem.IsMacOS())
                {
                    var (code, output) = Run("/usr/bin/security", "find-generic-password", "-a", Account, "-s", Service, "-w");
                    if (code != 0 || string.IsNullOrWhiteSpace(output)) return null;
                    json = Encoding.UTF8.GetString(Convert.FromBase64String(output.Trim()));
                }
                else
                {
                    if (!File.Exists(FallbackFile)) return null;
                    json = File.ReadAllText(FallbackFile);
                }

                var p = JsonConvert.DeserializeObject<Payload>(json);
                return p == null ? null : (p.Email, p.Password);
            }
            catch
            {
                Clear();
                return null;
            }
        }

        public void Clear()
        {
            if (OperatingSystem.IsMacOS())
            {
                Run("/usr/bin/security", "delete-generic-password", "-a", Account, "-s", Service);
                return;
            }
            try { if (File.Exists(FallbackFile)) File.Delete(FallbackFile); } catch { }
        }

        private static (int code, string stdout) Run(string file, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(file)
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
                };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10000);
                return (p.ExitCode, output);
            }
            catch (Exception ex) { return (-1, ex.Message); }
        }
    }
}
