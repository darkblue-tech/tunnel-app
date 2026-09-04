using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Client.Core.Services;

/// <summary>
/// Provides secure secret storage using AES-256-GCM encrypted files protected with
/// user and machine entropy, restricted Unix permissions, and memory caching.
/// Used on platforms or environments where native credential managers are unavailable.
/// </summary>
public class FallbackSecretStorageProvider : ISecretStorageProvider
{
    private readonly string _storageDir;
    private readonly byte[] _encryptionKey;
    private readonly ConcurrentDictionary<string, string> _memoryStore = new();

    public FallbackSecretStorageProvider(string? customStorageDir = null)
    {
        if (!string.IsNullOrEmpty(customStorageDir))
        {
            _storageDir = customStorageDir;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
            {
                appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            }
            _storageDir = Path.Combine(appData, "darkblue.tech", "Tunnel", "secrets");
        }

        Directory.CreateDirectory(_storageDir);
        SetRestrictedPermissions(_storageDir, isDirectory: true);
        _encryptionKey = DeriveKey();
    }

    private string GetFilePath(string key)
    {
        // Sanitize key for valid filename
        var safeKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(_storageDir, $"sec_{safeKey}.dat");
    }

    public async Task SaveSecretAsync(string key, string secret)
    {
        _memoryStore[key] = secret;

        var path = GetFilePath(key);
        var payload = Encoding.UTF8.GetBytes($"{key}:{secret}");

        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var tag = new byte[16];
        var ciphertext = new byte[payload.Length];

        using (var aesGcm = new AesGcm(_encryptionKey, 16))
        {
            aesGcm.Encrypt(nonce, payload, ciphertext, tag);
        }

        using (var ms = new MemoryStream(nonce.Length + tag.Length + ciphertext.Length))
        {
            await ms.WriteAsync(nonce);
            await ms.WriteAsync(tag);
            await ms.WriteAsync(ciphertext);
            await File.WriteAllBytesAsync(path, ms.ToArray());
        }

        SetRestrictedPermissions(path, isDirectory: false);
    }

    public async Task<string?> GetSecretAsync(string key)
    {
        if (_memoryStore.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var path = GetFilePath(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var data = await File.ReadAllBytesAsync(path);
            if (data.Length < 28) // 12 nonce + 16 tag + at least 0 ciphertext
            {
                return null;
            }

            var nonce = data.AsSpan(0, 12);
            var tag = data.AsSpan(12, 16);
            var ciphertext = data.AsSpan(28);
            var plaintext = new byte[ciphertext.Length];

            using (var aesGcm = new AesGcm(_encryptionKey, 16))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            var content = Encoding.UTF8.GetString(plaintext);
            var parts = content.Split(':', 2);
            if (parts.Length == 2 && parts[0] == key)
            {
                _memoryStore[key] = parts[1];
                return parts[1];
            }
        }
        catch (CryptographicException)
        {
            // Decryption failed or file tampered with
        }
        catch (Exception)
        {
            // File I/O or other error
        }

        return null;
    }

    public Task ClearSecretAsync(string key)
    {
        _memoryStore.TryRemove(key, out _);

        var path = GetFilePath(key);
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch { }
        }

        return Task.CompletedTask;
    }

    private static byte[] DeriveKey()
    {
        var entropy = $"{Environment.MachineName}:{Environment.UserName}:DarkTunnelSecureStorage";
        if (File.Exists("/etc/machine-id"))
        {
            try { entropy += ":" + File.ReadAllText("/etc/machine-id").Trim(); } catch { }
        }
        else if (File.Exists("/var/lib/dbus/machine-id"))
        {
            try { entropy += ":" + File.ReadAllText("/var/lib/dbus/machine-id").Trim(); } catch { }
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(entropy));
    }

    private static void SetRestrictedPermissions(string path, bool isDirectory)
    {
        if (OperatingSystem.IsWindows()) return;

        try
        {
            var mode = isDirectory
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(path, mode);
        }
        catch { }
    }
}
