using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Supabase;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace SecureGateway.Services
{
    public class AuthService
    {
        private const string SupabaseUrl = "https://yahzzatmmmdmwalindai.supabase.co";
        private const string SupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InlhaHp6YXRtbW1kbXdhbGluZGFpIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzE3ODA1NzMsImV4cCI6MjA4NzM1NjU3M30._OKdLLUDN80GR7nMHYsI3S0WmzPmGw-7QmpZYEIREj4";
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
        public string AccessToken => _client?.Auth?.CurrentSession?.AccessToken ?? "";

        /// <summary>
        /// Creates a SharedServerService that uses this auth session's credentials.
        /// </summary>
        public SharedServerService CreateSharedServerService()
        {
            return new SharedServerService(SupabaseUrl, SupabaseAnonKey, () => AccessToken);
        }

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
                // Direct REST call to Supabase auth — bypasses SDK version issues
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("apikey", SupabaseAnonKey);

                var payload = JsonConvert.SerializeObject(new { email, password });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await http.PostAsync(
                    $"{SupabaseUrl}/auth/v1/token?grant_type=password", content);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var error = JObject.Parse(body);
                    var msg = error["error_description"]?.ToString()
                           ?? error["msg"]?.ToString()
                           ?? "Sign in failed. Please check your credentials.";
                    return AuthResult.Failure(msg);
                }

                var tokenData = JObject.Parse(body);
                var accessToken = tokenData["access_token"]?.ToString();
                var refreshToken = tokenData["refresh_token"]?.ToString();

                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
                    return AuthResult.Failure("Sign in failed. Invalid server response.");

                // Establish the session on the SDK client
                var session = await _client.Auth.SetSession(accessToken, refreshToken);

                if (session == null)
                    return AuthResult.Failure("Sign in failed. Could not establish session.");

                // Check gateway.access permission before granting access
                var userId = session.User?.Id;
                if (string.IsNullOrEmpty(userId) || !await CheckGatewayAccessAsync(userId))
                {
                    try { await _client.Auth.SignOut(); } catch { }
                    return AuthResult.Failure("Access denied. Your account does not have gateway access.");
                }

                await SaveSessionAsync(session);

                if (rememberMe)
                    SaveCredentials(email, password);
                else
                    ClearCredentials();

                return AuthResult.Success(session.User?.Email ?? email);
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
                else
                {
                    // Re-verify permission on session restore
                    var userId = session.User?.Id;
                    if (string.IsNullOrEmpty(userId) || !await CheckGatewayAccessAsync(userId))
                    {
                        try { await _client.Auth.SignOut(); } catch { }
                        ClearSavedSession();
                    }
                }
            }
            catch
            {
                ClearSavedSession();
            }
        }

        private async Task<bool> CheckGatewayAccessAsync(string userId)
        {
            try
            {
                using var http = new HttpClient();
                var accessToken = _client.Auth.CurrentSession?.AccessToken ?? "";

                http.DefaultRequestHeaders.Add("apikey", SupabaseAnonKey);
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken);

                var url = $"{SupabaseUrl}/rest/v1/profiles?id=eq.{userId}&select=permissions";
                var response = await http.GetStringAsync(url);
                var rows = JArray.Parse(response);

                if (rows.Count == 0)
                    return false;

                var permissions = rows[0]["permissions"] as JArray;
                if (permissions == null)
                    return false;

                return permissions.Any(p => p.ToString() == "gateway.access");
            }
            catch
            {
                return false;
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
