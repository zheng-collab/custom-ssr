using System;
using System.IO;
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

        private static readonly string TokenFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SecureGateway", "auth_session.json");

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

            // Try to restore a previous session
            await TryRestoreSessionAsync();
        }

        public async Task<AuthResult> SignInAsync(string email, string password)
        {
            try
            {
                var session = await _client.Auth.SignIn(email, password);

                if (session != null)
                {
                    await SaveSessionAsync(session);
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

                // Try to refresh the session
                var session = await _client.Auth.RefreshSession();

                if (session == null)
                {
                    // Refresh token expired, clear it
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
                var dir = Path.GetDirectoryName(TokenFile);
                if (dir != null) Directory.CreateDirectory(dir);

                var saved = new SavedSession
                {
                    AccessToken = session.AccessToken ?? "",
                    RefreshToken = session.RefreshToken ?? "",
                    Email = session.User?.Email ?? ""
                };

                var json = JsonConvert.SerializeObject(saved);
                await File.WriteAllTextAsync(TokenFile, json);
            }
            catch
            {
                // Best effort
            }
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
        }
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
