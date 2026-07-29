using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System;
using Client.Desktop.Services;

namespace Client.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _parent;

    public ObservableCollection<TunnelItemViewModel> Tunnels => _parent.Tunnels;

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
    private bool _isConnected;

    [ObservableProperty]
    private string _selectedRegion = "DE";

    public string FlagPath => $"https://flagcdn.com/{SelectedRegion.ToLower()}.svg";

    partial void OnSelectedRegionChanged(string value)
    {
        OnPropertyChanged(nameof(FlagPath));
    }

    [ObservableProperty]
    private int _ping = 89;

    [ObservableProperty]
    private int _activeTunnelsCount;

    [ObservableProperty]
    private string _tunnelsCounterText = "0 / ∞";

    public MainViewModel(MainWindowViewModel parent)
    {
        _parent = parent;
        _statusKey = "Str_Status_Disconnected";
        _isConnected = false;
        
        parent.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Status))
            {
                OnPropertyChanged(nameof(Status));
            }
        };

        Tunnels.CollectionChanged += (s, e) => 
        {
            UpdateActiveCount();
            if (e.NewItems != null)
            {
                foreach (TunnelItemViewModel item in e.NewItems)
                {
                    item.PropertyChanged += Tunnel_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (TunnelItemViewModel item in e.OldItems)
                {
                    item.PropertyChanged -= Tunnel_PropertyChanged;
                }
            }
        };

        foreach (var t in Tunnels)
        {
            t.PropertyChanged += Tunnel_PropertyChanged;
        }
    }

    private void UpdateActiveCount()
    {
        // Simple logic for UI. Real app might track individual connection states
        ActiveTunnelsCount = Tunnels.Count;
        TunnelsCounterText = $"{ActiveTunnelsCount} / ∞";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        _parent.CurrentViewModel = new SettingsViewModel(_parent);
    }

    [RelayCommand]
    private void OpenStats()
    {
        _parent.CurrentViewModel = new StatsViewModel(_parent);
    }

    [RelayCommand]
    private void ToggleGlobalConnection()
    {
        if (IsConnected || StatusKey == "Str_Status_Connecting")
        {
            // Disconnect
            IsConnected = false;
            StatusKey = "Str_Status_Disconnected";
            foreach (var t in Tunnels)
            {
                if (t.IsConnected) t.Stop();
            }
        }
        else
        {
            // Connect
            var selectedTunnels = System.Linq.Enumerable.Where(Tunnels, t => t.IsSelected).ToList();
            if (selectedTunnels.Count == 0)
            {
                StatusKey = "Str_Status_Disconnected";
                return;
            }

            IsConnected = false;
            StatusKey = "Str_Status_Connecting";
            
            foreach (var t in selectedTunnels)
            {
                if (!t.IsConnected) t.Start();
            }
        }
        _ = SaveStateAsync();
    }

    private void Tunnel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TunnelItemViewModel.IsActuallyConnected))
        {
            if (StatusKey == "Str_Status_Connecting" && System.Linq.Enumerable.Any(Tunnels, t => t.IsSelected && t.IsActuallyConnected))
            {
                IsConnected = true;
                StatusKey = "Str_Status_Connected";
                _ = SaveStateAsync();
            }
        }
    }

    public async Task SaveStateAsync()
    {
        var storage = new SecretStorage();
        await storage.SaveSecretAsync("global_is_connected", IsConnected.ToString());
        
        var selectedIds = System.Linq.Enumerable.Where(Tunnels, t => t.IsSelected).Select(t => t.Data.Id.ToString());
        await storage.SaveSecretAsync("selected_tunnels", string.Join(",", selectedIds));
    }

    [RelayCommand]
    private async Task PullTunnelsAsync()
    {
        await _parent.RefreshTunnelsAsync();
    }

    [RelayCommand]
    private void OpenRegionSelector()
    {
        var url = "https://tunnel.darkblue.tech";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
