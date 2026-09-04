using System;
using System.Runtime.InteropServices;

namespace Client.Core.Services;

/// <summary>
/// Factory for resolving and instantiating the correct secure storage provider 
/// based on the current operating system (Windows, macOS, Linux).
/// </summary>
public static class SecretStorageFactory
{
    private static readonly Lazy<ISecretStorageProvider> _provider = new(() =>
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsSecretStorageProvider();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsSecretStorageProvider();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return IsSecretToolAvailable() ? new LinuxSecretStorageProvider() : new FallbackSecretStorageProvider();
        }
        
        return new FallbackSecretStorageProvider();
    });

    private static bool IsSecretToolAvailable()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "secret-tool",
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the singleton instance of the appropriate secret storage provider.
    /// </summary>
    /// <returns>An implementation of ISecretStorageProvider.</returns>
    public static ISecretStorageProvider GetProvider() => _provider.Value;
}
