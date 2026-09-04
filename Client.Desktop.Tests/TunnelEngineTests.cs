using Client.Core.Services;
using System.Threading.Tasks;
using Xunit;
using System;

namespace Client.Desktop.Tests;

public class TunnelEngineTests
{
    [Fact]
    public async Task StopTunnel_IntentionallyStopsWithoutReconnecting()
    {
        var engine = new TunnelEngine();
        bool disconnectedCalled = false;
        engine.OnDisconnected += () => disconnectedCalled = true;

        _ = engine.StartTunnelAsync("ws://localhost:9999", "test", "127.0.0.1", 8080, 7000, "token");
        
        await Task.Delay(100);

        engine.StopTunnel();

        Assert.False(disconnectedCalled);
    }
}
