using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Desktop
{
    public sealed class ControlChannelClient : IAsyncDisposable
    {
        private readonly ClientWebSocket _ws = new();

        public bool IsConnected => _ws.State == WebSocketState.Open;

        public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            await _ws.ConnectAsync(uri, cancellationToken);
        }

        public Task SendAuthAsync(string clientName, string? accessToken)
        {
            return SendJsonAsync(new { type = "auth", clientName, access_token = accessToken });
        }

        public Task SendPingAsync() => SendJsonAsync(new { type = "ping" });

        public Task SendRegisterTunnelAsync(string subdomain, string localHost, int localPort, int publicPort = 0)
        {
            if (publicPort > 0)
            {
                return SendJsonAsync(new { type = "register_tunnel", subdomain, localHost, localPort, publicPort });
            }

            return SendJsonAsync(new { type = "register_tunnel", subdomain, localHost, localPort });
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

        public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[16 * 1024];
            using var ms = new System.IO.MemoryStream();
            while (true)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;

                ms.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
        }

        private async Task SendJsonAsync(object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                }
            }
            catch { }

            _ws.Dispose();
        }
    }
}
