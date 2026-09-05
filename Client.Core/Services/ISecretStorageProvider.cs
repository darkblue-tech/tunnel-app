using System.Threading.Tasks;

namespace Client.Core.Services;

/// <summary>
/// Provides an interface for platform-specific secure secret storage (e.g., tokens, credentials).
/// </summary>
public interface ISecretStorageProvider
{
    /// <summary>
    /// Saves a secret string securely.
    /// </summary>
    /// <param name="key">The identifier for the secret.</param>
    /// <param name="secret">The secret value to store.</param>
    Task SaveSecretAsync(string key, string secret);

    /// <summary>
    /// Retrieves a previously stored secret.
    /// </summary>
    /// <param name="key">The identifier for the secret.</param>
    /// <returns>The stored secret, or null if not found.</returns>
    Task<string?> GetSecretAsync(string key);

    /// <summary>
    /// Removes a secret from secure storage.
    /// </summary>
    /// <param name="key">The identifier for the secret.</param>
    Task ClearSecretAsync(string key);
}
