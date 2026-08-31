using OpenCvSharp;
using Xunit;

namespace SoftcurseMediaLabAI.Tests;

public sealed class RegionSelectServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"softcurse_region_{Guid.NewGuid():N}");

    public RegionSelectServiceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task ClickedForegroundIsSelectedWhileImageCornerRemainsBackground()
    {
        string input = Path.Combine(_directory, "subject.png");
        using (var image = new Mat(new Size(120, 120), MatType.CV_8UC3, new Scalar(220, 80, 20)))
        {
            Cv2.Rectangle(image, new Rect(40, 35, 40, 50), new Scalar(20, 20, 230), -1);
            Assert.True(Cv2.ImWrite(input, image));
        }

        var service = new RegionSelectService();
        string maskPath = await service.GenerateMaskAsync(input, new System.Windows.Point(60, 60));
        try
        {
            using Mat mask = Cv2.ImRead(maskPath, ImreadModes.Grayscale);
            Assert.False(mask.Empty());
            Assert.True(mask.At<byte>(60, 60) > 0, "The clicked subject should be selected.");
            Assert.Equal(0, mask.At<byte>(5, 5));
        }
        finally
        {
            if (File.Exists(maskPath)) File.Delete(maskPath);
        }
    }

    [Fact]
    public async Task PointOutsideImageIsRejected()
    {
        string input = Path.Combine(_directory, "small.png");
        using (var image = new Mat(new Size(20, 20), MatType.CV_8UC3, Scalar.All(100)))
            Assert.True(Cv2.ImWrite(input, image));

        var service = new RegionSelectService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.GenerateMaskAsync(input, new System.Windows.Point(25, 10)));
    }

    [Fact]
    public async Task CancellationIsHonoredBeforeWorkStarts()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new RegionSelectService();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GenerateMaskAsync("unused.png", new System.Windows.Point(0, 0), cancellation.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
