using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Input;

namespace SoftcurseMediaLabAI
{
    public partial class MainWindow : Window
    {
        private Views.ImageEditorPage? _imageEditorPage;
        private Views.ToolkitLabPage? _toolkitPage;
        private Views.VideoLabPage? _videoPage;
        private Views.GenerativeFillPage? _genFillPage;
        private Views.FaqPage? _faqPage;
        private Views.SettingsPage? _settingsPage;

        private WatermarkService _sharedWatermarkService;
        private RegionSelectService _sharedRegionSelectService;

        public MainWindow()
        {
            InitializeComponent();
            _sharedWatermarkService = new WatermarkService();
            _sharedRegionSelectService = new RegionSelectService();

            // AI models are loaded lazily on first use so non-AI tools start quickly.
            TempFileManager.CleanupStale(TimeSpan.FromHours(24));

            _imageEditorPage = new Views.ImageEditorPage(_sharedWatermarkService, _sharedRegionSelectService);

            // Set default frame content
            ContentFrame.Navigate(_imageEditorPage);

            // Animate sidebar icons after layout is ready
            Loaded += (_, __) => 
            {
                PerformanceMetrics.MarkUiReady();
                StartSidebarAnimations();
                StartLogoGlitch();
                _ = RefreshHealthAsync();
            };
        }

        // ── SIDEBAR ICON ANIMATIONS (matching softcurse-full-app.html) ──
        private void StartSidebarAnimations()
        {
            // Image Editor: front frame opacity pulsing (3s cycle)
            var iePulse = new DoubleAnimation(1, 0.4, TimeSpan.FromSeconds(1.5))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            IEFrame.BeginAnimation(OpacityProperty, iePulse);

            // Toolkit Lab: outer ring rotation (3s), inner ring counter-rotation (2s)
            var mpOuterSpin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(3))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            MPOuterRotate.BeginAnimation(RotateTransform.AngleProperty, mpOuterSpin);

            var mpInnerSpin = new DoubleAnimation(360, 0, TimeSpan.FromSeconds(2))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            MPInnerRotate.BeginAnimation(RotateTransform.AngleProperty, mpInnerSpin);

            // Forge Lab: play triangle breathing scale (2s cycle)
            var vlBreathX = new DoubleAnimation(1, 0.85, TimeSpan.FromSeconds(1))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            var vlBreathY = new DoubleAnimation(1, 0.85, TimeSpan.FromSeconds(1))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            VLPlayScale.BeginAnimation(ScaleTransform.ScaleXProperty, vlBreathX);
            VLPlayScale.BeginAnimation(ScaleTransform.ScaleYProperty, vlBreathY);

            // Generative Image API: core breathing scale (2s cycle)
            var aiBreathX = new DoubleAnimation(1, 0.75, TimeSpan.FromSeconds(1))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            var aiBreathY = new DoubleAnimation(1, 0.75, TimeSpan.FromSeconds(1))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            AICoreScale.BeginAnimation(ScaleTransform.ScaleXProperty, aiBreathX);
            AICoreScale.BeginAnimation(ScaleTransform.ScaleYProperty, aiBreathY);
        }

        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            _videoPage?.Dispose();
            _sharedWatermarkService?.Dispose();
            TempFileManager.CleanupAll();
        }

        public void OpenImageInEditor(string filePath)
        {
            if (_imageEditorPage == null)
                _imageEditorPage = new Views.ImageEditorPage(_sharedWatermarkService, _sharedRegionSelectService);

            ContentFrame.Navigate(_imageEditorPage);
            _imageEditorPage.LoadImageFromPath(filePath);

            // Update nav button styles
            UpdateNavSelection(NavImageEditor);
        }

        public void OpenSettings()
        {
            if (_settingsPage == null)
                _settingsPage = new Views.SettingsPage();
            ContentFrame.Navigate(_settingsPage);
            UpdateNavSelection(NavSettings);
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                string? tag = btn.Tag?.ToString();
                switch (tag)
                {
                    case "ImagePage":
                        if (_imageEditorPage == null)
                            _imageEditorPage = new Views.ImageEditorPage(_sharedWatermarkService, _sharedRegionSelectService);
                        ContentFrame.Navigate(_imageEditorPage);
                        break;
                    case "ToolkitPage":
                        if (_toolkitPage == null)
                            _toolkitPage = new Views.ToolkitLabPage();
                        ContentFrame.Navigate(_toolkitPage);
                        break;
                    case "VideoPage":
                        if (_videoPage == null)
                            _videoPage = new Views.VideoLabPage(_sharedWatermarkService);
                        _videoPage.ShowRetouchMode();
                        ContentFrame.Navigate(_videoPage);
                        break;
                    case "ConverterPage":
                        if (_videoPage == null)
                            _videoPage = new Views.VideoLabPage(_sharedWatermarkService);
                        _videoPage.ShowConverterMode();
                        ContentFrame.Navigate(_videoPage);
                        break;
                    case "GenFillPage":
                        if (_genFillPage == null)
                            _genFillPage = new Views.GenerativeFillPage();
                        // Pass image path from editor if available
                        if (_imageEditorPage?.CurrentImagePath != null)
                            _genFillPage.SetImage(_imageEditorPage.CurrentImagePath);
                        ContentFrame.Navigate(_genFillPage);
                        break;
                    case "SettingsPage":
                        if (_settingsPage == null)
                            _settingsPage = new Views.SettingsPage();
                        ContentFrame.Navigate(_settingsPage);
                        break;
                    case "FaqPage":
                        if (_faqPage == null)
                            _faqPage = new Views.FaqPage();
                        ContentFrame.Navigate(_faqPage);
                        break;
                }

                UpdateNavSelection(btn);
            }
        }

        public async Task RefreshHealthAsync()
        {
            try
            {
                AppHealthSnapshot health = await AppHealthService.CheckAsync();
                ApplyHealth(CoreHealthDot, CoreHealthText, health.Core);
                ApplyHealth(FfmpegHealthDot, FfmpegHealthText, health.Ffmpeg);
                ApplyHealth(ApiHealthDot, ApiHealthText, health.Api);
            }
            catch (Exception ex)
            {
                ApiHealthText.Text = "HEALTH ERROR";
                ApiHealthText.ToolTip = ex.Message;
                ApiHealthText.Foreground = (Brush)FindResource("WarnBrush");
            }
        }

        private void ApplyHealth(System.Windows.Shapes.Ellipse dot, TextBlock text, ComponentHealth health)
        {
            string brushKey = health.Level switch
            {
                HealthLevel.Ready => "SuccessBrush",
                HealthLevel.Optional => "GoldAccentBrush",
                _ => "CyberMagentaBrush"
            };
            Brush brush = (Brush)FindResource(brushKey);
            dot.Fill = brush;
            text.Foreground = brush;
            text.Text = health.Label;
            text.ToolTip = health.Detail;
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            Button? destination = e.Key switch
            {
                Key.D1 or Key.NumPad1 => NavImageEditor,
                Key.D2 or Key.NumPad2 => NavBatch,
                Key.D3 or Key.NumPad3 => NavVideo,
                Key.D4 or Key.NumPad4 => NavConverter,
                Key.D5 or Key.NumPad5 => NavGenFill,
                Key.D6 or Key.NumPad6 => NavSettings,
                Key.D7 or Key.NumPad7 => NavFaq,
                _ => null
            };
            if (destination is null) return;
            NavButton_Click(destination, new RoutedEventArgs());
            destination.Focus();
            e.Handled = true;
        }

        private void UpdateNavSelection(Button selectedBtn)
        {
            // Reset all nav buttons to default style
            var navButtons = new[] { NavImageEditor, NavBatch, NavVideo, NavConverter, NavGenFill, NavFaq, NavSettings };
            foreach (var navBtn in navButtons)
            {
                if (navBtn != null)
                    navBtn.Style = (Style)FindResource("NavItemStyle");
            }

            // Set selected
            selectedBtn.Style = (Style)FindResource("NavItemSelectedStyle");
        }

        private void StartLogoGlitch()
        {
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(3.5);
            timer.Tick += (s, e) =>
            {
                // Random chance to trigger glitch
                if (new Random().NextDouble() > 0.4) return;
                RunGlitchSequence();
            };
            timer.Start();
        }

        private async void RunGlitchSequence()
        {
            var rng = new Random();
            int frames = rng.Next(3, 7);

            for (int i = 0; i < frames; i++)
            {
                double shiftX = rng.NextDouble() * 8 - 4;
                double shiftY = rng.NextDouble() * 2 - 1;

                GlitchTranslate1.X = shiftX;
                GlitchTranslate1.Y = shiftY;
                GlitchRed1.X = shiftX + rng.NextDouble() * 5;
                GlitchRed1.Y = shiftY;

                LogoImage.Opacity = rng.NextDouble() * 0.5 + 0.5;
                LogoImageRed.Opacity = 0.35;

                await Task.Delay(rng.Next(30, 90));
            }

            // Snap back
            GlitchTranslate1.X = 0;
            GlitchTranslate1.Y = 0;
            GlitchRed1.X = 0;
            GlitchRed1.Y = 0;
            LogoImage.Opacity = 1.0;
            LogoImageRed.Opacity = 0;
        }
    }
}
