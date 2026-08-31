using Xunit;

namespace SoftcurseMediaLabAI.Tests;

public sealed class ExternalIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"softcurse_external_{Guid.NewGuid():N}");

    public ExternalIntegrationTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ApiPathIsAppendedExactlyOnce()
    {
        Assert.Equal("http://127.0.0.1:7860/sdapi/v1/options",
            GenerativeApiClient.BuildUri("http://127.0.0.1:7860/", "sdapi/v1/options").AbsoluteUri);
        Assert.Equal("http://127.0.0.1:7860/sdapi/v1/options",
            GenerativeApiClient.BuildUri("http://127.0.0.1:7860/sdapi/v1/options", "sdapi/v1/options").AbsoluteUri);
    }

    [Theory]
    [InlineData("http://localhost:7860/", false)]
    [InlineData("http://127.0.0.1:7860/", false)]
    [InlineData("https://api.example.com/", true)]
    public void RemoteEndpointDetectionIsExplicit(string endpoint, bool expected)
    {
        Assert.Equal(expected, GenerativeApiClient.IsRemoteEndpoint(endpoint));
    }

    [Fact]
    public void AbsoluteFfmpegOverrideIsAccepted()
    {
        string executable = Path.Combine(_directory, "ffmpeg.exe");
        File.WriteAllBytes(executable, Array.Empty<byte>());
        Assert.Equal(executable, FfmpegService.ResolveExecutable(executable));
    }

    [Fact]
    public void RelativeTraversalAndMissingAbsolutePathAreRejected()
    {
        Assert.Throws<FileNotFoundException>(() => FfmpegService.ResolveExecutable("ffmpeg"));
        Assert.Throws<ArgumentException>(() => FfmpegService.ResolveExecutable("custom-ffmpeg"));
        Assert.Throws<ArgumentException>(() => FfmpegService.ResolveExecutable("tools/ffmpeg.exe"));
        Assert.Throws<FileNotFoundException>(() =>
            FfmpegService.ResolveExecutable(Path.Combine(_directory, "missing.exe")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
