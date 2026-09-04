using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Client.Core.Services;

public class MacOsSecretStorageProvider : ISecretStorageProvider
{
    private const string ServiceName = "DarkTunnel";
    private const string SecurityLibrary = "/System/Library/Frameworks/Security.framework/Security";

    [DllImport(SecurityLibrary)]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        string serviceName,
        uint accountNameLength,
        string accountName,
        uint passwordLength,
        byte[] passwordData,
        out IntPtr itemRef);

    [DllImport(SecurityLibrary)]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray,
        uint serviceNameLength,
        string serviceName,
        uint accountNameLength,
        string accountName,
        out uint passwordLength,
        out IntPtr passwordData,
        out IntPtr itemRef);

    [DllImport(SecurityLibrary)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(SecurityLibrary)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(SecurityLibrary)]
    private static extern int SecKeychainItemModifyAttributesAndData(
        IntPtr itemRef,
        IntPtr attrList,
        uint length,
        byte[] data);

    private const int errSecSuccess = 0;
    private const int errSecItemNotFound = -25300;
    private const int errSecDuplicateItem = -25299;

    public Task SaveSecretAsync(string key, string secret)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(secret);

        var status = SecKeychainAddGenericPassword(
            IntPtr.Zero,
            (uint)ServiceName.Length,
            ServiceName,
            (uint)key.Length,
            key,
            (uint)passwordBytes.Length,
            passwordBytes,
            out var itemRef);

        if (status == errSecDuplicateItem)
        {
            // Find and update
            status = SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)ServiceName.Length,
                ServiceName,
                (uint)key.Length,
                key,
                out var _,
                out var _,
                out itemRef);

            if (status == errSecSuccess && itemRef != IntPtr.Zero)
            {
                status = SecKeychainItemModifyAttributesAndData(itemRef, IntPtr.Zero, (uint)passwordBytes.Length, passwordBytes);
            }
        }

        if (status != errSecSuccess)
        {
            throw new Exception($"Failed to save secret to Keychain, OSStatus: {status}");
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetSecretAsync(string key)
    {
        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)ServiceName.Length,
            ServiceName,
            (uint)key.Length,
            key,
            out var length,
            out var dataPtr,
            out var itemRef);

        if (status == errSecItemNotFound)
        {
            return Task.FromResult<string?>(null);
        }

        if (status != errSecSuccess)
        {
            throw new Exception($"Failed to get secret from Keychain, OSStatus: {status}");
        }

        var passwordBytes = new byte[length];
        Marshal.Copy(dataPtr, passwordBytes, 0, (int)length);
        
        SecKeychainItemFreeContent(IntPtr.Zero, dataPtr);

        var result = Encoding.UTF8.GetString(passwordBytes);
        return Task.FromResult<string?>(result);
    }

    public Task ClearSecretAsync(string key)
    {
        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)ServiceName.Length,
            ServiceName,
            (uint)key.Length,
            key,
            out var _,
            out var dataPtr,
            out var itemRef);

        if (status == errSecSuccess)
        {
            if (dataPtr != IntPtr.Zero)
            {
                SecKeychainItemFreeContent(IntPtr.Zero, dataPtr);
            }
            if (itemRef != IntPtr.Zero)
            {
                SecKeychainItemDelete(itemRef);
            }
        }

        return Task.CompletedTask;
    }
}
