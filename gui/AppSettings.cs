using System;
using System.IO;
using System.Text.Json;
using System.Diagnostics;

namespace SoftcurseMediaLabAI
{
    public class AppSettingsConfig
    {
        public string ApiEndpoint        { get; set; } = "http://127.0.0.1:7860/";
        public string DefaultOutputFolder{ get; set; } = "";
        public string ModelDirectory     { get; set; } = ""; // empty = auto-resolve
        public int    ExecutionProvider  { get; set; } = 0;  // 0=CPU, 1=DirectML
        public int    LastKnownGoodExecutionProvider { get; set; } = -1; // -1=not measured
        public string FfmpegPath         { get; set; } = "ffmpeg";
    }

    public static class AppSettings
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftcurseMediaLabAI");
        private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
        private static readonly string LegacySettingsPath =
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
        /// Execution provider index: 0=CPU, 1=DirectML.
        /// </summary>
        public static int ExecutionProvider
        {
            get => _current.ExecutionProvider;
            set => _current.ExecutionProvider = value;
        }

        public static int LastKnownGoodExecutionProvider
        {
            get => _current.LastKnownGoodExecutionProvider;
            set => _current.LastKnownGoodExecutionProvider = value is 0 or 1 ? value : -1;
        }

        public static string LastKnownGoodExecutionProviderName =>
            LastKnownGoodExecutionProvider switch
            {
                0 => "CPU",
                1 => "DirectML",
                _ => "Not measured yet"
            };

        public static void RecordSuccessfulExecutionProvider(int provider)
        {
            if (provider is not (0 or 1) || LastKnownGoodExecutionProvider == provider) return;
            LastKnownGoodExecutionProvider = provider;
            Save();
        }

        public static string FfmpegPath
        {
            get => _current.FfmpegPath;
            set => _current.FfmpegPath = value;
        }

        static AppSettings() => Load();

        public static void Load()
        {
            string path = File.Exists(SettingsPath) ? SettingsPath : LegacySettingsPath;
            if (!File.Exists(path)) return;
            try
            {
                string json = File.ReadAllText(path);
                _current = JsonSerializer.Deserialize<AppSettingsConfig>(json)
                           ?? new AppSettingsConfig();
                // v3 stored DirectML as index 2. CUDA was advertised but never shipped.
                if (_current.ExecutionProvider == 2) _current.ExecutionProvider = 1;
                if (_current.ExecutionProvider is < 0 or > 1) _current.ExecutionProvider = 0;
                if (_current.LastKnownGoodExecutionProvider is < -1 or > 1)
                    _current.LastKnownGoodExecutionProvider = -1;
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
                Directory.CreateDirectory(SettingsDirectory);
                string json = JsonSerializer.Serialize(
                    _current,
                    new JsonSerializerOptions { WriteIndented = true });
                string tempPath = SettingsPath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, SettingsPath, overwrite: true);
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

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                reason = "Credentials must not be embedded in the endpoint URL.";
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
