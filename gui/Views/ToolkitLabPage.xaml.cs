using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OpenCvSharp;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace SoftcurseMediaLabAI.Views
{
    public partial class ToolkitLabPage : UserControl
    {
        // ── Resizer state ──
        private string? _resizerSourcePath;
        private int _resizerOrigW, _resizerOrigH;
        private bool _updatingDims;

        // ── Converter state ──
        private readonly List<string> _convertFiles = new();

        // ── Preset dimensions ──
        private static readonly Dictionary<string, (int w, int h)> Presets = new()
        {
            { "Instagram (1080×1080)", (1080, 1080) },
            { "Twitter (1200×675)", (1200, 675) },
            { "FB Cover (820×312)", (820, 312) },
            { "YT Thumb (1280×720)", (1280, 720) },
            { "Icon 256 (256×256)", (256, 256) },
            { "HD (1920×1080)", (1920, 1080) },
            { "4K (3840×2160)", (3840, 2160) },
        };

        public ToolkitLabPage()
        {
            InitializeComponent();
        }

        // ══════════════════════════════════════════════════════════════
        //  TOOL SELECTOR
        // ══════════════════════════════════════════════════════════════

        private void ToolButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clicked) return;

            // Uncheck all others
            var buttons = new[] { ToolResize, ToolConvert, ToolPalette, ToolMetadata, ToolCompare, ToolCrop };
            foreach (var btn in buttons)
            {
                if (btn != null && btn != clicked) btn.IsChecked = false;
            }

            // Show/hide panels
            if (ResizerPanel == null) return; // guard during init
            ResizerPanel.Visibility   = clicked == ToolResize   ? Visibility.Visible : Visibility.Collapsed;
            ConverterPanel.Visibility = clicked == ToolConvert  ? Visibility.Visible : Visibility.Collapsed;
            PalettePanel.Visibility   = clicked == ToolPalette  ? Visibility.Visible : Visibility.Collapsed;
            MetadataPanel.Visibility  = clicked == ToolMetadata ? Visibility.Visible : Visibility.Collapsed;
            ComparePanel.Visibility   = clicked == ToolCompare  ? Visibility.Visible : Visibility.Collapsed;
            CropPanel.Visibility      = clicked == ToolCrop     ? Visibility.Visible : Visibility.Collapsed;
        }

        // ══════════════════════════════════════════════════════════════
        //  IMAGE RESIZER
        // ══════════════════════════════════════════════════════════════

        private void ResizerBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tiff;*.tif;*.gif"
            };
            if (dlg.ShowDialog() == true) LoadResizerImage(dlg.FileName);
        }

        private void LoadResizerImage(string path)
        {
            _resizerSourcePath = path;
            using var mat = Cv2.ImRead(path, ImreadModes.Color);
            _resizerOrigW = mat.Width;
            _resizerOrigH = mat.Height;

            ResizerPreview.Source = LoadBitmapImage(path);
            ResizerEmpty.Visibility = Visibility.Collapsed;

            _updatingDims = true;
            ResizeWidth.Text = _resizerOrigW.ToString();
            ResizeHeight.Text = _resizerOrigH.ToString();
            ScaleSlider.Value = 100;
            ScaleLabel.Text = "100%";
            _updatingDims = false;

            ResizerInfo.Text = $"Original: {_resizerOrigW}×{_resizerOrigH}  |  {Path.GetFileName(path)}";
        }

        private void ResizeDim_Changed(object sender, TextChangedEventArgs e)
        {
            if (_updatingDims || _resizerOrigW == 0 || LockAspect == null) return;
            if (sender is not TextBox tb) return;

            if (LockAspect.IsChecked == true)
            {
                _updatingDims = true;
                double aspect = (double)_resizerOrigW / _resizerOrigH;

                if (tb == ResizeWidth && int.TryParse(ResizeWidth.Text, out int w) && w > 0)
                {
                    ResizeHeight.Text = Math.Max(1, (int)Math.Round(w / aspect)).ToString();
                }
                else if (tb == ResizeHeight && int.TryParse(ResizeHeight.Text, out int h) && h > 0)
                {
                    ResizeWidth.Text = Math.Max(1, (int)Math.Round(h * aspect)).ToString();
                }
                _updatingDims = false;
            }
        }

        private void ScaleSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingDims || _resizerOrigW == 0 || ScaleLabel == null) return;
            int pct = (int)ScaleSlider.Value;
            ScaleLabel.Text = $"{pct}%";

            _updatingDims = true;
            ResizeWidth.Text = Math.Max(1, (int)Math.Round(_resizerOrigW * pct / 100.0)).ToString();
            ResizeHeight.Text = Math.Max(1, (int)Math.Round(_resizerOrigH * pct / 100.0)).ToString();
            _updatingDims = false;
        }

        private void Preset_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (PresetCombo?.SelectedItem is not ComboBoxItem item) return;
            string name = item.Content?.ToString() ?? "";
            if (name == "Custom" || !Presets.ContainsKey(name)) return;

            var (w, h) = Presets[name];
            _updatingDims = true;
            ResizeWidth.Text = w.ToString();
            ResizeHeight.Text = h.ToString();
            _updatingDims = false;
        }

        private void ResizeSave_Click(object sender, RoutedEventArgs e)
        {
            if (_resizerSourcePath == null) return;
            if (!int.TryParse(ResizeWidth.Text, out int w) || !int.TryParse(ResizeHeight.Text, out int h) || w <= 0 || h <= 0)
            {
                DarkMessageBox.Show("Enter valid width and height.", "Invalid Size", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string ext = Path.GetExtension(_resizerSourcePath);
            var dlg = new SaveFileDialog
            {
                FileName = Path.GetFileNameWithoutExtension(_resizerSourcePath) + $"_{w}x{h}{ext}",
                Filter = "PNG|*.png|JPG|*.jpg|BMP|*.bmp|WebP|*.webp|All files|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var src = Cv2.ImRead(_resizerSourcePath, ImreadModes.Unchanged);
                using var dst = new Mat();
                Cv2.Resize(src, dst, new OpenCvSharp.Size(w, h), interpolation: w > src.Width ? InterpolationFlags.Cubic : InterpolationFlags.Area);
                Cv2.ImWrite(dlg.FileName, dst);
                DarkMessageBox.Show($"Saved: {dlg.FileName}", "Resized", MessageBoxButton.OK, MessageBoxImage.Information);

                // Update preview
                ResizerPreview.Source = LoadBitmapImage(dlg.FileName);
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show($"Resize failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  FORMAT CONVERTER
        // ══════════════════════════════════════════════════════════════

        private void ConverterBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tiff;*.tif;*.gif;*.ico"
            };
            if (dlg.ShowDialog() == true)
            {
                foreach (var f in dlg.FileNames)
                    if (!_convertFiles.Contains(f)) _convertFiles.Add(f);
                RefreshConvertList();
            }
        }

        private void ConverterDrop_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void ConverterDrop_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tiff", ".tif", ".gif", ".ico" };

            foreach (var f in files)
            {
                if (Directory.Exists(f))
                {
                    foreach (var sub in Directory.GetFiles(f, "*.*", SearchOption.AllDirectories))
                        if (exts.Contains(Path.GetExtension(sub)) && !_convertFiles.Contains(sub))
                            _convertFiles.Add(sub);
                }
                else if (exts.Contains(Path.GetExtension(f)) && !_convertFiles.Contains(f))
                {
                    _convertFiles.Add(f);
                }
            }
            RefreshConvertList();
        }

        private void RefreshConvertList()
        {
            ConvertFileList.Items.Clear();
            foreach (var f in _convertFiles)
                ConvertFileList.Items.Add(Path.GetFileName(f));

            bool hasFiles = _convertFiles.Count > 0;
            ConverterEmpty.Visibility = hasFiles ? Visibility.Collapsed : Visibility.Visible;
            ConvertFileList.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
            ConvertFileCount.Text = hasFiles ? $"{_convertFiles.Count} file(s)" : "";
        }

        private void ConverterClear_Click(object sender, RoutedEventArgs e)
        {
            _convertFiles.Clear();
            RefreshConvertList();
            ImgConvertProgress.Value = 0;
        }

        private void ImgFormat_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (QualityPanel == null) return;
            string fmt = GetSelectedFormat();
            // Show quality slider for lossy formats
            QualityPanel.Visibility = (fmt == "JPG" || fmt == "WebP") ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ImgQuality_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ImgQualityLabel != null)
                ImgQualityLabel.Text = $"{(int)ImgQualitySlider.Value}%";
        }

        private string GetSelectedFormat()
        {
            if (ImgFormatCombo?.SelectedItem is ComboBoxItem item)
                return item.Content?.ToString() ?? "PNG";
            return "PNG";
        }

        private async void ConvertAll_Click(object sender, RoutedEventArgs e)
        {
            if (_convertFiles.Count == 0) return;

            string fmt = GetSelectedFormat();
            string ext = fmt.ToLower() switch
            {
                "jpg" => ".jpg",
                "bmp" => ".bmp",
                "webp" => ".webp",
                "tiff" => ".tiff",
                "gif" => ".gif",
                "ico" => ".ico",
                _ => ".png"
            };

            var dlg = new OpenFolderDialog { Title = "Select Output Folder" };
            if (dlg.ShowDialog() != true) return;
            string outDir = dlg.FolderName;

            int quality = (int)ImgQualitySlider.Value;
            var files = _convertFiles.ToList();

            ImgConvertProgress.Maximum = files.Count;
            ImgConvertProgress.Value = 0;

            int done = 0;
            int errors = 0;

            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    try
                    {
                        string outName = Path.GetFileNameWithoutExtension(file) + ext;
                        string outPath = Path.Combine(outDir, outName);

                        if (fmt == "ICO")
                        {
                            ConvertToIco(file, outPath);
                        }
                        else
                        {
                            using var mat = Cv2.ImRead(file, ImreadModes.Unchanged);
                            var parms = new List<ImageEncodingParam>();

                            if (fmt == "JPG")
                                parms.Add(new ImageEncodingParam(ImwriteFlags.JpegQuality, quality));
                            else if (fmt == "WebP")
                                parms.Add(new ImageEncodingParam(ImwriteFlags.WebPQuality, quality));

                            Cv2.ImWrite(outPath, mat, parms.ToArray());
                        }
                        done++;
                    }
                    catch
                    {
                        errors++;
                    }

                    Dispatcher.BeginInvoke(() =>
                    {
                        ImgConvertProgress.Value = done + errors;
                        ConvertFileCount.Text = $"{done}/{files.Count} converted" + (errors > 0 ? $" ({errors} errors)" : "");
                    });
                }
            });

            DarkMessageBox.Show(
                $"Converted {done} of {files.Count} files to {fmt}." + (errors > 0 ? $"\n{errors} file(s) failed." : ""),
                "Conversion Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Convert image to ICO format with multiple sizes (16, 32, 48, 64, 128, 256).
        /// </summary>
        private void ConvertToIco(string inputPath, string outputPath)
        {
            int[] sizes = { 256, 128, 64, 48, 32, 16 };
            using var src = Cv2.ImRead(inputPath, ImreadModes.Unchanged);

            // Ensure 4-channel (BGRA) for transparency support
            Mat bgra;
            if (src.Channels() == 4)
                bgra = src;
            else if (src.Channels() == 3)
            {
                bgra = new Mat();
                Cv2.CvtColor(src, bgra, ColorConversionCodes.BGR2BGRA);
            }
            else
            {
                bgra = new Mat();
                Cv2.CvtColor(src, bgra, ColorConversionCodes.GRAY2BGRA);
            }

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // ICO header
            bw.Write((short)0);            // reserved
            bw.Write((short)1);            // type = ICO
            bw.Write((short)sizes.Length);  // image count

            // Collect PNG data for each size
            var pngDataList = new List<byte[]>();
            foreach (int sz in sizes)
            {
                using var resized = new Mat();
                Cv2.Resize(bgra, resized, new OpenCvSharp.Size(sz, sz), interpolation: InterpolationFlags.Area);
                Cv2.ImEncode(".png", resized, out byte[] pngBytes);
                pngDataList.Add(pngBytes);
            }

            // Directory entries (offset starts after header + all entries)
            int dataOffset = 6 + (sizes.Length * 16);
            for (int i = 0; i < sizes.Length; i++)
            {
                byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                bw.Write(dim);                         // width
                bw.Write(dim);                         // height
                bw.Write((byte)0);                     // color palette
                bw.Write((byte)0);                     // reserved
                bw.Write((short)1);                    // color planes
                bw.Write((short)32);                   // bits per pixel
                bw.Write(pngDataList[i].Length);        // size of image data
                bw.Write(dataOffset);                  // offset to image data
                dataOffset += pngDataList[i].Length;
            }

            // Image data
            foreach (var png in pngDataList)
                bw.Write(png);

            if (bgra != src) bgra.Dispose();

            File.WriteAllBytes(outputPath, ms.ToArray());
        }

        // ══════════════════════════════════════════════════════════════
        //  COLOR PALETTE EXTRACTOR
        // ══════════════════════════════════════════════════════════════

        private string? _paletteSourcePath;
        private List<string> _extractedHexColors = new();

        private void PaletteBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tiff;*.tif;*.gif"
            };
            if (dlg.ShowDialog() == true)
            {
                _paletteSourcePath = dlg.FileName;
                PalettePreview.Source = LoadBitmapImage(dlg.FileName);
                PaletteEmpty.Visibility = Visibility.Collapsed;
                ExtractPalette();
            }
        }

        private void PaletteCount_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_paletteSourcePath != null) ExtractPalette();
        }

        private void PaletteExtract_Click(object sender, RoutedEventArgs e)
        {
            if (_paletteSourcePath != null) ExtractPalette();
        }

        private int GetPaletteCount()
        {
            if (PaletteCountCombo?.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Content?.ToString(), out int count))
                return count;
            return 8;
        }

        private void ExtractPalette()
        {
            if (_paletteSourcePath == null) return;

            int k = GetPaletteCount();
            _extractedHexColors.Clear();
            PaletteSwatches.Children.Clear();

            try
            {
                using var src = Cv2.ImRead(_paletteSourcePath, ImreadModes.Color);
                // Resize for speed
                using var small = new Mat();
                double scale = Math.Min(1.0, 200.0 / Math.Max(src.Width, src.Height));
                Cv2.Resize(src, small, new OpenCvSharp.Size(0, 0), scale, scale);

                // Reshape to Nx3 float for K-means
                using var samples = small.Reshape(1, small.Rows * small.Cols);
                using var floatSamples = new Mat();
                samples.ConvertTo(floatSamples, MatType.CV_32F);

                using var labels = new Mat();
                using var centers = new Mat();
                Cv2.Kmeans(floatSamples, k, labels,
                    new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 10, 1.0),
                    3, KMeansFlags.PpCenters, centers);

                // Count pixels per cluster for sorting by dominance
                var clusterCounts = new int[k];
                for (int i = 0; i < labels.Rows; i++)
                    clusterCounts[labels.At<int>(i, 0)]++;

                var colorInfos = new List<(int b, int g, int r, int count)>();
                for (int i = 0; i < k; i++)
                {
                    int b = Math.Clamp((int)centers.At<float>(i, 0), 0, 255);
                    int g = Math.Clamp((int)centers.At<float>(i, 1), 0, 255);
                    int r = Math.Clamp((int)centers.At<float>(i, 2), 0, 255);
                    colorInfos.Add((b, g, r, clusterCounts[i]));
                }

                // Sort by pixel count descending
                colorInfos.Sort((a, b) => b.count.CompareTo(a.count));

                int totalPixels = labels.Rows;
                foreach (var (cb, cg, cr, count) in colorInfos)
                {
                    string hex = $"#{cr:X2}{cg:X2}{cb:X2}";
                    _extractedHexColors.Add(hex);
                    double pct = 100.0 * count / totalPixels;

                    var swatch = new Border
                    {
                        Width = 190, Height = 28, Margin = new Thickness(0, 0, 0, 4),
                        Background = new SolidColorBrush(Color.FromRgb((byte)cr, (byte)cg, (byte)cb)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0, 229, 255)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Cursor = System.Windows.Input.Cursors.Hand,
                        ToolTip = $"Click to copy {hex}"
                    };

                    var label = new TextBlock
                    {
                        Text = $"{hex}  ({pct:F1}%)",
                        FontSize = 10, FontFamily = new FontFamily("Consolas"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0),
                        Foreground = (cr * 0.299 + cg * 0.587 + cb * 0.114) > 128
                            ? Brushes.Black : Brushes.White
                    };

                    swatch.Child = label;
                    string hexCopy = hex;
                    swatch.MouseLeftButtonUp += (_, _) =>
                    {
                        Clipboard.SetText(hexCopy);
                        DarkMessageBox.Show($"Copied: {hexCopy}", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
                    };

                    PaletteSwatches.Children.Add(swatch);
                }
            }
            catch (Exception ex)
            {
                PaletteSwatches.Children.Add(new TextBlock
                {
                    Text = $"Error: {ex.Message}", FontSize = 10,
                    Foreground = Brushes.OrangeRed, FontFamily = new FontFamily("Segoe UI"),
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        private void PaletteCopyAll_Click(object sender, RoutedEventArgs e)
        {
            if (_extractedHexColors.Count == 0) return;
            Clipboard.SetText(string.Join("\n", _extractedHexColors));
            DarkMessageBox.Show($"Copied {_extractedHexColors.Count} hex codes to clipboard.",
                "Copied All", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ══════════════════════════════════════════════════════════════
        //  IMAGE METADATA VIEWER
        // ══════════════════════════════════════════════════════════════

        private string? _metaSourcePath;

        private void MetaBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tiff;*.tif;*.gif"
            };
            if (dlg.ShowDialog() == true)
            {
                _metaSourcePath = dlg.FileName;
                LoadMetadata();
            }
        }

        private void LoadMetadata()
        {
            if (_metaSourcePath == null) return;
            MetaProperties.Children.Clear();

            try
            {
                var fi = new FileInfo(_metaSourcePath);

                // Basic file info
                AddMetaRow("File Name", fi.Name);
                AddMetaRow("File Path", fi.DirectoryName ?? "");
                AddMetaRow("File Size", FormatFileSize(fi.Length));
                AddMetaRow("Format", fi.Extension.ToUpperInvariant().TrimStart('.'));
                AddMetaRow("Created", fi.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"));
                AddMetaRow("Modified", fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));

                AddMetaSeparator("IMAGE PROPERTIES");

                // Use OpenCV for dimensions + channels
                using var mat = Cv2.ImRead(_metaSourcePath, ImreadModes.Unchanged);
                AddMetaRow("Dimensions", $"{mat.Width} × {mat.Height} px");
                AddMetaRow("Channels", mat.Channels().ToString());
                AddMetaRow("Color Depth", $"{mat.Depth()} ({mat.ElemSize() * 8 / Math.Max(1, mat.Channels())} bits/channel)");
                AddMetaRow("Total Pixels", $"{(long)mat.Width * mat.Height:N0}");

                // Try System.Drawing for DPI + EXIF
                try
                {
                    using var img = System.Drawing.Image.FromFile(_metaSourcePath);
                    AddMetaRow("DPI", $"{img.HorizontalResolution:F0} × {img.VerticalResolution:F0}");
                    MetaDpiInput.Text = ((int)img.HorizontalResolution).ToString();

                    // EXIF properties
                    var exifTags = img.PropertyItems;
                    if (exifTags.Length > 0)
                    {
                        AddMetaSeparator("EXIF DATA");
                        foreach (var prop in exifTags)
                        {
                            string tagName = GetExifTagName(prop.Id);
                            string value = GetExifValue(prop);
                            if (!string.IsNullOrWhiteSpace(value) && value.Length < 200)
                                AddMetaRow(tagName, value);
                        }
                    }
                }
                catch
                {
                    AddMetaRow("DPI", "N/A (format not supported by GDI+)");
                }

                MetaEmpty.Visibility = Visibility.Collapsed;
                MetaProperties.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MetaProperties.Children.Clear();
                MetaProperties.Children.Add(new TextBlock
                {
                    Text = $"Error reading metadata: {ex.Message}",
                    Foreground = Brushes.OrangeRed, FontSize = 11, TextWrapping = TextWrapping.Wrap
                });
                MetaProperties.Visibility = Visibility.Visible;
            }
        }

        private void AddMetaRow(string label, string value)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("CyberAccentBrush"),
                FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(lbl, 0);

            var val = new TextBlock
            {
                Text = value, FontSize = 10, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextBrush"),
                FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(val, 1);

            row.Children.Add(lbl);
            row.Children.Add(val);
            MetaProperties.Children.Add(row);
        }

        private void AddMetaSeparator(string title)
        {
            MetaProperties.Children.Add(new TextBlock
            {
                Text = title, FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("CyberAccentBrush"),
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(0, 10, 0, 4)
            });
            MetaProperties.Children.Add(new WpfRectangle
            {
                Height = 1, HorizontalAlignment = HorizontalAlignment.Stretch,
                Fill = (Brush)FindResource("BorderBrush"),
                Margin = new Thickness(0, 0, 0, 6)
            });
        }

        private void MetaApplyDpi_Click(object sender, RoutedEventArgs e)
        {
            if (_metaSourcePath == null) return;
            if (!int.TryParse(MetaDpiInput.Text, out int dpi) || dpi <= 0 || dpi > 10000)
            {
                DarkMessageBox.Show("Enter a valid DPI value (1-10000).", "Invalid DPI", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var img = System.Drawing.Image.FromFile(_metaSourcePath);
                using var bmp = new System.Drawing.Bitmap(img);
                bmp.SetResolution(dpi, dpi);

                // Save to temp then replace
                string temp = _metaSourcePath + ".tmp";
                var imgFmt = GetDrawingFormat(_metaSourcePath);
                bmp.Save(temp, imgFmt);
                img.Dispose();
                File.Copy(temp, _metaSourcePath, true);
                File.Delete(temp);

                MetaStatusText.Text = $"✓ DPI set to {dpi}";
                LoadMetadata();
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show($"Failed to set DPI: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MetaStripExif_Click(object sender, RoutedEventArgs e)
        {
            if (_metaSourcePath == null) return;

            try
            {
                using var img = System.Drawing.Image.FromFile(_metaSourcePath);
                // Remove all property items
                foreach (var prop in img.PropertyItems)
                {
                    try { img.RemovePropertyItem(prop.Id); } catch { }
                }

                string temp = _metaSourcePath + ".tmp";
                var imgFmt = GetDrawingFormat(_metaSourcePath);
                img.Save(temp, imgFmt);
                img.Dispose();
                File.Copy(temp, _metaSourcePath, true);
                File.Delete(temp);

                MetaStatusText.Text = "✓ EXIF data stripped";
                LoadMetadata();
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show($"Failed to strip EXIF: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static System.Drawing.Imaging.ImageFormat GetDrawingFormat(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return ext switch
            {
                ".jpg" or ".jpeg" => System.Drawing.Imaging.ImageFormat.Jpeg,
                ".bmp" => System.Drawing.Imaging.ImageFormat.Bmp,
                ".gif" => System.Drawing.Imaging.ImageFormat.Gif,
                ".tiff" or ".tif" => System.Drawing.Imaging.ImageFormat.Tiff,
                _ => System.Drawing.Imaging.ImageFormat.Png
            };
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }

        private static string GetExifTagName(int id)
        {
            return id switch
            {
                0x010F => "Camera Make",
                0x0110 => "Camera Model",
                0x0112 => "Orientation",
                0x011A => "X Resolution",
                0x011B => "Y Resolution",
                0x0128 => "Resolution Unit",
                0x0131 => "Software",
                0x0132 => "Date/Time",
                0x013E => "White Point",
                0x8769 => "EXIF IFD",
                0x8825 => "GPS IFD",
                0x829A => "Exposure Time",
                0x829D => "F-Number",
                0x8827 => "ISO Speed",
                0x9003 => "Date Original",
                0x9004 => "Date Digitized",
                0x920A => "Focal Length",
                0xA001 => "Color Space",
                0xA002 => "Pixel X Dimension",
                0xA003 => "Pixel Y Dimension",
                0xA405 => "Focal Length 35mm",
                _ => $"Tag 0x{id:X4}"
            };
        }

        private static string GetExifValue(System.Drawing.Imaging.PropertyItem prop)
        {
            try
            {
                if (prop.Value == null || prop.Value.Length == 0) return "";
                return prop.Type switch
                {
                    2 => System.Text.Encoding.ASCII.GetString(prop.Value).TrimEnd('\0'),
                    3 => BitConverter.ToUInt16(prop.Value, 0).ToString(),
                    4 => BitConverter.ToUInt32(prop.Value, 0).ToString(),
                    5 when prop.Value.Length >= 8 =>
                        $"{BitConverter.ToUInt32(prop.Value, 0)}/{BitConverter.ToUInt32(prop.Value, 4)}",
                    _ => BitConverter.ToString(prop.Value).Replace("-", " ")
                };
            }
            catch { return ""; }
        }

        // ══════════════════════════════════════════════════════════════
        //  IMAGE COMPARE
        // ══════════════════════════════════════════════════════════════

        private string? _compareLeftPath, _compareRightPath;
        private bool _compareDragging;

        private void CompareLoadLeft_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tiff;*.tif;*.gif" };
            if (dlg.ShowDialog() == true)
            {
                _compareLeftPath = dlg.FileName;
                CompareLeft.Source = LoadBitmapImage(dlg.FileName);
                CompareLeft.Visibility = Visibility.Visible;
                TryShowCompare();
            }
        }

        private void CompareLoadRight_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tiff;*.tif;*.gif" };
            if (dlg.ShowDialog() == true)
            {
                _compareRightPath = dlg.FileName;
                CompareRight.Source = LoadBitmapImage(dlg.FileName);
                CompareRight.Visibility = Visibility.Visible;
                TryShowCompare();
            }
        }

        private void TryShowCompare()
        {
            if (_compareLeftPath != null && _compareRightPath != null)
            {
                CompareEmpty.Visibility = Visibility.Collapsed;
                CompareDividerCanvas.Visibility = Visibility.Visible;
                CompareSlider.Value = 50;
                CompareInfo.Text = "Left ◂ 50% ▸ Right";

                // Position divider after layout
                CompareContainer.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                {
                    UpdateCompareDivider(CompareContainer.ActualWidth * 0.5);
                });
            }
        }

        private void CompareSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (CompareContainer == null || _compareLeftPath == null || _compareRightPath == null) return;
            double pct = CompareSlider.Value / 100.0;
            double x = CompareContainer.ActualWidth * pct;
            UpdateCompareDivider(x);
            CompareInfo.Text = $"Left ◂ {(int)CompareSlider.Value}% ▸ Right";
        }

        private void UpdateCompareDivider(double x)
        {
            double w = CompareContainer.ActualWidth;
            double h = CompareContainer.ActualHeight;
            if (w <= 0 || h <= 0) return;

            x = Math.Clamp(x, 0, w);
            CompareClip.Rect = new System.Windows.Rect(0, 0, x, h);

            Canvas.SetLeft(CompareDividerLine, x);
            CompareDividerLine.Y2 = h;

            Canvas.SetLeft(CompareDividerHandle, x - 15);
            Canvas.SetTop(CompareDividerHandle, h / 2 - 15);
        }

        private void CompareDivider_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _compareDragging = true;
            ((UIElement)sender).CaptureMouse();
        }

        private void CompareDivider_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_compareDragging) return;
            double x = e.GetPosition(CompareContainer).X;
            double w = CompareContainer.ActualWidth;
            if (w <= 0) return;

            x = Math.Clamp(x, 0, w);
            UpdateCompareDivider(x);
            CompareSlider.Value = x / w * 100;
            CompareInfo.Text = $"Left ◂ {(int)CompareSlider.Value}% ▸ Right";
        }

        private void CompareDivider_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _compareDragging = false;
            ((UIElement)sender).ReleaseMouseCapture();
        }

        private void CompareSwap_Click(object sender, RoutedEventArgs e)
        {
            if (_compareLeftPath == null || _compareRightPath == null) return;
            (_compareLeftPath, _compareRightPath) = (_compareRightPath, _compareLeftPath);
            var tempSrc = CompareLeft.Source;
            CompareLeft.Source = CompareRight.Source;
            CompareRight.Source = tempSrc;
            UpdateCompareDivider(CompareContainer.ActualWidth * CompareSlider.Value / 100);
        }

        // ══════════════════════════════════════════════════════════════
        //  CROP TOOL
        // ══════════════════════════════════════════════════════════════

        private string? _cropSourcePath;
        private int _cropOrigW, _cropOrigH;
        private double _cropX, _cropY, _cropW, _cropH; // in canvas coords
        private string _cropDragMode = "none"; // none, move, tl, tr, bl, br
        private System.Windows.Point _cropDragStart;
        private double _cropStartX, _cropStartY, _cropStartW, _cropStartH;

        private void CropBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tiff;*.tif;*.gif" };
            if (dlg.ShowDialog() == true)
            {
                _cropSourcePath = dlg.FileName;
                using var mat = Cv2.ImRead(dlg.FileName, ImreadModes.Color);
                _cropOrigW = mat.Width;
                _cropOrigH = mat.Height;

                CropPreview.Source = LoadBitmapImage(dlg.FileName);
                CropPreview.Visibility = Visibility.Visible;
                CropCanvas.Visibility = Visibility.Visible;
                CropEmpty.Visibility = Visibility.Collapsed;

                // Init crop rect after layout
                CropCanvas.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                {
                    double cw = CropCanvas.ActualWidth;
                    double ch = CropCanvas.ActualHeight;
                    // Default crop: 80% centered
                    _cropX = cw * 0.1;
                    _cropY = ch * 0.1;
                    _cropW = cw * 0.8;
                    _cropH = ch * 0.8;
                    UpdateCropVisuals();
                });
            }
        }

        private void UpdateCropVisuals()
        {
            double cw = CropCanvas.ActualWidth;
            double ch = CropCanvas.ActualHeight;
            if (cw <= 0 || ch <= 0) return;

            // Clamp
            _cropX = Math.Clamp(_cropX, 0, cw - 10);
            _cropY = Math.Clamp(_cropY, 0, ch - 10);
            _cropW = Math.Clamp(_cropW, 10, cw - _cropX);
            _cropH = Math.Clamp(_cropH, 10, ch - _cropY);

            // Crop rect
            Canvas.SetLeft(CropRect, _cropX);
            Canvas.SetTop(CropRect, _cropY);
            CropRect.Width = _cropW;
            CropRect.Height = _cropH;

            // Overlay regions
            Canvas.SetLeft(CropOverlayTop, 0); Canvas.SetTop(CropOverlayTop, 0);
            CropOverlayTop.Width = cw; CropOverlayTop.Height = _cropY;

            Canvas.SetLeft(CropOverlayBottom, 0); Canvas.SetTop(CropOverlayBottom, _cropY + _cropH);
            CropOverlayBottom.Width = cw; CropOverlayBottom.Height = Math.Max(0, ch - _cropY - _cropH);

            Canvas.SetLeft(CropOverlayLeft, 0); Canvas.SetTop(CropOverlayLeft, _cropY);
            CropOverlayLeft.Width = _cropX; CropOverlayLeft.Height = _cropH;

            Canvas.SetLeft(CropOverlayRight, _cropX + _cropW); Canvas.SetTop(CropOverlayRight, _cropY);
            CropOverlayRight.Width = Math.Max(0, cw - _cropX - _cropW); CropOverlayRight.Height = _cropH;

            // Corner handles (centered on corners)
            Canvas.SetLeft(CropHandleTL, _cropX - 5); Canvas.SetTop(CropHandleTL, _cropY - 5);
            Canvas.SetLeft(CropHandleTR, _cropX + _cropW - 5); Canvas.SetTop(CropHandleTR, _cropY - 5);
            Canvas.SetLeft(CropHandleBL, _cropX - 5); Canvas.SetTop(CropHandleBL, _cropY + _cropH - 5);
            Canvas.SetLeft(CropHandleBR, _cropX + _cropW - 5); Canvas.SetTop(CropHandleBR, _cropY + _cropH - 5);

            // Info
            double scaleX = _cropOrigW / CropCanvas.ActualWidth;
            double scaleY = _cropOrigH / CropCanvas.ActualHeight;
            int realW = Math.Max(1, (int)(_cropW * scaleX));
            int realH = Math.Max(1, (int)(_cropH * scaleY));
            CropInfo.Text = $"Crop: {realW} × {realH} px";
        }

        private void CropCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(CropCanvas);
            _cropDragStart = pos;
            _cropStartX = _cropX; _cropStartY = _cropY;
            _cropStartW = _cropW; _cropStartH = _cropH;

            // Determine what we're dragging
            double hSize = 12;
            if (IsNear(pos, _cropX, _cropY, hSize)) _cropDragMode = "tl";
            else if (IsNear(pos, _cropX + _cropW, _cropY, hSize)) _cropDragMode = "tr";
            else if (IsNear(pos, _cropX, _cropY + _cropH, hSize)) _cropDragMode = "bl";
            else if (IsNear(pos, _cropX + _cropW, _cropY + _cropH, hSize)) _cropDragMode = "br";
            else if (pos.X >= _cropX && pos.X <= _cropX + _cropW && pos.Y >= _cropY && pos.Y <= _cropY + _cropH)
                _cropDragMode = "move";
            else
                _cropDragMode = "none";

            if (_cropDragMode != "none")
                CropCanvas.CaptureMouse();
        }

        private void CropCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_cropDragMode == "none") return;
            var pos = e.GetPosition(CropCanvas);
            double dx = pos.X - _cropDragStart.X;
            double dy = pos.Y - _cropDragStart.Y;

            double? aspectRatio = GetCropAspectRatio();

            switch (_cropDragMode)
            {
                case "move":
                    _cropX = _cropStartX + dx;
                    _cropY = _cropStartY + dy;
                    break;
                case "br":
                    _cropW = Math.Max(20, _cropStartW + dx);
                    _cropH = aspectRatio.HasValue ? _cropW / aspectRatio.Value : Math.Max(20, _cropStartH + dy);
                    break;
                case "bl":
                    double newW_bl = Math.Max(20, _cropStartW - dx);
                    _cropX = _cropStartX + _cropStartW - newW_bl;
                    _cropW = newW_bl;
                    _cropH = aspectRatio.HasValue ? _cropW / aspectRatio.Value : Math.Max(20, _cropStartH + dy);
                    break;
                case "tr":
                    _cropW = Math.Max(20, _cropStartW + dx);
                    double newH_tr = aspectRatio.HasValue ? _cropW / aspectRatio.Value : Math.Max(20, _cropStartH - dy);
                    _cropY = _cropStartY + _cropStartH - newH_tr;
                    _cropH = newH_tr;
                    break;
                case "tl":
                    double newW_tl = Math.Max(20, _cropStartW - dx);
                    double newH_tl = aspectRatio.HasValue ? newW_tl / aspectRatio.Value : Math.Max(20, _cropStartH - dy);
                    _cropX = _cropStartX + _cropStartW - newW_tl;
                    _cropY = _cropStartY + _cropStartH - newH_tl;
                    _cropW = newW_tl;
                    _cropH = newH_tl;
                    break;
            }

            UpdateCropVisuals();
        }

        private void CropCanvas_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _cropDragMode = "none";
            CropCanvas.ReleaseMouseCapture();
        }

        private void CropAspect_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_cropSourcePath == null || CropCanvas.ActualWidth <= 0) return;
            double? ar = GetCropAspectRatio();
            if (ar.HasValue)
            {
                // Adjust height to match new aspect ratio, keep width
                _cropH = _cropW / ar.Value;
                UpdateCropVisuals();
            }
        }

        private double? GetCropAspectRatio()
        {
            if (CropAspectCombo?.SelectedItem is not ComboBoxItem item) return null;
            string name = item.Content?.ToString() ?? "Free";
            return name switch
            {
                "1:1" => 1.0,
                "4:3" => 4.0 / 3.0,
                "3:4" => 3.0 / 4.0,
                "16:9" => 16.0 / 9.0,
                "9:16" => 9.0 / 16.0,
                "3:2" => 3.0 / 2.0,
                "2:3" => 2.0 / 3.0,
                _ => null
            };
        }

        private void CropSave_Click(object sender, RoutedEventArgs e)
        {
            if (_cropSourcePath == null || CropCanvas.ActualWidth <= 0) return;

            // Map canvas coords to actual image coords
            double scaleX = _cropOrigW / CropCanvas.ActualWidth;
            double scaleY = _cropOrigH / CropCanvas.ActualHeight;
            int rx = Math.Max(0, (int)(_cropX * scaleX));
            int ry = Math.Max(0, (int)(_cropY * scaleY));
            int rw = Math.Min(_cropOrigW - rx, Math.Max(1, (int)(_cropW * scaleX)));
            int rh = Math.Min(_cropOrigH - ry, Math.Max(1, (int)(_cropH * scaleY)));

            string ext = Path.GetExtension(_cropSourcePath);
            var dlg = new SaveFileDialog
            {
                FileName = Path.GetFileNameWithoutExtension(_cropSourcePath) + $"_crop{ext}",
                Filter = "PNG|*.png|JPG|*.jpg|BMP|*.bmp|All files|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var src = Cv2.ImRead(_cropSourcePath, ImreadModes.Unchanged);
                using var cropped = src.SubMat(new OpenCvSharp.Rect(rx, ry, rw, rh));
                Cv2.ImWrite(dlg.FileName, cropped);
                DarkMessageBox.Show($"Cropped to {rw}×{rh} and saved:\n{dlg.FileName}", "Cropped", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show($"Crop failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool IsNear(System.Windows.Point pos, double x, double y, double threshold)
            => Math.Abs(pos.X - x) < threshold && Math.Abs(pos.Y - y) < threshold;

        // ══════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════

        private static BitmapImage LoadBitmapImage(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }
}
