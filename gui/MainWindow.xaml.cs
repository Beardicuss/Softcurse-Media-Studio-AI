using System.Windows;
using ModernWpf.Controls;

namespace GeminiWatermarkRemover
{
    public partial class MainWindow : Window
    {
        private Views.ImageEditorPage? _imageEditorPage;
        private Views.BatchProcessorPage? _batchPage;
        private Views.VideoLabPage? _videoPage;
        private Views.GenerativeFillPage? _genFillPage;
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
            NavView.SelectedItem = NavView.MenuItems[0];
        }

        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            _sharedWatermarkService?.Dispose();
            _sharedSamService?.Dispose();
            TempFileManager.CleanupAll();
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                if (_settingsPage == null) _settingsPage = new Views.SettingsPage();
                ContentFrame.Navigate(_settingsPage);
                return;
            }

            var selectedItem = (NavigationViewItem)args.SelectedItem;
            string? tag = selectedItem.Tag?.ToString();

            switch (tag)
            {
                case "ImagePage":
                    if (_imageEditorPage == null) _imageEditorPage = new Views.ImageEditorPage(_sharedWatermarkService, _sharedSamService);
                    ContentFrame.Navigate(_imageEditorPage);
                    break;
                case "BatchPage":
                    if (_batchPage == null) _batchPage = new Views.BatchProcessorPage(_sharedWatermarkService);
                    ContentFrame.Navigate(_batchPage);
                    break;
                case "VideoPage":
                    if (_videoPage == null) _videoPage = new Views.VideoLabPage(_sharedWatermarkService);
                    ContentFrame.Navigate(_videoPage);
                    break;
                case "GenFillPage":
                    if (_genFillPage == null) _genFillPage = new Views.GenerativeFillPage();
                    ContentFrame.Navigate(_genFillPage);
                    break;
            }
        }
    }
}
