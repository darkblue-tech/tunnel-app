using System;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Desktop.Services;

public class ControlMessageDto
{
    public string Type { get; set; } = "";
    public string? ClientId { get; set; }
    public string? Error { get; set; }
    public string? PublicUrl { get; set; }
    public string? StreamId { get; set; }
    public byte[]? Payload { get; set; }
}

public interface IControlChannelClient : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
    Task SendAuthAsync(string clientName, string accessToken);
    Task SendPingAsync();
    Task SendRegisterTunnelAsync(string subdomain, string localHost, int localPort, int publicPort = 0);
    Task SendStreamDataAsync(string streamId, byte[] payload);
    Task SendStreamCloseAsync(string streamId);
    Task<ControlMessageDto?> ReceiveAsync(CancellationToken cancellationToken);
}
