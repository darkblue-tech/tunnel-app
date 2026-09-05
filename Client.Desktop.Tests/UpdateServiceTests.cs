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

    [Theory]
    [InlineData("1.0.2r", "1.0.2", false)]
    [InlineData("1.0.2", "1.0.2r", false)]
    [InlineData("1.0.2r", "1.0.2r", false)]
    [InlineData("v1.0.2r", "1.0.2r", false)]
    [InlineData("1.0.3", "1.0.2r", true)]
    [InlineData("1.0.3r", "1.0.2r", true)]
    [InlineData("2.0.0", "1.0.2r", true)]
    [InlineData("1.0.1", "1.0.2r", false)]
    [InlineData("1.0.1r", "1.0.2r", false)]
    public void IsVersionNewer_CorrectlyComparesVersions(string latest, string current, bool expected)
    {
        var result = UpdateService.IsVersionNewer(latest, current);
        Assert.Equal(expected, result);
    }
}
