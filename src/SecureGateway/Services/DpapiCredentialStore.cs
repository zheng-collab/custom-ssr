using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SecureGateway.Platform;

namespace SecureGateway.Services
{
    /// <summary>"Remember me" credentials encrypted with Windows DPAPI for the current user.</summary>
    public class DpapiCredentialStore : ICredentialStore
    {
        private static readonly string CredentialsFile = Path.Combine(AppPaths.DataDir, "saved_credentials.dat");

        private class Payload
        {
            public string Email { get; set; } = "";
            public string Password { get; set; } = "";
        }

        public void Save(string email, string password)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                var json = JsonConvert.SerializeObject(new Payload { Email = email, Password = password });
                var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(CredentialsFile, encrypted);
            }
            catch { }
        }

        public (string email, string password)? Load()
        {
            try
            {
                if (!File.Exists(CredentialsFile)) return null;
                var decrypted = ProtectedData.Unprotect(File.ReadAllBytes(CredentialsFile), null, DataProtectionScope.CurrentUser);
                var p = JsonConvert.DeserializeObject<Payload>(Encoding.UTF8.GetString(decrypted));
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
            try { if (File.Exists(CredentialsFile)) File.Delete(CredentialsFile); } catch { }
        }
    }
}
