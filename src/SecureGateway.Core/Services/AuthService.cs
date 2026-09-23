using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SecureGateway.Platform;
using Supabase;
using Supabase.Gotrue;
using Supabase.Gotrue.Exceptions;

namespace SecureGateway.Services
{
    public class AuthService
    {
        private readonly ICredentialStore _credentials;

        public AuthService(ICredentialStore credentialStore)
        {
            _credentials = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        }

        private const string SupabaseUrl = "https://yahzzatmmmdmwalindai.supabase.co";
        private const string SupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InlhaHp6YXRtbW1kbXdhbGluZGFpIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzE3ODA1NzMsImV4cCI6MjA4NzM1NjU3M30._OKdLLUDN80GR7nMHYsI3S0WmzPmGw-7QmpZYEIREj4";
        private const string RequiredPermission = "gateway.access";
        private const int SessionMaxDays = 120;

        /// <summary>
        /// Where "Forgot password?" sends the user. Non-empty: the login screen opens this page
        /// in the browser (the website runs the reset). Empty: the in-app reset panel is used
        /// (RequestPasswordResetAsync / ResetPasswordWithCodeAsync).
        /// </summary>
        public const string PasswordResetUrl = "https://fourthzodiac.com/forgot-password";

        public static bool UsesWebPasswordReset => !string.IsNullOrEmpty(PasswordResetUrl);

        /// <summary>Opens the website reset page in the default browser. Returns false if that failed.</summary>
        public static bool OpenPasswordResetPage()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PasswordResetUrl)
                {
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static readonly string AppDataDir = AppPaths.DataDir;
        private static readonly string TokenFile = Path.Combine(AppDataDir, "auth_session.json");

        private Supabase.Client _client;
        private DateTime _loginTimestampUtc = DateTime.UtcNow;

        /// <summary>
        /// Saved tokens that could not be verified at startup because the login server was
        /// unreachable (typical on restricted networks). The app may start with them; the
        /// session is verified via EnsureSessionAsync once a tunnel is up.
        /// </summary>
        private SavedSession _offlineSession;

        public bool IsAuthenticated => _client?.Auth?.CurrentSession != null;
        public bool HasOfflineSession => !IsAuthenticated && _offlineSession != null;
        public string UserEmail => _client?.Auth?.CurrentUser?.Email ?? _offlineSession?.Email ?? "";
        public string UserId => _client?.Auth?.CurrentUser?.Id ?? "";
        public string AccessToken => _client?.Auth?.CurrentSession?.AccessToken ?? "";

        public SharedServerService CreateSharedServerService()
        {
            return new SharedServerService(SupabaseUrl, SupabaseAnonKey, () => AccessToken);
        }

        public async Task InitializeAsync()
        {
            await CreateClientAsync();
            await TryRestoreSessionAsync();
        }

        /// <summary>
        /// Rebuilds the SDK client so it picks up the current HTTP proxy (see HttpClients).
        /// An established session is carried over; an offline session is re-verified.
        /// </summary>
        public async Task ReinitializeClientAsync()
        {
            var current = _client?.Auth?.CurrentSession;
            await CreateClientAsync();

            if (current != null && !string.IsNullOrEmpty(current.RefreshToken))
            {
                try { await _client.Auth.SetSession(current.AccessToken, current.RefreshToken); }
                catch { _offlineSession = new SavedSession { AccessToken = current.AccessToken, RefreshToken = current.RefreshToken, Email = current.User?.Email ?? "", LoginTimestampUtc = _loginTimestampUtc.ToString("o") }; }
            }
            else if (_offlineSession != null)
            {
                await EnsureSessionAsync();
            }
        }

        private async Task CreateClientAsync()
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
        }

        /// <summary>Is the login server reachable right now (through whatever proxy HttpClients uses)?</summary>
        public async Task<bool> PingAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{SupabaseUrl}/auth/v1/health");
                request.Headers.Add("apikey", SupabaseAnonKey);
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var response = await HttpClients.Shared.SendAsync(request, cts.Token);
                return (int)response.StatusCode < 500;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Turns an offline session into a verified one (call after a tunnel is up).
        /// Returns true if the user is now authenticated. Clears the saved session if the
        /// server rejected it, so the caller should send the user back to the login screen
        /// when this returns false and HasOfflineSession is also false.
        /// </summary>
        public async Task<bool> EnsureSessionAsync()
        {
            if (IsAuthenticated) return true;
            if (_offlineSession == null) return false;

            Session session;
            try
            {
                session = await _client.Auth.SetSession(_offlineSession.AccessToken, _offlineSession.RefreshToken);
            }
            catch (GotrueException)
            {
                _offlineSession = null;
                ClearSavedSession();
                return false;
            }
            catch
            {
                return false; // still unreachable; stay offline
            }

            if (session?.User == null)
            {
                _offlineSession = null;
                ClearSavedSession();
                return false;
            }

            _offlineSession = null;
            await PersistSessionAsync(session);

            var access = await CheckGatewayAccessAsync(session.User.Id);
            if (access == false)
            {
                await SignOutSdkAsync();
                ClearSavedSession();
                return false;
            }
            return true;
        }

        /// <summary>Network-level failures (blocked, reset, timed out) as opposed to auth rejections.</summary>
        public static bool IsNetworkError(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is HttpRequestException || e is System.Net.Sockets.SocketException
                    || e is System.Security.Authentication.AuthenticationException
                    || e is TaskCanceledException || e is System.IO.IOException)
                    return true;
            }
            return false;
        }

        private static AuthResult ConnectionFailure(Exception ex)
        {
            if (!IsNetworkError(ex))
                return AuthResult.Failure($"Connection error: {ex.Message}");

            var inner = ex; while (inner.InnerException != null) inner = inner.InnerException;
            return AuthResult.NetworkFailure(
                "Cannot reach the login server (" + inner.Message.TrimEnd('.') + "). " +
                "If you are on a restricted network, connect through your VPN server first using the option below.");
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

                _offlineSession = null;
                return AuthResult.Success(session.User.Email ?? email);
            }
            catch (Exception ex)
            {
                return ConnectionFailure(ex);
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
                return ConnectionFailure(ex);
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
                return ConnectionFailure(ex);
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
                // Accepts: the short code ({{ .Token }} in the e-mail template), the e-mail's
                // verify link or its token hash, or — if the user already clicked the link —
                // the page it redirected to, whose #fragment carries a recovery access token.
                var (kind, value) = ParseRecoveryInput(code);
                string accessToken;

                if (kind == RecoveryInputKind.AccessToken)
                {
                    accessToken = value;
                }
                else
                {
                    object payload = kind == RecoveryInputKind.TokenHash
                        ? new { type = "recovery", token_hash = value }
                        : new { type = "recovery", email, token = value };

                    string error;
                    (accessToken, _, error) = await AuthViaRestAsync($"{SupabaseUrl}/auth/v1/verify", payload);

                    if (error != null)
                        return AuthResult.Failure(error.Contains("expired", StringComparison.OrdinalIgnoreCase)
                            || error.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                            ? "That reset code or link is invalid, expired, or already used. Request a new one."
                            : error);

                    if (string.IsNullOrEmpty(accessToken))
                        return AuthResult.Failure("Could not verify the reset code. Request a new one.");
                }

                using var request = new HttpRequestMessage(HttpMethod.Put, $"{SupabaseUrl}/auth/v1/user");
                request.Headers.Add("apikey", SupabaseAnonKey);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(new { password = newPassword }), Encoding.UTF8, "application/json");

                using var response = await HttpClients.Shared.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    string msg = null;
                    try { var err = JObject.Parse(body); msg = err["msg"]?.ToString() ?? err["error_description"]?.ToString(); }
                    catch { }

                    if ((int)response.StatusCode == 401 && kind == RecoveryInputKind.AccessToken)
                        msg = "That reset link has expired (they last one hour). Request a new one.";

                    return AuthResult.Failure(msg ?? "Could not update the password. Please try again.");
                }

                // Invalidate the recovery session so only the new password works from here on.
                using var logout = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseUrl}/auth/v1/logout");
                logout.Headers.Add("apikey", SupabaseAnonKey);
                logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                try { using var _ = await HttpClients.Shared.SendAsync(logout); } catch { }

                ClearCredentials();
                return AuthResult.SuccessWithMessage("Password updated. Sign in with your new password.");
            }
            catch (Exception ex)
            {
                return ConnectionFailure(ex);
            }
        }

        public SavedCredentials LoadCredentials()
        {
            try
            {
                var stored = _credentials.Load();
                if (stored == null || string.IsNullOrEmpty(stored.Value.email)) return null;
                return new SavedCredentials { Email = stored.Value.email, Password = stored.Value.password ?? "" };
            }
            catch
            {
                ClearCredentials();
                return null;
            }
        }

        internal enum RecoveryInputKind { Code, TokenHash, AccessToken }

        /// <summary>
        /// Classifies what the user pasted into the reset-code field:
        ///  - the redirect page a clicked link landed on (#access_token=…&amp;type=recovery) → AccessToken
        ///  - the unclicked verify link (?token=… / ?token_hash=…) or a bare 40+ hex hash → TokenHash
        ///  - anything else → Code
        /// </summary>
        internal static (RecoveryInputKind kind, string value) ParseRecoveryInput(string input)
        {
            var s = (input ?? "").Trim();

            if (Uri.TryCreate(s, UriKind.Absolute, out var uri))
            {
                var fragment = ParseParams(uri.Fragment.TrimStart('#'));
                if (fragment.TryGetValue("access_token", out var at) && !string.IsNullOrEmpty(at))
                    return (RecoveryInputKind.AccessToken, at);

                var query = ParseParams(uri.Query.TrimStart('?'));
                if (query.TryGetValue("token_hash", out var th) && !string.IsNullOrEmpty(th))
                    return (RecoveryInputKind.TokenHash, th);
                if (query.TryGetValue("token", out var t) && !string.IsNullOrEmpty(t))
                    return (RecoveryInputKind.TokenHash, t);
            }

            bool looksLikeHash = s.Length >= 40 && s.All(c => Uri.IsHexDigit(c));
            return looksLikeHash ? (RecoveryInputKind.TokenHash, s) : (RecoveryInputKind.Code, s);
        }

        private static System.Collections.Generic.Dictionary<string, string> ParseParams(string s)
        {
            var d = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var pair in s.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                d[pair[..eq]] = Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
            return d;
        }

        private async Task<(string accessToken, string refreshToken, string error)> AuthViaRestAsync(
            string url, object payload)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("apikey", SupabaseAnonKey);
            request.Content = new StringContent(
                JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            using var response = await HttpClients.Shared.SendAsync(request);
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
                // Login server unreachable (restricted network / offline). Permission was verified
                // at login and the 120-day cap still holds, so let the app start with this session;
                // it is verified through the tunnel later (EnsureSessionAsync).
                _offlineSession = saved;
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

                using var response = await HttpClients.Shared.SendAsync(request);
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
                    Email = session.User?.Email ?? "",
                    LoginTimestampUtc = _loginTimestampUtc.ToString("o")
                };
                await File.WriteAllTextAsync(TokenFile, JsonConvert.SerializeObject(saved));
            }
            catch { }
        }

        private void SaveCredentials(string email, string password)
        {
            try { _credentials.Save(email, password); } catch { }
        }

        private void ClearCredentials()
        {
            try { _credentials.Clear(); } catch { }
        }

        private static void ClearSavedSession()
        {
            try { if (File.Exists(TokenFile)) File.Delete(TokenFile); } catch { }
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
        /// <summary>The login server could not be reached; not a credential problem and must not count as an attempt.</summary>
        public bool IsNetworkError { get; set; }

        public static AuthResult Success(string email) =>
            new() { IsSuccess = true, Email = email };

        public static AuthResult SuccessWithMessage(string message) =>
            new() { IsSuccess = true, Message = message, RequiresConfirmation = true };

        public static AuthResult Failure(string message) =>
            new() { IsSuccess = false, Message = message };

        public static AuthResult NetworkFailure(string message) =>
            new() { IsSuccess = false, Message = message, IsNetworkError = true };
    }
}
