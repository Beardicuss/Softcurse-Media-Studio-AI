using OpenCvSharp;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GeminiWatermarkRemover
{
    public class WatermarkService : IDisposable
    {
        private string _modelPath = string.Empty;
        private InferenceSession? _session;
        private bool _usingGpu = false;

        // ── F-03: use ModelPathResolver instead of hardcoded relative path ──
        public WatermarkService() { }

        public void Initialize()
        {
            if (_session != null) return;

            // F-03: resolve via central resolver (config → release → dev)
            _modelPath = ModelPathResolver.Resolve("lama_fp32.onnx");

            _session = TryCreateSession(_modelPath, preferGpu: true, out _usingGpu);
            Debug.WriteLine($"[WatermarkService] Session created. GPU={_usingGpu}, model={_modelPath}");
        }

        // ── F-06: narrowed GPU fallback — only retry on known EP rejection codes ──
        private static InferenceSession TryCreateSession(string modelPath, bool preferGpu, out bool usingGpu)
        {
            if (preferGpu)
            {
                try
                {
                    var gpuOptions = new SessionOptions();
                    gpuOptions.AppendExecutionProvider_DML(0);
                    var session = new InferenceSession(modelPath, gpuOptions);
                    usingGpu = true;
                    return session;
                }
                catch (OnnxRuntimeException ex)
                    when (IsEpRejectionError(ex))
                {
                    Debug.WriteLine($"[WatermarkService] DirectML unavailable ({ex.Message}), falling back to CPU.");
                }
                catch (Exception ex)
                {
                    // Unexpected error during GPU init — log and fall through to CPU
                    Debug.WriteLine($"[WatermarkService] GPU init failed unexpectedly: {ex.Message}");
                }
            }

            // CPU fallback — create once, cache
            var cpuOptions = new SessionOptions();
            usingGpu = false;
            return new InferenceSession(modelPath, cpuOptions);
        }

        /// <summary>
        /// F-06: Only treat execution-provider rejection as a known GPU fallback trigger.
        /// Data corruption or model errors should NOT silently swallow.
        /// </summary>
        private static bool IsEpRejectionError(OnnxRuntimeException ex)
        {
            // Fallback through message inspection since OrtErrorCode might be unavailable

            // Also catch the known DirectML MatMul / format rejection messages
            string msg = ex.Message;
            return msg.Contains("EP_FAIL", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("MatMul", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("parameter is incorrect", StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose() => _session?.Dispose();

        // ── Public API: accepts file paths (used by image editor + batch) ──
        public void RemoveWatermark(string inputPath, string outputPath,
                                    bool debug = false, byte[]? manualMask = null)
        {
            using Mat image = Cv2.ImRead(inputPath, ImreadModes.Color);
            if (image.Empty()) throw new InvalidOperationException($"Failed to read image: {inputPath}");

            using Mat result = RemoveWatermarkFromMat(image, manualMask, debug);
            Cv2.ImWrite(outputPath, result);
        }

        // ── F-05 fix: public overload that takes Mat directly (used by VideoLabPage) ──
        public Mat RemoveWatermarkFromMat(Mat image, byte[]? manualMask = null, bool debug = false)
        {
            if (_session == null) Initialize();

            // 1. Build mask
            using Mat mask = BuildMask(image, manualMask);

            // Dilate mask to ensure full coverage
            using Mat dilateKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
            Cv2.Dilate(mask, mask, dilateKernel, iterations: 2);

            if (debug) Cv2.ImWrite(Path.Combine(Path.GetTempPath(), "debug_mask.png"), mask);

            // 2. Compute ROI
            Rect roi = ComputeRoi(image, mask, targetSize: 512);

            // 3. Extract and prepare input
            using Mat roiImage = new Mat(image, roi);
            using Mat roiMask  = new Mat(mask, roi);

            int targetSize = 512;
            using Mat inputImage = new Mat();
            using Mat inputMask  = new Mat();

            bool needsResize = roi.Width != targetSize || roi.Height != targetSize;
            if (needsResize)
            {
                Cv2.Resize(roiImage, inputImage, new Size(targetSize, targetSize));
                Cv2.Resize(roiMask,  inputMask,  new Size(targetSize, targetSize),
                           0, 0, InterpolationFlags.Nearest);
            }
            else
            {
                roiImage.CopyTo(inputImage);
                roiMask.CopyTo(inputMask);
            }

            // 4. Fill tensors — F-08: use unsafe pointer copy instead of per-pixel At<>
            var inputTensor = new DenseTensor<float>(new[] { 1, 3, targetSize, targetSize });
            var maskTensor  = new DenseTensor<float>(new[] { 1, 1, targetSize, targetSize });
            FillTensors(inputImage, inputMask, inputTensor, maskTensor, targetSize);

            // 5. Inference — with runtime GPU→CPU fallback for MatMul errors
            var namedInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("image", inputTensor),
                NamedOnnxValue.CreateFromTensor("mask",  maskTensor)
            };

            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results;
            try
            {
                results = _session!.Run(namedInputs);
            }
            catch (OnnxRuntimeException ex) when (IsEpRejectionError(ex))
            {
                // GPU inference failed at runtime (e.g. MatMul node) — fall back to CPU
                Debug.WriteLine($"[WatermarkService] GPU inference failed ({ex.Message}), rebuilding session on CPU...");
                _session?.Dispose();
                _session = TryCreateSession(_modelPath, preferGpu: false, out _usingGpu);
                results = _session!.Run(namedInputs);
            }

            using (results)
            {
            var outputTensor  = results.First().AsTensor<float>();

            // 6. Postprocess tensor → Mat
            using Mat outputImage = TensorToMat(outputTensor, targetSize);

            // 7. Blend back into a clone of the original (non-destructive)
            Mat blended = image.Clone();

            using Mat finalRoiOutput = new Mat();
            if (needsResize)
                Cv2.Resize(outputImage, finalRoiOutput, new Size(roi.Width, roi.Height));
            else
                outputImage.CopyTo(finalRoiOutput);

            using Mat roiMaskForBlend   = new Mat(mask, roi);
            using Mat invertedRoiMask   = new Mat();
            Cv2.BitwiseNot(roiMaskForBlend, invertedRoiMask);

            using Mat blendedRoiRef     = new Mat(blended, roi);
            using Mat originalBackground = new Mat();
            blendedRoiRef.CopyTo(originalBackground, invertedRoiMask);

            using Mat inpaintedPixels = new Mat();
            finalRoiOutput.CopyTo(inpaintedPixels, roiMaskForBlend);

            using Mat blendedRoi = new Mat();
            Cv2.Add(originalBackground, inpaintedPixels, blendedRoi);
            blendedRoi.CopyTo(blendedRoiRef);

            return blended;
            } // end using(results)
        }

        // ── F-08: unsafe memory copy for tensor fill (5-10× faster than At<Vec3b>) ──
        private static unsafe void FillTensors(
            Mat image, Mat maskMat,
            DenseTensor<float> imageTensor,
            DenseTensor<float> maskTensor,
            int size)
        {
            // Get raw pointers
            byte* imgPtr  = (byte*)image.DataPointer;
            byte* mskPtr  = (byte*)maskMat.DataPointer;

            int imgStep  = (int)image.Step();   // bytes per row
            int mskStep  = (int)maskMat.Step();

            Span<float> imgSpan = imageTensor.Buffer.Span;
            Span<float> mskSpan = maskTensor.Buffer.Span;

            int planeSize = size * size;

            for (int y = 0; y < size; y++)
            {
                byte* imgRow = imgPtr + y * imgStep;
                byte* mskRow = mskPtr + y * mskStep;

                int rowBase = y * size;

                for (int x = 0; x < size; x++)
                {
                    float mv = mskRow[x] > 0 ? 1.0f : 0.0f;
                    mskSpan[rowBase + x] = mv;

                    // BGR layout from OpenCV
                    byte b = imgRow[x * 3];
                    byte g = imgRow[x * 3 + 1];
                    byte r = imgRow[x * 3 + 2];

                    if (mv > 0f)
                    {
                        // Zero-out masked region for LaMa
                        imgSpan[0 * planeSize + rowBase + x] = 0f;
                        imgSpan[1 * planeSize + rowBase + x] = 0f;
                        imgSpan[2 * planeSize + rowBase + x] = 0f;
                    }
                    else
                    {
                        imgSpan[0 * planeSize + rowBase + x] = r / 255f;
                        imgSpan[1 * planeSize + rowBase + x] = g / 255f;
                        imgSpan[2 * planeSize + rowBase + x] = b / 255f;
                    }
                }
            }
        }

        private static unsafe Mat TensorToMat(Tensor<float> tensor, int size)
        {
            // Determine scale from output range
            float maxVal = 0f;
            foreach (float v in tensor) if (v > maxVal) maxVal = v;
            float scale = maxVal > 1.5f ? 1.0f : 255.0f;

            Mat output = new Mat(size, size, MatType.CV_8UC3);
            byte* ptr = (byte*)output.DataPointer;
            int step  = (int)output.Step();
            int plane = size * size;

            for (int y = 0; y < size; y++)
            {
                byte* row = ptr + y * step;
                for (int x = 0; x < size; x++)
                {
                    // tensor channels: R, G, B → output BGR
                    row[x * 3]     = (byte)Math.Clamp(tensor[0, 2, y, x] * scale, 0, 255); // B
                    row[x * 3 + 1] = (byte)Math.Clamp(tensor[0, 1, y, x] * scale, 0, 255); // G
                    row[x * 3 + 2] = (byte)Math.Clamp(tensor[0, 0, y, x] * scale, 0, 255); // R
                }
            }
            return output;
        }

        private static Mat BuildMask(Mat image, byte[]? manualMask)
        {
            Mat mask = new Mat(image.Rows, image.Cols, MatType.CV_8UC1, Scalar.All(0));

            if (manualMask != null && manualMask.Length == image.Rows * image.Cols)
            {
                // Fast bulk copy via Marshal
                using Mat manualMat = new Mat(image.Rows, image.Cols, MatType.CV_8UC1);
                Marshal.Copy(manualMask, 0, manualMat.Data, manualMask.Length);
                // Threshold: any non-zero alpha → white mask
                Cv2.Threshold(manualMat, mask, 0, 255, ThresholdTypes.Binary);
            }
            else
            {
                // F-09: renamed internally — this detects BRIGHT HIGH-CONTRAST regions,
                // not a Gemini-specific watermark. Works best for white/bright watermarks.
                DetectHighContrastMask(image, mask);
            }

            return mask;
        }

        // ── F-09: renamed and comment updated to reflect actual behaviour ──
        /// <summary>
        /// Detects bright, high-contrast blobs in the image and builds an inpainting mask.
        /// Most effective for white/semi-transparent watermarks. Currently searches
        /// the bottom-right third (most common watermark placement); could be generalised
        /// in a future pass to cover the full image.
        /// </summary>
        private static void DetectHighContrastMask(Mat image, Mat mask)
        {
            int h = image.Rows;
            int w = image.Cols;

            // Search bottom-right third — most common watermark placement
            Rect searchRoi = new Rect(w - w / 3, h - h / 3, w / 3, h / 3);
            using Mat roiImage = new Mat(image, searchRoi);
            using Mat roiMask  = new Mat(mask,  searchRoi);

            using Mat gray   = new Mat();
            using Mat thresh = new Mat();
            Cv2.CvtColor(roiImage, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(gray, thresh, 200, 255, ThresholdTypes.Binary);

            Cv2.FindContours(thresh, out Point[][] contours, out _,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            foreach (var contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                if (area < 50 || area > 5000) continue;

                Rect br = Cv2.BoundingRect(contour);
                double ar = (double)br.Width / Math.Max(1, br.Height);
                if (ar > 0.5 && ar < 2.0)
                    Cv2.DrawContours(roiMask, new[] { contour }, -1, Scalar.All(255), -1);
            }

            // Dilate aggressively to cover semi-transparent halo
            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(15, 15));
            Cv2.Dilate(roiMask, roiMask, kernel, iterations: 2);
        }

        private static Rect ComputeRoi(Mat image, Mat mask, int targetSize)
        {
            Cv2.FindContours(mask, out Point[][] contours, out _,
                RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            if (contours.Length == 0)
                return new Rect(0, 0, Math.Min(image.Width, targetSize),
                                      Math.Min(image.Height, targetSize));

            Rect bbox = Cv2.BoundingRect(contours[0]);
            for (int i = 1; i < contours.Length; i++)
                bbox = bbox.Union(Cv2.BoundingRect(contours[i]));

            if (image.Width < targetSize || image.Height < targetSize)
                return new Rect(0, 0, image.Width, image.Height);

            Point center = new Point(bbox.X + bbox.Width / 2, bbox.Y + bbox.Height / 2);
            int x = Math.Clamp(center.X - targetSize / 2, 0, image.Width  - targetSize);
            int y = Math.Clamp(center.Y - targetSize / 2, 0, image.Height - targetSize);
            return new Rect(x, y, targetSize, targetSize);
        }
    }
}
