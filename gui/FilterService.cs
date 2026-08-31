using System;
using System.IO;
using OpenCvSharp;

namespace SoftcurseMediaLabAI
{
    public static class FilterService
    {
        public static void ApplyGaussianBlur(string inputPath, string outputPath, int kernelSize = 5)
        {
            if (kernelSize % 2 == 0) kernelSize++;
            if (kernelSize < 1) kernelSize = 1;
            using var source = ReadImage(inputPath);
            using var result = new Mat();
            Cv2.GaussianBlur(source, result, new Size(kernelSize, kernelSize), 0);
            WriteImage(outputPath, result);
        }

        public static void ApplySharpen(string inputPath, string outputPath, double strength = 1.5)
        {
            if (strength < 0) throw new ArgumentOutOfRangeException(nameof(strength));
            using var source = ReadImage(inputPath);
            using var blurred = new Mat();
            Cv2.GaussianBlur(source, blurred, new Size(0, 0), 3);
            using var result = new Mat();
            Cv2.AddWeighted(source, 1.0 + strength, blurred, -strength, 0, result);
            WriteImage(outputPath, result);
        }

        public static void ApplyNoise(string inputPath, string outputPath, double intensity = 25)
        {
            if (intensity < 0) throw new ArgumentOutOfRangeException(nameof(intensity));
            using var source = ReadImage(inputPath);
            using var sourceFloat = new Mat();
            source.ConvertTo(sourceFloat, MatType.CV_32F);
            using var noise = new Mat(source.Size(), sourceFloat.Type());
            Cv2.Randn(noise, Scalar.All(0), Scalar.All(intensity));
            using var resultFloat = new Mat();
            Cv2.Add(sourceFloat, noise, resultFloat);
            using var result = new Mat();
            resultFloat.ConvertTo(result, source.Type());
            WriteImage(outputPath, result);
        }

        private static Mat ReadImage(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("An input path is required.", nameof(inputPath));
            Mat image = Cv2.ImRead(inputPath, ImreadModes.Unchanged);
            if (image.Empty())
            {
                image.Dispose();
                throw new ArgumentException($"The image could not be read: {inputPath}", nameof(inputPath));
            }
            return image;
        }

        private static void WriteImage(string outputPath, Mat image)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("An output path is required.", nameof(outputPath));
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                throw new ArgumentException($"The output directory does not exist: {directory}", nameof(outputPath));
            if (!Cv2.ImWrite(outputPath, image))
                throw new InvalidOperationException($"The image could not be written: {outputPath}");
        }
    }
}
