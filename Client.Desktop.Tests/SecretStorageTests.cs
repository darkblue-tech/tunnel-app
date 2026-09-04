using Client.Core.Services;
using System.Threading.Tasks;
using Xunit;

namespace Client.Desktop.Tests;

public class SecretStorageTests
{
    [Fact]
    public async Task SaveAndGetSecret_Works()
    {
        var storage = new SecretStorage();
        var testKey = "test_access_token";
        var testSecret = "test_value_123";

        await storage.SaveSecretAsync(testKey, testSecret);
        var retrieved = await storage.GetSecretAsync(testKey);

        Assert.Equal(testSecret, retrieved);
    }
}
