namespace SecureGateway.Platform
{
    /// <summary>
    /// Stores the "Remember me" e-mail + password using the platform's secret store
    /// (Windows: DPAPI-encrypted file; macOS: Keychain). Implementations must not throw.
    /// </summary>
    public interface ICredentialStore
    {
        void Save(string email, string password);

        /// <summary>Returns null when nothing is stored or the data can't be read.</summary>
        (string email, string password)? Load();

        void Clear();
    }
}
