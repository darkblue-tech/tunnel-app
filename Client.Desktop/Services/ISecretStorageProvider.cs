using System.Threading.Tasks;

namespace Client.Desktop.Services;

public interface ISecretStorageProvider
{
    Task SaveSecretAsync(string key, string secret);
    Task<string?> GetSecretAsync(string key);
    Task ClearSecretAsync(string key);
}
