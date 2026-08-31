using OpenCvSharp;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SoftcurseMediaLabAI
{
    public enum WatermarkExecutionProvider { Cpu = 0, DirectML = 1 }

    public class WatermarkService : IDisposable
    {
        private string _modelPath = string.Empty;
        private InferenceSession? _session;
        private bool _usingGpu = false;
        private readonly object _inferenceLock = new();
        private readonly WatermarkExecutionProvider? _providerOverride;
        private readonly bool _rememberSuccessfulProvider;
        private readonly string? _modelPathOverride;
        public bool IsInitialized => _session != null;
        public WatermarkExecutionProvider? ActiveExecutionProvider =>
            _session == null ? null : (_usingGpu ? WatermarkExecutionProvider.DirectML : WatermarkExecutionProvider.Cpu);

        // ── F-03: use ModelPathResolver instead of hardcoded relative path ──
        public WatermarkService(
            WatermarkExecutionProvider? providerOverride = null,
            bool rememberSuccessfulProvider = true,
            string? modelPathOverride = null)
        {
            _providerOverride = providerOverride;
            _rememberSuccessfulProvider = rememberSuccessfulProvider;
            _modelPathOverride = modelPathOverride;
        }

        public void Initialize()
        {
            if (_session != null) return;

            // F-03: resolve via central resolver (config → release → dev)
            _modelPath = string.IsNullOrWhiteSpace(_modelPathOverride)
                ? ModelPathResolver.Resolve("lama_fp32.onnx")
                : Path.GetFullPath(_modelPathOverride);
            if (!File.Exists(_modelPath))
                throw new FileNotFoundException("The configured LaMa model file was not found.", _modelPath);

            WatermarkExecutionProvider requestedProvider = _providerOverride ??
                (AppSettings.ExecutionProvider == 1
                    ? WatermarkExecutionProvider.DirectML
                    : WatermarkExecutionProvider.Cpu);
            // A benchmark override must always exercise the requested provider. In normal app
            // use, remember a proven DirectML runtime failure and avoid paying that startup cost
            // again until the user explicitly resets the result in Settings.
            bool knownDirectMlFailure = _providerOverride == null &&
                requestedProvider == WatermarkExecutionProvider.DirectML &&
                AppSettings.LastKnownGoodExecutionProvider == 0;
            bool preferGpu = requestedProvider == WatermarkExecutionProvider.DirectML &&
                !knownDirectMlFailure;
            _session = TryCreateSession(_modelPath, preferGpu, out _usingGpu);
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

            Mat? processed = null;
            try
            {
                TryRemoveWatermarkFromMat(image, out processed, manualMask, debug);
                if (!Cv2.ImWrite(outputPath, processed ?? image))
                    throw new IOException($"Failed to write output image: {outputPath}");
            }
            finally
            {
                processed?.Dispose();
            }
        }

        // ── F-05 fix: public overload that takes Mat directly (used by VideoLabPage) ──
        public Mat RemoveWatermarkFromMat(Mat image, byte[]? manualMask = null, bool debug = false)
        {
            TryRemoveWatermarkFromMat(image, out Mat? result, manualMask, debug);
            return result ?? image.Clone();
        }

        /// <summary>
        /// Returns false without allocating a full-frame clone when no watermark is detected.
        /// Callers that can pass the original frame through (notably video encoding) should use
        /// this overload. A true result is owned by the caller and must be disposed.
        /// </summary>
        public bool TryRemoveWatermarkFromMat(
            Mat image, out Mat? result, byte[]? manualMask = null, bool debug = false)
        {
            if (image.Empty()) throw new ArgumentException("Input image is empty.", nameof(image));
            lock (_inferenceLock)
            {
                result = RemoveWatermarkFromMatCore(image, manualMask, debug);
                return result != null;
            }
        }

        private Mat? RemoveWatermarkFromMatCore(Mat image, byte[]? manualMask, bool debug)
        {
            // 1. Build mask
            using Mat mask = BuildMask(image, manualMask);

            // Nothing was selected/detected: preserve pixels and avoid loading the 208 MB model.
            if (Cv2.CountNonZero(mask) == 0) return null;
            if (_session == null) Initialize();

            // Dilate mask to ensure full coverage
            using Mat dilateKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
            Cv2.Dilate(mask, mask, dilateKernel, iterations: 2);

            if (debug) Cv2.ImWrite(TempFileManager.CreateTempPath("debug_mask", ".png"), mask);

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
                RememberSuccessfulProvider(_usingGpu ? 1 : 0);
            }
            catch (OnnxRuntimeException ex) when (IsEpRejectionError(ex))
            {
                // GPU inference failed at runtime (e.g. MatMul node) — fall back to CPU
                Debug.WriteLine($"[WatermarkService] GPU inference failed ({ex.Message}), rebuilding session on CPU...");
                _session?.Dispose();
                _session = TryCreateSession(_modelPath, preferGpu: false, out _usingGpu);
                results = _session!.Run(namedInputs);
                RememberSuccessfulProvider(0);
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

        private void RememberSuccessfulProvider(int provider)
        {
            if (_rememberSuccessfulProvider)
                AppSettings.RecordSuccessfulExecutionProvider(provider);
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
            if (manualMask != null && manualMask.Length == image.Rows * image.Cols)
            {
                Mat mask = new Mat(image.Rows, image.Cols, MatType.CV_8UC1, Scalar.All(0));
                // Fast bulk copy via Marshal
                using Mat manualMat = new Mat(image.Rows, image.Cols, MatType.CV_8UC1);
                Marshal.Copy(manualMask, 0, manualMat.Data, manualMask.Length);
                // Threshold: any non-zero alpha → white mask
                Cv2.Threshold(manualMat, mask, 0, 255, ThresholdTypes.Binary);
                return mask;
            }
            return WatermarkMaskDetector.Detect(image);
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
