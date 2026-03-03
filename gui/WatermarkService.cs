using OpenCvSharp;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace GeminiWatermarkRemover
{
    public class WatermarkService : IDisposable
    {
        private string ModelPath;
        private InferenceSession? _session;

        public WatermarkService()
        {
            ModelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "models", "lama_fp32.onnx");
        }

        /// <summary>
        /// Initializes the AI model asynchronously or synchronously.
        /// </summary>
        public void Initialize()
        {
            if (_session != null) return;
            
            if (!File.Exists(ModelPath)) 
            {
                // Fallback for deployed releases
                ModelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "lama_fp32.onnx");
                if (!File.Exists(ModelPath)) throw new FileNotFoundException("LaMa model not found at " + ModelPath);
            }

            // Attempt to load with DirectML (GPU Hardware Acceleration)
            SessionOptions options = new SessionOptions();
            try
            {
                options.AppendExecutionProvider_DML(0); // usually device ID 0 is the primary GPU
            }
            catch
            {
                // Fallback to CPU if DirectML fails or is unsupported
            }

            _session = new InferenceSession(ModelPath, options);
        }

        public void Dispose()
        {
            _session?.Dispose();
        }

        /// <summary>
        /// Removes watermark using LaMa AI Inpainting (ONNX).
        /// </summary>
        public void RemoveWatermark(string inputPath, string outputPath, bool debug = false, byte[]? manualMask = null)
        {
            // 1. Load Image and Create Mask
            using Mat image = Cv2.ImRead(inputPath, ImreadModes.Color);
            using Mat mask = new Mat(image.Rows, image.Cols, MatType.CV_8UC1, Scalar.All(0));

            if (manualMask != null && manualMask.Length == image.Rows * image.Cols)
            {
                for (int y = 0; y < image.Rows; y++)
                {
                    for (int x = 0; x < image.Cols; x++)
                    {
                        if (manualMask[y * image.Cols + x] > 0) mask.Set(y, x, (byte)255);
                    }
                }
            }
            else
            {
                CreateAutoMask(image, mask);
            }

            // Dilate mask slightly to ensure coverage
            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
            Cv2.Dilate(mask, mask, kernel, iterations: 2);

            if (debug) Cv2.ImWrite(Path.Combine(Path.GetTempPath(), "debug_mask.png"), mask);

            // 2. Smart Cropping logic
            // Instead of resizing the whole image (which loses detail), we crop a 512x512 area around the watermark.
            int targetSize = 512;
            Rect roi;
            
            // Find the bounding box of the mask
            Point[][] contours;
            HierarchyIndex[] hierarchy;
            Cv2.FindContours(mask, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            
            if (contours.Length > 0)
            {
                // Get bounding rect of all contours
                Rect boundingRect = Cv2.BoundingRect(contours[0]);
                for (int i = 1; i < contours.Length; i++)
                {
                    boundingRect = boundingRect.Union(Cv2.BoundingRect(contours[i]));
                }

                // Calculate center of the watermark
                Point center = new Point(boundingRect.X + boundingRect.Width / 2, boundingRect.Y + boundingRect.Height / 2);

                // Define 512x512 ROI centered on watermark
                int x = center.X - targetSize / 2;
                int y = center.Y - targetSize / 2;

                // Clamp to image bounds
                if (x < 0) x = 0;
                if (y < 0) y = 0;
                if (x + targetSize > image.Width) x = image.Width - targetSize;
                if (y + targetSize > image.Height) y = image.Height - targetSize;

                // Handle case where image is smaller than 512x512
                if (image.Width < targetSize || image.Height < targetSize)
                {
                    // Fallback to resize method if image is too small
                    roi = new Rect(0, 0, image.Width, image.Height);
                }
                else
                {
                    roi = new Rect(x, y, targetSize, targetSize);
                }
            }
            else
            {
                // No mask? Just use center or default
                roi = new Rect(0, 0, Math.Min(image.Width, targetSize), Math.Min(image.Height, targetSize));
            }

            // Extract ROI
            using Mat roiImage = new Mat(image, roi);
            using Mat roiMask = new Mat(mask, roi);

            // Prepare inputs for LaMa (Resize to 512x512 if ROI is smaller, otherwise just use it)
            using Mat inputImage = new Mat();
            using Mat inputMask = new Mat();
            
            if (roi.Width != targetSize || roi.Height != targetSize)
            {
                Cv2.Resize(roiImage, inputImage, new Size(targetSize, targetSize));
                Cv2.Resize(roiMask, inputMask, new Size(targetSize, targetSize), 0, 0, InterpolationFlags.Nearest);
            }
            else
            {
                roiImage.CopyTo(inputImage);
                roiMask.CopyTo(inputMask);
            }

            // 3. Prepare Tensors
            var inputTensor = new DenseTensor<float>(new[] { 1, 3, targetSize, targetSize });
            var maskTensor = new DenseTensor<float>(new[] { 1, 1, targetSize, targetSize });

            // Normalize and fill tensors
            for (int y = 0; y < targetSize; y++)
            {
                for (int x = 0; x < targetSize; x++)
                {
                    float maskVal = inputMask.At<byte>(y, x) > 0 ? 1.0f : 0.0f;
                    maskTensor[0, 0, y, x] = maskVal;

                    Vec3b pixel = inputImage.At<Vec3b>(y, x);
                    
                    if (maskVal > 0)
                    {
                        inputTensor[0, 0, y, x] = 0;
                        inputTensor[0, 1, y, x] = 0;
                        inputTensor[0, 2, y, x] = 0;
                    }
                    else
                    {
                        inputTensor[0, 0, y, x] = pixel.Item2 / 255f; // R
                        inputTensor[0, 1, y, x] = pixel.Item1 / 255f; // G
                        inputTensor[0, 2, y, x] = pixel.Item0 / 255f; // B
                    }
                }
            }

            // 4. Run Inference
            if (_session == null) Initialize();

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("image", inputTensor),
                NamedOnnxValue.CreateFromTensor("mask", maskTensor)
            };

            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = null;
            try
            {
                results = _session!.Run(inputs);
            }
            catch (OnnxRuntimeException ex) when (ex.Message.Contains("MatMul") || ex.Message.Contains("parameter is incorrect") || ex.Message.Contains("Exception"))
            {
                // DirectML rejected the MatMul execution format mapping. Fall back to CPU runtime.
                System.Diagnostics.Debug.WriteLine($"LaMa GPU Exception intercepted: {ex.Message}. Falling back to CPU.");
                
                _session?.Dispose();
                
                SessionOptions cpuOptions = new SessionOptions();
                
                // Reload session completely fresh on CPU
                byte[] modelBytes;
                if (ModelPath.StartsWith("http"))
                {
                    using var client = new System.Net.Http.HttpClient();
                    modelBytes = client.GetByteArrayAsync(ModelPath).Result;
                }
                else
                {
                    modelBytes = System.IO.File.ReadAllBytes(ModelPath);
                }
                _session = new InferenceSession(modelBytes, cpuOptions);
                
                results = _session.Run(inputs);
            }

            if (results == null) return; // Unrecoverable

            var outputTensor = results.First().AsTensor<float>();

            // 5. Postprocess
            float maxVal = 0;
            foreach (var val in outputTensor) if (val > maxVal) maxVal = val;
            float scale = maxVal > 1.5f ? 1.0f : 255.0f;

            using Mat outputImage = new Mat(targetSize, targetSize, MatType.CV_8UC3);
            for (int y = 0; y < targetSize; y++)
            {
                for (int x = 0; x < targetSize; x++)
                {
                    byte r = (byte)Math.Clamp(outputTensor[0, 0, y, x] * scale, 0, 255);
                    byte g = (byte)Math.Clamp(outputTensor[0, 1, y, x] * scale, 0, 255);
                    byte b = (byte)Math.Clamp(outputTensor[0, 2, y, x] * scale, 0, 255);
                    outputImage.Set(y, x, new Vec3b(b, g, r));
                }
            }

            // 6. Blend back into original image
            // First, resize output back to ROI size if needed (only if we resized earlier)
            using Mat finalRoiOutput = new Mat();
            if (roi.Width != targetSize || roi.Height != targetSize)
            {
                Cv2.Resize(outputImage, finalRoiOutput, new Size(roi.Width, roi.Height));
            }
            else
            {
                outputImage.CopyTo(finalRoiOutput);
            }

            // Now blend finalRoiOutput into the original image at the ROI position
            // We only want to replace the pixels that were masked IN THE ROI
            
            // Get the sub-region of the original image
            using Mat originalRoi = new Mat(image, roi);
            
            // Create mask for the ROI
            using Mat roiMaskForBlending = new Mat(mask, roi);
            
            // Copy inpainted pixels to original ROI where mask is set
            // finalRoiOutput contains the inpainted result for the ROI
            
            // Extract inpainted pixels
            using Mat inpaintedPixels = new Mat();
            finalRoiOutput.CopyTo(inpaintedPixels, roiMaskForBlending);
            
            // Clear masked region in original ROI
            using Mat invertedRoiMask = new Mat();
            Cv2.BitwiseNot(roiMaskForBlending, invertedRoiMask);
            using Mat originalBackground = new Mat();
            originalRoi.CopyTo(originalBackground, invertedRoiMask);
            
            // Combine
            using Mat blendedRoi = new Mat();
            Cv2.Add(originalBackground, inpaintedPixels, blendedRoi);
            
            // Copy blended ROI back to original image
            blendedRoi.CopyTo(originalRoi);

            if (results != null)
            {
                results.Dispose();
            }

            Cv2.ImWrite(outputPath, image);
        }

        /// <summary>
        /// Creates an automatic mask for the Gemini watermark.
        /// Searches for bright/white star-shaped watermark in bottom-right.
        /// </summary>
        private void CreateAutoMask(Mat image, Mat mask)
        {
            // 1. Focus on Bottom-Right Quadrant
            int h = image.Rows;
            int w = image.Cols;
            Rect roi = new Rect(w - w / 3, h - h / 3, w / 3, h / 3); // Look in bottom-right 1/3
            
            using Mat roiImage = new Mat(image, roi);
            using Mat roiMask = new Mat(mask, roi);

            // 2. Brightness Thresholding
            using Mat gray = new Mat();
            Cv2.CvtColor(roiImage, gray, ColorConversionCodes.BGR2GRAY);
            
            // The Gemini watermark is usually white/bright.
            // Let's use a high threshold to pick up the star.
            using Mat thresh = new Mat();
            Cv2.Threshold(gray, thresh, 200, 255, ThresholdTypes.Binary);

            // 3. Find Contours
            Point[][] contours;
            HierarchyIndex[] hierarchy;
            Cv2.FindContours(thresh, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            // 4. Filter Contours (Look for star-like shapes)
            foreach (var contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                if (area < 50 || area > 5000) continue; // Filter noise and huge blobs

                // Analyze shape
                Rect boundingRect = Cv2.BoundingRect(contour);
                double aspectRatio = (double)boundingRect.Width / boundingRect.Height;
                
                // Stars are roughly square-ish (aspect ratio ~1.0)
                if (aspectRatio > 0.5 && aspectRatio < 2.0)
                {
                    // Draw this contour onto the mask
                    Cv2.DrawContours(roiMask, new[] { contour }, -1, Scalar.All(255), -1);
                }
            }
            
            // 5. Aggressive Dilation for Auto Mode
            // Since we only detected the bright core, we need to expand significantly
            // to cover the glowing edges and semi-transparent parts.
            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(15, 15));
            Cv2.Dilate(roiMask, roiMask, kernel, iterations: 2);
        }
    }
}
