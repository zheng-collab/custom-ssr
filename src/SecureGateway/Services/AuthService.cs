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
using Supabase.Gotrue.Exceptions;

namespace SecureGateway.Services
{
    public class AuthService
    {
        private const string SupabaseUrl = "https://yahzzatmmmdmwalindai.supabase.co";
        private const string SupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InlhaHp6YXRtbW1kbXdhbGluZGFpIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzE3ODA1NzMsImV4cCI6MjA4NzM1NjU3M30._OKdLLUDN80GR7nMHYsI3S0WmzPmGw-7QmpZYEIREj4";
        private const string RequiredPermission = "gateway.access";
        private const int SessionMaxDays = 120;

        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SecureGateway");

        private static readonly string TokenFile = Path.Combine(AppDataDir, "auth_session.json");
        private static readonly string CredentialsFile = Path.Combine(AppDataDir, "saved_credentials.dat");

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

        private Supabase.Client _client;
        private DateTime _loginTimestampUtc = DateTime.UtcNow;

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
            _client = new Supabase.Client(SupabaseUrl, SupabaseAnonKey, new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = false
            });
            await _client.InitializeAsync();

            // Supabase rotates refresh tokens. Persist every refresh so the next launch
            // (on this device) can restore the session; otherwise the stored token goes
            // stale and every device with the same account keeps getting logged out.
            _client.Auth.AddStateChangedListener((sender, state) =>
            {
                if (state == Supabase.Gotrue.Constants.AuthState.TokenRefreshed && sender.CurrentSession != null)
                    _ = PersistSessionAsync(sender.CurrentSession);
            });

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
                if (session?.User == null)
                    return AuthResult.Failure("Sign in failed. Could not establish session.");

                var access = await CheckGatewayAccessAsync(session.User.Id);
                if (access != true)
                {
                    await SignOutSdkAsync();
                    return AuthResult.Failure(access == false
                        ? "Access denied. Your account does not have gateway access."
                        : "Could not verify account permissions. Please try again.");
                }

                _loginTimestampUtc = DateTime.UtcNow;
                await PersistSessionAsync(session);

                if (rememberMe)
                    SaveCredentials(email, password);
                else
                    ClearCredentials();

                return AuthResult.Success(session.User.Email ?? email);
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

                // No tokens means e-mail confirmation is required before the account is usable.
                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
                    return AuthResult.SuccessWithMessage(
                        "Account created. Check your e-mail to confirm the account, then sign in.");

                // Account is live but has no gateway.access yet — don't keep the session.
                return AuthResult.SuccessWithMessage(
                    "Account created. Ask your administrator to grant gateway access, then sign in.");
            }
            catch (Exception ex)
            {
                return AuthResult.Failure($"Connection error: {ex.Message}");
            }
        }

        public async Task SignOutAsync()
        {
            await SignOutSdkAsync();
            ClearSavedSession();
            ClearCredentials();
        }

        /// <summary>Asks Supabase to e-mail a password-reset code to the address.</summary>
        public async Task<AuthResult> RequestPasswordResetAsync(string email)
        {
            try
            {
                var (_, _, error) = await AuthViaRestAsync($"{SupabaseUrl}/auth/v1/recover", new { email });
                if (error != null)
                    return AuthResult.Failure(error);

                return AuthResult.SuccessWithMessage(
                    "If an account exists for that address, a reset code has been e-mailed to it.");
            }
            catch (Exception ex)
            {
                return AuthResult.Failure($"Connection error: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifies the e-mailed recovery code and sets a new password. The temporary
        /// session this creates is discarded; the user signs in normally afterwards.
        /// </summary>
        public async Task<AuthResult> ResetPasswordWithCodeAsync(string email, string code, string newPassword)
        {
            try
            {
                var (accessToken, _, error) = await AuthViaRestAsync(
                    $"{SupabaseUrl}/auth/v1/verify",
                    new { type = "recovery", email, token = code.Trim() });

                if (error != null)
                    return AuthResult.Failure(error.Contains("expired", StringComparison.OrdinalIgnoreCase)
                        || error.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                        ? "That reset code is invalid or has expired. Request a new one."
                        : error);

                if (string.IsNullOrEmpty(accessToken))
                    return AuthResult.Failure("Could not verify the reset code. Request a new one.");

                using var request = new HttpRequestMessage(HttpMethod.Put, $"{SupabaseUrl}/auth/v1/user");
                request.Headers.Add("apikey", SupabaseAnonKey);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(new { password = newPassword }), Encoding.UTF8, "application/json");

                using var response = await Http.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    string msg = null;
                    try { var err = JObject.Parse(body); msg = err["msg"]?.ToString() ?? err["error_description"]?.ToString(); }
                    catch { }
                    return AuthResult.Failure(msg ?? "Could not update the password. Please try again.");
                }

                // Invalidate the recovery session so only the new password works from here on.
                using var logout = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseUrl}/auth/v1/logout");
                logout.Headers.Add("apikey", SupabaseAnonKey);
                logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                try { using var _ = await Http.SendAsync(logout); } catch { }

                ClearCredentials();
                return AuthResult.SuccessWithMessage("Password updated. Sign in with your new password.");
            }
            catch (Exception ex)
            {
                return AuthResult.Failure($"Connection error: {ex.Message}");
            }
        }

        public SavedCredentials LoadCredentials()
        {
            try
            {
                if (!File.Exists(CredentialsFile)) return null;

                var decrypted = ProtectedData.Unprotect(
                    File.ReadAllBytes(CredentialsFile), null, DataProtectionScope.CurrentUser);
                return JsonConvert.DeserializeObject<SavedCredentials>(Encoding.UTF8.GetString(decrypted));
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
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("apikey", SupabaseAnonKey);
            request.Content = new StringContent(
                JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string msg = null;
                try
                {
                    var err = JObject.Parse(body);
                    msg = err["error_description"]?.ToString() ?? err["msg"]?.ToString();
                }
                catch { }

                return (null, null, msg ?? "Authentication failed. Please check your credentials.");
            }

            var data = JObject.Parse(body);
            return (data["access_token"]?.ToString(), data["refresh_token"]?.ToString(), null);
        }

        private async Task TryRestoreSessionAsync()
        {
            SavedSession saved;
            try
            {
                if (!File.Exists(TokenFile)) return;
                saved = JsonConvert.DeserializeObject<SavedSession>(await File.ReadAllTextAsync(TokenFile));
            }
            catch
            {
                ClearSavedSession();
                return;
            }

            if (saved == null || string.IsNullOrEmpty(saved.RefreshToken) || string.IsNullOrEmpty(saved.AccessToken))
            {
                ClearSavedSession();
                return;
            }

            if (DateTime.TryParse(saved.LoginTimestampUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var loginTime))
            {
                if ((DateTime.UtcNow - loginTime).TotalDays > SessionMaxDays)
                {
                    ClearSavedSession();
                    return;
                }
                _loginTimestampUtc = loginTime;
            }
            else
            {
                _loginTimestampUtc = DateTime.UtcNow;
            }

            Session session;
            try
            {
                session = await _client.Auth.SetSession(saved.AccessToken, saved.RefreshToken);
            }
            catch (GotrueException)
            {
                // Server rejected the token (revoked, expired, reused) — force re-login.
                ClearSavedSession();
                return;
            }
            catch
            {
                // Network problem — keep the saved session for next time, stay logged out now.
                return;
            }

            if (session?.User == null)
            {
                ClearSavedSession();
                return;
            }

            // Tokens may have rotated during SetSession; store the current pair immediately.
            await PersistSessionAsync(session);

            var access = await CheckGatewayAccessAsync(session.User.Id);
            if (access == false)
            {
                await SignOutSdkAsync();
                ClearSavedSession();
            }
            // access == null: server unreachable for the permission lookup. Permission was
            // verified at login and the 120-day cap still applies, so keep the session.
        }

        /// <summary>true = allowed, false = denied, null = could not determine.</summary>
        private async Task<bool?> CheckGatewayAccessAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return false;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{SupabaseUrl}/rest/v1/profiles?id=eq.{Uri.EscapeDataString(userId)}&select=permissions");
                request.Headers.Add("apikey", SupabaseAnonKey);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

                using var response = await Http.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return (int)response.StatusCode >= 500 ? null : false;

                var rows = JArray.Parse(await response.Content.ReadAsStringAsync());
                if (rows.Count == 0) return false;

                var permissions = rows[0]["permissions"] as JArray;
                return permissions?.Any(p => p.ToString() == RequiredPermission) ?? false;
            }
            catch
            {
                return null;
            }
        }

        private async Task SignOutSdkAsync()
        {
            try { await _client.Auth.SignOut(); } catch { }
        }

        private async Task PersistSessionAsync(Session session)
        {
            try
            {
                Directory.CreateDirectory(AppDataDir);
                var saved = new SavedSession
                {
                    AccessToken = session.AccessToken ?? "",
                    RefreshToken = session.RefreshToken ?? "",
                    LoginTimestampUtc = _loginTimestampUtc.ToString("o")
                };
                await File.WriteAllTextAsync(TokenFile, JsonConvert.SerializeObject(saved));
            }
            catch { }
        }

        private void SaveCredentials(string email, string password)
        {
            try
            {
                Directory.CreateDirectory(AppDataDir);
                var json = JsonConvert.SerializeObject(new SavedCredentials { Email = email, Password = password });
                var encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(CredentialsFile, encrypted);
            }
            catch { }
        }

        private static void ClearCredentials()
        {
            try { if (File.Exists(CredentialsFile)) File.Delete(CredentialsFile); } catch { }
        }

        private static void ClearSavedSession()
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
