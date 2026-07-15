using System;

namespace Client.Desktop
{
    // Placeholder for WebRTC client integration.
    // Suggested libraries:
    // - Microsoft.MixedReality.WebRTC (for .NET desktop client)
    // - use SIPSorcery on server-side for a pure C# WebRTC peer
    // Implementation notes:
    // - Client should perform signaling via Server.Api (/api/v1/webrtc/offer)
    // - Create DataChannel for multiplexed streams to carry tunneled TCP data
    public class WebRtcClient
    {
        public WebRtcClient()
        {
        }

        public void Start()
        {
            Console.WriteLine("WebRTC client placeholder — implement using MixedReality.WebRTC or other library.");
        }
    }
}
