using Client.Core.Services;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Client.Desktop.Tests;

public class SessionStabilityTests : IDisposable
{
    private readonly string _testDir;

    public SessionStabilityTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "darktunnel_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    private static string CreateMockJwt(DateTimeOffset expiration)
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        
        var payloadObj = new { sub = "12345", name = "Test User", exp = expiration.ToUnixTimeSeconds() };
        var payloadJson = JsonSerializer.Serialize(payloadObj);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        return $"{header}.{payload}.dGVzdHNpZ25hdHVyZQ";
    }

    [Fact]
    public void IsTokenExpired_ReturnsTrue_ForExpiredToken()
    {
        var expiredToken = CreateMockJwt(DateTimeOffset.UtcNow.AddMinutes(-10));
        Assert.True(AuthService.IsTokenExpired(expiredToken));
    }

    [Fact]
    public void IsTokenExpired_ReturnsFalse_ForValidToken()
    {
        var validToken = CreateMockJwt(DateTimeOffset.UtcNow.AddHours(2));
        Assert.False(AuthService.IsTokenExpired(validToken));
    }

    [Fact]
    public void IsTokenExpired_ReturnsTrue_WhenExpiringWithinSkew()
    {
        // Token expires in 30 seconds, skew is 60 seconds -> should be treated as expired
        var nearExpiredToken = CreateMockJwt(DateTimeOffset.UtcNow.AddSeconds(30));
        Assert.True(AuthService.IsTokenExpired(nearExpiredToken, skewSeconds: 60));
    }

    [Fact]
    public void IsTokenExpired_HandlesEmptyOrNull()
    {
        Assert.True(AuthService.IsTokenExpired(null!));
        Assert.True(AuthService.IsTokenExpired(""));
        Assert.True(AuthService.IsTokenExpired("   "));
    }

    [Fact]
    public void IsTokenExpired_HandlesMalformedString()
    {
        // Malformed non-JWT string cannot be verified as expired statically
        Assert.False(AuthService.IsTokenExpired("not-a-jwt"));
    }

    [Fact]
    public async Task FallbackSecretStorage_PersistsEncryptedSecretsAcrossInstances()
    {
        var instance1 = new FallbackSecretStorageProvider(_testDir);
        await instance1.SaveSecretAsync("refresh_token", "rt_secret_value_12345");
        await instance1.SaveSecretAsync("theme", "Dark");

        // Verify the file was physically created on disk
        var files = Directory.GetFiles(_testDir, "sec_*.dat");
        Assert.Equal(2, files.Length);

        // Verify file contents are encrypted (not plain text)
        var rawBytes = await File.ReadAllBytesAsync(files[0]);
        var rawText = Encoding.UTF8.GetString(rawBytes);
        Assert.DoesNotContain("rt_secret_value_12345", rawText);
        Assert.DoesNotContain("Dark", rawText);

        // Verify a new second instance correctly reads and decrypts the persisted secret
        var instance2 = new FallbackSecretStorageProvider(_testDir);
        var retrievedRt = await instance2.GetSecretAsync("refresh_token");
        var retrievedTheme = await instance2.GetSecretAsync("theme");

        Assert.Equal("rt_secret_value_12345", retrievedRt);
        Assert.Equal("Dark", retrievedTheme);

        // Verify clearing removes the file
        await instance2.ClearSecretAsync("theme");
        var clearedTheme = await instance2.GetSecretAsync("theme");
        Assert.Null(clearedTheme);

        var remainingFiles = Directory.GetFiles(_testDir, "sec_*.dat");
        Assert.Single(remainingFiles);
    }
}
