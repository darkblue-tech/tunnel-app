using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Client.Core.Services;

/// <summary>
/// Service responsible for checking and applying application updates.
/// </summary>
public class UpdateService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    public static string CurrentVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.1";

    public UpdateService()
    {
        _baseUrl = Environment.GetEnvironmentVariable("TUNNEL_API_URL") ?? "https://tunnel.darkblue.tech/api";
        _httpClient = new HttpClient();
    }

    public async Task<UpdateCheckResult?> CheckForUpdatesAsync()
    {
        try
        {
            var rid = GetCurrentPlatformRID();
            var response = await _httpClient.GetAsync($"{_baseUrl}/version/check?current={CurrentVersion}&platform={rid}");
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<UpdateCheckResult>();
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Update check failed: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> ApplyUpdateAsync(UpdateCheckResult updateInfo)
    {
        if (string.IsNullOrEmpty(updateInfo.WebSetupUrl) && string.IsNullOrEmpty(updateInfo.DownloadUrl))
        {
            return false;
        }

        try
        {
            var downloadUrl = !string.IsNullOrEmpty(updateInfo.WebSetupUrl) ? updateInfo.WebSetupUrl : updateInfo.DownloadUrl;
            var targetFile = Path.Combine(Path.GetTempPath(), Path.GetFileName(new Uri(downloadUrl).AbsolutePath));

            using (var downloadStream = await _httpClient.GetStreamAsync(downloadUrl))
            using (var fileStream = File.Create(targetFile))
            {
                await downloadStream.CopyToAsync(fileStream);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Execute silent install if web installer / setup .exe
                if (targetFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = targetFile,
                        Arguments = "/S", // Silent install flag for NSIS / Inno Setup
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                    Environment.Exit(0);
                    return true;
                }
            }

            // Open download link for non-Windows platforms
            Process.Start(new ProcessStartInfo
            {
                FileName = downloadUrl,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to apply update: {ex.Message}");
            return false;
        }
    }

    public static string GetCurrentPlatformRID()
    {
        var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return $"win-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return $"linux-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return $"osx-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("FREEBSD"))) return $"freebsd-{arch}";
        return $"win-{arch}";
    }
}

/// <summary>
/// Represents the result of an update check.
/// </summary>
public class UpdateCheckResult
{
    [JsonPropertyName("hasUpdate")]
    public bool HasUpdate { get; set; }

    [JsonPropertyName("latestVersion")]
    public string LatestVersion { get; set; } = string.Empty;

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("webSetupUrl")]
    public string? WebSetupUrl { get; set; }

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = string.Empty;

    [JsonPropertyName("mandatory")]
    public bool Mandatory { get; set; }
}
