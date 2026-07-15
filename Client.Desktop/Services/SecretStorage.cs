using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Client.Desktop.Services;

public class SecretStorage
{
    private readonly string _storagePath;

    public SecretStorage()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var orgDir = Path.Combine(appData, "darkblue.tech");
        var darkTunnelDir = Path.Combine(orgDir, "Tunnel");
        Directory.CreateDirectory(darkTunnelDir);
        _storagePath = Path.Combine(darkTunnelDir, "secrets.dat");
    }

    public async Task SaveSecretAsync(string key, string secret)
    {
        var payload = Encoding.UTF8.GetBytes($"{key}:{secret}");
        
        byte[] encrypted;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Use DPAPI on Windows
            encrypted = ProtectedData.Protect(payload, null, DataProtectionScope.CurrentUser);
        }
        else
        {
            // On Linux/macOS, .NET's ProtectedData might throw PlatformNotSupportedException 
            // if not configured properly, but in .NET 8/10 it often uses a local key file.
            // For MVP, we will try to use it, and fallback to base64 if it fails.
            try
            {
                encrypted = ProtectedData.Protect(payload, null, DataProtectionScope.CurrentUser);
            }
            catch (PlatformNotSupportedException)
            {
                encrypted = payload; // Warning: Fallback for MVP on non-Windows
            }
        }

        await File.WriteAllBytesAsync(_storagePath, encrypted);
    }

    public async Task<string?> GetSecretAsync(string key)
    {
        if (!File.Exists(_storagePath))
        {
            return null;
        }

        var encrypted = await File.ReadAllBytesAsync(_storagePath);
        byte[] decrypted;
        
        try
        {
            decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        }
        catch
        {
            // Fallback for non-Windows MVP
            decrypted = encrypted;
        }

        var content = Encoding.UTF8.GetString(decrypted);
        var parts = content.Split(':', 2);
        
        if (parts.Length == 2 && parts[0] == key)
        {
            return parts[1];
        }

        return null;
    }
}
