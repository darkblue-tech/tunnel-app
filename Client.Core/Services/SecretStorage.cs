using System.Threading.Tasks;

namespace Client.Core.Services;

/// <summary>
/// Facade for secret storage to maintain backward compatibility with existing usage.
/// Resolves the appropriate platform-specific provider at runtime.
/// </summary>
public class SecretStorage
{
    private readonly ISecretStorageProvider _provider;

    /// <summary>
    /// Initializes a new instance of the SecretStorage class using the platform-appropriate provider.
    /// </summary>
    public SecretStorage()
    {
        _provider = SecretStorageFactory.GetProvider();
    }

    /// <summary>
    /// Saves a secret string securely.
    /// </summary>
    public Task SaveSecretAsync(string key, string secret)
    {
        return _provider.SaveSecretAsync(key, secret);
    }

    /// <summary>
    /// Retrieves a previously stored secret.
    /// </summary>
    public Task<string?> GetSecretAsync(string key)
    {
        return _provider.GetSecretAsync(key);
    }

    /// <summary>
    /// Removes a secret from secure storage.
    /// </summary>
    public Task ClearSecretAsync(string key)
    {
        return _provider.ClearSecretAsync(key);
    }
}
