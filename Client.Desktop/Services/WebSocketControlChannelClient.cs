using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Desktop.Services;

public class WebSocketControlChannelClient : IControlChannelClient
{
    private readonly ClientWebSocket _ws = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly byte[] _receiveBuffer = new byte[16 * 1024];

    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        await _ws.ConnectAsync(uri, cancellationToken);
    }

    public Task SendAuthAsync(string clientName, string accessToken)
    {
        return SendJsonAsync(new { type = "auth", clientName, access_token = accessToken });
    }

    public Task SendPingAsync() => SendJsonAsync(new { type = "ping" });

    public Task SendRegisterTunnelAsync(string subdomain, string localHost, int localPort, int publicPort = 0)
    {
        return SendJsonAsync(new { type = "register_tunnel", subdomain, localHost, localPort, publicPort });
    }

    public Task SendStreamDataAsync(string streamId, byte[] payload)
    {
        var b64 = Convert.ToBase64String(payload);
        return SendJsonAsync(new { type = "stream_data", streamId, payload_b64 = b64 });
    }

    public Task SendStreamCloseAsync(string streamId)
    {
        return SendJsonAsync(new { type = "stream_close", streamId });
    }

    public async Task<ControlMessageDto?> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (_ws.State != WebSocketState.Open) return null;

        using var ms = new MemoryStream();
        while (true)
        {
            var result = await _ws.ReceiveAsync(new ArraySegment<byte>(_receiveBuffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;

            ms.Write(_receiveBuffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                var jsonStr = Encoding.UTF8.GetString(ms.ToArray());
                using var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;
                
                var type = root.GetProperty("type").GetString() ?? "";
                var dto = new ControlMessageDto { Type = type };
                
                switch (type)
                {
                    case "auth_ok":
                        dto.ClientId = root.GetProperty("clientId").GetString();
                        break;
                    case "auth_error":
                    case "register_error":
                    case "rate_limited":
                        dto.Error = root.GetProperty("error").GetString();
                        break;
                    case "register_ack":
                        dto.PublicUrl = root.GetProperty("publicUrl").GetString();
                        break;
                    case "open_stream":
                    case "stream_close":
                        dto.StreamId = root.GetProperty("streamId").GetString();
                        break;
                    case "stream_data":
                        dto.StreamId = root.GetProperty("streamId").GetString();
                        var b64 = root.GetProperty("payload_b64").GetString();
                        if (b64 != null) dto.Payload = Convert.FromBase64String(b64);
                        break;
                }
                return dto;
            }
        }
    }

    private async Task SendJsonAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync();
        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
        }
        catch { }
        _ws.Dispose();
        _sendLock.Dispose();
    }
}
