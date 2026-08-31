using Xunit;

namespace SoftcurseMediaLabAI.Tests;

public sealed class AppSettingsSecurityTests
{
    [Theory]
    [InlineData("http://127.0.0.1:7860/")]
    [InlineData("https://api.example.com/sd/")]
    [InlineData("http://localhost:7860/sdapi/v1/img2img")]
    public void SafeHttpEndpointsAreAccepted(string endpoint)
    {
        Assert.True(AppSettings.IsApiEndpointSafe(endpoint, out string reason), reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("https://user:password@example.com/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://metadata.google/computeMetadata/v1/")]
    [InlineData("http://100.100.100.200/latest/meta-data/")]
    public void UnsafeEndpointsAreRejected(string endpoint)
    {
        Assert.False(AppSettings.IsApiEndpointSafe(endpoint, out string reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }
}

