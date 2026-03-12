using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SoftcurseMediaLabAI.Views
{
    public partial class SpriteGeneratorPage : UserControl
    {
        // ── Dependencies ────────────────────────────────────────────────
        private readonly WatermarkService _watermarkService;
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        // ── State ───────────────────────────────────────────────────────
        private CancellationTokenSource? _cts;
        private List<string> _generatedFramePaths = new();
        private string? _spriteSheetPath;
        private string? _importedFilePath;
        private int _animFrameIndex;
        private DispatcherTimer? _animTimer;

        // ── Animation prompt map ────────────────────────────────────────
        private static readonly Dictionary<string, string> AnimPrompts = new()
        {
            ["Idle Breathe"] = "idle animation, gentle breathing, subtle sway",
            ["Walk Cycle"]   = "walking cycle, smooth leg motion, arms swinging",
            ["Run Cycle"]    = "running fast, dynamic motion, speed lines",
            ["Attack Slash"] = "sword slash attack motion, aggressive pose",
            ["Jump Arc"]     = "jumping arc, airborne pose",
            ["Death Fall"]   = "falling down, dying animation",
            ["Spell Cast"]   = "casting spell, magical effect, glowing hands",
        };

        public SpriteGeneratorPage(WatermarkService watermarkService)
        {
            InitializeComponent();
            _watermarkService = watermarkService;
        }

        // ══════════════════════════════════════════════════════════════════
        //  TAB SWITCHING
        // ══════════════════════════════════════════════════════════════════
        private void TabGenerate_Checked(object sender, RoutedEventArgs e)
        {
            if (GeneratePanel == null) return;
            GeneratePanel.Visibility = Visibility.Visible;
            ImportPanel.Visibility = Visibility.Collapsed;
            if (TabImport != null) TabImport.IsChecked = false;
        }

        private void TabImport_Checked(object sender, RoutedEventArgs e)
        {
            if (ImportPanel == null) return;
            ImportPanel.Visibility = Visibility.Visible;
            GeneratePanel.Visibility = Visibility.Collapsed;
            if (TabGenerate != null) TabGenerate.IsChecked = false;
        }

        private void BrowseImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
            };
            if (dlg.ShowDialog() == true)
            {
                _importedFilePath = dlg.FileName;
                ImportFileLabel.Text = Path.GetFileName(dlg.FileName);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  SLIDER / COMBO HANDLERS
        // ══════════════════════════════════════════════════════════════════
        private void DenoiseSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (DenoiseLabel != null)
                DenoiseLabel.Text = e.NewValue.ToString("F2");
        }

        private void OutlineCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (OutlineColorPanel == null) return;
            OutlineColorPanel.Visibility = OutlineCombo.SelectedIndex > 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OutlineColor_Changed(object sender, TextChangedEventArgs e)
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(OutlineColorBox.Text);
                ColorSwatch.Background = new System.Windows.Media.SolidColorBrush(color);
            }
            catch { /* ignore invalid colour strings while typing */ }
        }

        private void FpsSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (FpsLabel != null)
                FpsLabel.Text = ((int)e.NewValue).ToString();
            if (_animTimer != null)
                _animTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, (int)e.NewValue));
        }

        // ══════════════════════════════════════════════════════════════════
        //  PREVIEW MODE SWITCHING
        // ══════════════════════════════════════════════════════════════════
        private void PreviewFrames_Checked(object sender, RoutedEventArgs e)
        {
            SetPreviewMode("frames");
            if (PreviewSheet != null) PreviewSheet.IsChecked = false;
            if (PreviewAnim != null) PreviewAnim.IsChecked = false;
        }

        private void PreviewSheet_Checked(object sender, RoutedEventArgs e)
        {
            SetPreviewMode("sheet");
            if (PreviewFrames != null) PreviewFrames.IsChecked = false;
            if (PreviewAnim != null) PreviewAnim.IsChecked = false;
        }

        private void PreviewAnim_Checked(object sender, RoutedEventArgs e)
        {
            SetPreviewMode("anim");
            if (PreviewFrames != null) PreviewFrames.IsChecked = false;
            if (PreviewSheet != null) PreviewSheet.IsChecked = false;
            StartAnimation();
        }

        private void PreviewAnim_Unchecked(object sender, RoutedEventArgs e)
        {
            StopAnimation();
        }

        private void SetPreviewMode(string mode)
        {
            if (FramesScrollView == null) return;
            FramesScrollView.Visibility = mode == "frames" ? Visibility.Visible : Visibility.Collapsed;
            SheetScrollView.Visibility  = mode == "sheet"  ? Visibility.Visible : Visibility.Collapsed;
            AnimView.Visibility         = mode == "anim"   ? Visibility.Visible : Visibility.Collapsed;
        }

        // ══════════════════════════════════════════════════════════════════
        //  ANIMATION PLAYBACK
        // ══════════════════════════════════════════════════════════════════
        private void StartAnimation()
        {
            if (_generatedFramePaths.Count == 0) return;
            _animFrameIndex = 0;
            _animTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, (int)FpsSlider.Value))
            };
            _animTimer.Tick += (s, e) =>
            {
                if (_generatedFramePaths.Count == 0) { StopAnimation(); return; }
                _animFrameIndex = (_animFrameIndex + 1) % _generatedFramePaths.Count;
                try
                {
                    AnimPreviewImage.Source = LoadBitmap(_generatedFramePaths[_animFrameIndex]);
                }
                catch { }
            };
            _animTimer.Start();
        }

        private void StopAnimation()
        {
            _animTimer?.Stop();
            _animTimer = null;
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════
        private int ParseFrameCount()
        {
            string text = (FrameCountCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "4";
            // Extract leading number: "4 (Walk)" → 4
            string num = new string(text.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(num, out int n) ? n : 4;
        }

        private int ParseSize()
        {
            string text = (SizeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "512x512";
            string dim = text.Split('x')[0];
            return int.TryParse(dim, out int s) ? s : 512;
        }

        private int ParseColumns()
        {
            string text = (ColsCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "4";
            return int.TryParse(text, out int c) ? c : 4;
        }

        // ══════════════════════════════════════════════════════════════════
        //  MAIN GENERATE ENTRY POINT
        // ══════════════════════════════════════════════════════════════════
        private async void GenerateBtn_Click(object sender, RoutedEventArgs e)
        {
            string apiUrl = AppSettings.ApiEndpoint;
            if (!AppSettings.IsApiEndpointSafe(apiUrl, out string urlErr))
            {
                DarkMessageBox.Show($"API endpoint is invalid:\n{urlErr}\n\nUpdate it in Settings.",
                    "Invalid API Endpoint", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetGenerating(true);
            CleanupPreviousFrames();

            var token = (_cts = new CancellationTokenSource()).Token;

            try
            {
                if (!apiUrl.EndsWith("/")) apiUrl += "/";

                string prompt    = PromptBox.Text.Trim();
                string negPrompt = NegPromptBox.Text.Trim();
                int    size      = ParseSize();
                int    frameCount = ParseFrameCount();
                int    columns   = ParseColumns();
                bool   removeBg  = RemoveBgCheck.IsChecked == true;

                // Add animation style to prompt
                string animStyle = (AnimStyleCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Idle Breathe";
                if (AnimPrompts.TryGetValue(animStyle, out string? animSuffix))
                    prompt += ", " + animSuffix;

                // STEP 1: Generate frames
                var frames = new List<string>();
                for (int i = 0; i < frameCount; i++)
                {
                    token.ThrowIfCancellationRequested();
                    SetStatus($"Generating frame {i + 1}/{frameCount}...",
                        10 + (int)((double)i / frameCount * 50));

                    long seed = new Random().Next(0, int.MaxValue);
                    string path = await GenerateStaticSpriteAsync(
                        apiUrl, prompt, negPrompt, size, size, 20, 7.0, seed, token);

                    if (!string.IsNullOrEmpty(path))
                    {
                        TempFileManager.RegisterTempFile(path);
                        frames.Add(path);
                        ShowFrameStrip(frames);
                    }
                }

                if (frames.Count == 0)
                {
                    SetStatus("GENERATION FAILED — check API connection.", 0);
                    return;
                }

                // STEP 2: Background removal
                if (removeBg)
                {
                    SetStatus("Removing backgrounds...", 65);
                    frames = await Task.Run(() =>
                        frames.Select(f =>
                        {
                            string outPath = Path.Combine(Path.GetTempPath(), $"nobg_{Guid.NewGuid()}.png");
                            SpriteSheetService.RemoveBackgroundGrabCut(f, outPath);
                            TempFileManager.RegisterTempFile(outPath);
                            return outPath;
                        }).ToList(), token);
                }

                _generatedFramePaths = frames;
                ShowFrameStrip(frames);

                // STEP 3: Assemble sprite sheet
                SetStatus("Assembling sprite sheet...", 85);
                string sheetPath = await Task.Run(() =>
                    SpriteSheetService.BuildSheet(frames, columns), token);

                TempFileManager.RegisterTempFile(sheetPath);
                _spriteSheetPath = sheetPath;

                // Show sheet preview
                SheetPreviewImage.Source = LoadBitmap(sheetPath);
                int rows = (int)Math.Ceiling(frames.Count / (double)columns);
                InfoLabel.Text = $"{frames.Count} frames  |  {columns} cols × {rows} rows  |  {size}×{size}px";

                ExportSheetBtn.IsEnabled = true;
                ExportFramesBtn.IsEnabled = true;
                EmptyState.Visibility = Visibility.Collapsed;
                FramesScrollView.Visibility = Visibility.Visible;
                SetStatus($"DONE — {frames.Count} frames generated.", 100);
            }
            catch (OperationCanceledException)
            {
                SetStatus("CANCELLED.", 0);
            }
            catch (Exception ex)
            {
                SetStatus($"ERROR: {ex.Message}", 0);
                DarkMessageBox.Show(
                    $"Sprite generation failed:\n\n{ex.Message}\n\n" +
                    "Make sure Stable Diffusion WebUI is running with --api.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetGenerating(false);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  API CALL
        // ══════════════════════════════════════════════════════════════════
        private async Task<string> GenerateStaticSpriteAsync(
            string apiBase, string prompt, string negPrompt,
            int w, int h, int steps, double cfg, long seed,
            CancellationToken token)
        {
            var payload = new
            {
                prompt,
                negative_prompt = negPrompt,
                width = w, height = h,
                steps, cfg_scale = cfg,
                seed,
                sampler_name = "DPM++ 2M Karras",
                restore_faces = false,
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            string url = apiBase + "sdapi/v1/txt2img";
            var response = await _http.PostAsync(url,
                new StringContent(json, Encoding.UTF8, "application/json"), token);

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync(token);
                throw new InvalidOperationException(
                    $"SD API error ({response.StatusCode}). Is --api flag enabled?\n{err[..Math.Min(300, err.Length)]}");
            }

            string body = await response.Content.ReadAsStringAsync(token);
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("images", out JsonElement imEl) || imEl.GetArrayLength() == 0)
                throw new InvalidOperationException("SD API returned no images.");

            byte[] bytes = Convert.FromBase64String(imEl[0].GetString()!);
            string path = Path.Combine(Path.GetTempPath(), $"sprite_{Guid.NewGuid()}.png");
            await File.WriteAllBytesAsync(path, bytes, token);
            return path;
        }

        // ══════════════════════════════════════════════════════════════════
        //  UI HELPERS
        // ══════════════════════════════════════════════════════════════════
        private void ShowFrameStrip(List<string> paths)
        {
            Dispatcher.Invoke(() =>
            {
                FrameWrap.Children.Clear();
                foreach (string p in paths)
                {
                    try
                    {
                        var img = new System.Windows.Controls.Image
                        {
                            Source  = LoadBitmap(p),
                            Width   = 64, Height = 64,
                            Stretch = System.Windows.Media.Stretch.Uniform,
                            Margin  = new Thickness(2),
                        };
                        System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.NearestNeighbor);

                        var border = new Border
                        {
                            BorderBrush     = (System.Windows.Media.Brush)FindResource("BorderBrush"),
                            BorderThickness = new Thickness(1),
                            Child           = img,
                        };
                        FrameWrap.Children.Add(border);
                    }
                    catch { }
                }
            });
        }

        private static BitmapImage LoadBitmap(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource   = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private void SetStatus(string text, double progress)
        {
            Dispatcher.Invoke(() =>
            {
                StatusLabel.Text       = text;
                ProgressBar.Value      = progress;
                ProgressBar.Visibility = progress > 0 ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        private void SetGenerating(bool generating)
        {
            Dispatcher.Invoke(() =>
            {
                GenerateBtn.Visibility = generating ? Visibility.Collapsed : Visibility.Visible;
                CancelBtn.Visibility   = generating ? Visibility.Visible   : Visibility.Collapsed;
                if (!generating) ProgressBar.Visibility = Visibility.Collapsed;
            });
        }

        private void CleanupPreviousFrames()
        {
            Dispatcher.Invoke(() =>
            {
                FrameWrap.Children.Clear();
                SheetPreviewImage.Source = null;
                EmptyState.Visibility   = Visibility.Visible;
                InfoLabel.Text          = "No sprites generated yet";
                ExportSheetBtn.IsEnabled  = false;
                ExportFramesBtn.IsEnabled = false;
            });
            _generatedFramePaths.Clear();
            _spriteSheetPath = null;
            StopAnimation();
        }

        // ══════════════════════════════════════════════════════════════════
        //  CANCEL / EXPORT
        // ══════════════════════════════════════════════════════════════════
        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            CancelBtn.IsEnabled = false;
        }

        private void DownloadSdWebui_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/AUTOMATIC1111/stable-diffusion-webui/releases",
                UseShellExecute = true
            });
        }

        private void ExportSheet_Click(object sender, RoutedEventArgs e)
        {
            if (_spriteSheetPath == null) return;
            var dlg = new SaveFileDialog
            {
                Filter   = "PNG Image (*.png)|*.png",
                FileName = "spritesheet.png"
            };
            if (dlg.ShowDialog() == true)
                File.Copy(_spriteSheetPath, dlg.FileName, overwrite: true);
        }

        private void ExportFrames_Click(object sender, RoutedEventArgs e)
        {
            if (_generatedFramePaths.Count == 0) return;
            // Use SaveFileDialog to pick a folder by saving the first frame, then export all there
            var dlg = new SaveFileDialog
            {
                Filter   = "PNG Image (*.png)|*.png",
                FileName = "frame_000.png",
                Title    = "Choose location to save frames (all frames will be saved to this folder)"
            };
            if (dlg.ShowDialog() != true) return;
            string folder = Path.GetDirectoryName(dlg.FileName)!;
            for (int i = 0; i < _generatedFramePaths.Count; i++)
                File.Copy(_generatedFramePaths[i],
                    Path.Combine(folder, $"frame_{i:D3}.png"), overwrite: true);
        }
    }
}
