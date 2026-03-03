using System;
using System.IO;
using System.Text.Json;
using System.Diagnostics;

namespace GeminiWatermarkRemover
{
    public class AppSettingsConfig
    {
        public string ApiEndpoint        { get; set; } = "http://127.0.0.1:7860/";
        public string DefaultOutputFolder{ get; set; } = "";
        public string ModelDirectory     { get; set; } = ""; // empty = auto-resolve
        public int    ExecutionProvider  { get; set; } = 0;  // 0=CPU, 1=CUDA, 2=DirectML
    }

    public static class AppSettings
    {
        private static readonly string SettingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        private static AppSettingsConfig _current = new AppSettingsConfig();

        // Raised when settings fail to load or save — UI can subscribe and show a warning
        public static event Action<string>? LoadError;
        public static event Action<string>? SaveError;

        public static string ApiEndpoint
        {
            get => _current.ApiEndpoint;
            set => _current.ApiEndpoint = value;
        }

        public static string DefaultOutputFolder
        {
            get => _current.DefaultOutputFolder;
            set => _current.DefaultOutputFolder = value;
        }

        /// <summary>
        /// User-configured model directory. Empty string means use auto-resolve via ModelPathResolver.
        /// </summary>
        public static string ModelDirectory
        {
            get => _current.ModelDirectory;
            set => _current.ModelDirectory = value;
        }

        /// <summary>
        /// Execution provider index: 0=CPU, 1=CUDA, 2=DirectML.
        /// </summary>
        public static int ExecutionProvider
        {
            get => _current.ExecutionProvider;
            set => _current.ExecutionProvider = value;
        }

        static AppSettings() => Load();

        public static void Load()
        {
            if (!File.Exists(SettingsPath)) return;
            try
            {
                string json = File.ReadAllText(SettingsPath);
                _current = JsonSerializer.Deserialize<AppSettingsConfig>(json)
                           ?? new AppSettingsConfig();
            }
            catch (Exception ex)
            {
                // Log and raise — do NOT silently swallow
                string msg = $"[AppSettings] Failed to load settings.json: {ex.Message}";
                Debug.WriteLine(msg);
                LoadError?.Invoke(msg);
                // Keep defaults
                _current = new AppSettingsConfig();
            }
        }

        public static void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(
                    _current,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                string msg = $"[AppSettings] Failed to save settings.json: {ex.Message}";
                Debug.WriteLine(msg);
                SaveError?.Invoke(msg);
            }
        }

        // ── F-04: API endpoint validation ────────────────────────────────
        /// <summary>
        /// Returns true if the endpoint is a safe, well-formed HTTP/HTTPS URL.
        /// Rejects non-http schemes and bare IP ranges used by cloud metadata services.
        /// </summary>
        public static bool IsApiEndpointSafe(string endpoint, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                reason = "Endpoint is empty.";
                return false;
            }

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri))
            {
                reason = "Not a valid URI.";
                return false;
            }

            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                reason = $"Scheme '{uri.Scheme}' is not allowed. Use http or https.";
                return false;
            }

            // Block well-known cloud metadata / link-local addresses
            string host = uri.Host.ToLowerInvariant();
            string[] blocked = { "169.254.", "metadata.google", "169.254.169.254",
                                  "fd00:", "100.100.100.200" };
            foreach (string b in blocked)
            {
                if (host.StartsWith(b))
                {
                    reason = $"Host '{host}' is a reserved/metadata address and is not permitted.";
                    return false;
                }
            }

            return true;
        }
    }
}
