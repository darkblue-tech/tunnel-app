using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

#pragma warning disable CA1416

namespace Client.Core.Services;

/// <summary>
/// Provides secure secret storage on Windows using the Data Protection API (DPAPI).
/// </summary>
public class WindowsSecretStorageProvider : ISecretStorageProvider
{
    private readonly string _storageDir;

    public WindowsSecretStorageProvider()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var orgDir = Path.Combine(appData, "darkblue.tech");
        _storageDir = Path.Combine(orgDir, "Tunnel");
        Directory.CreateDirectory(_storageDir);
    }

    private string GetStoragePath(string key) => Path.Combine(_storageDir, $"secret_{key}.dat");

    public async Task SaveSecretAsync(string key, string secret)
    {
        var payload = Encoding.UTF8.GetBytes($"{key}:{secret}");
        var encrypted = ProtectedData.Protect(payload, null, DataProtectionScope.CurrentUser);
        var path = GetStoragePath(key);
        await File.WriteAllBytesAsync(path, encrypted);
    }

    public async Task<string?> GetSecretAsync(string key)
    {
        var path = GetStoragePath(key);
        if (!File.Exists(path))
        {
            return null;
        }

        var encrypted = await File.ReadAllBytesAsync(path);
        
        try
        {
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var content = Encoding.UTF8.GetString(decrypted);
            var parts = content.Split(':', 2);
            
            if (parts.Length == 2 && parts[0] == key)
            {
                return parts[1];
            }
        }
        catch (CryptographicException)
        {
            // Decryption failed
        }

        return null;
    }

    public Task ClearSecretAsync(string key)
    {
        var path = GetStoragePath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }
}
