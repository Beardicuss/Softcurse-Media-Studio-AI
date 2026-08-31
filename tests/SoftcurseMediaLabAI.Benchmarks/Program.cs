using System.Diagnostics;
using OpenCvSharp;
using SoftcurseMediaLabAI;

const int width = 1920;
const int height = 1080;
string input = TempFileManager.CreateTempPath("benchmark_input", ".png");
string blur = TempFileManager.CreateTempPath("benchmark_blur", ".png");
string sharpen = TempFileManager.CreateTempPath("benchmark_sharpen", ".png");
string sourceVideo = TempFileManager.CreateTempPath("benchmark_video_source", ".avi");
string outputVideo = TempFileManager.CreateTempPath("benchmark_video_output", ".avi");

try
{
    using (var image = new Mat(new Size(width, height), MatType.CV_8UC3, new Scalar(45, 80, 125)))
    {
        Cv2.PutText(image, "SOFTCURSE", new Point(1480, 1000), HersheyFonts.HersheySimplex,
            1.5, Scalar.All(240), 2, LineTypes.AntiAlias);
        Cv2.ImWrite(input, image);
    }

    Run("Gaussian blur 1080p", () => FilterService.ApplyGaussianBlur(input, blur, 9));
    Run("Sharpen 1080p", () => FilterService.ApplySharpen(input, sharpen));
    Run("Watermark mask 1080p", () =>
    {
        using Mat image = Cv2.ImRead(input, ImreadModes.Color);
        using Mat mask = WatermarkMaskDetector.Detect(image);
    });
    RunVideoThroughput(sourceVideo, outputVideo);
    RunProviderBenchmarks(args.FirstOrDefault());

    Console.WriteLine($"Managed memory after benchmarks: {GC.GetTotalMemory(true) / 1024.0 / 1024.0:F1} MB");
}
finally
{
    TempFileManager.CleanupAll();
}

static void RunProviderBenchmarks(string? modelPath)
{
    if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
    {
        Console.WriteLine("LaMa provider benchmarks: skipped (explicit model path unavailable)");
        return;
    }

    foreach (WatermarkExecutionProvider requested in Enum.GetValues<WatermarkExecutionProvider>())
    {
        try
        {
            using var service = new WatermarkService(
                requested, rememberSuccessfulProvider: false, modelPathOverride: modelPath);
            using var image = new Mat(new Size(512, 512), MatType.CV_8UC3, new Scalar(55, 90, 130));
            byte[] mask = new byte[512 * 512];
            for (int y = 224; y < 288; y++)
                Array.Fill(mask, (byte)255, y * 512 + 224, 64);

            var cold = Stopwatch.StartNew();
            using Mat coldResult = service.RemoveWatermarkFromMat(image, mask);
            cold.Stop();

            var warm = Stopwatch.StartNew();
            using Mat warmResult = service.RemoveWatermarkFromMat(image, mask);
            warm.Stop();

            string active = service.ActiveExecutionProvider?.ToString() ?? "Unavailable";
            string fallback = active.Equals(requested.ToString(), StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $", fallback from {requested}";
            Console.WriteLine(
                $"LaMa {active}{fallback}: cold {cold.Elapsed.TotalMilliseconds:F1} ms, " +
                $"warm {warm.Elapsed.TotalMilliseconds:F1} ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LaMa {requested}: unavailable ({ex.GetType().Name}: {ex.Message})");
        }
    }
}

static void RunVideoThroughput(string sourcePath, string outputPath)
{
    const int frameWidth = 1280;
    const int frameHeight = 720;
    const int frameCount = 90;
    const double fps = 30;
    int codec = FourCC.MJPG;

    using (var sourceWriter = new VideoWriter(
        sourcePath, codec, fps, new Size(frameWidth, frameHeight), true))
    {
        if (!sourceWriter.IsOpened())
        {
            Console.WriteLine("Video throughput 720p: skipped (MJPG encoder unavailable)");
            return;
        }

        using var syntheticFrame = new Mat(
            new Size(frameWidth, frameHeight), MatType.CV_8UC3, new Scalar(35, 55, 75));
        for (int i = 0; i < frameCount; i++)
        {
            Cv2.PutText(syntheticFrame, i.ToString(), new Point(40, 80),
                HersheyFonts.HersheySimplex, 1, Scalar.All(180), 2);
            sourceWriter.Write(syntheticFrame);
        }
    }

    using var capture = new VideoCapture(sourcePath);
    using var outputWriter = new VideoWriter(
        outputPath, codec, fps, new Size(frameWidth, frameHeight), true);
    if (!capture.IsOpened() || !outputWriter.IsOpened())
    {
        Console.WriteLine("Video throughput 720p: skipped (video codec unavailable)");
        return;
    }

    using var service = new WatermarkService();
    using var frame = new Mat();
    byte[] emptyMask = new byte[frameWidth * frameHeight];
    int processedFrames = 0;
    var stopwatch = Stopwatch.StartNew();
    while (capture.Read(frame) && !frame.Empty())
    {
        Mat? processed = null;
        try
        {
            service.TryRemoveWatermarkFromMat(frame, out processed, emptyMask);
            outputWriter.Write(processed ?? frame);
            processedFrames++;
        }
        finally
        {
            processed?.Dispose();
        }
    }
    stopwatch.Stop();

    double throughput = processedFrames / stopwatch.Elapsed.TotalSeconds;
    string budget = throughput >= PerformanceMetrics.VideoThroughputBudgetFps ? "budget met" : "below budget";
    Console.WriteLine(
        $"Video pass-through 720p: {throughput:F1} fps ({processedFrames} frames, {budget}; " +
        $"target {PerformanceMetrics.VideoThroughputBudgetFps:F0} fps)");
}

static void Run(string name, Action operation)
{
    operation(); // warm-up
    var stopwatch = Stopwatch.StartNew();
    for (int i = 0; i < 3; i++) operation();
    stopwatch.Stop();
    Console.WriteLine($"{name}: {stopwatch.Elapsed.TotalMilliseconds / 3:F1} ms average (3 runs)");
}
