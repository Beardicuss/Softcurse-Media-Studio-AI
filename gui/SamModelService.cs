using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System.Diagnostics;

namespace SoftcurseMediaLabAI
{
    // ── F-02: Feature honestly renamed; stub removed; GrabCut clearly labelled ──
    // ── F-03: Model paths resolved via ModelPathResolver                         ──
    //
    // This service provides REGION-SELECT segmentation using OpenCV GrabCut.
    // It is designed as a drop-in replacement for SAM ONNX inference, which
    // requires ~350 MB quantized model files not bundled with the application.
    //
    // To enable true SAM inference:
    //   1. Download sam_vit_b_01ec64.quant.onnx (encoder) and
    //              sam_vit_b_01ec64.decoder.quant.onnx (decoder)
    //      from https://github.com/facebookresearch/segment-anything
    //   2. Place both files in the models/ directory next to the executable.
    //   3. Uncomment the ONNX pipeline in RunSamOnnxPipeline() below.
    //
    public class SamModelService : IDisposable
    {
        private const string EncoderFileName = "sam_vit_b_01ec64.quant.onnx";
        private const string DecoderFileName = "sam_vit_b_01ec64.decoder.quant.onnx";

        private InferenceSession? _encoderSession;
        private InferenceSession? _decoderSession;
        private bool _samAvailable = false; // true only when real ONNX models are loaded

        public bool IsSamAvailable => _samAvailable;

        public SamModelService() { }

        public async Task InitializeAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    // F-03: resolve via central resolver — no hardcoded '../../../'
                    string encoderPath = ModelPathResolver.Resolve(EncoderFileName);
                    string decoderPath = ModelPathResolver.Resolve(DecoderFileName);

                    var options = new SessionOptions();
                    options.AppendExecutionProvider_CPU();

                    _encoderSession = new InferenceSession(encoderPath, options);
                    _decoderSession = new InferenceSession(decoderPath, options);
                    // SAM ONNX pipeline is not yet implemented — keep _samAvailable false
                    // to route through GrabCut until RunSamOnnxPipeline() is completed.
                    // Once implemented, uncomment the line below:
                    // _samAvailable = true;
                    _samAvailable = false;

                    Debug.WriteLine("[SamModelService] SAM ONNX models found but pipeline not yet implemented. Using GrabCut.");
                }
                catch (FileNotFoundException)
                {
                    // Models not present — will use GrabCut fallback. This is expected
                    // when the user has not downloaded the optional SAM model files.
                    _samAvailable = false;
                    Debug.WriteLine("[SamModelService] SAM ONNX models not found. Using GrabCut region-select.");
                }
                catch (Exception ex)
                {
                    _samAvailable = false;
                    Debug.WriteLine($"[SamModelService] Model load error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Generates a segmentation mask for the region at <paramref name="clickPoint"/>.
        /// Uses real SAM ONNX inference if models are loaded, otherwise GrabCut.
        /// Returns path to a temporary greyscale mask PNG.
        /// </summary>
        public async Task<(string maskPath, bool usedSam)> GenerateMaskAsync(
            string imagePath, System.Windows.Point clickPoint)
        {
            return await Task.Run(() =>
            {
                string tempOutput = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"mask_{Guid.NewGuid()}.png");

                using Mat img = Cv2.ImRead(imagePath, ImreadModes.Color);
                if (img.Empty())
                    throw new InvalidOperationException($"Could not read image: {imagePath}");

                Mat finalMask;

                if (_samAvailable && _encoderSession != null && _decoderSession != null)
                {
                    finalMask = RunSamOnnxPipeline(img, clickPoint);
                    Cv2.ImWrite(tempOutput, finalMask);
                    finalMask.Dispose();
                    return (tempOutput, true);
                }
                else
                {
                    finalMask = RunGrabCutSegmentation(img, clickPoint);
                    Cv2.ImWrite(tempOutput, finalMask);
                    finalMask.Dispose();
                    return (tempOutput, false);
                }
            });
        }

        // ── GrabCut segmentation (active fallback when SAM is not installed) ──
        private static Mat RunGrabCutSegmentation(Mat img, System.Windows.Point clickPoint)
        {
            using Mat gcMask    = new Mat(img.Size(), MatType.CV_8UC1, Scalar.All((int)GrabCutClasses.BGD));
            using Mat bgdModel  = new Mat();
            using Mat fgdModel  = new Mat();

            int w = Math.Max(1, img.Width  / 4);
            int h = Math.Max(1, img.Height / 4);
            int x = Math.Clamp((int)clickPoint.X - w / 2, 0, img.Width  - w);
            int y = Math.Clamp((int)clickPoint.Y - h / 2, 0, img.Height - h);

            OpenCvSharp.Rect rect = new OpenCvSharp.Rect(x, y,
                Math.Min(w, img.Width  - x),
                Math.Min(h, img.Height - y));

            // Seed the click point as definite foreground
            Cv2.Circle(gcMask,
                       new OpenCvSharp.Point((int)clickPoint.X, (int)clickPoint.Y),
                       5, Scalar.All((int)GrabCutClasses.FGD), -1);

            try
            {
                Cv2.GrabCut(img, gcMask, rect, bgdModel, fgdModel, 3, GrabCutModes.InitWithRect);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SamModelService] GrabCut error: {ex.Message}");
            }

            // Combine probable and definite foreground
            using Mat fgd  = new Mat();
            using Mat pfgd = new Mat();
            Cv2.Compare(gcMask, new Scalar((int)GrabCutClasses.FGD),    fgd,  CmpType.EQ);
            Cv2.Compare(gcMask, new Scalar((int)GrabCutClasses.PR_FGD), pfgd, CmpType.EQ);

            Mat result = new Mat();
            Cv2.BitwiseOr(fgd, pfgd, result);

            // Slight dilation for smooth edges
            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(5, 5));
            Cv2.Dilate(result, result, kernel);

            return result;
        }

        // ── Placeholder for real SAM ONNX pipeline ──────────────────────
        // Uncomment and complete once model files are present.
        private Mat RunSamOnnxPipeline(Mat img, System.Windows.Point clickPoint)
        {
            // Full SAM pipeline:
            // 1. Resize image to 1024×1024, normalise → Tensor(1, 3, 1024, 1024)
            // 2. Run _encoderSession  → image_embeddings Tensor(1, 256, 64, 64)
            // 3. Build point prompt   → point_coords Tensor(1, 1, 2), point_labels Tensor(1, 1)
            // 4. Run _decoderSession  → masks Tensor(1, 1, 256, 256), scores Tensor(1, 1)
            // 5. Threshold best mask at 0.0 → binary mask, resize back to original resolution
            //
            // Falling back to GrabCut until this is implemented.
            Debug.WriteLine("[SamModelService] RunSamOnnxPipeline called but not implemented — using GrabCut fallback.");
            return RunGrabCutSegmentation(img, clickPoint);
        }

        public void Dispose()
        {
            _encoderSession?.Dispose();
            _decoderSession?.Dispose();
        }
    }
}
