using System;
using OpenCvSharp;

namespace GeminiWatermarkRemover
{
    /// <summary>
    /// Provides simple image filters using OpenCvSharp.
    /// All methods take an input path, apply the filter, and write to an output path.
    /// </summary>
    public static class FilterService
    {
        /// <summary>
        /// Applies Gaussian blur to the image.
        /// </summary>
        /// <param name="inputPath">Source image path.</param>
        /// <param name="outputPath">Output image path.</param>
        /// <param name="kernelSize">Blur kernel size (must be odd, e.g. 3, 5, 7, 15).</param>
        public static void ApplyGaussianBlur(string inputPath, string outputPath, int kernelSize = 5)
        {
            // Ensure odd kernel
            if (kernelSize % 2 == 0) kernelSize++;
            if (kernelSize < 1) kernelSize = 1;

            using var src = Cv2.ImRead(inputPath, ImreadModes.Unchanged);
            using var dst = new Mat();
            Cv2.GaussianBlur(src, dst, new Size(kernelSize, kernelSize), 0);
            Cv2.ImWrite(outputPath, dst);
        }

        /// <summary>
        /// Applies an unsharp-mask sharpen to the image.
        /// </summary>
        /// <param name="inputPath">Source image path.</param>
        /// <param name="outputPath">Output image path.</param>
        /// <param name="strength">Sharpen strength (0.5 = subtle, 2.0 = heavy).</param>
        public static void ApplySharpen(string inputPath, string outputPath, double strength = 1.5)
        {
            using var src = Cv2.ImRead(inputPath, ImreadModes.Unchanged);
            using var blurred = new Mat();
            Cv2.GaussianBlur(src, blurred, new Size(0, 0), 3);
            using var dst = new Mat();
            Cv2.AddWeighted(src, 1.0 + strength, blurred, -strength, 0, dst);
            Cv2.ImWrite(outputPath, dst);
        }

        /// <summary>
        /// Adds random Gaussian noise to the image.
        /// </summary>
        /// <param name="inputPath">Source image path.</param>
        /// <param name="outputPath">Output image path.</param>
        /// <param name="intensity">Noise standard deviation (10 = subtle, 50 = heavy).</param>
        public static void ApplyNoise(string inputPath, string outputPath, double intensity = 25)
        {
            using var src = Cv2.ImRead(inputPath, ImreadModes.Unchanged);

            // Convert to float for safe addition
            using var srcF = new Mat();
            src.ConvertTo(srcF, MatType.CV_32F);

            // Generate noise
            using var noise = new Mat(src.Size(), srcF.Type());
            Cv2.Randn(noise, Scalar.All(0), Scalar.All(intensity));

            using var dstF = new Mat();
            Cv2.Add(srcF, noise, dstF);

            // Clamp and convert back
            using var dst = new Mat();
            dstF.ConvertTo(dst, src.Type());
            Cv2.ImWrite(outputPath, dst);
        }
    }
}
