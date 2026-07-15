using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;

namespace Client.Desktop.Grpc;

/// <summary>
/// gRPC client for tunnel control channel.
/// Implements type-safe bi-directional stream communication with server.
/// </summary>
internal class ControlChannelGrpcClient : IAsyncDisposable
{
    private GrpcChannel? _channel;
    private TunnelControl.TunnelControlClient? _client;
    private AsyncDuplexStreamingCall<ControlMessage, ControlResponse>? _streamCall;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Connects to gRPC server and establishes control channel.
    /// </summary>
    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        var channelOptions = new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler 
            { 
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
                KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(10)
            },
            Credentials = ChannelCredentials.Insecure, // TODO: Add TLS in production
            DisposeHttpClient = true,
            MaxReceiveMessageSize = 16 * 1024 * 1024 // 16MB
        };

        _channel = GrpcChannel.ForAddress(uri, channelOptions);
        _client = new TunnelControl.TunnelControlClient(_channel);
        _cts = new CancellationTokenSource();

        // Start bi-directional stream
        _streamCall = _client.StreamControlMessages(cancellationToken: cancellationToken);
        
        // Start receive loop in background
        _ = Task.Run(() => ReceiveLoopAsync(cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Sends authentication message to server.
    /// </summary>
    public async Task SendAuthAsync(string clientName, string? accessToken = null)
    {
        if (_streamCall?.RequestStream == null)
            throw new InvalidOperationException("Not connected to gRPC server");

        var message = new ControlMessage 
        { 
            Auth = new AuthMessage 
            { 
                ClientName = clientName,
                AccessToken = accessToken ?? ""
            }
        };

        await _streamCall.RequestStream.WriteAsync(message);
    }

    /// <summary>
    /// Sends heartbeat ping to server.
    /// </summary>
    public async Task SendPingAsync()
    {
        if (_streamCall?.RequestStream == null)
            throw new InvalidOperationException("Not connected to gRPC server");

        var message = new ControlMessage 
        { 
            Ping = new PingMessage()
        };

        await _streamCall.RequestStream.WriteAsync(message);
    }

    /// <summary>
    /// Sends tunnel registration request to server.
    /// </summary>
    public async Task SendRegisterTunnelAsync(
        string subdomain,
        string localHost,
        int localPort,
        int publicPort = 0)
    {
        if (_streamCall?.RequestStream == null)
            throw new InvalidOperationException("Not connected to gRPC server");

        var message = new ControlMessage 
        { 
            RegisterTunnel = new RegisterTunnelMessage 
            { 
                Subdomain = subdomain,
                LocalHost = localHost,
                LocalPort = localPort,
                PublicPort = publicPort
            }
        };

        await _streamCall.RequestStream.WriteAsync(message);
    }

    /// <summary>
    /// Sends stream data to server (binary payload, no base64 encoding needed).
    /// </summary>
    public async Task SendStreamDataAsync(string streamId, byte[] payload)
    {
        if (_streamCall?.RequestStream == null)
            throw new InvalidOperationException("Not connected to gRPC server");

        var message = new ControlMessage 
        { 
            StreamData = new StreamDataMessage 
            { 
                StreamId = streamId,
                Payload = Google.Protobuf.ByteString.CopyFrom(payload)
            }
        };

        await _streamCall.RequestStream.WriteAsync(message);
    }

    /// <summary>
    /// Sends stream close notification to server.
    /// </summary>
    public async Task SendStreamCloseAsync(string streamId)
    {
        if (_streamCall?.RequestStream == null)
            throw new InvalidOperationException("Not connected to gRPC server");

        var message = new ControlMessage 
        { 
            StreamClose = new StreamCloseMessage 
            { 
                StreamId = streamId
            }
        };

        await _streamCall.RequestStream.WriteAsync(message);
    }

    /// <summary>
    /// Receives response from server.
    /// Returns null when stream is closed.
    /// </summary>
    public async Task<ControlResponse?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        if (_streamCall?.ResponseStream == null)
            throw new InvalidOperationException("Not connected to gRPC server");

        try
        {
            if (await _streamCall.ResponseStream.MoveNext(cancellationToken))
            {
                return _streamCall.ResponseStream.Current;
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            // Stream was cancelled or closed normally
        }

        return null;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && await ReceiveAsync(cancellationToken) != null)
            {
                // Responses are handled by calling ReceiveAsync explicitly
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            // Stream ended normally
        }
    }

    /// <summary>
    /// Closes the connection gracefully and releases resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_streamCall?.RequestStream != null)
            {
                await _streamCall.RequestStream.CompleteAsync();
            }
        }
        catch
        {
            // Ignore errors during cleanup
        }

        _cts?.Cancel();
        _cts?.Dispose();

        if (_channel != null)
        {
            await _channel.ShutdownAsync();
            _channel.Dispose();
        }

        _streamCall?.Dispose();
    }
}
