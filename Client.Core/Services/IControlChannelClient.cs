using System;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Core.Services;

/// <summary>
/// Data transfer object for control messages between client and server.
/// </summary>
public class ControlMessageDto
{
    /// <summary>
    /// The type of the control message.
    /// </summary>
    public string Type { get; set; } = "";
    
    /// <summary>
    /// The unique client identifier.
    /// </summary>
    public string? ClientId { get; set; }
    
    /// <summary>
    /// Error message, if the control message represents an error.
    /// </summary>
    public string? Error { get; set; }
    
    /// <summary>
    /// The public URL allocated for a registered tunnel.
    /// </summary>
    public string? PublicUrl { get; set; }
    
    /// <summary>
    /// The identifier for the data stream.
    /// </summary>
    public string? StreamId { get; set; }
    
    /// <summary>
    /// Binary payload data for the stream.
    /// </summary>
    public byte[]? Payload { get; set; }
}

/// <summary>
/// Defines the contract for a control channel client used to orchestrate tunnel connections.
/// </summary>
public interface IControlChannelClient : IAsyncDisposable
{
    /// <summary>
    /// Connects to the control channel endpoint.
    /// </summary>
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
    
    /// <summary>
    /// Authenticates the client using an access token.
    /// </summary>
    Task SendAuthAsync(string clientName, string accessToken);
    
    /// <summary>
    /// Sends a ping message to keep the connection alive.
    /// </summary>
    Task SendPingAsync();
    
    /// <summary>
    /// Registers a new tunnel with the server.
    /// </summary>
    Task SendRegisterTunnelAsync(string subdomain, string localHost, int localPort, int publicPort = 0);
    
    /// <summary>
    /// Sends stream data over the control channel.
    /// </summary>
    Task SendStreamDataAsync(string streamId, ReadOnlyMemory<byte> payload);
    
    /// <summary>
    /// Notifies the server that a stream is closed.
    /// </summary>
    Task SendStreamCloseAsync(string streamId);
    
    /// <summary>
    /// Receives the next control message from the server asynchronously.
    /// </summary>
    Task<ControlMessageDto?> ReceiveAsync(CancellationToken cancellationToken);
}
