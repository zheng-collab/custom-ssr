using System;
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

        private static readonly HttpClient Http = new();

        private Supabase.Client _client;

        public bool IsAuthenticated => _client?.Auth?.CurrentSession != null;
        public string UserEmail => _client?.Auth?.CurrentUser?.Email ?? "";
        public string UserId => _client?.Auth?.CurrentUser?.Id ?? "";
        public string AccessToken => _client?.Auth?.CurrentSession?.AccessToken ?? "";

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
                var (accessToken, refreshToken, error) = await AuthViaRestAsync(
                    $"{SupabaseUrl}/auth/v1/token?grant_type=password",
                    new { email, password });

                if (error != null)
                    return AuthResult.Failure(error);

                var session = await _client.Auth.SetSession(accessToken, refreshToken);
                if (session == null)
                    return AuthResult.Failure("Sign in failed. Could not establish session.");

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
                var (accessToken, refreshToken, error) = await AuthViaRestAsync(
                    $"{SupabaseUrl}/auth/v1/signup",
                    new { email, password });

                if (error != null)
                    return AuthResult.Failure(error);

                if (string.IsNullOrEmpty(accessToken))
                {
                    return AuthResult.SuccessWithMessage(
                        "Account created. Please check your email to confirm your account before signing in.");
                }

                var session = await _client.Auth.SetSession(accessToken, refreshToken);
                if (session?.User != null)
                {
                    await SaveSessionAsync(session);
                    return AuthResult.Success(session.User.Email ?? email);
                }

                return AuthResult.SuccessWithMessage(
                    "Account created. Please check your email to confirm your account before signing in.");
            }
            catch (Exception ex)
            {
                return AuthResult.Failure($"Connection error: {ex.Message}");
            }
        }

        public async Task SignOutAsync()
        {
            try { await _client.Auth.SignOut(); } catch { }
            ClearSavedSession();
            ClearCredentials();
        }

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
                ClearCredentials();
                return null;
            }
        }

        private async Task<(string accessToken, string refreshToken, string error)> AuthViaRestAsync(
            string url, object payload)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("apikey", SupabaseAnonKey);
            request.Content = new StringContent(
                JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var err = JObject.Parse(body);
                var msg = err["error_description"]?.ToString()
                       ?? err["msg"]?.ToString()
                       ?? "Authentication failed. Please check your credentials.";
                return (null, null, msg);
            }

            var data = JObject.Parse(body);
            var accessToken = data["access_token"]?.ToString();
            var refreshToken = data["refresh_token"]?.ToString();

            return (accessToken, refreshToken, null);
        }

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
            try { if (File.Exists(CredentialsFile)) File.Delete(CredentialsFile); } catch { }
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

                if (!string.IsNullOrEmpty(saved.LoginTimestampUtc)
                    && DateTime.TryParse(saved.LoginTimestampUtc, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var loginTime)
                    && (DateTime.UtcNow - loginTime).TotalDays > SessionMaxDays)
                {
                    ClearSavedSession();
                    return;
                }

                // Load saved tokens into SDK before attempting refresh
                var session = await _client.Auth.SetSession(saved.AccessToken, saved.RefreshToken);
                if (session == null)
                {
                    ClearSavedSession();
                    return;
                }

                // Re-verify permission on session restore
                var userId = session.User?.Id;
                if (string.IsNullOrEmpty(userId) || !await CheckGatewayAccessAsync(userId))
                {
                    try { await _client.Auth.SignOut(); } catch { }
                    ClearSavedSession();
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
                var accessToken = _client.Auth.CurrentSession?.AccessToken ?? "";

                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{SupabaseUrl}/rest/v1/profiles?id=eq.{userId}&select=permissions");
                request.Headers.Add("apikey", SupabaseAnonKey);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var response = await Http.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                var rows = JArray.Parse(body);

                if (rows.Count == 0) return false;

                var permissions = rows[0]["permissions"] as JArray;
                return permissions?.Any(p => p.ToString() == "gateway.access") ?? false;
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
                    LoginTimestampUtc = DateTime.UtcNow.ToString("o")
                };
                var json = JsonConvert.SerializeObject(saved);
                await File.WriteAllTextAsync(TokenFile, json);
            }
            catch { }
        }

        private void ClearSavedSession()
        {
            try { if (File.Exists(TokenFile)) File.Delete(TokenFile); } catch { }
        }

        private class SavedSession
        {
            public string AccessToken { get; set; } = "";
            public string RefreshToken { get; set; } = "";
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
