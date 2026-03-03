using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace GeminiWatermarkRemover.Views
{
    public partial class SettingsPage : UserControl
    {
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
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "Select Default Output Folder" };
            if (dialog.ShowDialog() == true)
            {
                DefaultOutputTextBox.Text = dialog.FolderName;
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.DefaultOutputFolder = DefaultOutputTextBox.Text;
            AppSettings.ApiEndpoint = ApiEndpointTextBox.Text;
            AppSettings.ExecutionProvider = ExecutionProviderCombo.SelectedIndex;
            AppSettings.Save(); // Single write to disk

            DarkMessageBox.Show("Settings saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
