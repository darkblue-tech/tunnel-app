using System.Threading.Tasks;

namespace Client.Core.Services;

public class FallbackSecretStorageProvider : ISecretStorageProvider
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _memoryStore = new();

    public Task SaveSecretAsync(string key, string secret)
    {
        _memoryStore[key] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecretAsync(string key)
    {
        _memoryStore.TryGetValue(key, out var val);
        return Task.FromResult(val);
    }

    public Task ClearSecretAsync(string key)
    {
        _memoryStore.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
