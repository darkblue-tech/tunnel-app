using System;
using System.Runtime.InteropServices;

namespace Client.Core.Services;

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

    public static ISecretStorageProvider GetProvider() => _provider.Value;
}
