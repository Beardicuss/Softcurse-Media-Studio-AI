using OpenCvSharp;
using Xunit;

namespace SoftcurseMediaLabAI.Tests;

public sealed class WatermarkMaskDetectorTests
{
    [Fact]
    public void DetectsBrightTextInLowerRightWithoutMaskingUpperLeft()
    {
        using var image = new Mat(new Size(400, 240), MatType.CV_8UC3, Scalar.All(75));
        Cv2.PutText(image, "WM", new Point(300, 205), HersheyFonts.HersheySimplex,
            1.2, Scalar.All(235), 2, LineTypes.AntiAlias);

        using Mat mask = WatermarkMaskDetector.Detect(image);

        using Mat lowerRight = new Mat(mask, new Rect(200, 120, 200, 120));
        using Mat upperLeft = new Mat(mask, new Rect(0, 0, 200, 120));
        Assert.True(Cv2.CountNonZero(lowerRight) > 0);
        Assert.Equal(0, Cv2.CountNonZero(upperLeft));
    }

    [Fact]
    public void UniformImageProducesEmptyMask()
    {
        using var image = new Mat(new Size(200, 120), MatType.CV_8UC3, Scalar.All(180));
        using Mat mask = WatermarkMaskDetector.Detect(image);
        Assert.Equal(0, Cv2.CountNonZero(mask));
    }
}
