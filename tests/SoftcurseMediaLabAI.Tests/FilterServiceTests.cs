using OpenCvSharp;
using Xunit;

namespace SoftcurseMediaLabAI.Tests;

public sealed class FilterServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"softcurse_tests_{Guid.NewGuid():N}");

    public FilterServiceTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("blur")]
    [InlineData("sharpen")]
    [InlineData("noise")]
    public void FiltersCreateReadableOutputWithOriginalDimensions(string operation)
    {
        string input = Path.Combine(_directory, "input.png");
        string output = Path.Combine(_directory, $"{operation}.png");

        using (var image = new Mat(new Size(32, 24), MatType.CV_8UC3, new Scalar(30, 120, 220)))
            Assert.True(Cv2.ImWrite(input, image));

        switch (operation)
        {
            case "blur": FilterService.ApplyGaussianBlur(input, output, 4); break;
            case "sharpen": FilterService.ApplySharpen(input, output); break;
            case "noise": FilterService.ApplyNoise(input, output, 5); break;
        }

        using var result = Cv2.ImRead(output, ImreadModes.Unchanged);
        Assert.False(result.Empty());
        Assert.Equal(32, result.Width);
        Assert.Equal(24, result.Height);
    }

    [Fact]
    public void InvalidImageProducesActionableException()
    {
        string invalid = Path.Combine(_directory, "invalid.png");
        File.WriteAllText(invalid, "not an image");

        var error = Assert.Throws<ArgumentException>(() =>
            FilterService.ApplyGaussianBlur(invalid, Path.Combine(_directory, "output.png")));

        Assert.Contains("could not be read", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingOutputDirectoryProducesActionableException()
    {
        string input = Path.Combine(_directory, "valid.png");
        using (var image = new Mat(new Size(8, 8), MatType.CV_8UC3, Scalar.All(80)))
            Assert.True(Cv2.ImWrite(input, image));

        var error = Assert.Throws<ArgumentException>(() =>
            FilterService.ApplySharpen(input, Path.Combine(_directory, "missing", "output.png")));

        Assert.Contains("output directory", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
