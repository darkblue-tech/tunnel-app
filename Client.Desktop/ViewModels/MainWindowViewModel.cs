using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Client.Desktop.Services;

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

    public MainWindowViewModel()
    {
        _authService = new AuthService();
        _apiService = new ApiService(_authService);
        RunAtStartup = AutostartHelper.IsAutostartEnabled();

        var storage = new Client.Desktop.Services.SecretStorage();
        _ = Task.Run(async () =>
        {
            if (bool.TryParse(await storage.GetSecretAsync("close_to_tray"), out var ctt))
            {
                CloseToTray = ctt;
            }

            if (bool.TryParse(await storage.GetSecretAsync("start_minimized"), out var sm))
            {
                StartMinimized = sm;
            }
        });

        // Default route
        CurrentViewModel = new LoginViewModel(this);

        _ = InitializeAsync();
        _ = ApplyThemeAsync();
        _ = ApplyLanguageAsync();
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
        var token = await _authService.GetTokenAsync();
        if (!string.IsNullOrEmpty(token))
        {
            IsAuthenticated = true;
            MainVM ??= new MainViewModel(this);
            CurrentViewModel = MainVM;
            await LoadTunnelsAsync(token);
        }
    }

    [ObservableProperty]
    private bool _runAtStartup;

    [ObservableProperty]
    private bool _closeToTray;

    [ObservableProperty]
    private bool _startMinimized;

    partial void OnRunAtStartupChanged(bool value)
    {
        AutostartHelper.SetAutostart(value);
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        _ = new Client.Desktop.Services.SecretStorage().SaveSecretAsync("close_to_tray", value.ToString());
    }

    partial void OnStartMinimizedChanged(bool value)
    {
        _ = new Client.Desktop.Services.SecretStorage().SaveSecretAsync("start_minimized", value.ToString());
    }

    public async Task LoginAndLoadTunnelsAsync()
    {
        StatusKey = "Str_Status_Authenticating";
        var token = await _authService.LoginAsync();
        
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
            IsAuthenticated = false;
            StatusKey = "Str_Status_SessionExpired";
            await LogOutAsync();
            return;
        }

        await LoadTunnelsAsync(token);
    }

    private async Task LoadTunnelsAsync(string serverToken)
    {
        List<Client.Desktop.Models.TunnelModel> tunnelsData;
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
                var tvm = new TunnelItemViewModel(data, _authService, _apiService, () => (CurrentViewModel as MainViewModel)?.IsConnected ?? false);
                
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
