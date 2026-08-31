using OpenCvSharp;
using Xunit;

namespace SoftcurseMediaLabAI.Tests;

public sealed class ImageFileValidatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"softcurse_validation_{Guid.NewGuid():N}");

    public ImageFileValidatorTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ValidImageReturnsDimensionsAndSize()
    {
        string path = Path.Combine(_directory, "valid.png");
        using (var image = new Mat(new Size(17, 11), MatType.CV_8UC3, Scalar.All(120)))
            Assert.True(Cv2.ImWrite(path, image));

        ImageFileInfo info = ImageFileValidator.Validate(path);
        Assert.Equal(17, info.PixelWidth);
        Assert.Equal(11, info.PixelHeight);
        Assert.True(info.FileBytes > 0);
    }

    [Fact]
    public void NonImageIsRejected()
    {
        string path = Path.Combine(_directory, "fake.png");
        File.WriteAllText(path, "not an image");
        Assert.Throws<InvalidDataException>(() => ImageFileValidator.Validate(path));
    }

    [Fact]
    public void ConfiguredPixelLimitIsEnforced()
    {
        string path = Path.Combine(_directory, "large.png");
        using (var image = new Mat(new Size(20, 20), MatType.CV_8UC3, Scalar.All(50)))
            Assert.True(Cv2.ImWrite(path, image));

        Assert.Throws<InvalidDataException>(() => ImageFileValidator.Validate(path, maxPixels: 399));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
