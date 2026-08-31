using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Threading;

namespace SoftcurseMediaLabAI.Views
{
    public partial class SettingsPage : UserControl
    {
        private int _loadedExecutionProvider;
        private string _loadedModelDirectory = string.Empty;

        public SettingsPage()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            DefaultOutputTextBox.Text = AppSettings.DefaultOutputFolder;
            if (string.IsNullOrEmpty(DefaultOutputTextBox.Text))
            {
                DefaultOutputTextBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            }
            ApiEndpointTextBox.Text = AppSettings.ApiEndpoint;
            if (string.IsNullOrEmpty(ApiEndpointTextBox.Text))
            {
                ApiEndpointTextBox.Text = "http://127.0.0.1:7860/";
            }
            ExecutionProviderCombo.SelectedIndex = AppSettings.ExecutionProvider;
            LastProviderText.Text = $"Last verified provider: {AppSettings.LastKnownGoodExecutionProviderName}";
            RetryDirectMlButton.Visibility =
                AppSettings.ExecutionProvider == 1 && AppSettings.LastKnownGoodExecutionProvider == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            // Model directory — show user override or placeholder with auto-resolved path
            ModelDirectoryTextBox.Text = AppSettings.ModelDirectory;
            FfmpegPathTextBox.Text = AppSettings.FfmpegPath;
            _loadedExecutionProvider = AppSettings.ExecutionProvider;
            _loadedModelDirectory = AppSettings.ModelDirectory;
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Select Default Output Folder" };
            if (dialog.ShowDialog() == true)
            {
                DefaultOutputTextBox.Text = dialog.FolderName;
            }
        }

        private void BrowseModelDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Select Model Directory" };
            if (dialog.ShowDialog() == true)
            {
                ModelDirectoryTextBox.Text = dialog.FolderName;
            }
        }

        private void RetryDirectMl_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.LastKnownGoodExecutionProvider = -1;
            AppSettings.Save();
            LastProviderText.Text = "Last verified provider: Not measured yet";
            RetryDirectMlButton.Visibility = Visibility.Collapsed;
            DarkMessageBox.Show(
                "DirectML will be tested again the next time the LaMa model is used. " +
                "If inference fails, the app will fall back to CPU and remember that result.",
                "DirectML Retry Enabled", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select ffmpeg.exe",
                Filter = "FFmpeg executable (ffmpeg.exe)|ffmpeg.exe|Executable files (*.exe)|*.exe"
            };
            if (dialog.ShowDialog() == true) FfmpegPathTextBox.Text = dialog.FileName;
        }

        private async void TestApi_Click(object sender, RoutedEventArgs e)
        {
            TestApiButton.IsEnabled = false;
            UiStatus.Set(ApiStatusText, "Testing connection…", UiStatusKind.Working);
            try
            {
                UiStatus.Set(ApiStatusText, await GenerativeApiClient.TestConnectionAsync(
                    ApiEndpointTextBox.Text.Trim(), CancellationToken.None), UiStatusKind.Success);
            }
            catch (Exception ex)
            {
                UiStatus.Set(ApiStatusText, ex.Message, UiStatusKind.Error);
            }
            finally { TestApiButton.IsEnabled = true; }
        }

        private async void TestFfmpeg_Click(object sender, RoutedEventArgs e)
        {
            TestFfmpegButton.IsEnabled = false;
            UiStatus.Set(FfmpegStatusText, "Testing FFmpeg…", UiStatusKind.Working);
            string previous = AppSettings.FfmpegPath;
            AppSettings.FfmpegPath = FfmpegPathTextBox.Text.Trim();
            try
            {
                FfmpegProbeResult result = await FfmpegService.ProbeAsync();
                UiStatus.Set(FfmpegStatusText, result.Message,
                    result.Available ? UiStatusKind.Success : UiStatusKind.Warning);
            }
            finally
            {
                AppSettings.FfmpegPath = previous;
                TestFfmpegButton.IsEnabled = true;
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            string outputFolder = DefaultOutputTextBox.Text.Trim();
            string endpoint = ApiEndpointTextBox.Text.Trim();
            string modelDirectory = ModelDirectoryTextBox.Text.Trim();
            string ffmpegPath = FfmpegPathTextBox.Text.Trim();

            if (!string.IsNullOrEmpty(outputFolder) && !Directory.Exists(outputFolder))
            {
                DarkMessageBox.Show("The default output folder does not exist.", "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!AppSettings.IsApiEndpointSafe(endpoint, out string endpointError))
            {
                DarkMessageBox.Show($"The API endpoint is invalid:\n{endpointError}", "Invalid Endpoint", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!string.IsNullOrEmpty(modelDirectory) && !Directory.Exists(modelDirectory))
            {
                DarkMessageBox.Show("The model directory does not exist.", "Invalid Model Directory", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                FfmpegService.ResolveExecutable(ffmpegPath);
            }
            catch (Exception ex) when (ex is ArgumentException or FileNotFoundException)
            {
                DarkMessageBox.Show(ex.Message, "Invalid FFmpeg Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool restartRequired = ExecutionProviderCombo.SelectedIndex != _loadedExecutionProvider ||
                                   !string.Equals(modelDirectory, _loadedModelDirectory, StringComparison.OrdinalIgnoreCase);

            if (ExecutionProviderCombo.SelectedIndex != _loadedExecutionProvider)
                AppSettings.LastKnownGoodExecutionProvider = -1;

            AppSettings.DefaultOutputFolder = outputFolder;
            AppSettings.ApiEndpoint = endpoint;
            AppSettings.ExecutionProvider = ExecutionProviderCombo.SelectedIndex;
            AppSettings.ModelDirectory = modelDirectory;
            AppSettings.FfmpegPath = string.IsNullOrEmpty(ffmpegPath) ? "ffmpeg" : ffmpegPath;
            AppSettings.Save(); // Single write to disk

            if (Application.Current.MainWindow is MainWindow mainWindow)
                _ = mainWindow.RefreshHealthAsync();

            _loadedExecutionProvider = AppSettings.ExecutionProvider;
            _loadedModelDirectory = AppSettings.ModelDirectory;
            DarkMessageBox.Show(
                restartRequired
                    ? "Settings saved. Restart the app to apply model directory or execution provider changes."
                    : "Settings saved and active.",
                "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
