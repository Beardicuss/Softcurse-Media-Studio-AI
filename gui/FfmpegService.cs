using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SoftcurseMediaLabAI
{
    public readonly record struct FfmpegProbeResult(bool Available, string Message);

    public static class FfmpegService
    {
        public static string GetExecutable() => ResolveExecutable(AppSettings.FfmpegPath);

        public static string BundledExecutablePath => Path.Combine(
            AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");

        public static string ResolveExecutable(string? configured)
        {
            string value = configured?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(value) ||
                string.Equals(value, "ffmpeg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(BundledExecutablePath)) return BundledExecutablePath;
                throw new FileNotFoundException(
                    "The bundled FFmpeg component is missing. Reinstall Softcurse Media Lab AI.",
                    BundledExecutablePath);
            }
            if (Path.IsPathRooted(value))
            {
                if (!File.Exists(value)) throw new FileNotFoundException("Configured FFmpeg executable was not found.", value);
                if (!string.Equals(Path.GetExtension(value), ".exe", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("The configured FFmpeg path must point to an .exe file.", nameof(configured));
                return value;
            }
            throw new ArgumentException(
                "An FFmpeg override must be an absolute path to ffmpeg.exe.", nameof(configured));
        }

        public static async Task<FfmpegProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var startInfo = CreateStartInfo();
                startInfo.ArgumentList.Add("-version");
                using Process? process = Process.Start(startInfo);
                if (process is null) return new(false, "FFmpeg could not be started.");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                string firstLine = (await process.StandardOutput.ReadLineAsync(timeout.Token)) ?? "FFmpeg detected.";
                await process.WaitForExitAsync(timeout.Token);
                return process.ExitCode == 0
                    ? new(true, firstLine.Trim())
                    : new(false, $"FFmpeg exited with code {process.ExitCode}.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(false, "FFmpeg version check timed out.");
            }
            catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or ArgumentException)
            {
                return new(false, ex.Message);
            }
        }

        public static ProcessStartInfo CreateStartInfo() => new()
        {
            FileName = GetExecutable(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        public static void Terminate(Process? process)
        {
            if (process is null) return;
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        }
    }
}
