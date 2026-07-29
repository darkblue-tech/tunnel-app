using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Client.Desktop.Services;

public class TunnelEngine
{
    public event Action<string>? OnLog;
    public event Action? OnDisconnected;
    public event Action? OnTokenExpired;
    public event Action<string>? OnConnected;
    public event Action<int>? OnConnectionCountChanged;
    private CancellationTokenSource? _lifetime;
    private bool _intentionalStop;

    public async Task StartTunnelAsync(string serverUrl, string subdomain, string localHost, int localPort, int publicPort, string serverToken, string transport = "Auto")
    {
        _lifetime = new CancellationTokenSource();
        _intentionalStop = false;
        
        int retryDelay = 1000;
        
        while (!_lifetime.IsCancellationRequested && !_intentionalStop)
        {
            try
            {
                await ConnectLoopAsync(serverUrl, subdomain, localHost, localPort, publicPort, serverToken, transport);
            }
            catch (Exception ex)
            {
                if (!_intentionalStop)
                {
                    Log($"Engine error: {ex.Message}");
                }
            }

            if (!_intentionalStop)
            {
                OnDisconnected?.Invoke();
                Log($"Reconnecting in {retryDelay / 1000} seconds...");
                try
                {
                    await Task.Delay(retryDelay, _lifetime.Token);
                }
                catch (OperationCanceledException) { break; }
                
                retryDelay = Math.Min(retryDelay * 2, 30000); // Max 30 seconds backoff
            }
        }
    }

    private async Task<(IControlChannelClient Client, ControlMessageDto FirstMsg)> TryConnectProtocolAsync(IControlChannelClient client, Uri uri, string serverToken)
    {
        await client.ConnectAsync(uri, _lifetime!.Token);
        await client.SendAuthAsync("desktop-avalonia", serverToken);
        var msg = await client.ReceiveAsync(_lifetime!.Token);
        if (msg == null) throw new Exception("Connection closed immediately after auth");
        return (client, msg);
    }

    private async Task ConnectLoopAsync(string serverUrl, string subdomain, string localHost, int localPort, int publicPort, string serverToken, string transport)
    {
        var streams = new ConcurrentDictionary<string, LocalStreamSession>();
        IControlChannelClient? client = null;
        ControlMessageDto? firstMsg = null;
        
        var baseUri = new Uri(serverUrl);
        var httpsUri = new Uri($"https://{baseUri.Host}:{baseUri.Port}");
        var quicUri = new Uri($"https://{baseUri.Host}:5003");

        var transportsToTry = transport == "Auto" 
            ? new[] { "QUIC", "WebRTC", "gRPC", "WebSocket" }
            : new[] { transport };

        foreach (var t in transportsToTry)
        {
            try
            {
                Log($"Attempting {t} connection...");
                if (t == "QUIC")
                {
                    var quicClient = new QuicControlChannelClient();
                    var res = await TryConnectProtocolAsync(quicClient, quicUri, serverToken);
                    client = res.Client;
                    firstMsg = res.FirstMsg;
                }
                else if (t == "WebRTC")
                {
                    var webrtcClient = new WebRtcControlChannelClient(serverToken);
                    var res = await TryConnectProtocolAsync(webrtcClient, baseUri, serverToken);
                    client = res.Client;
                    firstMsg = res.FirstMsg;
                }
                else if (t == "gRPC")
                {
                    var grpcClient = new Client.Desktop.Grpc.ControlChannelGrpcClient();
                    var res = await TryConnectProtocolAsync(grpcClient, httpsUri, serverToken);
                    client = res.Client;
                    firstMsg = res.FirstMsg;
                }
                else if (t == "WebSocket")
                {
                    var wsClient = new WebSocketControlChannelClient();
                    var res = await TryConnectProtocolAsync(wsClient, baseUri, serverToken);
                    client = res.Client;
                    firstMsg = res.FirstMsg;
                }
                
                Log($"Connected via {t}");
                break;
            }
            catch (Exception ex)
            {
                Log($"{t} failed ({ex.Message})");
            }
        }

        if (client == null)
        {
            Log("All transport methods failed.");
            return;
        }

        _ = Task.Run(async () =>
        {
            while (_lifetime != null && !_lifetime.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), _lifetime.Token);
                    if (client != null) await client.SendPingAsync();
                }
                catch (OperationCanceledException) { break; }
                catch { break; }
            }
        });

        var isRegistered = false;
        var message = firstMsg;

        while (_lifetime != null && !_lifetime.IsCancellationRequested)
        {
            if (message is null) break;

            var type = message.Type;

            switch (type)
            {
                case "auth_ok":
                    Log($"Auth OK: {message.ClientId}");
                    if (!isRegistered)
                    {
                        await client.SendRegisterTunnelAsync(subdomain, localHost, localPort, publicPort);
                        Log($"Registered tunnel request for {subdomain}.tunnel.darkblue.tech:{publicPort} -> {localHost}:{localPort}");
                        isRegistered = true;
                    }
                    break;
                case "auth_error":
                    Log($"Auth failed: {message.Error}");
                    if (message.Error == "token_expired" || message.Error == "token_invalid")
                    {
                        OnTokenExpired?.Invoke();
                    }
                    _intentionalStop = true;
                    _lifetime.Cancel();
                    return;
                case "pong":
                    break;
                case "rate_limited":
                    Log($"Rate limited by server: {message.Error}");
                    break;
                case "register_ack":
                    var publicUrl = message.PublicUrl ?? "";
                    Log($"Tunnel ready: {publicUrl}");
                    OnConnected?.Invoke(publicUrl);
                    break;
                case "register_error":
                    Log($"Tunnel registration failed: {message.Error}");
                    _intentionalStop = true;
                    _lifetime.Cancel();
                    return;
                case "open_stream":
                    {
                        var streamId = message.StreamId ?? "";
                        var session = new LocalStreamSession(streamId, localHost, localPort, client, streams, Log, (count) => OnConnectionCountChanged?.Invoke(count));
                        if (streams.TryAdd(streamId, session))
                        {
                            OnConnectionCountChanged?.Invoke(streams.Count);
                            _ = session.StartAsync();
                            Log($"Accepted public connection -> stream {streamId}");
                        }
                        break;
                    }
                case "stream_data":
                    {
                        var streamId = message.StreamId;
                        var payload = message.Payload;
                        if (streamId is null || payload is null) break;

                        if (streams.TryGetValue(streamId, out var session))
                        {
                            await session.WriteToLocalAsync(payload);
                        }
                        break;
                    }
                case "stream_close":
                    {
                        var streamId = message.StreamId;
                        if (streamId is null) break;

                        if (streams.TryRemove(streamId, out var session))
                        {
                            await session.CloseAsync();
                            OnConnectionCountChanged?.Invoke(streams.Count);
                            Log($"Stream closed by server: {streamId}");
                        }
                        break;
                    }
                default:
                    Log($"Unknown control message: {type}");
                    break;
            }
            
            message = await client.ReceiveAsync(_lifetime!.Token);
        }

        foreach (var kv in streams)
        {
            await kv.Value.CloseAsync();
        }
        
        if (client != null)
        {
            await client.DisposeAsync();
        }
    }

    public void StopTunnel()
    {
        _intentionalStop = true;
        _lifetime?.Cancel();
        Log("Tunnel stopped.");
    }

    private void Log(string message) => OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");


}

internal sealed class LocalStreamSession
{
    private readonly string _streamId;
    private readonly string _localHost;
    private readonly int _localPort;
    private readonly IControlChannelClient _client;
    private readonly ConcurrentDictionary<string, LocalStreamSession> _registry;
    private readonly Action<string> _log;
    private readonly Action<int>? _onCountChanged;
    private readonly System.Threading.Channels.Channel<byte[]> _writeChannel;
    private readonly CancellationTokenSource _cts = new();
    private TcpClient? _localClient;
    private NetworkStream? _localStream;
    private bool _closeNotified;
    private int _started;

    public LocalStreamSession(string streamId, string localHost, int localPort, IControlChannelClient client, ConcurrentDictionary<string, LocalStreamSession> registry, Action<string> log, Action<int>? onCountChanged = null)
    {
        _streamId = streamId;
        _localHost = localHost;
        _localPort = localPort;
        _client = client;
        _registry = registry;
        _log = log;
        _onCountChanged = onCountChanged;
        _writeChannel = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
    }

    public async Task StartAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;

        try
        {
            _localClient = new TcpClient();
            await _localClient.ConnectAsync(_localHost, _localPort);
            _localStream = _localClient.GetStream();

            var readTask = ReadLoopAsync();
            var writeTask = WriteLoopAsync();

            await Task.WhenAll(readTask, writeTask);
        }
        catch (Exception ex)
        {
            if (!_cts.IsCancellationRequested)
            {
                _log($"Stream {_streamId} error: {ex.Message}");
            }
        }
        finally
        {
            await CloseAndNotifyAsync();
        }
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[16 * 1024];
        while (!_cts.IsCancellationRequested && _localStream != null)
        {
            var read = await _localStream.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
            if (read <= 0) break;

            var payload = new byte[read];
            Array.Copy(buffer, payload, read);
            
            BandwidthTracker.AddTx(read);
            
            try
            {
                await _client.SendStreamDataAsync(_streamId, payload);
            }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested)
                {
                    _log($"Stream {_streamId} send error: {ex.Message}");
                    break;
                }
            }
        }
        _writeChannel.Writer.TryComplete();
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var payload in _writeChannel.Reader.ReadAllAsync(_cts.Token))
        {
            if (_localStream != null)
            {
                await _localStream.WriteAsync(payload, 0, payload.Length, _cts.Token);
                await _localStream.FlushAsync(_cts.Token);
                BandwidthTracker.AddRx(payload.Length);
            }
        }
    }

    public Task WriteToLocalAsync(byte[] payload)
    {
        if (!_cts.IsCancellationRequested)
        {
            _writeChannel.Writer.TryWrite(payload);
        }
        return Task.CompletedTask;
    }

    public async Task CloseAsync()
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            _writeChannel.Writer.TryComplete();
            await DisposeLocalAsync();
        }
    }

    private async Task CloseAndNotifyAsync()
    {
        if (_closeNotified) return;
        _closeNotified = true;

        if (_registry.TryRemove(_streamId, out _))
        {
            _onCountChanged?.Invoke(_registry.Count);
            _cts.Cancel();
            _writeChannel.Writer.TryComplete();
            
            try
            {
                await _client.SendStreamCloseAsync(_streamId);
            }
            catch { }
        }

        await DisposeLocalAsync();
    }

    private async Task DisposeLocalAsync()
    {
        try { _localStream?.Close(); } catch { }
        try { _localClient?.Close(); } catch { }
        _cts.Dispose();
        await Task.CompletedTask;
    }
}
