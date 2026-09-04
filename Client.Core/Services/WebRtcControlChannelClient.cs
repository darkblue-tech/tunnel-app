using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SIPSorcery.Net;

namespace Client.Core.Services;

public class WebRtcControlChannelClient : IControlChannelClient
{
    private RTCPeerConnection? _pc;
    private RTCDataChannel? _dc;
    private readonly Channel<ControlMessageDto> _receiveChannel = Channel.CreateUnbounded<ControlMessageDto>();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly string _serverToken;
    private readonly string _clientId = Guid.NewGuid().ToString("N");
    private readonly HttpClient _httpClient = new();
    
    private TaskCompletionSource<bool> _dataChannelOpen = new();

    public WebRtcControlChannelClient(string serverToken)
    {
        _serverToken = serverToken;
    }

    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        var port = uri.Port > 0 ? uri.Port : 443;
        var signalingBaseUrl = $"https://{uri.Host}:{port}/webrtc";
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _serverToken);

        var rtcConfig = new RTCConfiguration
        {
            iceServers = new System.Collections.Generic.List<RTCIceServer>
            {
                new RTCIceServer { urls = "stun:stun.l.google.com:19302" },
                new RTCIceServer { urls = "stun:stun.yandex.ru:3478" },
                new RTCIceServer { urls = "stun:stun.mail.ru:3478" },
                new RTCIceServer { urls = "stun:stun.cloudflare.com:3478" }
            }
        };

        _pc = new RTCPeerConnection(rtcConfig);
        _dc = await _pc.createDataChannel("control");

        _dc.onmessage += (RTCDataChannel dc, SIPSorcery.Net.DataChannelPayloadProtocols protocol, byte[] data) =>
        {
            if (protocol == SIPSorcery.Net.DataChannelPayloadProtocols.WebRTC_String)
            {
                var jsonStr = Encoding.UTF8.GetString(data);
                try
                {
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
                    _receiveChannel.Writer.TryWrite(dto);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WebRTC JSON parse error: {ex.Message}");
                }
            }
        };

        _dc.onclose += () => _receiveChannel.Writer.TryComplete();
        _pc.onconnectionstatechange += (state) =>
        {
            if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed)
            {
                _receiveChannel.Writer.TryComplete();
            }
        };

        _dc.onopen += () => _dataChannelOpen.TrySetResult(true);

        bool offerSent = false;
        var bufferedCandidates = new System.Collections.Generic.List<RTCIceCandidate>();

        void SendCandidate(RTCIceCandidate candidate)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var icePayload = new
                    {
                        clientId = _clientId,
                        candidate = candidate.candidate,
                        sdpMid = candidate.sdpMid,
                        sdpMLineIndex = candidate.sdpMLineIndex
                    };
                    await _httpClient.PostAsJsonAsync($"{signalingBaseUrl}/ice", icePayload, cancellationToken);
                }
                catch { }
            });
        }

        _pc.onicecandidate += (candidate) =>
        {
            if (candidate != null)
            {
                lock (bufferedCandidates)
                {
                    if (!offerSent)
                    {
                        bufferedCandidates.Add(candidate);
                        return;
                    }
                }
                SendCandidate(candidate);
            }
        };

        var offer = _pc.createOffer(null);
        await _pc.setLocalDescription(offer);

        var offerPayload = new
        {
            clientId = _clientId,
            sdp = offer.sdp
        };

        var response = await _httpClient.PostAsJsonAsync($"{signalingBaseUrl}/offer", offerPayload, cancellationToken);
        response.EnsureSuccessStatusCode();

        lock (bufferedCandidates)
        {
            offerSent = true;
            foreach (var c in bufferedCandidates)
            {
                SendCandidate(c);
            }
            bufferedCandidates.Clear();
        }

        var answerJson = await response.Content.ReadAsStringAsync();
        using var answerDoc = JsonDocument.Parse(answerJson);
        var answerSdp = answerDoc.RootElement.GetProperty("sdp").GetString();

        var remoteDesc = new RTCSessionDescriptionInit
        {
            sdp = answerSdp,
            type = RTCSdpType.answer
        };
        _pc.setRemoteDescription(remoteDesc);

        // Wait for data channel to open
        var tcs = Task.WhenAny(_dataChannelOpen.Task, Task.Delay(15000, cancellationToken));
        await tcs;
        if (!_dataChannelOpen.Task.IsCompleted)
        {
            throw new TimeoutException("WebRTC DataChannel did not open within the timeout.");
        }
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

    public Task SendStreamDataAsync(string streamId, ReadOnlyMemory<byte> payload)
    {
        var b64 = Convert.ToBase64String(payload.Span);
        return SendJsonAsync(new { type = "stream_data", streamId, payload_b64 = b64 });
    }

    public Task SendStreamCloseAsync(string streamId)
    {
        return SendJsonAsync(new { type = "stream_close", streamId });
    }

    public async Task<ControlMessageDto?> ReceiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await _receiveChannel.Reader.WaitToReadAsync(cancellationToken))
            {
                if (_receiveChannel.Reader.TryRead(out var msg))
                {
                    return msg;
                }
            }
        }
        catch (OperationCanceledException) { }
        return null;
    }

    private async Task SendJsonAsync(object payload)
    {
        if (_dc == null || _dc.readyState != RTCDataChannelState.open) return;

        var json = JsonSerializer.Serialize(payload);

        await _sendLock.WaitAsync();
        try
        {
            _dc.send(json);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        _sendLock.Dispose();
        
        try
        {
            _dc?.close();
            _pc?.Close("Disposed");
        }
        catch { }

        await Task.CompletedTask;
    }
}
