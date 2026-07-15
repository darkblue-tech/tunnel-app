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
    private readonly string _serverToken;

    [ObservableProperty]
    private TunnelModel _data;

    [ObservableProperty]
    private string _status = "DISCONNECTED";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _logs = "";

    [ObservableProperty]
    private string _displayUrl = "";

    public TunnelItemViewModel(TunnelModel model, string serverToken)
    {
        _data = model;
        _serverToken = serverToken;
        _displayUrl = model.PublicUrl;
        _engine = new TunnelEngine();
        _engine.OnLog += OnEngineLog;
        _engine.OnDisconnected += OnEngineDisconnected;
        _engine.OnConnected += OnEngineConnected;
    }

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        if (IsConnected)
        {
            _engine.StopTunnel();
            IsConnected = false;
            Status = "DISCONNECTED";
        }
        else
        {
            Status = "CONNECTING...";
            IsConnected = true;

            var serverUrl = Environment.GetEnvironmentVariable("TUNNEL_HOST_WS") ?? "wss://tunnel.darkblue.tech/ws";

            // Parse subdomain and local target
            var subdomain = ParseSubdomain(Data.PublicUrl);
            var (localHost, localPort) = ParseLocalTarget(Data.LocalTarget);

            // Important: Use http:// instead of https:// for raw TCP ports (7000+) because TLS is only on 443
            _ = _engine.StartTunnelAsync(serverUrl, subdomain, localHost, localPort, Data.PublicPort, _serverToken);
        }
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
                OnEngineLog("Connection lost. Engine is attempting to reconnect...");
            }
        });
    }

    private void OnEngineLog(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Logs += $"{message}{Environment.NewLine}";
        });
    }
}
