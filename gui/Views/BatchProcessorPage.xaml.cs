using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace GeminiWatermarkRemover.Views
{
    public partial class BatchProcessorPage : UserControl
    {
        private readonly WatermarkService _watermarkService;
        private CancellationTokenSource? _cancellationTokenSource;
        public ObservableCollection<BatchFileItem> BatchFiles { get; set; } = new ObservableCollection<BatchFileItem>();

        public BatchProcessorPage(WatermarkService watermarkService)
        {
            InitializeComponent();
            _watermarkService = watermarkService;
            FileListView.ItemsSource = BatchFiles;
            
            // Default output folder
            OutputFolderTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "WatermarkRemoved");
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Select Folder with Images" };
            if (dialog.ShowDialog() == true)
            {
                var files = Directory.GetFiles(dialog.FolderName, "*.*")
                                     .Where(s => s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || 
                                                 s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                                                 s.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
                foreach (var file in files)
                {
                    if (!BatchFiles.Any(x => x.FilePath == file))
                        BatchFiles.Add(new BatchFileItem { FilePath = file, FileName = Path.GetFileName(file), Status = "Pending" });
                }
            }
        }

        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog { Multiselect = true, Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string file in openFileDialog.FileNames)
                {
                    if (!BatchFiles.Any(x => x.FilePath == file))
                        BatchFiles.Add(new BatchFileItem { FilePath = file, FileName = Path.GetFileName(file), Status = "Pending" });
                }
            }
        }

        private void ClearList_Click(object sender, RoutedEventArgs e)
        {
            BatchFiles.Clear();
        }

        private void BrowseOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Select Output Folder" };
            if (dialog.ShowDialog() == true)
            {
                OutputFolderTextBox.Text = dialog.FolderName;
            }
        }

        private async void StartBatch_Click(object sender, RoutedEventArgs e)
        {
            if (BatchFiles.Count == 0) return;
            string outputDir = OutputFolderTextBox.Text;
            
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            StartBatchButton.Visibility = Visibility.Collapsed;
            CancelBatchButton.Visibility = Visibility.Visible;
            CancelBatchButton.IsEnabled = true;
            BatchProgressBar.Maximum = BatchFiles.Count;
            BatchProgressBar.Value = 0;

            int completed = 0;
            _cancellationTokenSource = new CancellationTokenSource();
            CancellationToken token = _cancellationTokenSource.Token;
            
            try
            {
                await Task.Run(() =>
                {
                    foreach (var item in BatchFiles)
                    {
                        if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();

                        if (item.Status == "Done") continue;

                        Application.Current.Dispatcher.BeginInvoke(new Action(() => item.Status = "Processing..."));
                        string outputFile = Path.Combine(outputDir, item.FileName);
                        
                        try
                        {
                            if (Path.GetExtension(outputFile).ToLower() != ".png")
                            {
                                outputFile = Path.ChangeExtension(outputFile, ".png");
                            }
                            
                            _watermarkService.RemoveWatermark(item.FilePath, outputFile, false, null);
                            
                            Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                                item.Status = "Done";
                                completed++;
                                BatchProgressBar.Value = completed;
                            }));
                        }
                        catch (Exception ex)
                        {
                            // F-14: capture error detail for diagnostics — write to batch log
                            string errorMsg = ex.Message;
                            string logPath  = Path.Combine(outputDir, "batch_errors.log");
                            try
                            {
                                File.AppendAllText(logPath,
                                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FAILED: {item.FileName}\n" +
                                    $"  Error: {errorMsg}\n\n");
                            }
                            catch { /* log write failure is non-fatal */ }

                            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                item.Status = $"Error: {errorMsg}";
                            }));
                        }
                    }
                }, token);

                if (!token.IsCancellationRequested)
                {
                    DarkMessageBox.Show("Batch processing complete!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (OperationCanceledException)
            {
                DarkMessageBox.Show("Batch processing was canceled.", "Canceled", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                StartBatchButton.Visibility = Visibility.Visible;
                CancelBatchButton.Visibility = Visibility.Collapsed;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void CancelBatch_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            CancelBatchButton.IsEnabled = false;
        }
    }

    public class BatchFileItem : INotifyPropertyChanged
    {
        private string _status = "Pending";
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Status 
        { 
            get => _status; 
            set { _status = value; OnPropertyChanged(nameof(Status)); } 
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
