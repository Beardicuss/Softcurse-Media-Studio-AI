using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SoftcurseMediaLabAI
{
    public enum HealthLevel { Ready, Optional, Unavailable }
    public readonly record struct ComponentHealth(HealthLevel Level, string Label, string Detail);
    public readonly record struct AppHealthSnapshot(ComponentHealth Core, ComponentHealth Ffmpeg, ComponentHealth Api);

    public static class AppHealthService
    {
        public static async Task<AppHealthSnapshot> CheckAsync(CancellationToken cancellationToken = default)
        {
            ComponentHealth core;
            try
            {
                string model = ModelPathResolver.Resolve("lama_fp32.onnx");
                string startupDetail = PerformanceMetrics.StartupSnapshot is { } startup
                    ? $" UI ready in {startup.Elapsed.TotalMilliseconds:F0} ms " +
                      $"({startup.PrivateMemoryBytes / 1024d / 1024d:F0} MB private memory)."
                    : string.Empty;
                core = new(HealthLevel.Ready, "CORE READY",
                    $"LaMa model found: {Path.GetFileName(model)}.{startupDetail}");
            }
            catch (Exception ex)
            {
                core = new(HealthLevel.Unavailable, "CORE MISSING", ex.Message);
            }

            Task<FfmpegProbeResult> ffmpegTask = FfmpegService.ProbeAsync(cancellationToken);
            Task<ComponentHealth> apiTask = CheckApiAsync(cancellationToken);
            FfmpegProbeResult ffmpegResult = await ffmpegTask;
            ComponentHealth api = await apiTask;
            ComponentHealth ffmpeg = ffmpegResult.Available
                ? new(HealthLevel.Ready, "FFMPEG READY", ffmpegResult.Message)
                : new(HealthLevel.Optional, "FFMPEG OFFLINE", ffmpegResult.Message);
            return new(core, ffmpeg, api);
        }

        private static async Task<ComponentHealth> CheckApiAsync(CancellationToken cancellationToken)
        {
            string endpoint = AppSettings.ApiEndpoint;
            if (!AppSettings.IsApiEndpointSafe(endpoint, out string reason))
                return new(HealthLevel.Optional, "API INVALID", reason);
            if (GenerativeApiClient.IsRemoteEndpoint(endpoint))
                return new(HealthLevel.Optional, "API REMOTE", "Remote API is not contacted automatically. Test it in Settings.");
            try
            {
                string detail = await GenerativeApiClient.TestConnectionAsync(endpoint, cancellationToken);
                return new(HealthLevel.Ready, "API READY", detail);
            }
            catch (Exception ex)
            {
                return new(HealthLevel.Optional, "API OFFLINE", ex.Message);
            }
        }
    }
}
