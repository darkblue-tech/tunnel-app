using CommunityToolkit.Mvvm.ComponentModel;

using Client.Core.Services;
using Client.Core.Models;
namespace Client.Desktop.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private string _appVersionDisplay = "Unknown Version";

    protected ViewModelBase()
    {
        try
        {
            if (System.IO.File.Exists("appsettings.json"))
            {
                var json = System.IO.File.ReadAllText("appsettings.json");
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("AppInfo", out var appInfo))
                {
                    var codename = appInfo.GetProperty("Codename").GetString();
                    var version = appInfo.GetProperty("FullVersionDisplay").GetString();
                    _appVersionDisplay = $"{version} {codename}";
                }
            }
        }
        catch
        {
            _appVersionDisplay = "Unknown Version";
        }
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
