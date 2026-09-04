using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Client.Core.Services;
using Client.Core.Models;
namespace Client.Desktop.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _parent;

    public SettingsViewModel(MainWindowViewModel parent)
    {
        _parent = parent;
        RunAtStartup = parent.RunAtStartup;
        CloseToTray = parent.CloseToTray;
        StartMinimized = parent.StartMinimized;
        _ = LoadProfileAsync();
    }

    private async Task LoadProfileAsync()
    {
        var storage = new Client.Core.Services.SecretStorage();
        UserName = await storage.GetSecretAsync("profile_name") ?? "System Administrator";
        
        var savedTheme = await storage.GetSecretAsync("theme");
        if (!string.IsNullOrEmpty(savedTheme))
        {
            Theme = savedTheme;
        }

        var savedLang = await storage.GetSecretAsync("language") ?? "en";
        Language = savedLang switch
        {
            "ru" => "Русский",
            "de" => "Deutsch",
            "fr" => "Français",
            "es" => "Español",
            "zh" => "中文",
            "ja" => "日本語",
            _ => "English"
        };
        
        var savedTransport = await storage.GetSecretAsync("transport") ?? "Auto";
        Transport = savedTransport;
    }

    [ObservableProperty]
    private string _userName = "Loading...";

    [ObservableProperty]
    private bool _runAtStartup;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _closeToTray;

    [ObservableProperty]
    private string _theme = "System";

    public string[] Themes { get; } = new[] { "System", "Light", "Dark" };

    partial void OnThemeChanged(string value)
    {
        if (Avalonia.Application.Current != null)
        {
            Avalonia.Application.Current.RequestedThemeVariant = value switch
            {
                "Dark" => Avalonia.Styling.ThemeVariant.Dark,
                "Light" => Avalonia.Styling.ThemeVariant.Light,
                _ => Avalonia.Styling.ThemeVariant.Default
            };
        }
        _ = new Client.Core.Services.SecretStorage().SaveSecretAsync("theme", value);
    }

    [ObservableProperty]
    private string _language = "English";

    public string[] Languages { get; } = new[] { "English", "Русский", "Deutsch", "Français", "Español", "中文", "日本語" };

    partial void OnLanguageChanged(string value)
    {
        var langCode = value switch
        {
            "Русский" => "ru",
            "Deutsch" => "de",
            "Français" => "fr",
            "Español" => "es",
            "中文" => "zh",
            "日本語" => "ja",
            _ => "en"
        };
        _parent.SetLanguageCore(langCode);
        _ = new Client.Core.Services.SecretStorage().SaveSecretAsync("language", langCode);
    }

    [ObservableProperty]
    private string _transport = "Auto";

    public string[] Transports { get; } = new[] { "Auto", "QUIC", "WebRTC", "gRPC", "WebSocket" };

    partial void OnTransportChanged(string value)
    {
        _ = new Client.Core.Services.SecretStorage().SaveSecretAsync("transport", value);
        _parent.TriggerReconnectTunnels();
    }

    partial void OnRunAtStartupChanged(bool value)
    {
        _parent.RunAtStartup = value;
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        _parent.CloseToTray = value;
    }

    partial void OnStartMinimizedChanged(bool value)
    {
        _parent.StartMinimized = value;
    }

    [RelayCommand]
    private void GoBack()
    {
        _parent.CurrentViewModel = _parent.MainVM!;
    }

    [RelayCommand]
    private async Task LogOut()
    {
        await _parent.LogOutAsync();
    }
}
