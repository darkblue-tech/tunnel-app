using System.Threading.Tasks;

namespace Client.Core.Services;

// Facade to maintain backward compatibility with existing usage
public class SecretStorage
{
    private readonly ISecretStorageProvider _provider;

    public SecretStorage()
    {
        _provider = SecretStorageFactory.GetProvider();
    }

    public Task SaveSecretAsync(string key, string secret)
    {
        return _provider.SaveSecretAsync(key, secret);
    }

    public Task<string?> GetSecretAsync(string key)
    {
        return _provider.GetSecretAsync(key);
    }

    public Task ClearSecretAsync(string key)
    {
        return _provider.ClearSecretAsync(key);
    }
}
