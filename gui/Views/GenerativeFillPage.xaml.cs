using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace SoftcurseMediaLabAI.Views
{
    public partial class GenerativeFillPage : UserControl
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private string _imagePath;
        private byte[]? _maskBytes;
        private CancellationTokenSource? _cts;

        public string? ResultImagePath { get; private set; }

        public GenerativeFillPage(string? imagePath = null, byte[]? maskBytes = null)
        {
            InitializeComponent();
            _imagePath = imagePath ?? string.Empty;
            _maskBytes = maskBytes;
        }

        /// <summary>
        /// Allows MainWindow or ImageEditorPage to pass the current image to this page.
        /// </summary>
        public void SetImage(string imagePath, byte[]? maskBytes = null)
        {
            _imagePath = imagePath;
            _maskBytes = maskBytes;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            GenerateButton.IsEnabled = true;
        }

        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_imagePath))
            {
                DarkMessageBox.Show("Please load an image into the Image Editor first, then navigate to this tab.\n\nThe AI Generation Hub uses the currently loaded image from the editor.", "Missing Image", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GenerateButton.IsEnabled = false;
            _cts = new CancellationTokenSource();

            string prompt = PromptTextBox.Text;
            string negativePrompt = NegativePromptTextBox.Text;
            string apiUrl = AppSettings.ApiEndpoint;

            // F-04: validate URL before making any network request
            if (!AppSettings.IsApiEndpointSafe(apiUrl, out string genUrlError))
            {
                DarkMessageBox.Show($"Generative Fill API endpoint is invalid:\n{genUrlError}\n\nPlease update it in Settings.", "Invalid API Endpoint", MessageBoxButton.OK, MessageBoxImage.Warning);
                GenerateButton.IsEnabled = true;
                return;
            }

            // Build the correct img2img URL — avoid double-appending
            string generativeApiUrl = BuildApiUrl(apiUrl, "sdapi/v1/img2img");

            try
            {
                string base64Image = Convert.ToBase64String(File.ReadAllBytes(_imagePath));
                string? base64Mask = _maskBytes != null ? Convert.ToBase64String(_maskBytes) : null;

                var payload = new
                {
                    prompt = prompt,
                    negative_prompt = negativePrompt,
                    init_images = new[] { base64Image },
                    mask = base64Mask,
                    inpainting_fill = 1, // original
                    inpaint_full_res = true,
                    inpaint_full_res_padding = 32,
                    inpainting_mask_invert = 0,
                    steps = 20,
                    cfg_scale = 7.0,
                    width = 512,
                    height = 512,
                    restore_faces = false,
                    denoising_strength = 0.75
                };

                var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                string jsonPayload = JsonSerializer.Serialize(payload, jsonOptions);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync(generativeApiUrl, content, _cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    using JsonDocument doc = JsonDocument.Parse(responseBody);
                    if (doc.RootElement.TryGetProperty("images", out JsonElement imagesElement) && imagesElement.GetArrayLength() > 0)
                    {
                        string returningBase64 = imagesElement[0].GetString()!;
                        byte[] returningBytes = Convert.FromBase64String(returningBase64);

                        string tempFile = Path.Combine(Path.GetTempPath(), $"genfill_{Guid.NewGuid()}.png");
                        File.WriteAllBytes(tempFile, returningBytes);
                        TempFileManager.RegisterTempFile(tempFile);

                        ResultImagePath = tempFile;
                        DarkMessageBox.Show("Generation Complete! Result saved.\nSwitch to Image Editor to see the result.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                        // Navigate back to Image Editor with the result
                        var mainWindow = Application.Current.MainWindow as MainWindow;
                        mainWindow?.OpenImageInEditor(tempFile);
                        return;
                    }
                }
                else
                {
                    DarkMessageBox.Show($"API Error: {response.StatusCode}\nEnsure Stable Diffusion is running with --api.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled by user
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show($"Could not connect to API: {ex.Message}\n\nPlease ensure you have Stable Diffusion WebUI running locally with the '--api' flag enabled.", "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                GenerateButton.IsEnabled = true;
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Builds the full API URL, avoiding double-appending the path segment.
        /// </summary>
        private static string BuildApiUrl(string baseUrl, string apiPath)
        {
            // If the base URL already contains the specific API path, use it as-is
            if (baseUrl.Contains(apiPath, StringComparison.OrdinalIgnoreCase))
                return baseUrl;

            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            return baseUrl + apiPath;
        }
    }
}
