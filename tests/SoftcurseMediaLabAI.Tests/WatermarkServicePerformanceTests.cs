using OpenCvSharp;
using Xunit;

namespace SoftcurseMediaLabAI.Tests;

public sealed class WatermarkServicePerformanceTests
{
    [Fact]
    public void EmptyAutomaticMaskSkipsModelInitializationAndPreservesPixels()
    {
        using var service = new WatermarkService();
        using var input = new Mat(new Size(128, 96), MatType.CV_8UC3, Scalar.All(80));
        using Mat result = service.RemoveWatermarkFromMat(input);

        Assert.False(service.IsInitialized);
        using var difference = new Mat();
        Cv2.Absdiff(input, result, difference);
        Assert.Equal(0, Cv2.CountNonZero(difference.Reshape(1)));
    }

    [Fact]
    public void PassThroughApiReturnsNoAllocationForAnEmptyManualMask()
    {
        using var service = new WatermarkService();
        using var image = new Mat(new Size(640, 360), MatType.CV_8UC3, new Scalar(20, 30, 40));
        byte[] emptyMask = new byte[image.Rows * image.Cols];

        bool changed = service.TryRemoveWatermarkFromMat(image, out Mat? result, emptyMask);

        Assert.False(changed);
        Assert.Null(result);
        Assert.False(service.IsInitialized);
    }
}
