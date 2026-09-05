using Client.Core.Services;
using Xunit;

namespace Client.Desktop.Tests;

public class UpdateServiceTests
{
    [Fact]
    public void CurrentVersion_ReturnsValidVersionString()
    {
        var version = UpdateService.CurrentVersion;
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.True(version.Contains('.'));
    }

    [Fact]
    public void GetCurrentPlatformRID_ReturnsRecognizedPlatform()
    {
        var rid = UpdateService.GetCurrentPlatformRID();
        Assert.False(string.IsNullOrWhiteSpace(rid));
        Assert.True(rid.StartsWith("win-") || rid.StartsWith("linux-") || rid.StartsWith("osx-"));
    }
}
