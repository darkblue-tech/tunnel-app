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
        Log($"Connecting control channel to {serverUrl}...");

        using var webSocket = new ClientWebSocket();
        var sendLock = new SemaphoreSlim(1, 1);
        var streams = new ConcurrentDictionary<string, LocalStreamSession>();

        await webSocket.ConnectAsync(new Uri(serverUrl), _lifetime!.Token);

        await SendJsonAsync(webSocket, new
        {
            type = "auth",
            clientName = "desktop-avalonia",
            access_token = serverToken
        }, sendLock);

        _ = Task.Run(async () =>
        {
            while (!_lifetime.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), _lifetime.Token);
                    if (webSocket.State != WebSocketState.Open) break;
                    await SendJsonAsync(webSocket, new { type = "ping" }, sendLock);
                }
                catch (OperationCanceledException) { break; }
            }
        });

        var isRegistered = false;
        var controlBuffer = new byte[16 * 1024];

        while (webSocket.State == WebSocketState.Open && !_lifetime.IsCancellationRequested)
        {
            var message = await ReceiveTextMessageAsync(webSocket, controlBuffer, _lifetime.Token);
            if (message is null) break;

            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "auth_ok":
                    Log($"Auth OK: {root.GetProperty("clientId").GetString()}");
                    if (!isRegistered)
                    {
                        await SendJsonAsync(webSocket, new
                        {
                            type = "register_tunnel",
                            subdomain,
                            localHost,
                            localPort,
                            publicPort
                        }, sendLock);
                        Log($"Registered tunnel request for {subdomain}.tunnel.darkblue.tech:{publicPort} -> {localHost}:{localPort}");
                        isRegistered = true;
                    }
                    break;
                case "auth_error":
                    Log($"Auth failed: {root.GetProperty("error").GetString()}");
                    _intentionalStop = true;
                    _lifetime.Cancel();
                    return;
                case "pong":
                    break;
                case "rate_limited":
                    Log($"Rate limited by server: {root.GetProperty("error").GetString()}");
                    break;
                case "register_ack":
                    var publicUrl = root.GetProperty("publicUrl").GetString() ?? "";
                    Log($"Tunnel ready: {publicUrl}");
                    OnConnected?.Invoke(publicUrl);
                    break;
                case "register_error":
                    Log($"Tunnel registration failed: {root.GetProperty("error").GetString()}");
                    _intentionalStop = true;
                    _lifetime.Cancel();
                    return;
                case "open_stream":
                    {
                        var streamId = root.GetProperty("streamId").GetString();
                        if (streamId is null) break;

                        var session = new LocalStreamSession(streamId, localHost, localPort, webSocket, sendLock, streams, Log);
                        if (streams.TryAdd(streamId, session))
                        {
                            _ = session.StartAsync();
                            Log($"Accepted public connection -> stream {streamId}");
                        }
                        break;
                    }
                case "stream_data":
                    {
                        var streamId = root.GetProperty("streamId").GetString();
                        var payloadB64 = root.GetProperty("payload_b64").GetString();
                        if (streamId is null || payloadB64 is null) break;

                        if (streams.TryGetValue(streamId, out var session))
                        {
                            await session.WriteToLocalAsync(Convert.FromBase64String(payloadB64));
                        }
                        break;
                    }
                case "stream_close":
                    {
                        var streamId = root.GetProperty("streamId").GetString();
                        if (streamId is null) break;

                        if (streams.TryRemove(streamId, out var session))
                        {
                            await session.CloseAsync();
                            Log($"Stream closed by server: {streamId}");
                        }
                        break;
                    }
                default:
                    Log($"Unknown control message: {message}");
                    break;
            }
        }

        foreach (var kv in streams)
        {
            await kv.Value.CloseAsync();
        }
    }

    public void StopTunnel()
    {
        _intentionalStop = true;
        _lifetime?.Cancel();
        Log("Tunnel stopped.");
    }

    private void Log(string message) => OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");

    private static async Task<string?> ReceiveTextMessageAsync(WebSocket webSocket, byte[] buffer, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    private static async Task SendJsonAsync(ClientWebSocket webSocket, object payload, SemaphoreSlim sendLock)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);

        await sendLock.WaitAsync();
        try
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        finally
        {
            sendLock.Release();
        }
    }
}

internal sealed class LocalStreamSession
{
    private readonly string _streamId;
    private readonly string _localHost;
    private readonly int _localPort;
    private readonly ClientWebSocket _controlSocket;
    private readonly SemaphoreSlim _sendLock;
    private readonly ConcurrentDictionary<string, LocalStreamSession> _registry;
    private readonly Action<string> _log;
    private readonly System.Threading.Channels.Channel<byte[]> _writeChannel;
    private readonly CancellationTokenSource _cts = new();
    private TcpClient? _localClient;
    private NetworkStream? _localStream;
    private bool _closeNotified;
    private int _started;

    public LocalStreamSession(string streamId, string localHost, int localPort, ClientWebSocket controlSocket, SemaphoreSlim sendLock, ConcurrentDictionary<string, LocalStreamSession> registry, Action<string> log)
    {
        _streamId = streamId;
        _localHost = localHost;
        _localPort = localPort;
        _controlSocket = controlSocket;
        _sendLock = sendLock;
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

            var payload = Convert.ToBase64String(buffer, 0, read);
            
            var json = JsonSerializer.Serialize(new
            {
                type = "stream_data",
                streamId = _streamId,
                payload_b64 = payload
            });
            var bytes = Encoding.UTF8.GetBytes(json);

            await _sendLock.WaitAsync(_cts.Token);
            try
            {
                if (_controlSocket.State == WebSocketState.Open)
                {
                    await _controlSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            finally
            {
                _sendLock.Release();
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
            
            var json = JsonSerializer.Serialize(new { type = "stream_close", streamId = _streamId });
            var bytes = Encoding.UTF8.GetBytes(json);
            
            try
            {
                await _sendLock.WaitAsync(TimeSpan.FromSeconds(2));
                try
                {
                    if (_controlSocket.State == WebSocketState.Open)
                    {
                        await _controlSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
                finally
                {
                    _sendLock.Release();
                }
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
