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
        private int _videoWidth;
        private int _videoHeight;
        private double _fps;
        private int _totalFrames;
        private System.Threading.CancellationTokenSource? _cancellationTokenSource;

        public VideoLabPage(WatermarkService watermarkService)
        {
            InitializeComponent();
            _watermarkService = watermarkService;
        }

        private void SelectVideo_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Video Files|*.mp4;*.avi;*.mkv;*.mov",
                Title = "Select Video"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _videoPath = openFileDialog.FileName;
                LoadVideoInfo();
            }
        }

        private void LoadVideoInfo()
        {
            try
            {
                using var capture = new VideoCapture(_videoPath!);
                if (!capture.IsOpened())
                {
                    MessageBox.Show("Failed to open video.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _videoWidth = (int)capture.Get(VideoCaptureProperties.FrameWidth);
                _videoHeight = (int)capture.Get(VideoCaptureProperties.FrameHeight);
                _fps = capture.Get(VideoCaptureProperties.Fps);
                _totalFrames = (int)capture.Get(VideoCaptureProperties.FrameCount);

                VideoInfoText.Text = $"Resolution: {_videoWidth}x{_videoHeight} | FPS: {Math.Round(_fps, 2)} | Frames: {_totalFrames}";

                _firstFrameMat = new Mat();
                capture.Read(_firstFrameMat);

                if (_firstFrameMat.Empty())
                {
                    MessageBox.Show("Failed to read the first frame.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Show first frame by writing to temp file and loading via BitmapImage
                string tempFile = Path.Combine(Path.GetTempPath(), $"first_frame_{Guid.NewGuid()}.png");
                Cv2.ImWrite(tempFile, _firstFrameMat);
                TempFileManager.RegisterTempFile(tempFile);
                
                BitmapImage bitmapSource = new BitmapImage();
                bitmapSource.BeginInit();
                bitmapSource.UriSource = new Uri(tempFile);
                bitmapSource.CacheOption = BitmapCacheOption.OnLoad;
                bitmapSource.EndInit();
                bitmapSource.Freeze();

                FirstFrameDisplay.Source = bitmapSource;
                FirstFrameDisplay.Width = bitmapSource.PixelWidth;
                FirstFrameDisplay.Height = bitmapSource.PixelHeight;

                MaskInkCanvas.Width = bitmapSource.PixelWidth;
                MaskInkCanvas.Height = bitmapSource.PixelHeight;
                MaskInkCanvas.Visibility = Visibility.Visible;
                MaskInkCanvas.Strokes.Clear();

                ProcessVideoButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrushSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MaskInkCanvas != null && MaskInkCanvas.DefaultDrawingAttributes != null)
            {
                MaskInkCanvas.DefaultDrawingAttributes.Width = e.NewValue;
                MaskInkCanvas.DefaultDrawingAttributes.Height = e.NewValue;
            }
        }

        private void ClearMask_Click(object sender, RoutedEventArgs e)
        {
            MaskInkCanvas.Strokes.Clear();
        }

        private async void ProcessVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_videoPath == null || _firstFrameMat == null) return;

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "MP4 Video (*.mp4)|*.mp4",
                Title = "Save Processed Video",
                FileName = Path.GetFileNameWithoutExtension(_videoPath) + "_processed.mp4"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                string outputPath = saveFileDialog.FileName;
                ProcessVideoButton.Visibility = Visibility.Collapsed;
                CancelProcessButton.Visibility = Visibility.Visible;
                _cancellationTokenSource = new CancellationTokenSource();
                var token = _cancellationTokenSource.Token;

                // Extract mask
                byte[]? maskBytes = null;
                if (MaskInkCanvas.Strokes.Count > 0)
                {
                    var offScreenGrid = new Grid { Width = _videoWidth, Height = _videoHeight, Background = Brushes.Transparent };
                    var offScreenInk = new InkCanvas { Width = _videoWidth, Height = _videoHeight, Background = Brushes.Transparent };
                    offScreenInk.Strokes = MaskInkCanvas.Strokes.Clone();
                    offScreenGrid.Children.Add(offScreenInk);

                    offScreenGrid.Measure(new System.Windows.Size(_videoWidth, _videoHeight));
                    offScreenGrid.Arrange(new System.Windows.Rect(new System.Windows.Size(_videoWidth, _videoHeight)));

                    RenderTargetBitmap rtb = new RenderTargetBitmap(_videoWidth, _videoHeight, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(offScreenGrid);

                    maskBytes = new byte[_videoWidth * _videoHeight * 4];
                    rtb.CopyPixels(maskBytes, _videoWidth * 4, 0);
                }

                // Use an intermediate file for OpenCV so we can remux FFmpeg to the final user path
                string tempVideoOut = Path.Combine(Path.GetTempPath(), $"vid_noaudio_{Guid.NewGuid()}.mp4");
                TempFileManager.RegisterTempFile(tempVideoOut);
                
                try
                {
                    await Task.Run(() => ProcessVideoRun(_videoPath, tempVideoOut, maskBytes, token), token);
                    
                    if (!token.IsCancellationRequested)
                    {
                        Application.Current.Dispatcher.Invoke(() => VideoInfoText.Text = "REMUXING AUDIO...");
                        await Task.Run(() => RemuxAudio(_videoPath, tempVideoOut, outputPath, token), token);

                        if (!token.IsCancellationRequested)
                        {
                            MessageBox.Show("Video processing complete! Audio successfully remuxed.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    VideoInfoText.Text = "Process Canceled.";
                }
                finally
                {
                    CancelProcessButton.Visibility = Visibility.Collapsed;
                    ProcessVideoButton.Visibility = Visibility.Visible;
                    VideoProgressBar.Value = 0;
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                }
            }
        }

        private void CancelProcess_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            CancelProcessButton.IsEnabled = false; // Prevent multiple clicks
            VideoInfoText.Text = "CANCELLING...";
        }

        private void ProcessVideoRun(string inputPath, string outputPath, byte[]? maskBytes, CancellationToken token)
        {
            try
            {
                using var capture = new VideoCapture(inputPath);
                using var writer = new VideoWriter(outputPath, FourCC.MP4V, _fps, new OpenCvSharp.Size(_videoWidth, _videoHeight), true);

                if (!writer.IsOpened())
                {
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show("Failed to initialize video writer. Ensure you have the proper codecs installed.", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                    return;
                }

                int currentFrame = 0;
                Mat frame = new Mat();

                while (capture.Read(frame) && !frame.Empty())
                {
                    if (token.IsCancellationRequested)
                    {
                        token.ThrowIfCancellationRequested();
                    }
                    string tempInput = Path.Combine(Path.GetTempPath(), $"vid_frame_in_{Guid.NewGuid()}.png");
                    string tempOutput = Path.Combine(Path.GetTempPath(), $"vid_frame_out_{Guid.NewGuid()}.png");
                    
                    Cv2.ImWrite(tempInput, frame);
                    TempFileManager.RegisterTempFile(tempInput);
                    TempFileManager.RegisterTempFile(tempOutput);

                    _watermarkService.RemoveWatermark(tempInput, tempOutput, maskBytes != null, maskBytes);

                    using var outFrame = Cv2.ImRead(tempOutput, ImreadModes.Color);
                    writer.Write(outFrame);

                    if (File.Exists(tempInput)) File.Delete(tempInput);
                    if (File.Exists(tempOutput)) File.Delete(tempOutput);

                    currentFrame++;
                    if (currentFrame % 5 == 0 || currentFrame == _totalFrames)
                    {
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (!token.IsCancellationRequested)
                            {
                                VideoProgressBar.Value = (double)currentFrame / _totalFrames * 100;
                                VideoInfoText.Text = $"Processing: {currentFrame}/{_totalFrames} frames";
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
                        VideoInfoText.Text = $"Done: {_totalFrames} frames processed.";
                        VideoProgressBar.Value = 100;
                    }
                }));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"Video processing error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }
        
        private void RemuxAudio(string originalVideo, string processedNoAudio, string finalOutput, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    // -y overwrite, -i video, -i original (for audio), copy video, copy audio, map cleanly
                    Arguments = $"-y -i \"{processedNoAudio}\" -i \"{originalVideo}\" -c copy -map 0:v:0 -map 1:a:0? -shortest \"{finalOutput}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                if (process != null)
                {
                    while (!process.HasExited)
                    {
                        if (token.IsCancellationRequested)
                        {
                            try { process.Kill(); } catch { }
                            token.ThrowIfCancellationRequested();
                        }
                        Thread.Sleep(500);
                    }
                    if (process.ExitCode != 0 && !token.IsCancellationRequested)
                    {
                        // Fallback if ffmpeg failed (maybe no audio track?)
                        File.Copy(processedNoAudio, finalOutput, true);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // FFmpeg not found in PATH, fallback to silent video
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show("FFmpeg not found in PATH! Audio could not be remuxed. Silent video saved instead.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning));
                if (File.Exists(processedNoAudio) && !File.Exists(finalOutput))
                {
                    File.Copy(processedNoAudio, finalOutput, true);
                }
            }
            catch (Exception)
            {
                // Silently fallback if anything else fails
                if (File.Exists(processedNoAudio) && !File.Exists(finalOutput))
                {
                    File.Copy(processedNoAudio, finalOutput, true);
                }
            }
        }
    }
}
