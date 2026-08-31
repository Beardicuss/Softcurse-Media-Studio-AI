using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace SoftcurseMediaLabAI
{
    /// <summary>
    /// Click-guided foreground selection powered by OpenCV GrabCut.
    /// The clicked point is a definite foreground seed; no SAM model is required.
    /// </summary>
    public sealed class RegionSelectService
    {
        public Task InitializeAsync() => Task.CompletedTask;

        public Task<string> GenerateMaskAsync(
            string imagePath,
            System.Windows.Point clickPoint,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                using Mat image = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (image.Empty())
                    throw new InvalidOperationException($"Could not read image: {imagePath}");

                int clickX = (int)Math.Round(clickPoint.X);
                int clickY = (int)Math.Round(clickPoint.Y);
                if (clickX < 0 || clickX >= image.Width || clickY < 0 || clickY >= image.Height)
                    throw new ArgumentOutOfRangeException(nameof(clickPoint), "The selection point is outside the image.");

                using Mat mask = GenerateMask(image, new OpenCvSharp.Point(clickX, clickY), cancellationToken);
                string outputPath = TempFileManager.CreateTempPath("region_mask", ".png");
                if (!Cv2.ImWrite(outputPath, mask))
                    throw new IOException($"Failed to write region mask: {outputPath}");

                return outputPath;
            }, cancellationToken);
        }

        internal static Mat GenerateMask(Mat image, OpenCvSharp.Point clickPoint, CancellationToken cancellationToken = default)
        {
            if (image.Empty()) throw new ArgumentException("Input image is empty.", nameof(image));
            cancellationToken.ThrowIfCancellationRequested();

            // Definite background border, probable background interior.
            Mat grabCutMask = new Mat(image.Size(), MatType.CV_8UC1, Scalar.All((int)GrabCutClasses.PR_BGD));
            int border = Math.Clamp(Math.Min(image.Width, image.Height) / 50, 1, 12);
            Cv2.Rectangle(grabCutMask, new Rect(0, 0, image.Width, image.Height),
                Scalar.All((int)GrabCutClasses.BGD), border);

            // A click-centred region is probable foreground; the click itself is definite foreground.
            int regionWidth = Math.Max(3, image.Width / 3);
            int regionHeight = Math.Max(3, image.Height / 3);
            int regionX = Math.Clamp(clickPoint.X - regionWidth / 2, border, Math.Max(border, image.Width - border - regionWidth));
            int regionY = Math.Clamp(clickPoint.Y - regionHeight / 2, border, Math.Max(border, image.Height - border - regionHeight));
            regionWidth = Math.Min(regionWidth, image.Width - border - regionX);
            regionHeight = Math.Min(regionHeight, image.Height - border - regionY);
            if (regionWidth <= 0 || regionHeight <= 0)
            {
                grabCutMask.Dispose();
                throw new InvalidOperationException("The image is too small for region selection.");
            }

            Cv2.Rectangle(grabCutMask, new Rect(regionX, regionY, regionWidth, regionHeight),
                Scalar.All((int)GrabCutClasses.PR_FGD), -1);
            int seedRadius = Math.Clamp(Math.Min(image.Width, image.Height) / 80, 2, 8);
            Cv2.Circle(grabCutMask, clickPoint, seedRadius, Scalar.All((int)GrabCutClasses.FGD), -1);

            using Mat backgroundModel = new Mat();
            using Mat foregroundModel = new Mat();
            Cv2.GrabCut(image, grabCutMask, new Rect(), backgroundModel, foregroundModel, 4, GrabCutModes.InitWithMask);
            cancellationToken.ThrowIfCancellationRequested();

            using Mat definiteForeground = new Mat();
            using Mat probableForeground = new Mat();
            Cv2.Compare(grabCutMask, new Scalar((int)GrabCutClasses.FGD), definiteForeground, CmpType.EQ);
            Cv2.Compare(grabCutMask, new Scalar((int)GrabCutClasses.PR_FGD), probableForeground, CmpType.EQ);

            Mat result = new Mat();
            Cv2.BitwiseOr(definiteForeground, probableForeground, result);
            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(3, 3));
            Cv2.MorphologyEx(result, result, MorphTypes.Close, kernel);
            grabCutMask.Dispose();
            return result;
        }
    }
}
