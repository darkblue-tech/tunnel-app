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
    public event Action<string>? OnConnected;
    private CancellationTokenSource? _lifetime;
    private bool _intentionalStop;

    public async Task StartTunnelAsync(string serverUrl, string subdomain, string localHost, int localPort, int publicPort, string serverToken)
    {
        _lifetime = new CancellationTokenSource();
        _intentionalStop = false;
        
        int retryDelay = 1000;
        
        while (!_lifetime.IsCancellationRequested && !_intentionalStop)
        {
            try
            {
                await ConnectLoopAsync(serverUrl, subdomain, localHost, localPort, publicPort, serverToken);
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

    private async Task ConnectLoopAsync(string serverUrl, string subdomain, string localHost, int localPort, int publicPort, string serverToken)
    {
        var streams = new ConcurrentDictionary<string, LocalStreamSession>();
        IControlChannelClient? client = null;
        
        var baseUri = new Uri(serverUrl);
        var httpsUri = new Uri($"https://{baseUri.Host}:{baseUri.Port}");
        
        // Smart Fallback
        Log($"Attempting QUIC connection to {httpsUri}...");
        try
        {
            var quicClient = new QuicControlChannelClient();
            await quicClient.ConnectAsync(httpsUri, _lifetime!.Token);
            client = quicClient;
            Log("Connected via QUIC");
        }
        catch (Exception ex)
        {
            Log($"QUIC failed ({ex.Message}), falling back to gRPC...");
            try
            {
                var grpcClient = new Client.Desktop.Grpc.ControlChannelGrpcClient();
                await grpcClient.ConnectAsync(httpsUri, _lifetime.Token);
                client = grpcClient;
                Log("Connected via gRPC");
            }
            catch (Exception ex2)
            {
                Log($"gRPC failed ({ex2.Message}), falling back to WebSocket...");
                var wsClient = new WebSocketControlChannelClient();
                await wsClient.ConnectAsync(new Uri(serverUrl), _lifetime.Token);
                client = wsClient;
                Log("Connected via WebSocket");
            }
        }

        await client.SendAuthAsync("desktop-avalonia", serverToken);

        _ = Task.Run(async () =>
        {
            while (!_lifetime.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), _lifetime.Token);
                    await client.SendPingAsync();
                }
                catch (OperationCanceledException) { break; }
                catch { break; }
            }
        });

        var isRegistered = false;

        while (!_lifetime.IsCancellationRequested)
        {
            var message = await client.ReceiveAsync(_lifetime.Token);
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
                        var streamId = message.StreamId;
                        if (streamId is null) break;

                        var session = new LocalStreamSession(streamId, localHost, localPort, client, streams, Log);
                        if (streams.TryAdd(streamId, session))
                        {
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
                            Log($"Stream closed by server: {streamId}");
                        }
                        break;
                    }
                default:
                    Log($"Unknown control message: {type}");
                    break;
            }
        }

        foreach (var kv in streams)
        {
            await kv.Value.CloseAsync();
        }
        
        await client.DisposeAsync();
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
    private readonly System.Threading.Channels.Channel<byte[]> _writeChannel;
    private readonly CancellationTokenSource _cts = new();
    private TcpClient? _localClient;
    private NetworkStream? _localStream;
    private bool _closeNotified;
    private int _started;

    public LocalStreamSession(string streamId, string localHost, int localPort, IControlChannelClient client, ConcurrentDictionary<string, LocalStreamSession> registry, Action<string> log)
    {
        _streamId = streamId;
        _localHost = localHost;
        _localPort = localPort;
        _client = client;
        _registry = registry;
        _log = log;
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
