using System;
using System.Runtime.InteropServices;

namespace Client.Desktop.Services;

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
            return new LinuxSecretStorageProvider();
        }
        
        throw new PlatformNotSupportedException("This OS platform is not supported for secure secret storage.");
    });

    public static ISecretStorageProvider GetProvider() => _provider.Value;
}
