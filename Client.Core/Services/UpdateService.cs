using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
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
                // 1. Check InformationalVersion from entry assembly
                var infoVer = System.Reflection.Assembly.GetEntryAssembly()?
                    .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;
                if (!string.IsNullOrEmpty(infoVer))
                {
                    var plusIndex = infoVer.IndexOf('+');
                    var cleanVer = (plusIndex >= 0 ? infoVer[..plusIndex] : infoVer).Trim().TrimStart('v', '.');
                    if (!string.IsNullOrEmpty(cleanVer)) return cleanVer;
                }

                // 2. Check appsettings.json
                var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (!File.Exists(configPath) && File.Exists("appsettings.json")) configPath = "appsettings.json";
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("AppInfo", out var appInfo) &&
                        appInfo.TryGetProperty("FullVersionDisplay", out var verProp))
                    {
                        var vStr = verProp.GetString()?.Trim().TrimStart('v', '.');
                        if (!string.IsNullOrEmpty(vStr)) return vStr;
                    }
                }

                // 3. Fallback to entry assembly version
                var entryVer = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
                if (entryVer != null && entryVer != new Version(0, 0, 0, 0))
                {
                    return entryVer.ToString(3);
                }
            }
            catch { }

            return "1.0.2r";
        }
    }

    public UpdateService()
    {
        _baseUrl = Environment.GetEnvironmentVariable("TUNNEL_API_URL") ?? "https://tunnel.darkblue.tech/api";
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Checks whether the latest version string is strictly newer than current version string.
    /// Normalizes release suffixes like 'r' so identical versions (e.g. 1.0.2 and 1.0.2r) don't trigger updates.
    /// </summary>
    public static bool IsVersionNewer(string latest, string current)
    {
        if (string.IsNullOrWhiteSpace(latest) || string.IsNullOrWhiteSpace(current))
            return false;

        var l = latest.Trim().TrimStart('v', '.');
        var c = current.Trim().TrimStart('v', '.');

        if (string.Equals(l, c, StringComparison.OrdinalIgnoreCase))
            return false;

        var cleanL = l.TrimEnd('r', 'R');
        var cleanC = c.TrimEnd('r', 'R');

        if (string.Equals(cleanL, cleanC, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Version.TryParse(cleanL, out var vl) && Version.TryParse(cleanC, out var vc))
        {
            return vl > vc;
        }

        return string.Compare(l, c, StringComparison.OrdinalIgnoreCase) > 0;
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
            if (result != null && result.HasUpdate)
            {
                // Verify client-side that the reported latest version is actually newer
                if (!IsVersionNewer(result.LatestVersion, CurrentVersion))
                {
                    result.HasUpdate = false;
                }
            }

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

        var rawUrl = !string.IsNullOrEmpty(updateInfo.WebSetupUrl) ? updateInfo.WebSetupUrl : updateInfo.DownloadUrl;
        var downloadUri = ResolveAbsoluteUrl(rawUrl);

        try
        {
            var targetFile = Path.Combine(Path.GetTempPath(), Path.GetFileName(downloadUri.LocalPath));

            using (var downloadStream = await _httpClient.GetStreamAsync(downloadUri))
            using (var fileStream = File.Create(targetFile))
            {
                await downloadStream.CopyToAsync(fileStream);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (targetFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = targetFile,
                        UseShellExecute = true
                    });
                    Environment.Exit(0);
                    return true;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (targetFile.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.SetUnixFileMode(targetFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = targetFile,
                            UseShellExecute = false
                        });
                        Environment.Exit(0);
                        return true;
                    }
                    catch { }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (targetFile.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(new ProcessStartInfo("open", targetFile));
                    return true;
                }
            }

            OpenUrl(downloadUri.ToString());
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to apply update directly: {ex.Message}");
            try
            {
                // Fallback: open browser download page
                OpenUrl(downloadUri.ToString());
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url.Replace("&", "^&")}") { CreateNoWindow = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start(new ProcessStartInfo("xdg-open", url));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo("open", url));
            }
            else
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }
        catch
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
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
