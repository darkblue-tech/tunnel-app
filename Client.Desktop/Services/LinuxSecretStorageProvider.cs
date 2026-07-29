using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Client.Desktop.Services;

public class LinuxSecretStorageProvider : ISecretStorageProvider
{
    private const string ServiceName = "DarkTunnel";

    public async Task SaveSecretAsync(string key, string secret)
    {
        var result = await RunCommandWithInputAsync("secret-tool", secret, "store", "--label=DarkTunnel", "service", ServiceName, "account", key);
        if (result.ExitCode != 0)
        {
            throw new Exception($"Failed to save secret using secret-tool: {result.Error}. Ensure libsecret-tools is installed.");
        }
    }

    public async Task<string?> GetSecretAsync(string key)
    {
        var result = await RunCommandAsync("secret-tool", "lookup", "service", ServiceName, "account", key);
        if (result.ExitCode != 0 || string.IsNullOrEmpty(result.Output))
        {
            return null;
        }
        return result.Output;
    }

    public async Task ClearSecretAsync(string key)
    {
        var result = await RunCommandAsync("secret-tool", "clear", "service", ServiceName, "account", key);
        if (result.ExitCode != 0)
        {
            // Ignore clear errors (e.g., if it didn't exist)
        }
    }

    private async Task<(int ExitCode, string Output, string Error)> RunCommandAsync(string fileName, params string[] args)
    {
        return await RunCommandWithInputAsync(fileName, null, args);
    }

    private async Task<(int ExitCode, string Output, string Error)> RunCommandWithInputAsync(string fileName, string? standardInput, params string[] args)
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
        process.StartInfo.RedirectStandardInput = standardInput != null;
        process.StartInfo.CreateNoWindow = true;

        try
        {
            process.Start();

            if (standardInput != null)
            {
                await process.StandardInput.WriteAsync(standardInput);
                process.StandardInput.Close();
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var output = await outputTask;
            var error = await errorTask;

            return (process.ExitCode, output, error);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new Exception($"Command '{fileName}' not found. Please ensure libsecret-tools is installed on your Linux distribution.");
        }
    }
}
