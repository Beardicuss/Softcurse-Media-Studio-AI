using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OpenCvSharp;

namespace GeminiWatermarkRemover.Views
{
    public partial class VideoLabPage : UserControl
    {
        private readonly WatermarkService _watermarkService;
        private string? _videoPath;
        private Mat? _firstFrameMat;
        private int    _videoWidth;
        private int    _videoHeight;
        private double _fps;
        private int    _totalFrames;
        private CancellationTokenSource? _cancellationTokenSource;

        public VideoLabPage(WatermarkService watermarkService)
        {
            InitializeComponent();
            _watermarkService = watermarkService;
        }

        private void SelectVideo_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Video Files|*.mp4;*.avi;*.mkv;*.mov",
                Title  = "Select Video"
            };
            if (dlg.ShowDialog() == true)
            {
                _videoPath = dlg.FileName;
                LoadVideoInfo();
            }
        }

        private void LoadVideoInfo()
        {
            try
            {
                using var cap = new VideoCapture(_videoPath!);
                if (!cap.IsOpened())
                {
                    DarkMessageBox.Show("Failed to open video.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _videoWidth  = (int)cap.Get(VideoCaptureProperties.FrameWidth);
                _videoHeight = (int)cap.Get(VideoCaptureProperties.FrameHeight);
                _fps         = cap.Get(VideoCaptureProperties.Fps);
                _totalFrames = (int)cap.Get(VideoCaptureProperties.FrameCount);

                VideoInfoText.Text =
                    $"Resolution: {_videoWidth}×{_videoHeight}  |  " +
                    $"FPS: {Math.Round(_fps, 2)}  |  " +
                    $"Frames: {_totalFrames}";

                _firstFrameMat = new Mat();
                cap.Read(_firstFrameMat);
                if (_firstFrameMat.Empty())
                {
                    DarkMessageBox.Show("Failed to read first frame.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Show preview via temp file (one-time, not per-frame)
                string tempPreview = Path.Combine(Path.GetTempPath(), $"preview_{Guid.NewGuid()}.png");
                Cv2.ImWrite(tempPreview, _firstFrameMat);
                TempFileManager.RegisterTempFile(tempPreview);

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource     = new Uri(tempPreview);
                bmp.CacheOption   = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                FirstFrameDisplay.Source = bmp;
                FirstFrameDisplay.Width  = bmp.PixelWidth;
                FirstFrameDisplay.Height = bmp.PixelHeight;

                MaskInkCanvas.Width  = bmp.PixelWidth;
                MaskInkCanvas.Height = bmp.PixelHeight;
                MaskInkCanvas.Visibility = Visibility.Visible;
                MaskInkCanvas.Strokes.Clear();

                ProcessVideoButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show($"Error loading video: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrushSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MaskInkCanvas?.DefaultDrawingAttributes != null)
            {
                MaskInkCanvas.DefaultDrawingAttributes.Width  = e.NewValue;
                MaskInkCanvas.DefaultDrawingAttributes.Height = e.NewValue;
            }
        }

        private void ClearMask_Click(object sender, RoutedEventArgs e)
            => MaskInkCanvas.Strokes.Clear();

        private async void ProcessVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_videoPath == null || _firstFrameMat == null) return;

            var saveDialog = new SaveFileDialog
            {
                Filter   = "MP4 Video (*.mp4)|*.mp4",
                Title    = "Save Processed Video",
                FileName = Path.GetFileNameWithoutExtension(_videoPath) + "_processed.mp4"
            };
            if (saveDialog.ShowDialog() != true) return;

            string outputPath = saveDialog.FileName;
            ProcessVideoButton.Visibility = Visibility.Collapsed;
            CancelProcessButton.Visibility = Visibility.Visible;
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            // Extract mask bytes on UI thread before going async
            byte[]? maskBytes = ExtractMaskBytes();

            // Use intermediate file so we can remux audio afterwards
            string tempVideoOut = Path.Combine(Path.GetTempPath(), $"vid_noaudio_{Guid.NewGuid()}.mp4");
            TempFileManager.RegisterTempFile(tempVideoOut);

            try
            {
                // F-05: ProcessVideoRun now passes Mat objects in-memory — no per-frame disk I/O
                await Task.Run(() => ProcessVideoRun(_videoPath, tempVideoOut, maskBytes, token), token);

                if (!token.IsCancellationRequested)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        VideoInfoText.Text = "REMUXING AUDIO...");
                    await Task.Run(() => RemuxAudio(_videoPath, tempVideoOut, outputPath, token), token);

                    if (!token.IsCancellationRequested)
                        DarkMessageBox.Show("Video processing complete! Audio remuxed.",
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (OperationCanceledException)
            {
                VideoInfoText.Text = "Process cancelled.";
            }
            finally
            {
                CancelProcessButton.Visibility  = Visibility.Collapsed;
                ProcessVideoButton.Visibility   = Visibility.Visible;
                VideoProgressBar.Value          = 0;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void CancelProcess_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            CancelProcessButton.IsEnabled = false;
            VideoInfoText.Text = "CANCELLING...";
        }

        // ── F-05: In-memory frame pipeline — no per-frame PNG write/read ──
        private void ProcessVideoRun(
            string inputPath, string outputPath,
            byte[]? maskBytes, CancellationToken token)
        {
            try
            {
                using var capture = new VideoCapture(inputPath);
                using var writer  = new VideoWriter(
                    outputPath, FourCC.MP4V, _fps,
                    new OpenCvSharp.Size(_videoWidth, _videoHeight), true);

                if (!writer.IsOpened())
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        DarkMessageBox.Show(
                            "Failed to open video writer. Ensure codec support is installed.",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                    return;
                }

                int currentFrame = 0;
                using Mat frame = new Mat();

                while (capture.Read(frame) && !frame.Empty())
                {
                    token.ThrowIfCancellationRequested();

                    // F-05 fix: pass Mat directly — no temp file written per frame
                    using Mat processed = _watermarkService.RemoveWatermarkFromMat(frame, maskBytes);
                    writer.Write(processed);

                    currentFrame++;
                    if (currentFrame % 5 == 0 || currentFrame == _totalFrames)
                    {
                        int cf = currentFrame; // capture for closure
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (!token.IsCancellationRequested)
                            {
                                VideoProgressBar.Value = (double)cf / _totalFrames * 100;
                                VideoInfoText.Text     = $"Processing: {cf}/{_totalFrames} frames";
                            }
                        }));
                    }
                }

                capture.Release();
                writer.Release();

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        VideoInfoText.Text     = $"Done: {_totalFrames} frames processed.";
                        VideoProgressBar.Value = 100;
                    }
                }));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                    DarkMessageBox.Show($"Video processing error: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }

        // ── F-13: FFmpeg availability check exposed as a public utility ──
        public static bool IsFfmpegAvailable()
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName               = "ffmpeg",
                    Arguments              = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                });
                p?.WaitForExit(2000);
                return p?.ExitCode == 0;
            }
            catch { return false; }
        }

        private void RemuxAudio(
            string originalVideo, string processedNoAudio,
            string finalOutput, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName               = "ffmpeg",
                    Arguments              = $"-y -i \"{processedNoAudio}\" -i \"{originalVideo}\" " +
                                             $"-c copy -map 0:v:0 -map 1:a:0? -shortest \"{finalOutput}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    while (!process.HasExited)
                    {
                        token.ThrowIfCancellationRequested();
                        Thread.Sleep(500);
                    }
                    if (process.ExitCode != 0 && !token.IsCancellationRequested)
                        File.Copy(processedNoAudio, finalOutput, true); // silent audio-less fallback
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (System.ComponentModel.Win32Exception)
            {
                // F-13: explicit message; ffmpeg not in PATH
                Application.Current.Dispatcher.Invoke(() =>
                    DarkMessageBox.Show(
                        "ffmpeg was not found in your system PATH.\n\n" +
                        "Audio could not be remuxed — a silent video has been saved instead.\n\n" +
                        "Install ffmpeg from https://ffmpeg.org/download.html and add it to PATH.",
                        "ffmpeg Not Found", MessageBoxButton.OK, MessageBoxImage.Warning));
                if (File.Exists(processedNoAudio) && !File.Exists(finalOutput))
                    File.Copy(processedNoAudio, finalOutput, true);
            }
            catch (Exception)
            {
                if (File.Exists(processedNoAudio) && !File.Exists(finalOutput))
                    File.Copy(processedNoAudio, finalOutput, true);
            }
        }

        private byte[]? ExtractMaskBytes()
        {
            if (MaskInkCanvas.Strokes.Count == 0) return null;

            var grid    = new System.Windows.Controls.Grid { Width = _videoWidth, Height = _videoHeight, Background = Brushes.Transparent };
            var inkCopy = new System.Windows.Controls.InkCanvas { Width = _videoWidth, Height = _videoHeight, Background = Brushes.Transparent };
            inkCopy.Strokes = MaskInkCanvas.Strokes.Clone();
            grid.Children.Add(inkCopy);

            grid.Measure(new System.Windows.Size(_videoWidth, _videoHeight));
            grid.Arrange(new System.Windows.Rect(new System.Windows.Size(_videoWidth, _videoHeight)));

            var rtb = new RenderTargetBitmap(_videoWidth, _videoHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(grid);

            byte[] pixels = new byte[_videoWidth * _videoHeight * 4];
            rtb.CopyPixels(pixels, _videoWidth * 4, 0);

            byte[] mask = new byte[_videoWidth * _videoHeight];
            for (int i = 0; i < mask.Length; i++)
                mask[i] = pixels[i * 4 + 3]; // alpha channel

            return mask;
        }
    }
}
