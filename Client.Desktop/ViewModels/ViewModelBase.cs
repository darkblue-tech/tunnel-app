using CommunityToolkit.Mvvm.ComponentModel;
using System.Reflection;

using Client.Core.Services;
using Client.Core.Models;
namespace Client.Desktop.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    private static readonly System.Lazy<string> _cachedVersionDisplay = new(ResolveVersionDisplay);

    [ObservableProperty]
    private string _appVersionDisplay = _cachedVersionDisplay.Value;

    protected ViewModelBase()
    {
    }

    private static string ResolveVersionDisplay()
    {
        string? version = null;
        string? codename = null;

        try
        {
            var configPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "appsettings.json");
            if (!System.IO.File.Exists(configPath) && System.IO.File.Exists("appsettings.json"))
            {
                configPath = "appsettings.json";
            }

            if (System.IO.File.Exists(configPath))
            {
                var json = System.IO.File.ReadAllText(configPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("AppInfo", out var appInfo))
                {
                    if (appInfo.TryGetProperty("FullVersionDisplay", out var vProp))
                        version = vProp.GetString();
                    if (appInfo.TryGetProperty("Codename", out var cProp))
                        codename = cProp.GetString();
                }
            }
        }
        catch { }

        if (string.IsNullOrWhiteSpace(version))
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetEntryAssembly() ?? typeof(ViewModelBase).Assembly;
                var infoVer = assembly
                    .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;

                if (!string.IsNullOrWhiteSpace(infoVer))
                {
                    var plusIndex = infoVer.IndexOf('+');
                    version = (plusIndex >= 0 ? infoVer[..plusIndex] : infoVer).Trim();
                    if (!version.StartsWith('v') && !version.StartsWith('V'))
                    {
                        version = "v" + version;
                    }
                }
                else
                {
                    var ver = assembly.GetName().Version;
                    if (ver != null && ver != new System.Version(0, 0, 0, 0))
                    {
                        version = $"v{ver.ToString(3)}";
                    }
                }
            }
            catch { }
        }

        version = string.IsNullOrWhiteSpace(version) ? "v1.0.3r" : version;
        codename = string.IsNullOrWhiteSpace(codename) ? "Fallen Angel" : codename;

        return $"{version} {codename}".Trim();
    }

    protected string GetString(string key)
    {
        if (Avalonia.Application.Current != null && 
            Avalonia.Application.Current.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var res) && 
            res is string str)
        {
            return str;
        }
        return key;
    }
}
