using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace SoftcurseMediaLabAI
{
    public readonly record struct ImageFileInfo(int PixelWidth, int PixelHeight, long FileBytes);

    public static class ImageFileValidator
    {
        public const long DefaultMaxFileBytes = 256L * 1024 * 1024;
        public const long DefaultMaxPixels = 100_000_000;

        public static ImageFileInfo Validate(
            string path,
            long maxFileBytes = DefaultMaxFileBytes,
            long maxPixels = DefaultMaxPixels)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("An image path is required.", nameof(path));
            if (maxFileBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
            if (maxPixels <= 0) throw new ArgumentOutOfRangeException(nameof(maxPixels));

            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("The image file was not found.", path);
            if (file.Length == 0) throw new InvalidDataException("The image file is empty.");
            if (file.Length > maxFileBytes)
                throw new InvalidDataException($"The image file is too large ({file.Length / 1024 / 1024} MB). The limit is {maxFileBytes / 1024 / 1024} MB.");

            try
            {
                using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                BitmapDecoder decoder = BitmapDecoder.Create(
                    stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
                if (decoder.Frames.Count == 0) throw new InvalidDataException("The file contains no image frames.");

                int width = decoder.Frames[0].PixelWidth;
                int height = decoder.Frames[0].PixelHeight;
                long pixels = checked((long)width * height);
                if (width <= 0 || height <= 0) throw new InvalidDataException("The image dimensions are invalid.");
                if (pixels > maxPixels)
                    throw new InvalidDataException($"The image dimensions are too large ({width}×{height}). The limit is {maxPixels:N0} pixels.");
                return new ImageFileInfo(width, height, file.Length);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException)
            {
                throw new InvalidDataException("The selected file is not a supported or readable image.", ex);
            }
        }
    }
}
