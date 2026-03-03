using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace GeminiWatermarkRemover.Views
{
    public partial class GenerativeFillPage : UserControl
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private string _imagePath;
        private byte[]? _maskBytes;

        public string? ResultImagePath { get; private set; }

        public GenerativeFillPage(string? imagePath = null, byte[]? maskBytes = null)
        {
            InitializeComponent();
            _imagePath = imagePath ?? string.Empty;
            _maskBytes = maskBytes;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // DialogResult = false;
            // Close();
        }

        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_imagePath))
            {
                MessageBox.Show("Please load an image into the editor first before attempting Generative Fill.", "Missing Image", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GenerateButton.IsEnabled = false;
            
            string prompt = PromptTextBox.Text;
            string negativePrompt = NegativePromptTextBox.Text;
            string apiUrl = AppSettings.ApiEndpoint;
            
            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                Dispatcher.Invoke(() => {
                    MessageBox.Show("Generative Fill pipeline failed: API Endpoint is empty. Please set a valid Stable Diffusion API URL in the Settings menu.", "Connection Refused", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                GenerateButton.IsEnabled = true;
                return;
            }
            
            // Route to img2img specifically if it's just the base URL
            if (!apiUrl.EndsWith("/")) apiUrl += "/";
            string generativeApiUrl = apiUrl + "sdapi/v1/img2img";
            
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

                // Note: This will throw if the API is not running locally (intended)
                HttpResponseMessage response = await _httpClient.PostAsync(generativeApiUrl, content);
                
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
                        // DialogResult = true;
                        // Close();
                        MessageBox.Show("Generation Complete!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                }
                else
                {
                    MessageBox.Show($"API Error: {response.StatusCode}\nEnsure Stable Diffusion is running with --api.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not connect to API: {ex.Message}\n\nPlease ensure you have Stable Diffusion WebUI running locally with the '--api' flag enabled.", "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                GenerateButton.IsEnabled = true;
            }
        }
    }
}
