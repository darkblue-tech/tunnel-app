using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Client.Core.Services;

namespace Client.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private readonly ApiService _apiService;

    private string _statusKey = "Str_Status_Disconnected";
    public string StatusKey
    {
        get => _statusKey;
        set
        {
            if (SetProperty(ref _statusKey, value))
            {
                OnPropertyChanged(nameof(Status));
            }
        }
    }
    public string Status => GetString(_statusKey);

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private ViewModelBase _currentViewModel;

    [ObservableProperty]
    private MainViewModel? _mainVM;

    // Logs overlay logic
    [ObservableProperty]
    private bool _isLogsPanelOpen;

    [ObservableProperty]
    private string _globalLogs = "[23:55.10] Starting tunnel client...\n";

    public ObservableCollection<TunnelItemViewModel> Tunnels { get; } = new();

    private bool _isLoadingSettings;
    private bool _isInitializingAutostart;

    public MainWindowViewModel()
    {
        _authService = new AuthService();
        _apiService = new ApiService(_authService);
        
        _isInitializingAutostart = true;
        RunAtStartup = AutostartHelper.IsAutostartEnabled();
        _isInitializingAutostart = false;

        // Default route
        CurrentViewModel = new LoginViewModel(this);

        _ = LoadSettingsAsync();
        _ = InitializeAsync();
        _ = ApplyThemeAsync();
        _ = ApplyLanguageAsync();
    }

    public async Task LoadSettingsAsync()
    {
        try
        {
            _isLoadingSettings = true;
            var storage = new Client.Core.Services.SecretStorage();
            var cttVal = await storage.GetSecretAsync("close_to_tray");
            if (bool.TryParse(cttVal, out var ctt))
            {
                CloseToTray = ctt;
            }

            var smVal = await storage.GetSecretAsync("start_minimized");
            if (bool.TryParse(smVal, out var sm))
            {
                StartMinimized = sm;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load preferences: {ex.Message}");
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private async Task ApplyLanguageAsync()
    {
        var lang = await new SecretStorage().GetSecretAsync("language") ?? "en";
        SetLanguageCore(lang);
    }

    public void SetLanguageCore(string langCode)
    {
        if (Avalonia.Application.Current == null) return;
        var dicts = Avalonia.Application.Current.Resources.MergedDictionaries;
        
        // Remove old languages (ColorPalette is at index 0)
        while (dicts.Count > 1)
        {
            dicts.RemoveAt(1);
        }
        
        try
        {
            var newDict = new Avalonia.Markup.Xaml.Styling.ResourceInclude(new System.Uri("avares://DarkTunnel Client/App.axaml"))
            {
                Source = new System.Uri($"avares://DarkTunnel Client/Assets/i18n/{langCode}.axaml")
            };
            dicts.Add(newDict);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(Status)), Avalonia.Threading.DispatcherPriority.Background);
        }
        catch { }
    }

    private async Task ApplyThemeAsync()
    {
        var theme = await new SecretStorage().GetSecretAsync("theme") ?? "System";
        if (Avalonia.Application.Current != null)
        {
            Avalonia.Application.Current.RequestedThemeVariant = theme switch
            {
                "Dark" => Avalonia.Styling.ThemeVariant.Dark,
                "Light" => Avalonia.Styling.ThemeVariant.Light,
                _ => Avalonia.Styling.ThemeVariant.Default
            };
        }
    }

    private async Task InitializeAsync()
    {
        var hasSession = await _authService.HasSavedSessionAsync();
        if (hasSession)
        {
            IsAuthenticated = true;
            MainVM ??= new MainViewModel(this);
            CurrentViewModel = MainVM;

            string token = string.Empty;
            // Retry token acquisition to accommodate network establishing during PC boot
            for (int attempt = 0; attempt < 3; attempt++)
            {
                token = await _authService.GetTokenAsync();
                if (!string.IsNullOrEmpty(token)) break;

                if (!await _authService.HasSavedSessionAsync())
                {
                    await LogOutAsync();
                    _ = CheckForUpdatesBackgroundAsync();
                    return;
                }

                await Task.Delay(1000);
            }

            if (!string.IsNullOrEmpty(token))
            {
                await LoadTunnelsAsync(token);
            }
        }

        _ = CheckForUpdatesBackgroundAsync();
    }

    [ObservableProperty]
    private bool _hasUpdateResult;

    [ObservableProperty]
    private string _updateVersionText = string.Empty;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private double _updateProgressPercentage;

    [ObservableProperty]
    private string _updateProgressText = string.Empty;

    private UpdateCheckResult? _pendingUpdateInfo;

    private async Task CheckForUpdatesBackgroundAsync()
    {
        try
        {
            var updateService = new UpdateService();
            var result = await updateService.CheckForUpdatesAsync();
            if (result != null && result.HasUpdate)
            {
                _pendingUpdateInfo = result;
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateVersionText = result.LatestVersion;
                    HasUpdateResult = true;
                });
            }
        }
        catch { }
    }

    [RelayCommand]
    public async Task ApplyUpdateAsync()
    {
        if (_pendingUpdateInfo == null) return;
        IsUpdating = true;
        UpdateProgressPercentage = 0;
        UpdateProgressText = "Connecting...";

        var progress = new Progress<UpdateProgress>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdateProgressPercentage = p.Percentage;
                if (p.TotalBytes > 0)
                {
                    var downloadedMb = p.DownloadedBytes / (1024.0 * 1024.0);
                    var totalMb = p.TotalBytes / (1024.0 * 1024.0);
                    UpdateProgressText = $"Downloading {p.Percentage:0}% ({downloadedMb:0.0} / {totalMb:0.0} MB)";
                }
                else
                {
                    var downloadedMb = p.DownloadedBytes / (1024.0 * 1024.0);
                    UpdateProgressText = $"Downloading ({downloadedMb:0.0} MB)...";
                }

                if (p.Percentage >= 100.0)
                {
                    UpdateProgressText = "Installing & restarting...";
                }
            });
        });

        try
        {
            var updateService = new UpdateService();
            var success = await updateService.ApplyUpdateAsync(_pendingUpdateInfo, progress);
            if (success && !OperatingSystem.IsWindows())
            {
                HasUpdateResult = false;
            }
        }
        finally
        {
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        HasUpdateResult = false;
    }

    [ObservableProperty]
    private bool _runAtStartup;

    [ObservableProperty]
    private bool _closeToTray;

    [ObservableProperty]
    private bool _startMinimized;

    partial void OnRunAtStartupChanged(bool value)
    {
        if (_isInitializingAutostart) return;
        AutostartHelper.SetAutostart(value);
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (_isLoadingSettings) return;
        _ = new Client.Core.Services.SecretStorage().SaveSecretAsync("close_to_tray", value.ToString());
    }

    partial void OnStartMinimizedChanged(bool value)
    {
        if (_isLoadingSettings) return;
        _ = new Client.Core.Services.SecretStorage().SaveSecretAsync("start_minimized", value.ToString());
    }

    public async Task LoginAndLoadTunnelsAsync()
    {
        StatusKey = "Str_Status_Authenticating";
        var token = await _authService.LoginAsync();
        if (token == null)
        {
            // Cancelled by another concurrent login attempt. Do nothing.
            return;
        }
        
        if (string.IsNullOrEmpty(token))
        {
            StatusKey = "Str_Status_AuthFailed";
            IsAuthenticated = false;
            return;
        }

        IsAuthenticated = true;
        
        // Navigate to Main View
        MainVM ??= new MainViewModel(this);
        CurrentViewModel = MainVM;

        await LoadTunnelsAsync(token);
    }

    public async Task RefreshTunnelsAsync()
    {
        if (!IsAuthenticated)
        {
            await LoginAndLoadTunnelsAsync();
            return;
        }

        StatusKey = "Str_Status_Refreshing";
        var token = await _authService.GetTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            if (!await _authService.HasSavedSessionAsync())
            {
                IsAuthenticated = false;
                StatusKey = "Str_Status_SessionExpired";
                await LogOutAsync();
                return;
            }
        }

        await LoadTunnelsAsync(token);
    }

    private async Task LoadTunnelsAsync(string serverToken)
    {
        List<Client.Core.Models.TunnelModel> tunnelsData;
        try
        {
            tunnelsData = await _apiService.GetTunnelsAsync();
        }
        catch (UnauthorizedAccessException)
        {
            await LogOutAsync();
            return;
        }
        
        var storage = new SecretStorage();
        var isGlobalStr = await storage.GetSecretAsync("global_is_connected");
        bool isGlobalConnected = isGlobalStr == "True";

        var selectedStr = await storage.GetSecretAsync("selected_tunnels") ?? "";
        var selectedSet = new System.Collections.Generic.HashSet<string>(selectedStr.Split(',', StringSplitOptions.RemoveEmptyEntries));

        Dispatcher.UIThread.Post(() =>
        {
            if (MainVM != null)
            {
                MainVM.IsConnected = isGlobalConnected;
                MainVM.StatusKey = isGlobalConnected ? "Str_Status_Connecting" : "Str_Status_Disconnected";
            }

            foreach (var t in Tunnels)
            {
                if (t.IsConnected) t.Stop();
            }

            Tunnels.Clear();
            foreach (var data in tunnelsData)
            {
                var tvm = new TunnelItemViewModel(data, _authService, _apiService, this);
                
                if (selectedSet.Contains(data.Id.ToString()))
                {
                    tvm.IsSelected = true;
                }
                else if (selectedSet.Count == 0 && Tunnels.Count == 0)
                {
                    // Select first by default if nothing saved
                    tvm.IsSelected = true;
                }

                tvm.OnSessionExpired += () =>
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        if (StatusKey != "Str_Status_AuthFailed")
                        {
                            StatusKey = "Str_Status_SessionExpired";
                            IsAuthenticated = false;
                            await LogOutAsync();
                        }
                    });
                };

                tvm.OnLogReceived += (msg) => 
                {
                    Dispatcher.UIThread.Post(() => 
                    {
                        var timestamp = DateTime.Now.ToString("HH:mm:ss");
                        var newLog = $"[{timestamp}] [{tvm.Data.Name}] {msg}\n";
                        GlobalLogs += newLog;
                        
                        // Keep logs reasonable
                        if (GlobalLogs.Length > 10000)
                        {
                            GlobalLogs = GlobalLogs.Substring(GlobalLogs.Length - 5000);
                        }
                    });
                };

                tvm.PropertyChanged += (s, e) => {
                    if (e.PropertyName == nameof(TunnelItemViewModel.IsSelected))
                    {
                        if (CurrentViewModel is MainViewModel mvm)
                        {
                            _ = mvm.SaveStateAsync();
                        }
                    }
                };
                Tunnels.Add(tvm);
            }

            if (CurrentViewModel is MainViewModel mainVm)
            {
                if (isGlobalConnected)
                {
                    mainVm.IsConnected = true;
                    mainVm.StatusKey = "Str_Status_Connected";
                    StatusKey = "Str_Status_Connected";
                    foreach(var t in Tunnels)
                    {
                        if (t.IsSelected && !t.IsConnected) t.Start();
                    }
                }
            }
        });
    }

    public async Task LogOutAsync()
    {
        await _authService.LogoutAsync();
        IsAuthenticated = false;
        StatusKey = "Str_Status_Disconnected";
        Tunnels.Clear();
        CurrentViewModel = new LoginViewModel(this);
    }

    [RelayCommand]
    public void ToggleLogsPanel()
    {
        IsLogsPanelOpen = !IsLogsPanelOpen;
    }

    public void TriggerReconnectTunnels()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (MainVM != null && MainVM.IsConnected)
            {
                foreach (var t in Tunnels)
                {
                    if (t.IsSelected && t.IsConnected)
                    {
                        t.Stop();
                        t.Start();
                    }
                }
            }
        });
    }
}
