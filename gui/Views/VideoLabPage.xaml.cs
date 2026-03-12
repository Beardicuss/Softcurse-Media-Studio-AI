using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OpenCvSharp;

namespace SoftcurseMediaLabAI.Views
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

        // ── Converter state ─────────────────────────────────────────────
        private readonly List<string> _convertFiles = new();
        private readonly HashSet<string> _convertFilesSet = new(StringComparer.OrdinalIgnoreCase);
        private bool _isConverting;

        private static readonly string[] AudioFormats = { "mp3", "wav", "flac", "aac", "ogg", "opus", "m4a", "wma", "aiff", "ac3" };
        private static readonly string[] VideoFormats = { "mp4", "mkv", "avi", "mov", "webm", "ogv", "wmv", "flv", "m4v", "ts", "3gp" };
        private static readonly string[] AudioBitrates = { "320k", "256k", "192k", "128k", "96k", "64k" };
        private static readonly string[] SampleRates = { "44100", "48000", "22050", "16000", "8000" };
        private static readonly string[] Resolutions = { "Original", "3840:2160 (4K)", "1920:1080 (1080p)", "1280:720 (720p)", "854:480 (480p)", "640:360 (360p)" };
        private static readonly string[] Qualities = { "High (CRF 18)", "Medium (CRF 23)", "Low (CRF 28)", "Very Low (CRF 35)" };

        // codec map: format -> (audio_codec, video_codec_or_null)
        private static readonly Dictionary<string, (string acodec, string? vcodec)> CodecMap = new()
        {
            ["mp3"]  = ("libmp3lame", null),  ["wav"]  = ("pcm_s16le", null),
            ["flac"] = ("flac",       null),  ["aac"]  = ("aac",       null),
            ["ogg"]  = ("libvorbis",  null),  ["opus"] = ("libopus",   null),
            ["m4a"]  = ("aac",        null),  ["wma"]  = ("wmav2",     null),
            ["aiff"] = ("pcm_s16be",  null),  ["ac3"]  = ("ac3",       null),
            ["mp4"]  = ("aac",        "libx264"),  ["mkv"]  = ("aac",       "libx264"),
            ["avi"]  = ("libmp3lame", "mpeg4"),     ["mov"]  = ("aac",       "libx264"),
            ["webm"] = ("libvorbis",  "libvpx-vp9"),["ogv"]  = ("libvorbis", "libtheora"),
            ["wmv"]  = ("wmav2",      "wmv2"),      ["flv"]  = ("aac",       "libx264"),
            ["m4v"]  = ("aac",        "libx264"),  ["ts"]   = ("aac",       "libx264"),
            ["3gp"]  = ("aac",        "libx264"),
        };

        private static readonly HashSet<string> NoBitrateCodecs = new() { "pcm_s16le", "pcm_s16be", "flac" };
        private static readonly HashSet<string> VideoExts = new(VideoFormats.Select(f => "." + f), StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> AllInputExts = new(
            AudioFormats.Concat(VideoFormats).Select(f => "." + f), StringComparer.OrdinalIgnoreCase);

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

        // ════════════════════════════════════════════════════════════════
        // CONVERTER MODE
        // ════════════════════════════════════════════════════════════════

        private void RetouchMode_Checked(object sender, RoutedEventArgs e)
        {
            if (ConvertModeBtn == null || RetouchPanel == null) return;
            ConvertModeBtn.IsChecked = false;
            RetouchPanel.Visibility = Visibility.Visible;
            ConvertPanel.Visibility = Visibility.Collapsed;
            LoadVideoBtn.Content = "LOAD VIDEO";
            LoadVideoBtn.Visibility = Visibility.Visible;
            VideoInfoText.Text = _videoPath != null ? $"{Path.GetFileName(_videoPath)}" : "AWAITING VIDEO INPUT...";
        }

        private void ConvertMode_Checked(object sender, RoutedEventArgs e)
        {
            if (RetouchModeBtn == null || ConvertPanel == null) return;
            RetouchModeBtn.IsChecked = false;
            RetouchPanel.Visibility = Visibility.Collapsed;
            ConvertPanel.Visibility = Visibility.Visible;
            LoadVideoBtn.Visibility = Visibility.Collapsed;
            VideoInfoText.Text = $"{_convertFiles.Count} file(s) queued.";
        }

        // ── Type mode change (Audio / Video) ────────────────────────────
        private void TypeMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (OutputFormatCombo == null) return;
            OutputFormatCombo.Items.Clear();

            bool isVideo = TypeModeCombo.SelectedIndex == 1;
            var formats = isVideo ? VideoFormats : AudioFormats;
            foreach (var f in formats)
            {
                var item = new ComboBoxItem
                {
                    Content = f,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A1520")),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"))
                };
                OutputFormatCombo.Items.Add(item);
            }
            OutputFormatCombo.SelectedIndex = 0;

            if (AudioSettingsPanel != null && VideoSettingsPanel != null)
            {
                AudioSettingsPanel.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
                VideoSettingsPanel.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // ── File management ──────────────────────────────────────────────
        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "All Media|" + string.Join(";", AllInputExts.Select(x => "*" + x)) +
                         "|Audio|" + string.Join(";", AudioFormats.Select(f => "*." + f)) +
                         "|Video|" + string.Join(";", VideoFormats.Select(f => "*." + f)) +
                         "|All Files|*.*",
                Multiselect = true,
                Title = "Select audio/video files"
            };
            if (dlg.ShowDialog() != true) return;

            int added = 0;
            foreach (var path in dlg.FileNames)
                added += IngestFile(path);

            if (string.IsNullOrWhiteSpace(OutputFolderBox.Text) && dlg.FileNames.Length > 0)
                OutputFolderBox.Text = Path.GetDirectoryName(dlg.FileNames[0]) ?? "";

            UpdateFileCount();
            ConvertStatusText.Text = $"{added} file(s) added — {_convertFiles.Count} queued.";
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select folder" };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            string folder = dlg.SelectedPath;
            if (string.IsNullOrWhiteSpace(OutputFolderBox.Text))
                OutputFolderBox.Text = Directory.GetParent(folder)?.FullName ?? folder;

            ConvertStatusText.Text = "Scanning folder...";
            Task.Run(() =>
            {
                var files = ScanFolder(folder);
                Dispatcher.Invoke(() =>
                {
                    int added = 0;
                    foreach (var f in files) added += IngestFile(f);
                    UpdateFileCount();
                    ConvertStatusText.Text = files.Count == 0
                        ? "No supported files found."
                        : $"{added} file(s) added — {_convertFiles.Count} queued.";
                });
            });
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = ConvertFileList.SelectedItems.Cast<object>().ToList();
            if (selected.Count == 0) return;

            foreach (var item in selected)
            {
                int idx = ConvertFileList.Items.IndexOf(item);
                if (idx >= 0 && idx < _convertFiles.Count)
                {
                    _convertFilesSet.Remove(_convertFiles[idx]);
                    _convertFiles.RemoveAt(idx);
                    ConvertFileList.Items.RemoveAt(idx);
                }
            }
            UpdateFileCount();
        }

        private void ClearFiles_Click(object sender, RoutedEventArgs e)
        {
            _convertFiles.Clear();
            _convertFilesSet.Clear();
            ConvertFileList.Items.Clear();
            UpdateFileCount();
            ConvertProgressBar.Value = 0;
            ConvertStatusText.Text = "Ready — add files to begin.";
        }

        private int IngestFile(string path)
        {
            if (_convertFilesSet.Contains(path)) return 0;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (!AllInputExts.Contains(ext)) return 0;

            string icon = VideoExts.Contains(ext) ? "🎬" : "🎵";
            _convertFilesSet.Add(path);
            _convertFiles.Add(path);
            ConvertFileList.Items.Add($"{icon}  {Path.GetFileName(path)}");
            return 1;
        }

        private void UpdateFileCount()
        {
            int audio = _convertFiles.Count(p => !VideoExts.Contains(Path.GetExtension(p).ToLowerInvariant()));
            int video = _convertFiles.Count - audio;
            var parts = new List<string>();
            if (audio > 0) parts.Add($"{audio} audio");
            if (video > 0) parts.Add($"{video} video");
            FileCountText.Text = parts.Count > 0 ? string.Join(" · ", parts) : "";
            VideoInfoText.Text = $"{_convertFiles.Count} file(s) queued.";
        }

        private List<string> ScanFolder(string folder)
        {
            var found = new List<string>();
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
                {
                    if (AllInputExts.Contains(Path.GetExtension(file).ToLowerInvariant()))
                        found.Add(file);
                }
            }
            catch { /* permission errors etc */ }
            return found;
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select output folder" };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                OutputFolderBox.Text = dlg.SelectedPath;
        }

        // ── FFmpeg command builder ───────────────────────────────────────
        private string GetComboValue(ComboBox combo)
        {
            return (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        }

        private List<string> BuildFfmpegCmd(string src, string dst, string outFmt, Dictionary<string, string> settings)
        {
            var (acodec, vcodec) = CodecMap.GetValueOrDefault(outFmt, ("aac", null));
            bool srcIsVideo = VideoExts.Contains(Path.GetExtension(src).ToLowerInvariant());
            bool dstIsVideo = vcodec != null;

            var cmd = new List<string> { "ffmpeg", "-y", "-i", src };

            if (dstIsVideo && srcIsVideo)
            {
                // Video → Video
                cmd.AddRange(new[] { "-c:v", vcodec! });

                string res = settings.GetValueOrDefault("resolution", "Original");
                if (res != "Original" && res.Contains(':'))
                {
                    string wh = res.Split(' ')[0];
                    cmd.AddRange(new[] { "-vf",
                        $"scale={wh}:force_original_aspect_ratio=decrease," +
                        $"pad={wh}:(ow-iw)/2:(oh-ih)/2," +
                        $"scale=trunc(ow/2)*2:trunc(oh/2)*2" });
                }

                string quality = settings.GetValueOrDefault("quality", "Medium (CRF 23)");
                string crf = quality switch
                {
                    "High (CRF 18)" => "18", "Low (CRF 28)" => "28",
                    "Very Low (CRF 35)" => "35", _ => "23"
                };

                if (vcodec is "libx264" or "libx265")
                    cmd.AddRange(new[] { "-crf", crf, "-preset", "medium" });
                else if (vcodec == "libvpx-vp9")
                    cmd.AddRange(new[] { "-crf", crf, "-b:v", "0" });
                else if (vcodec == "libtheora")
                {
                    string tq = crf switch { "18" => "10", "23" => "7", "28" => "4", _ => "1" };
                    cmd.AddRange(new[] { "-q:v", tq });
                }
                else if (vcodec is "wmv2" or "mpeg4")
                {
                    string qv = crf switch { "18" => "2", "23" => "6", "28" => "14", _ => "24" };
                    cmd.AddRange(new[] { "-q:v", qv });
                }

                cmd.AddRange(new[] { "-c:a", acodec, "-b:a", settings.GetValueOrDefault("vid_audio_bitrate", "192k") });
            }
            else if (dstIsVideo && !srcIsVideo)
            {
                // Audio → Video container (audio-only)
                cmd.AddRange(new[] { "-vn", "-c:a", acodec });
                if (!NoBitrateCodecs.Contains(acodec))
                    cmd.AddRange(new[] { "-b:a", settings.GetValueOrDefault("vid_audio_bitrate", "192k") });
                cmd.AddRange(new[] { "-ar", settings.GetValueOrDefault("samplerate", "44100"), "-ac", settings.GetValueOrDefault("channels", "2") });
            }
            else
            {
                // Any → Audio
                cmd.AddRange(new[] { "-vn", "-c:a", acodec });
                if (!NoBitrateCodecs.Contains(acodec))
                    cmd.AddRange(new[] { "-b:a", settings.GetValueOrDefault("audio_bitrate", "192k") });
                string sr = settings.GetValueOrDefault("samplerate", "44100");
                if (acodec == "libopus" && sr != "8000" && sr != "12000" && sr != "16000" && sr != "24000" && sr != "48000")
                    sr = "48000";
                cmd.AddRange(new[] { "-ar", sr, "-ac", settings.GetValueOrDefault("channels", "2") });
            }

            cmd.Add(dst);
            return cmd;
        }

        // ── Batch conversion ─────────────────────────────────────────────
        private async void ConvertAll_Click(object sender, RoutedEventArgs e)
        {
            if (_isConverting) return;
            if (_convertFiles.Count == 0)
            {
                DarkMessageBox.Show("Please add files first.", "No files", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string outDir = OutputFolderBox.Text.Trim();
            if (string.IsNullOrEmpty(outDir) || !Directory.Exists(outDir))
            {
                DarkMessageBox.Show("Please choose a valid output folder.", "No output folder",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string outFmt = GetComboValue(OutputFormatCombo);
            if (string.IsNullOrEmpty(outFmt)) return;

            _isConverting = true;
            ConvertAllBtn.IsEnabled = false;
            ConvertProgressBar.Value = 0;

            // Capture all UI values on the UI thread before background work
            var settings = new Dictionary<string, string>
            {
                ["resolution"] = GetComboValue(ResolutionCombo),
                ["quality"] = GetComboValue(QualityCombo),
                ["vid_audio_bitrate"] = GetComboValue(VidAudioBitrateCombo),
                ["audio_bitrate"] = GetComboValue(AudioBitrateCombo),
                ["samplerate"] = GetComboValue(SampleRateCombo),
                ["channels"] = GetComboValue(ChannelsCombo) == "Mono" ? "1" : "2",
            };

            int total = _convertFiles.Count;
            int done = 0, ok = 0;
            var filesToConvert = _convertFiles.ToList();

            await Task.Run(() =>
            {
                foreach (var src in filesToConvert)
                {
                    string stem = Path.GetFileNameWithoutExtension(src);
                    if (string.IsNullOrEmpty(stem)) stem = "_unnamed";
                    string dst = Path.Combine(outDir, stem + "." + outFmt);
                    int idx = 1;
                    while (File.Exists(dst))
                    {
                        dst = Path.Combine(outDir, $"{stem}_{idx}.{outFmt}");
                        idx++;
                    }

                    var cmd = BuildFfmpegCmd(src, dst, outFmt, settings);
                    bool success = false;
                    string msg = "";

                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = cmd[0],
                            Arguments = string.Join(" ", cmd.Skip(1).Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = Process.Start(psi);
                        if (proc != null)
                        {
                            string stderr = proc.StandardError.ReadToEnd();
                            proc.WaitForExit();
                            if (proc.ExitCode == 0)
                            {
                                success = true;
                                msg = $"Saved: {Path.GetFileName(dst)}";
                            }
                            else
                            {
                                msg = $"Error: {BestError(stderr)}";
                            }
                        }
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        msg = "ffmpeg not found — install from ffmpeg.org";
                    }
                    catch (Exception ex)
                    {
                        msg = ex.Message;
                    }

                    done++;
                    if (success) ok++;
                    int d = done, o = ok;
                    string m = msg;
                    Dispatcher.BeginInvoke(() =>
                    {
                        ConvertProgressBar.Value = (double)d / total * 100;
                        ConvertStatusText.Text = $"[{d}/{total}]  {m}";
                    });
                }
            });

            _isConverting = false;
            ConvertAllBtn.IsEnabled = true;
            ConvertStatusText.Text = ok == total
                ? $"✔  Done! {ok}/{total} converted → {outDir}"
                : $"⚠  Finished with errors: {ok}/{total} succeeded.";
        }

        private static string BestError(string stderr)
        {
            if (string.IsNullOrEmpty(stderr)) return "Unknown error";
            var lines = stderr.Trim().Split('\n');
            var priority = lines.Where(l =>
                l.Contains("Error") || l.Contains("error") || l.Contains("Invalid") ||
                l.Contains("not found") || l.Contains("failed") || l.Contains("Failed")).ToArray();
            return priority.Length > 0
                ? priority.Last().Trim()
                : string.Join(" | ", lines.TakeLast(2).Select(l => l.Trim()).Where(l => l.Length > 0));
        }
    }
}
