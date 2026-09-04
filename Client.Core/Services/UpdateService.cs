using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
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

    /// <summary>
    /// Gets the current running application version.
    /// </summary>
    public static string CurrentVersion
    {
        get
        {
            try
            {
                var entryVer = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
                if (entryVer != null && entryVer != new Version(0, 0, 0, 0))
                {
                    return entryVer.ToString(3);
                }

                if (File.Exists("appsettings.json"))
                {
                    var json = File.ReadAllText("appsettings.json");
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("AppInfo", out var appInfo) &&
                        appInfo.TryGetProperty("FullVersionDisplay", out var verProp))
                    {
                        var vStr = verProp.GetString()?.Trim().TrimStart('v', '.').TrimEnd('r');
                        if (!string.IsNullOrEmpty(vStr)) return vStr;
                    }
                }
            }
            catch { }

            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.2";
        }
    }

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
            var checkUri = ResolveAbsoluteUrl($"version/check?current={CurrentVersion}&platform={rid}");
            var response = await _httpClient.GetAsync(checkUri);
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
            var rawUrl = !string.IsNullOrEmpty(updateInfo.WebSetupUrl) ? updateInfo.WebSetupUrl : updateInfo.DownloadUrl;
            var downloadUri = ResolveAbsoluteUrl(rawUrl);
            var targetFile = Path.Combine(Path.GetTempPath(), Path.GetFileName(downloadUri.LocalPath));

            using (var downloadStream = await _httpClient.GetStreamAsync(downloadUri))
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
                        Arguments = "/S", // Silent install flag for NSIS
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
                FileName = downloadUri.ToString(),
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

    private Uri ResolveAbsoluteUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absUri))
        {
            return absUri;
        }

        var baseUri = new Uri(_baseUrl.EndsWith("/") ? _baseUrl : _baseUrl + "/");
        var relative = url.StartsWith("/") ? url.Substring(1) : url;
        return new Uri(baseUri, relative);
    }

    public static string GetCurrentPlatformRID()
    {
        var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return $"win-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return $"linux-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return $"osx-{arch}";
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
