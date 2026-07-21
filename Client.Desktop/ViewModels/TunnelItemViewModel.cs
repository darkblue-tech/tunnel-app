using Avalonia.Threading;
using Client.Desktop.Models;
using Client.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;

namespace Client.Desktop.ViewModels;

public partial class TunnelItemViewModel : ViewModelBase
{
    private readonly TunnelEngine _engine;
    private readonly AuthService _authService;
    private readonly ApiService _apiService;

    [ObservableProperty]
    private TunnelModel _data;

    [ObservableProperty]
    private string _status = "DISCONNECTED";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isActuallyConnected;

    [ObservableProperty]
    private int _activeConnections;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _logs = "";

    [ObservableProperty]
    private string _displayUrl = "";

    public event Action<string>? OnLogReceived;
    public event Action? OnSessionExpired;

    private readonly Func<bool> _isGlobalConnected;

    public TunnelItemViewModel(TunnelModel model, AuthService authService, ApiService apiService, Func<bool> isGlobalConnected)
    {
        _data = model;
        _authService = authService;
        _apiService = apiService;
        _isGlobalConnected = isGlobalConnected;
        _displayUrl = model.PublicUrl;
        _engine = new TunnelEngine();
        _engine.OnLog += OnEngineLog;
        _engine.OnDisconnected += OnEngineDisconnected;
        _engine.OnConnected += OnEngineConnected;
        _engine.OnConnectionCountChanged += OnConnectionCountChanged;
        _engine.OnTokenExpired += OnEngineTokenExpired;
    }

    private void OnEngineTokenExpired()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var newToken = await _authService.RefreshTokenAsync();
            if (string.IsNullOrEmpty(newToken))
            {
                IsActuallyConnected = false;
                Status = "SESSION EXPIRED";
                OnSessionExpired?.Invoke();
            }
            else
            {
                // Restart with new token
                IsConnected = false;
                Start();
            }
        });
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && _isGlobalConnected())
        {
            Start();
        }
        else if (!value)
        {
            Stop();
        }
    }

    public void Start()
    {
        if (IsConnected) return;
        Status = "CONNECTING...";
        IsConnected = true;

        var subdomain = ParseSubdomain(Data.PublicUrl);
        var (localHost, localPort) = ParseLocalTarget(Data.LocalTarget);

        _ = Task.Run(async () =>
        {
            var edgeNodeInfo = await _apiService.GetPreferredEdgeNodeAsync();
            var serverUrl = edgeNodeInfo?.Url ?? "wss://tunnel.darkblue.tech/ws";

            if (edgeNodeInfo != null && _parent.MainVM != null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _parent.MainVM.SelectedRegion = string.IsNullOrEmpty(edgeNodeInfo.Region) ? "EU" : edgeNodeInfo.Region.ToUpper();
                });

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var ping = new System.Net.NetworkInformation.Ping();
                        var host = new Uri(serverUrl).Host;
                        var reply = await ping.SendPingAsync(host, 3000);
                        if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() => _parent.MainVM.Ping = (int)reply.RoundtripTime);
                        }
                    }
                    catch { }
                });
            }

            var storage = new SecretStorage();
            var transport = await storage.GetSecretAsync("transport") ?? "Auto";
            var token = await _authService.GetTokenAsync();
            await _engine.StartTunnelAsync(serverUrl, subdomain, localHost, localPort, Data.PublicPort, token, transport);
        });
    }

    public void Stop()
    {
        if (!IsConnected) return;
        _engine.StopTunnel();
        IsConnected = false;
        IsActuallyConnected = false;
        ActiveConnections = 0;
        Status = "DISCONNECTED";
    }

    private string ParseSubdomain(string publicUrl)
    {
        // https://subdomain.tunnel.darkblue.tech:port
        try
        {
            var uri = new Uri(publicUrl);
            var hostParts = uri.Host.Split('.');
            if (hostParts.Length > 0) return hostParts[0];
        }
        catch { }
        return Data.Name;
    }

    private (string, int) ParseLocalTarget(string localTarget)
    {
        var parts = localTarget.Split(':');
        var host = parts.Length > 0 ? parts[0] : "127.0.0.1";
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 80;
        return (host, port);
    }

    private void OnEngineConnected(string publicUrl)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsConnected) 
            {
                Status = "CONNECTED";
                IsActuallyConnected = true;
                if (!string.IsNullOrEmpty(publicUrl)) 
                {
                    // If it's a dynamic TCP port (7000+), replace https:// with http://
                    var url = publicUrl;
                    if (url.Contains(":70") && url.StartsWith("https://"))
                    {
                        url = "http://" + url.Substring(8);
                    }
                    DisplayUrl = url;
                }
            }
        });
    }

    private void OnEngineDisconnected()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsConnected)
            {
                Status = "RECONNECTING...";
                IsActuallyConnected = false;
                ActiveConnections = 0;
                OnEngineLog("Connection lost. Engine is attempting to reconnect...");
            }
        });
    }

    private void OnConnectionCountChanged(int count)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ActiveConnections = count;
        });
    }

    private void OnEngineLog(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Logs += $"{message}{Environment.NewLine}";
            OnLogReceived?.Invoke(message);
        });
    }
}
