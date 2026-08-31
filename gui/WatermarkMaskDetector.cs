using System;
using OpenCvSharp;

namespace SoftcurseMediaLabAI
{
    public static class WatermarkMaskDetector
    {
        /// <summary>
        /// Detects small bright, locally contrasting marks in the lower-right half.
        /// This covers common white and semi-transparent watermark text without masking
        /// large bright areas such as skies or full image borders.
        /// </summary>
        public static Mat Detect(Mat image)
        {
            if (image.Empty()) throw new ArgumentException("Input image is empty.", nameof(image));

            Mat mask = new Mat(image.Rows, image.Cols, MatType.CV_8UC1, Scalar.All(0));
            int roiX = image.Width / 2;
            int roiY = image.Height / 2;
            var searchArea = new Rect(roiX, roiY, image.Width - roiX, image.Height - roiY);
            using Mat roiImage = new Mat(image, searchArea);
            using Mat roiMask = new Mat(mask, searchArea);
            using Mat gray = new Mat();
            Cv2.CvtColor(roiImage, gray, ColorConversionCodes.BGR2GRAY);

            int kernelSize = Math.Clamp(Math.Min(roiImage.Width, roiImage.Height) / 12, 9, 31);
            if (kernelSize % 2 == 0) kernelSize++;
            using Mat topHatKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(kernelSize, kernelSize));
            using Mat localContrast = new Mat();
            Cv2.MorphologyEx(gray, localContrast, MorphTypes.TopHat, topHatKernel);

            using Mat contrastMask = new Mat();
            Cv2.Threshold(localContrast, contrastMask, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            using Mat brightMask = new Mat();
            Cv2.Threshold(gray, brightMask, 155, 255, ThresholdTypes.Binary);
            using Mat candidates = new Mat();
            Cv2.BitwiseAnd(contrastMask, brightMask, candidates);

            using Mat joinKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 3));
            Cv2.MorphologyEx(candidates, candidates, MorphTypes.Close, joinKernel);
            Cv2.FindContours(candidates, out Point[][] contours, out _,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            double roiArea = searchArea.Width * searchArea.Height;
            foreach (Point[] contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                Rect bounds = Cv2.BoundingRect(contour);
                if (area < 2 || area > roiArea * 0.08) continue;
                if (bounds.Width > searchArea.Width * 0.75 || bounds.Height > searchArea.Height * 0.4) continue;
                Cv2.DrawContours(roiMask, new[] { contour }, -1, Scalar.All(255), -1);
            }

            int dilation = Math.Clamp(Math.Min(image.Width, image.Height) / 250, 2, 6) * 2 + 1;
            using Mat dilationKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(dilation, dilation));
            Cv2.Dilate(roiMask, roiMask, dilationKernel);
            return mask;
        }
    }
}
