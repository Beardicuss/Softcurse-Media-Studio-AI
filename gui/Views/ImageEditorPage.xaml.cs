using System;
using System.Collections.Generic;
using System.Windows.Ink;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Linq;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls;
using System.Threading.Tasks;

namespace GeminiWatermarkRemover.Views
{
    public partial class ImageEditorPage : UserControl
    {
        private string? _currentImagePath;
        private string? _processedImagePath;

        /// <summary>Path to the currently loaded/processed image. Used by MainWindow to pass to AI Gen Hub.</summary>
        public string? CurrentImagePath => _currentImagePath;

        private string? _originalImagePath;


        private WatermarkService _watermarkService;
        private SamModelService _samService;
        private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

        public ImageEditorPage(WatermarkService sharedService, SamModelService samService)
        {
            InitializeComponent();
            this.Unloaded += ImageEditorPage_Unloaded;
            // Enable mouse wheel zoom
            ImageScrollViewer.PreviewMouseWheel += ImageScrollViewer_PreviewMouseWheel;
            _watermarkService = sharedService;
            _samService = samService;
        }

        private void ImageEditorPage_Unloaded(object sender, RoutedEventArgs e)
        {
            // Do NOT dispose _watermarkService here, as it's owned by MainWindow
        }

        private void SelectImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*";
            if (openFileDialog.ShowDialog() == true)
            {
                LoadImage(openFileDialog.FileName);
            }
        }

        /// <summary>Public entry-point so MainWindow can load a file programmatically (e.g. from Sprite Generator).</summary>
        public void LoadImageFromPath(string path) => LoadImage(path);

        private void LoadImage(string path)
        {
            _originalImagePath = path;
            _currentImagePath = path;
            
            DisplayImage(path);
            
            // Reset zoom
            ImageScaleTransform.ScaleX = 1;
            ImageScaleTransform.ScaleY = 1;

            // Force 1:1 pixel mapping to ensure InkCanvas strokes match image pixels
            // regardless of DPI settings.
            BitmapImage bitmap = (BitmapImage)ImageDisplay.Source;
            ImageDisplay.Width = bitmap.PixelWidth;
            ImageDisplay.Height = bitmap.PixelHeight;
            ImageDisplay.Stretch = Stretch.Fill;

            ManualInkCanvas.Width = bitmap.PixelWidth;
            ManualInkCanvas.Height = bitmap.PixelHeight;
            
            SamCanvas.Width = bitmap.PixelWidth;
            SamCanvas.Height = bitmap.PixelHeight;
            
            PolygonCanvas.Width = bitmap.PixelWidth;
            PolygonCanvas.Height = bitmap.PixelHeight;
            
            StatusText.Text = $"Loaded: {System.IO.Path.GetFileName(path)}";
            DropZone.Visibility = Visibility.Collapsed;
            
            // Reset processed image path
            _processedImagePath = null;
            SaveButton.IsEnabled = false;
            CopyButton.IsEnabled = false;
            UpscaleButton.IsEnabled = true;
            BackgroundButton.IsEnabled = true;
            ExpandButton.IsEnabled = true;
            RetouchButton.IsEnabled = true;
            if (CompareToggleButton != null) CompareToggleButton.IsEnabled = false;
            if (CompareToggleButton != null) CompareToggleButton.IsChecked = false;
            
            // Clear manual mask elements across all modes
            if (ManualInkCanvas != null)
            {
                ManualInkCanvas.Strokes.Clear();
                if (PolygonCanvas != null) PolygonCanvas.Children.Clear();
                if (SamCanvas != null) SamCanvas.Children.Clear();
            }
        }
        
        private void DisplayImage(string path)
        {
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // Important for file locking
            bitmap.EndInit();
            bitmap.Freeze(); // Optimize memory by making it read-only
            ImageDisplay.Source = bitmap;
        }

        private void ImageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                double zoom = e.Delta > 0 ? 0.1 : -0.1;
                double newScale = ImageScaleTransform.ScaleX + zoom;
                
                // Clamp zoom
                if (newScale < 0.1) newScale = 0.1;
                if (newScale > 5.0) newScale = 5.0;

                ImageScaleTransform.ScaleX = newScale;
                ImageScaleTransform.ScaleY = newScale;
            }
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    LoadImage(files[0]);
                }
            }
        }
        private Polygon? _currentPolygon;
        private System.Windows.Shapes.Path? _previewLine;

        private void ToolComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ManualInkCanvas == null) return;
            
            ManualInkCanvas.Visibility = Visibility.Collapsed;
            PolygonCanvas.Visibility = Visibility.Collapsed;
            SamCanvas.Visibility = Visibility.Collapsed;
            BrushSizePanel.Visibility = Visibility.Collapsed;
            ClearMaskButton.Visibility = Visibility.Collapsed;
            StatusText.Text = "Auto Mode: Ready to remove watermark.";

            UpdateToolBorderHighlights(ToolComboBox.SelectedIndex);

            switch (ToolComboBox.SelectedIndex)
            {
                case 1: // Brush Tool
                    ManualInkCanvas.Visibility = Visibility.Visible;
                    ManualInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                    BrushSizePanel.Visibility = Visibility.Visible;
                    ClearMaskButton.Visibility = Visibility.Visible;
                    StatusText.Text = "Cyber Brush: Paint mask, then click APPLY MASK to process.";
                    break;
                case 2: // Eraser Tool
                    ManualInkCanvas.Visibility = Visibility.Visible;
                    ManualInkCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
                    ManualInkCanvas.EraserShape = new EllipseStylusShape(BrushSizeSlider.Value, BrushSizeSlider.Value);
                    BrushSizePanel.Visibility = Visibility.Visible;
                    ClearMaskButton.Visibility = Visibility.Visible;
                    StatusText.Text = "Eraser Tool: Erase parts of the mask.";
                    break;
                case 3: // Polygonal Lasso
                    PolygonCanvas.Visibility = Visibility.Visible;
                    ClearMaskButton.Visibility = Visibility.Visible;
                    StatusText.Text = "Poly Lasso: Left-click points, Right-click to close shape, then APPLY MASK.";
                    break;
                case 4: // Magic Wand (SAM)
                    SamCanvas.Visibility = Visibility.Visible;
                    ClearMaskButton.Visibility = Visibility.Visible;
                    StatusText.Text = "Magic Wand: Click an object to generate a mask, then APPLY MASK.";
                    break;
            }
        }

        private void ClearMask_Click(object sender, RoutedEventArgs e)
        {
            ManualInkCanvas.Strokes.Clear();
            PolygonCanvas.Children.Clear();
            SamCanvas.Children.Clear();
            _currentPolygon = null;
            _previewLine = null;
        }

        private void PolygonCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(PolygonCanvas);
            if (_currentPolygon == null)
            {
                _currentPolygon = new Polygon
                {
                    Stroke = Brushes.Red,
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(128, 255, 0, 0))
                };
                PolygonCanvas.Children.Add(_currentPolygon);
                _previewLine = new System.Windows.Shapes.Path
                {
                    Stroke = Brushes.Red,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                PolygonCanvas.Children.Add(_previewLine);
            }
            
            _currentPolygon.Points.Add(p);
        }

        private void PolygonCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_currentPolygon != null && _currentPolygon.Points.Count > 0 && _previewLine != null)
            {
                 Point p = e.GetPosition(PolygonCanvas);
                 var lastPoint = _currentPolygon.Points[_currentPolygon.Points.Count - 1];
                 var geometry = new LineGeometry(lastPoint, p);
                 _previewLine.Data = geometry;
            }
        }

        private void PolygonCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Finish drawing
            if (_previewLine != null)
            {
                PolygonCanvas.Children.Remove(_previewLine);
                _previewLine = null;
            }
            _currentPolygon = null;
        }

        private async void SamCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
             if (string.IsNullOrEmpty(_currentImagePath)) return;
             
             Point p = e.GetPosition(SamCanvas);
             
             // F-02: show honest label depending on whether real SAM models are loaded
             bool samLoaded = _samService.IsSamAvailable;
             StatusText.Text = samLoaded
                 ? "SAM: Generating mask from point..."
                 : "Region Select (GrabCut): Generating mask from point...";
             SamCanvas.IsHitTestVisible = false;

             try
             {
                 // F-02: GenerateMaskAsync now returns (maskPath, usedSam) tuple
                 var (maskPath, usedSam) = await _samService.GenerateMaskAsync(_currentImagePath, p);

                 TempFileManager.RegisterTempFile(maskPath);

                 BitmapImage maskBitmap = new BitmapImage();
                 maskBitmap.BeginInit();
                 maskBitmap.UriSource   = new Uri(maskPath);
                 maskBitmap.CacheOption = BitmapCacheOption.OnLoad;
                 maskBitmap.EndInit();
                 maskBitmap.Freeze();

                 System.Windows.Controls.Image maskOverlay = new System.Windows.Controls.Image
                 {
                     Source  = maskBitmap,
                     Width   = SamCanvas.Width,
                     Height  = SamCanvas.Height,
                     Stretch = Stretch.Fill,
                     Opacity = 0.5
                 };

                 SamCanvas.Children.Clear();
                 SamCanvas.Children.Add(maskOverlay);

                 StatusText.Text = usedSam
                     ? "SAM Mask Generated."
                     : "Region Mask Generated (GrabCut). For better results, install SAM ONNX models.";
             }
             catch (Exception ex)
             {
                 StatusText.Text = $"Region Select Error: {ex.Message}";
             }
             finally
             {
                 SamCanvas.IsHitTestVisible = true;
             }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_originalImagePath))
            {
                // Reload the original image
                LoadImage(_originalImagePath);
                ManualInkCanvas.Strokes.Clear();
                StatusText.Text = "Reset to original image.";
            }
        }

        private async void RemoveWatermark_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentImagePath)) return;

            StatusText.Text = "Processing...";
            
            // Capture UI state on the UI thread!
            bool isManualMode = ToolComboBox.SelectedIndex > 0;
            string inputPath = _currentImagePath; // Use current state as input

            await Task.Run(() =>
            {
                try
                {
                    string tempOutput = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"processed_{Guid.NewGuid()}.png");
                    
                    byte[]? mask = null;
                    if (isManualMode)
                    {
                        // Use OpenCV to get image dimensions
                        using var imgForSize = OpenCvSharp.Cv2.ImRead(inputPath, OpenCvSharp.ImreadModes.Color);
                        int imgWidth = imgForSize.Cols;
                        int imgHeight = imgForSize.Rows;
                        
                        // Create an in-memory Grid for rendering the combined mask
                        // We need to dispatch this to the UI thread because WPF UIElements are bound to it.
                        Dispatcher.Invoke(() => 
                        {
                            var offScreenGrid = new Grid();
                            offScreenGrid.Width = imgWidth;
                            offScreenGrid.Height = imgHeight;
                            offScreenGrid.Background = Brushes.Transparent;
                            
                            var offScreenInk = new InkCanvas { Width = imgWidth, Height = imgHeight, Background = Brushes.Transparent };
                            offScreenInk.Strokes = ManualInkCanvas.Strokes.Clone();
                            offScreenGrid.Children.Add(offScreenInk);
                            
                            var offScreenPolygon = new Canvas { Width = imgWidth, Height = imgHeight, Background = Brushes.Transparent };
                            foreach (UIElement child in PolygonCanvas.Children)
                            {
                                if (child is Polygon p)
                                {
                                    var newP = new Polygon { Stroke = p.Stroke, StrokeThickness = p.StrokeThickness, Fill = p.Fill };
                                    foreach (var pt in p.Points) newP.Points.Add(pt);
                                    offScreenPolygon.Children.Add(newP);
                                }
                            }
                            offScreenGrid.Children.Add(offScreenPolygon);
                            
                            var offScreenSam = new Canvas { Width = imgWidth, Height = imgHeight, Background = Brushes.Transparent };
                            foreach (UIElement child in SamCanvas.Children)
                            {
                                if (child is System.Windows.Controls.Image img)
                                {
                                    var newImg = new System.Windows.Controls.Image { Source = img.Source, Width = img.Width, Height = img.Height, Stretch = img.Stretch, Opacity = 1.0 };
                                    offScreenSam.Children.Add(newImg);
                                }
                            }
                            offScreenGrid.Children.Add(offScreenSam);
                            
                            // Force layout update
                            offScreenGrid.Measure(new Size(imgWidth, imgHeight));
                            offScreenGrid.Arrange(new Rect(new Size(imgWidth, imgHeight)));
                            
                            RenderTargetBitmap rtb = new RenderTargetBitmap(imgWidth, imgHeight, 96, 96, PixelFormats.Pbgra32);
                            rtb.Render(offScreenGrid);

                            byte[] pixels = new byte[imgWidth * imgHeight * 4];
                            rtb.CopyPixels(pixels, imgWidth * 4, 0);
                            
                            mask = new byte[imgWidth * imgHeight];
                            for (int i = 0; i < mask.Length; i++)
                            {
                                mask[i] = pixels[i * 4 + 3];
                            }
                        });
                    }

                    _watermarkService.RemoveWatermark(inputPath, tempOutput, false, mask);
                    
                    Dispatcher.Invoke(() =>
                    {
                        // Update current state
                        _currentImagePath = tempOutput;
                        _processedImagePath = tempOutput;
                        TempFileManager.RegisterTempFile(tempOutput);
                        
                        DisplayImage(_currentImagePath);
                        
                        StatusText.Text = "Watermark removed successfully!";
                        SaveButton.IsEnabled = true;
                        CopyButton.IsEnabled = true;
                        if (CompareToggleButton != null) CompareToggleButton.IsEnabled = true;
                        
                        // Clear strokes if in manual mode so they don't cover the result
                        if (isManualMode)
                        {
                            ClearMask_Click(this, new RoutedEventArgs());
                        }
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => StatusText.Text = $"Error: {ex.Message}");
                }
            });
        }

        private async void RemoveBackground_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentImagePath)) return;
            StatusText.Text = "Isolating Background...";
            BackgroundButton.IsEnabled = false;

            string inputPath = _currentImagePath;

            await Task.Run(() =>
            {
                try
                {
                    string tempOutput = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"bgrm_{Guid.NewGuid()}.png");
                    
                    using OpenCvSharp.Mat img = OpenCvSharp.Cv2.ImRead(inputPath, OpenCvSharp.ImreadModes.Color);
                    using OpenCvSharp.Mat mask = new OpenCvSharp.Mat(img.Size(), OpenCvSharp.MatType.CV_8UC1, OpenCvSharp.Scalar.All((int)OpenCvSharp.GrabCutClasses.BGD));
                    using OpenCvSharp.Mat bgdModel = new OpenCvSharp.Mat();
                    using OpenCvSharp.Mat fgdModel = new OpenCvSharp.Mat();

                    // Define a rectangle that is 90% of the image
                    OpenCvSharp.Rect rect = new OpenCvSharp.Rect(img.Width / 20, img.Height / 20, img.Width - img.Width / 10, img.Height - img.Height / 10);
                    
                    OpenCvSharp.Cv2.GrabCut(img, mask, rect, bgdModel, fgdModel, 5, OpenCvSharp.GrabCutModes.InitWithRect);

                    // Modify mask
                    using OpenCvSharp.Mat mask2 = new OpenCvSharp.Mat();
                    OpenCvSharp.Cv2.Compare(mask, new OpenCvSharp.Scalar((int)OpenCvSharp.GrabCutClasses.PR_FGD), mask2, OpenCvSharp.CmpType.EQ);
                    using OpenCvSharp.Mat mask3 = new OpenCvSharp.Mat();
                    OpenCvSharp.Cv2.Compare(mask, new OpenCvSharp.Scalar((int)OpenCvSharp.GrabCutClasses.FGD), mask3, OpenCvSharp.CmpType.EQ);
                    
                    using OpenCvSharp.Mat finalMask = new OpenCvSharp.Mat();
                    OpenCvSharp.Cv2.BitwiseOr(mask2, mask3, finalMask);

                    // Create output image with Alpha channel
                    using OpenCvSharp.Mat result = new OpenCvSharp.Mat(img.Size(), OpenCvSharp.MatType.CV_8UC4, OpenCvSharp.Scalar.All(0));
                    
                    OpenCvSharp.Mat[] imgChannels = OpenCvSharp.Cv2.Split(img);
                    OpenCvSharp.Mat[] resultChannels = new OpenCvSharp.Mat[] { imgChannels[0], imgChannels[1], imgChannels[2], finalMask };
                    OpenCvSharp.Cv2.Merge(resultChannels, result);

                    // Dispose split channel Mats to prevent memory leak
                    foreach (var ch in imgChannels) ch.Dispose();

                    OpenCvSharp.Cv2.ImWrite(tempOutput, result);

                    Dispatcher.Invoke(() =>
                    {
                        _currentImagePath = tempOutput;
                        _processedImagePath = tempOutput;
                        TempFileManager.RegisterTempFile(tempOutput);
                        DisplayImage(_currentImagePath);
                        StatusText.Text = "Background removed successfully!";
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => StatusText.Text = $"Background Removal Error: {ex.Message}");
                }
            });

            BackgroundButton.IsEnabled = true;
        }

        private async void Expand_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentImagePath)) return;
            StatusText.Text = "Expanding Canvas (AI Outpainting)...";
            ExpandButton.IsEnabled = false;

            string inputPath = _currentImagePath;

            await Task.Run(async () =>
            {
                try
                {
                    string base64Image;
                    string base64Mask;
                    int padSize = 128; 
                    
                    using (OpenCvSharp.Mat img = OpenCvSharp.Cv2.ImRead(inputPath, OpenCvSharp.ImreadModes.Color))
                    {
                        using OpenCvSharp.Mat paddedImg = new OpenCvSharp.Mat();
                        OpenCvSharp.Cv2.CopyMakeBorder(img, paddedImg, padSize, padSize, padSize, padSize, OpenCvSharp.BorderTypes.Reflect101);
                        
                        using OpenCvSharp.Mat mask = new OpenCvSharp.Mat(paddedImg.Size(), OpenCvSharp.MatType.CV_8UC1, OpenCvSharp.Scalar.All(255));
                        OpenCvSharp.Cv2.Rectangle(mask, new OpenCvSharp.Rect(padSize, padSize, img.Width, img.Height), OpenCvSharp.Scalar.All(0), -1);

                        byte[] imgBytes = paddedImg.ToBytes(".png");
                        byte[] maskBytes = mask.ToBytes(".png");
                        base64Image = Convert.ToBase64String(imgBytes);
                        base64Mask = Convert.ToBase64String(maskBytes);
                    }

                    var payload = new
                    {
                        prompt = "seamless background expansion, high quality, matching style, continuation",
                        negative_prompt = "border, frame, bad quality, disjointed",
                        init_images = new[] { base64Image },
                        mask = base64Mask,
                        inpainting_fill = 1,
                        inpaint_full_res = true,
                        inpaint_full_res_padding = 32,
                        inpainting_mask_invert = 0,
                        steps = 20,
                        cfg_scale = 7.0,
                        width = 512,
                        height = 512,
                        restore_faces = false,
                        denoising_strength = 0.85
                    };

                    var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                    string jsonPayload = JsonSerializer.Serialize(payload, jsonOptions);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    string apiUrl = AppSettings.ApiEndpoint;
                    
                    // F-04: validate URL before making any network request
                    if (!AppSettings.IsApiEndpointSafe(apiUrl, out string expandUrlError))
                    {
                        Dispatcher.Invoke(() => {
                            DarkMessageBox.Show($"Expand API endpoint is invalid:\n{expandUrlError}\n\nPlease update it in Settings.", "Invalid API Endpoint", MessageBoxButton.OK, MessageBoxImage.Warning);
                            StatusText.Text = "Expand Failed: Invalid API endpoint.";
                        });
                        return;
                    }
                    
                    // Route to img2img specifically — avoid double-appending
                    string expandApiUrl;
                    if (apiUrl.Contains("sdapi/v1/img2img", System.StringComparison.OrdinalIgnoreCase))
                        expandApiUrl = apiUrl;
                    else
                    {
                        if (!apiUrl.EndsWith("/")) apiUrl += "/";
                        expandApiUrl = apiUrl + "sdapi/v1/img2img";
                    }

                    HttpResponseMessage response = await _httpClient.PostAsync(expandApiUrl, content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        using JsonDocument doc = JsonDocument.Parse(responseBody);
                        if (doc.RootElement.TryGetProperty("images", out JsonElement imagesElement) && imagesElement.GetArrayLength() > 0)
                        {
                            string returningBase64 = imagesElement[0].GetString()!;
                            byte[] returningBytes = Convert.FromBase64String(returningBase64);
                            
                            string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"expand_{Guid.NewGuid()}.png");
                            System.IO.File.WriteAllBytes(tempFile, returningBytes);
                            
                            Dispatcher.Invoke(() =>
                            {
                                _currentImagePath = tempFile;
                                _processedImagePath = tempFile;
                                TempFileManager.RegisterTempFile(tempFile);
                                DisplayImage(_currentImagePath);
                                StatusText.Text = "Canvas Expanded successfully!";
                            });
                        }
                    }
                    else
                    {
                        Dispatcher.Invoke(() => DarkMessageBox.Show($"Expand API Error: {response.StatusCode}\nEnsure Stable Diffusion is running with --api.", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                        Dispatcher.Invoke(() => StatusText.Text = "Expand API Error");
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => StatusText.Text = $"Expand API connection failed: {ex.Message}");
                }
            });

            ExpandButton.IsEnabled = true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_processedImagePath == null) return;
            
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg";
            saveFileDialog.DefaultExt = ".png";
            saveFileDialog.AddExtension = true;
            
            if (saveFileDialog.ShowDialog() == true)
            {
                string ext = System.IO.Path.GetExtension(saveFileDialog.FileName).ToLower();
                
                if (ext == ".jpg" || ext == ".jpeg")
                {
                    // Convert to JPEG
                    BitmapImage bitmap = new BitmapImage(new Uri(_processedImagePath));
                    JpegBitmapEncoder encoder = new JpegBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (FileStream stream = new FileStream(saveFileDialog.FileName, FileMode.Create))
                    {
                        encoder.Save(stream);
                    }
                }
                else
                {
                    // Default to PNG (Just copy the temp file which is already PNG)
                    File.Copy(_processedImagePath, saveFileDialog.FileName, true);
                }
                
                StatusText.Text = "Image saved successfully.";
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (_processedImagePath != null && File.Exists(_processedImagePath))
            {
                try
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(_processedImagePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    
                    Clipboard.SetImage(bitmap);
                    StatusText.Text = "Image copied to clipboard.";
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Failed to copy: {ex.Message}";
                }
            }
        }

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Trigger Copy
                if (CopyButton.IsEnabled)
                {
                    Copy_Click(this, new RoutedEventArgs());
                }
                e.Handled = true;
            }
        }



        private async void Upscale_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImagePath == null) return;
            string targetPath = _processedImagePath ?? _currentImagePath;
            
            StatusText.Text = "Upscaling image (AI ESRGAN 2x)...";
            UpscaleButton.IsEnabled = false;
            
            await Task.Run(async () =>
            {
                try
                {
                    string base64Image = Convert.ToBase64String(File.ReadAllBytes(targetPath));

                    var payload = new
                    {
                        resize_mode = 0,
                        show_extras_results = true,
                        gfpgan_visibility = 0,
                        codeformer_visibility = 0,
                        codeformer_weight = 0,
                        upscaling_resize = 2,
                        upscaling_resize_w = 512,
                        upscaling_resize_h = 512,
                        upscaling_crop = true,
                        upscaler_1 = "ESRGAN_4x",
                        upscaler_2 = "None",
                        extras_upscaler_2_visibility = 0,
                        upscale_first = false,
                        image = base64Image
                    };

                    var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                    string jsonPayload = JsonSerializer.Serialize(payload, jsonOptions);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    string apiUrl = AppSettings.ApiEndpoint;
                    
                    // F-04: validate URL before making any network request
                    if (!AppSettings.IsApiEndpointSafe(apiUrl, out string upscaleUrlError))
                    {
                        Dispatcher.Invoke(() => {
                            DarkMessageBox.Show($"Upscale API endpoint is invalid:\n{upscaleUrlError}\n\nPlease update it in Settings.", "Invalid API Endpoint", MessageBoxButton.OK, MessageBoxImage.Warning);
                            StatusText.Text = "Upscale Failed: Invalid API endpoint.";
                        });
                        return; // Fallback happens outside
                    }
                    
                    // Avoid URL double-append
                    string upscaleApiUrl;
                    if (apiUrl.Contains("sdapi/v1/extra-single-image", System.StringComparison.OrdinalIgnoreCase))
                        upscaleApiUrl = apiUrl;
                    else
                    {
                        if (!apiUrl.EndsWith("/")) apiUrl += "/";
                        upscaleApiUrl = apiUrl + "sdapi/v1/extra-single-image";
                    }

                    // Note: Don't throw error on AI failure, just silently fall back
                    try 
                    {
                        HttpResponseMessage response = await _httpClient.PostAsync(upscaleApiUrl, content);
                        if (response.IsSuccessStatusCode)
                        {
                            string responseBody = await response.Content.ReadAsStringAsync();
                            using JsonDocument doc = JsonDocument.Parse(responseBody);
                            if (doc.RootElement.TryGetProperty("image", out JsonElement imageElement))
                            {
                                string returningBase64 = imageElement.GetString()!;
                                byte[] returningBytes = Convert.FromBase64String(returningBase64);
                                
                                string tempOutput = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upscaled_{Guid.NewGuid()}.png");
                                System.IO.File.WriteAllBytes(tempOutput, returningBytes);
                                
                                Dispatcher.Invoke(() =>
                                {
                                    _processedImagePath = tempOutput;
                                    _currentImagePath = tempOutput;
                                    TempFileManager.RegisterTempFile(tempOutput);
                                    DisplayImage(tempOutput);
                                    
                                    StatusText.Text = "Image upscaled successfully! (AI ESRGAN 2x)";
                                    UpscaleButton.IsEnabled = true;
                                    SaveButton.IsEnabled = true;
                                    CopyButton.IsEnabled = true;
                                    if (CompareToggleButton != null) CompareToggleButton.IsEnabled = true;
                                });
                                return; // Success! Skip fallback
                            }
                        }
                    } 
                    catch { /* Ignore API errors to trigger fallback */ }
                    
                    // Fallback to OpenCV if API fails or SD isn't running
                    Dispatcher.Invoke(() => StatusText.Text = "API failed, falling back to Cubic Filter...");
                    using var img = OpenCvSharp.Cv2.ImRead(targetPath, OpenCvSharp.ImreadModes.Unchanged);
                    using var upscaled = new OpenCvSharp.Mat();
                    OpenCvSharp.Cv2.Resize(img, upscaled, new OpenCvSharp.Size(img.Cols * 2, img.Rows * 2), 0, 0, OpenCvSharp.InterpolationFlags.Cubic);
                    
                    string tempFallback = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"upscaled_{Guid.NewGuid()}.png");
                    OpenCvSharp.Cv2.ImWrite(tempFallback, upscaled);
                    
                    Dispatcher.Invoke(() =>
                    {
                        _processedImagePath = tempFallback;
                        _currentImagePath = tempFallback;
                        TempFileManager.RegisterTempFile(tempFallback);
                        DisplayImage(tempFallback);
                        
                        StatusText.Text = "Image upscaled successfully! (Cubic fall-back)";
                        UpscaleButton.IsEnabled = true;
                        SaveButton.IsEnabled = true;
                        CopyButton.IsEnabled = true;
                        if (CompareToggleButton != null) CompareToggleButton.IsEnabled = true;
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = $"Upscale failed: {ex.Message}";
                        UpscaleButton.IsEnabled = true;
                    });
                }
            });
        }

        private void Compare_Checked(object sender, RoutedEventArgs e)
        {
            if (_originalImagePath != null && _processedImagePath != null)
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(_originalImagePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                OriginalImageDisplay.Source = bitmap;
                
                OriginalImageDisplay.Visibility = Visibility.Visible;
                CompareSlider.Visibility = Visibility.Visible;
                UpdateCompareClip();
            }
        }

        private void Compare_Unchecked(object sender, RoutedEventArgs e)
        {
            OriginalImageDisplay.Visibility = Visibility.Collapsed;
            CompareSlider.Visibility = Visibility.Collapsed;
            if (ImageDisplay != null)
                ImageClipGeometry.Rect = new Rect(0, 0, ImageDisplay.ActualWidth, ImageDisplay.ActualHeight);
        }

        private void CompareSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateCompareClip();
        }

        private void UpdateCompareClip()
        {
            if (ImageDisplay != null && ImageDisplay.ActualWidth > 0 && OriginalImageDisplay.Visibility == Visibility.Visible)
            {
                double splitX = ImageDisplay.ActualWidth * CompareSlider.Value;
                // Original on left, Processed on right. So processed image is clipped from splitX to end.
                ImageClipGeometry.Rect = new Rect(splitX, 0, ImageDisplay.ActualWidth - splitX, ImageDisplay.ActualHeight);
            }
        }

        private void ImageContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (CompareToggleButton?.IsChecked == true)
            {
                UpdateCompareClip();
            }
            else
            {
                if (ImageDisplay != null)
                {
                    ImageClipGeometry.Rect = new Rect(0, 0, ImageDisplay.ActualWidth, ImageDisplay.ActualHeight);
                }
            }
        }

        private void BrushSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ManualInkCanvas != null && ManualInkCanvas.DefaultDrawingAttributes != null)
            {
                ManualInkCanvas.DefaultDrawingAttributes.Width = e.NewValue;
                ManualInkCanvas.DefaultDrawingAttributes.Height = e.NewValue;
                
                if (ToolComboBox != null && ToolComboBox.SelectedIndex == 2)
                {
                    ManualInkCanvas.EraserShape = new EllipseStylusShape(e.NewValue, e.NewValue);
                }
            }
        }

        // ── TOOLBAR ICON CLICK HANDLERS ─────────────────────────────────
        // Each handler switches the ToolComboBox to the corresponding index:
        //   0 = AUTO, 1 = CYBER BRUSH, 2 = ERASER, 3 = POLY LASSO, 4 = MAGIC WAND

        private void ToolIcon_Auto_Click(object sender, MouseButtonEventArgs e)
        {
            ToolComboBox.SelectedIndex = 0;
            e.Handled = true;
        }

        private void ToolIcon_CyberBrush_Click(object sender, MouseButtonEventArgs e)
        {
            ToolComboBox.SelectedIndex = 1;
            e.Handled = true;
        }

        private void ToolIcon_Eraser_Click(object sender, MouseButtonEventArgs e)
        {
            ToolComboBox.SelectedIndex = 2;
            e.Handled = true;
        }

        private void ToolIcon_PolyLasso_Click(object sender, MouseButtonEventArgs e)
        {
            ToolComboBox.SelectedIndex = 3;
            e.Handled = true;
        }

        private void ToolIcon_MagicWand_Click(object sender, MouseButtonEventArgs e)
        {
            ToolComboBox.SelectedIndex = 4;
            e.Handled = true;
        }

        // ── TOOL SELECTION VISUAL HIGHLIGHT ─────────────────────────────
        private void UpdateToolBorderHighlights(int selectedIndex)
        {
            var borders = new[] { AutoToolBorder, BrushToolBorder, EraserToolBorder, LassoToolBorder, WandToolBorder };

            var defaultBg = new SolidColorBrush(Color.FromRgb(0x07, 0x10, 0x1A));
            var defaultBorder = (Brush)FindResource("BorderBrightBrush");
            var activeBg = new SolidColorBrush(Color.FromArgb(0x25, 0x00, 0xE5, 0xFF));
            var activeBorder = (Brush)FindResource("CyberAccentBrush");

            for (int i = 0; i < borders.Length; i++)
            {
                if (borders[i] == null) continue;

                if (i == selectedIndex)
                {
                    borders[i].Background = activeBg;
                    borders[i].BorderBrush = activeBorder;
                    borders[i].Effect = new DropShadowEffect
                    {
                        Color = Color.FromRgb(0x00, 0xE5, 0xFF),
                        BlurRadius = 16,
                        ShadowDepth = 0,
                        Opacity = 0.6
                    };
                }
                else
                {
                    borders[i].Background = defaultBg;
                    borders[i].BorderBrush = defaultBorder;
                    borders[i].Effect = null;
                }
            }
        }
    }
}
