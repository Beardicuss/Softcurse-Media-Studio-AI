using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SoftcurseMediaLabAI
{
    public partial class MainWindow : Window
    {
        private Views.ImageEditorPage? _imageEditorPage;
        private Views.ToolkitLabPage? _toolkitPage;
        private Views.VideoLabPage? _videoPage;
        private Views.GenerativeFillPage? _genFillPage;
        private Views.SpriteGeneratorPage? _spritePage;
        private Views.SettingsPage? _settingsPage;

        private WatermarkService _sharedWatermarkService;
        private SamModelService _sharedSamService;

        public MainWindow()
        {
            InitializeComponent();
            _sharedWatermarkService = new WatermarkService();
            _sharedSamService = new SamModelService();

            // Start initializing the models in the background immediately
            System.Threading.Tasks.Task.Run(() => _sharedWatermarkService.Initialize());
            System.Threading.Tasks.Task.Run(() => _sharedSamService.InitializeAsync());

            _imageEditorPage = new Views.ImageEditorPage(_sharedWatermarkService, _sharedSamService);

            // Set default frame content
            ContentFrame.Navigate(_imageEditorPage);

            // Animate sidebar icons after layout is ready
            Loaded += (_, __) => 
            {
                StartSidebarAnimations();
                StartLogoGlitch();
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

            // AI Generation Hub: core breathing scale (2s cycle)
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
            _sharedWatermarkService?.Dispose();
            _sharedSamService?.Dispose();
            TempFileManager.CleanupAll();
        }

        public void OpenImageInEditor(string filePath)
        {
            if (_imageEditorPage == null)
                _imageEditorPage = new Views.ImageEditorPage(_sharedWatermarkService, _sharedSamService);

            ContentFrame.Navigate(_imageEditorPage);
            _imageEditorPage.LoadImageFromPath(filePath);

            // Update nav button styles
            UpdateNavSelection(NavImageEditor);
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
                            _imageEditorPage = new Views.ImageEditorPage(_sharedWatermarkService, _sharedSamService);
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
                    case "SpritePage":
                        if (_spritePage == null)
                            _spritePage = new Views.SpriteGeneratorPage(_sharedWatermarkService);
                        ContentFrame.Navigate(_spritePage);
                        break;
                    case "SettingsPage":
                        if (_settingsPage == null)
                            _settingsPage = new Views.SettingsPage();
                        ContentFrame.Navigate(_settingsPage);
                        break;
                }

                UpdateNavSelection(btn);
            }
        }

        private void UpdateNavSelection(Button selectedBtn)
        {
            // Reset all nav buttons to default style
            var navButtons = new[] { NavImageEditor, NavBatch, NavVideo, NavGenFill, NavSprite, NavSettings };
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
