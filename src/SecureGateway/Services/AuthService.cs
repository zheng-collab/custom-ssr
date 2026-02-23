using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Supabase;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace SecureGateway.Services
{
    public class AuthService
    {
        private const string SupabaseUrl = "https://yahzzatmmmdmwalindai.supabase.co";
        private const string SupabaseAnonKey = "sb_publishable_XBgu5qAbVb2CcV1ynhbWMg_njzj972c";
        private const int SessionMaxDays = 120;

        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SecureGateway");

        private static readonly string TokenFile = Path.Combine(AppDataDir, "auth_session.json");
        private static readonly string CredentialsFile = Path.Combine(AppDataDir, "saved_credentials.dat");

        private Supabase.Client _client;

        public bool IsAuthenticated => _client?.Auth?.CurrentSession != null;
        public string UserEmail => _client?.Auth?.CurrentUser?.Email ?? "";
        public string UserId => _client?.Auth?.CurrentUser?.Id ?? "";

        public async Task InitializeAsync()
        {
            var options = new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = false
            };

            _client = new Supabase.Client(SupabaseUrl, SupabaseAnonKey, options);
            await _client.InitializeAsync();

            await TryRestoreSessionAsync();
        }

        public async Task<AuthResult> SignInAsync(string email, string password, bool rememberMe = false)
        {
            try
            {
                var session = await _client.Auth.SignIn(email, password);

                if (session != null)
                {
                    await SaveSessionAsync(session);

                    if (rememberMe)
                        SaveCredentials(email, password);
                    else
                        ClearCredentials();

                    return AuthResult.Success(session.User?.Email ?? email);
                }

                return AuthResult.Failure("Sign in failed. Please check your credentials.");
            }
            catch (Supabase.Gotrue.Exceptions.GotrueException ex)
            {
                return AuthResult.Failure(ex.Message);
            }
            catch (Exception ex)
            {
                return AuthResult.Failure($"Connection error: {ex.Message}");
            }
        }

        public async Task<AuthResult> SignUpAsync(string email, string password)
        {
            try
            {
                var session = await _client.Auth.SignUp(email, password);

                if (session?.User != null)
                {
                    if (session.User.ConfirmedAt.HasValue)
                    {
                        await SaveSessionAsync(session);
                        return AuthResult.Success(session.User.Email ?? email);
                    }

                    return AuthResult.SuccessWithMessage(
                        "Account created. Please check your email to confirm your account before signing in.");
                }

                return AuthResult.Failure("Sign up failed. Please try again.");
            }
            catch (Supabase.Gotrue.Exceptions.GotrueException ex)
            {
                return AuthResult.Failure(ex.Message);
            }
            catch (Exception ex)
            {
                return AuthResult.Failure($"Connection error: {ex.Message}");
            }
        }

        public async Task SignOutAsync()
        {
            try
            {
                await _client.Auth.SignOut();
            }
            catch
            {
                // Best effort
            }

            ClearSavedSession();
            ClearCredentials();
        }

        /// <summary>
        /// Load saved credentials (email + password) protected by Windows DPAPI.
        /// Returns null if none saved or decryption fails.
        /// </summary>
        public SavedCredentials LoadCredentials()
        {
            try
            {
                if (!File.Exists(CredentialsFile)) return null;

                var encryptedBytes = File.ReadAllBytes(CredentialsFile);
                var decryptedBytes = ProtectedData.Unprotect(
                    encryptedBytes, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(decryptedBytes);
                return JsonConvert.DeserializeObject<SavedCredentials>(json);
            }
            catch
            {
                // Decryption failed or file corrupt — clear it
                ClearCredentials();
                return null;
            }
        }

        public bool HasSavedCredentials => File.Exists(CredentialsFile);

        private void SaveCredentials(string email, string password)
        {
            try
            {
                Directory.CreateDirectory(AppDataDir);

                var creds = new SavedCredentials { Email = email, Password = password };
                var json = JsonConvert.SerializeObject(creds);
                var plainBytes = Encoding.UTF8.GetBytes(json);
                var encryptedBytes = ProtectedData.Protect(
                    plainBytes, null, DataProtectionScope.CurrentUser);

                File.WriteAllBytes(CredentialsFile, encryptedBytes);
            }
            catch { }
        }

        private void ClearCredentials()
        {
            try
            {
                if (File.Exists(CredentialsFile))
                    File.Delete(CredentialsFile);
            }
            catch { }
        }

        private async Task TryRestoreSessionAsync()
        {
            try
            {
                if (!File.Exists(TokenFile)) return;

                var json = await File.ReadAllTextAsync(TokenFile);
                var saved = JsonConvert.DeserializeObject<SavedSession>(json);

                if (saved == null || string.IsNullOrEmpty(saved.RefreshToken))
                    return;

                // Check 120-day session expiry
                if (!string.IsNullOrEmpty(saved.LoginTimestampUtc))
                {
                    if (DateTime.TryParse(saved.LoginTimestampUtc, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var loginTime))
                    {
                        if ((DateTime.UtcNow - loginTime).TotalDays > SessionMaxDays)
                        {
                            // Session too old — force re-login
                            ClearSavedSession();
                            return;
                        }
                    }
                }

                // Try to refresh the session with the server
                var session = await _client.Auth.RefreshSession();

                if (session == null)
                {
                    // Server invalidated the token
                    ClearSavedSession();
                }
            }
            catch
            {
                ClearSavedSession();
            }
        }

        private async Task SaveSessionAsync(Session session)
        {
            try
            {
                Directory.CreateDirectory(AppDataDir);

                var saved = new SavedSession
                {
                    AccessToken = session.AccessToken ?? "",
                    RefreshToken = session.RefreshToken ?? "",
                    Email = session.User?.Email ?? "",
                    LoginTimestampUtc = DateTime.UtcNow.ToString("o")
                };

                var json = JsonConvert.SerializeObject(saved);
                await File.WriteAllTextAsync(TokenFile, json);
            }
            catch { }
        }

        private void ClearSavedSession()
        {
            try
            {
                if (File.Exists(TokenFile))
                    File.Delete(TokenFile);
            }
            catch { }
        }

        private class SavedSession
        {
            public string AccessToken { get; set; } = "";
            public string RefreshToken { get; set; } = "";
            public string Email { get; set; } = "";
            public string LoginTimestampUtc { get; set; } = "";
        }
    }

    public class SavedCredentials
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class AuthResult
    {
        public bool IsSuccess { get; set; }
        public string Email { get; set; } = "";
        public string Message { get; set; } = "";
        public bool RequiresConfirmation { get; set; }

        public static AuthResult Success(string email) =>
            new() { IsSuccess = true, Email = email };

        public static AuthResult SuccessWithMessage(string message) =>
            new() { IsSuccess = true, Message = message, RequiresConfirmation = true };

        public static AuthResult Failure(string message) =>
            new() { IsSuccess = false, Message = message };
    }
}
