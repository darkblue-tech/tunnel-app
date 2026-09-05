using System;
using System.IO;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CA1416

namespace Client.Core.Services;

/// <summary>
/// QUIC implementation of the control channel client.
/// Provides a fast, multiplexed, and secure connection.
/// </summary>
public class QuicControlChannelClient : IControlChannelClient
{
    private QuicConnection? _connection;
    private QuicStream? _controlStream;
    private StreamReader? _reader;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!QuicConnection.IsSupported)
        {
            throw new NotSupportedException("QUIC is not supported on this platform.");
        }

        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 443;
        
        var endpoint = new DnsEndPoint(host, port);

        var options = new QuicClientConnectionOptions
        {
            RemoteEndPoint = endpoint,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                TargetHost = host,
                ApplicationProtocols = new System.Collections.Generic.List<SslApplicationProtocol> 
                { 
                    new SslApplicationProtocol("tunnel-quic") 
                },
                // Validate the remote certificate
                RemoteCertificateValidationCallback = (sender, cert, chain, errors) => errors == SslPolicyErrors.None
            },
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
            MaxInboundBidirectionalStreams = 100,
            MaxInboundUnidirectionalStreams = 100,
            IdleTimeout = TimeSpan.FromMinutes(2)
        };

        _connection = await QuicConnection.ConnectAsync(options, cancellationToken);
        
        // Open the primary control stream
        _controlStream = await _connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken);
        _reader = new StreamReader(_controlStream, Encoding.UTF8, leaveOpen: true);
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

    public async Task SendStreamDataAsync(string streamId, ReadOnlyMemory<byte> payload)
    {
        if (_controlStream == null) return;

        var bufferWriter = new System.Buffers.ArrayBufferWriter<byte>(256 + payload.Length * 2);
        using (var jsonWriter = new Utf8JsonWriter(bufferWriter))
        {
            jsonWriter.WriteStartObject();
            jsonWriter.WriteString("type", "stream_data");
            jsonWriter.WriteString("streamId", streamId);
            jsonWriter.WriteBase64String("payload_b64", payload.Span);
            jsonWriter.WriteEndObject();
        }

        var span = bufferWriter.GetSpan(1);
        span[0] = (byte)'\n';
        bufferWriter.Advance(1);

        await _sendLock.WaitAsync();
        try
        {
            await _controlStream.WriteAsync(bufferWriter.WrittenMemory, CancellationToken.None);
            await _controlStream.FlushAsync();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public Task SendStreamCloseAsync(string streamId)
    {
        return SendJsonAsync(new { type = "stream_close", streamId });
    }

    public async Task<ControlMessageDto?> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (_reader == null) return null;

        try
        {
            var line = await _reader.ReadLineAsync(cancellationToken);
            if (line == null) return null; // Stream closed

            using var doc = JsonDocument.Parse(line);
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
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task SendJsonAsync(object payload)
    {
        if (_controlStream == null) return;
        
        var json = JsonSerializer.Serialize(payload) + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync();
        try
        {
            await _controlStream.WriteAsync(bytes, CancellationToken.None);
            await _controlStream.FlushAsync();
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
            if (_controlStream != null)
            {
                await _controlStream.DisposeAsync();
            }
            
            if (_connection != null)
            {
                await _connection.CloseAsync(0, cancellationToken: CancellationToken.None);
                await _connection.DisposeAsync();
            }
        }
        catch { }
        
        _sendLock.Dispose();
    }
}
