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

    [ObservableProperty]
    private string _status = "DISCONNECTED";

    [ObservableProperty]
    private bool _isAuthenticated;

    public ObservableCollection<TunnelItemViewModel> Tunnels { get; } = new();

    public MainWindowViewModel()
    {
        _authService = new AuthService();
        _apiService = new ApiService(_authService);
    }

    [RelayCommand]
    public async Task LoginAndLoadTunnelsAsync()
    {
        Status = "AUTHENTICATING...";
        var token = await _authService.LoginAsync();
        
        if (string.IsNullOrEmpty(token))
        {
            Status = "AUTH FAILED";
            IsAuthenticated = false;
            return;
        }

        IsAuthenticated = true;
        Status = "AUTHENTICATED";
        
        await LoadTunnelsAsync(token);
    }

    [RelayCommand]
    public async Task RefreshTunnelsAsync()
    {
        if (!IsAuthenticated)
        {
            await LoginAndLoadTunnelsAsync();
            return;
        }

        Status = "REFRESHING...";
        var token = await _authService.GetTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            IsAuthenticated = false;
            Status = "SESSION EXPIRED";
            return;
        }

        await LoadTunnelsAsync(token);
        Status = "READY";
    }

    private async Task LoadTunnelsAsync(string serverToken)
    {
        var tunnelsData = await _apiService.GetTunnelsAsync();
        
        Dispatcher.UIThread.Post(() =>
        {
            // For a clean refresh, we stop all active engines before clearing
            // In a more advanced implementation, we'd diff the lists to keep active tunnels running
            foreach (var t in Tunnels)
            {
                if (t.IsConnected) t.ToggleConnectionCommand.Execute(null);
            }

            Tunnels.Clear();
            foreach (var data in tunnelsData)
            {
                Tunnels.Add(new TunnelItemViewModel(data, serverToken));
            }
        });
    }
}
