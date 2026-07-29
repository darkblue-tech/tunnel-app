using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Client.Desktop.Services;

public class MacOsSecretStorageProvider : ISecretStorageProvider
{
    private const string ServiceName = "DarkTunnel";

    public async Task SaveSecretAsync(string key, string secret)
    {
        // First delete if exists to avoid interactive prompt, or use -U to update
        await RunCommandAsync("security", "delete-generic-password", "-a", key, "-s", ServiceName);
        
        var result = await RunCommandAsync("security", "add-generic-password", "-a", key, "-s", ServiceName, "-w", secret, "-U");
        if (result.ExitCode != 0)
        {
            throw new Exception($"Failed to save secret to Keychain: {result.Error}");
        }
    }

    public async Task<string?> GetSecretAsync(string key)
    {
        var result = await RunCommandAsync("security", "find-generic-password", "-a", key, "-s", ServiceName, "-w");
        if (result.ExitCode != 0)
        {
            // Usually exit code 44 means item not found in keychain
            return null;
        }
        return result.Output.Trim('\n', '\r');
    }

    public async Task ClearSecretAsync(string key)
    {
        await RunCommandAsync("security", "delete-generic-password", "-a", key, "-s", ServiceName);
    }

    private async Task<(int ExitCode, string Output, string Error)> RunCommandAsync(string fileName, params string[] args)
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;

        return (process.ExitCode, output, error);
    }
}
