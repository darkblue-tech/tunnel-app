using Client.Core.Services;
using Xunit;

namespace Client.Desktop.Tests;

public class SingleInstanceIpcTests
{
    [Theory]
    [InlineData("darktunnel://auth?code=standard_code_123", "standard_code_123")]
    [InlineData("darktunnel://auth/?code=standard_code_123", "standard_code_123")]
    [InlineData("\"darktunnel://auth?code=quoted_code_456\"", "quoted_code_456")]
    [InlineData("'darktunnel://auth?code=single_quoted_789'", "single_quoted_789")]
    [InlineData("darktunnel://auth?state=xyz&code=multi_param_code&scope=openid", "multi_param_code")]
    [InlineData("darktunnel://auth/?code=code_with_trailing_slash/", "code_with_trailing_slash")]
    [InlineData("darktunnel://auth?code=url%20encoded%2Bcode%3D", "url encoded+code=")]
    [InlineData("darktunnel://auth?code=hash_fragment_code#some_fragment", "hash_fragment_code")]
    [InlineData("darktunnel://auth/?other=123&code=complex%2Ftest%3D%3D&user=admin", "complex/test==")]
    public void ExtractAuthCode_ValidUrls_ExtractsCodeCorrectly(string input, string expectedCode)
    {
        var result = SingleInstanceIpc.ExtractAuthCode(input);
        Assert.Equal(expectedCode, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("wakeup")]
    [InlineData("https://tunnel.darkblue.tech")]
    [InlineData("darktunnel://invalid_no_code")]
    [InlineData("darktunnel://auth?nocode=123")]
    public void ExtractAuthCode_InvalidOrNonAuthUrls_ReturnsNull(string? input)
    {
        var result = SingleInstanceIpc.ExtractAuthCode(input!);
        Assert.Null(result);
    }
}
