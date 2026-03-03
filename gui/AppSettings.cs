using System;
using System.IO;
using System.Text.Json;

namespace GeminiWatermarkRemover
{
    public class AppSettingsConfig
    {
        public string ApiEndpoint { get; set; } = "http://127.0.0.1:7860/";
        public string DefaultOutputFolder { get; set; } = "";
    }

    public static class AppSettings
    {
        private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private static AppSettingsConfig _current = new AppSettingsConfig();

        public static string ApiEndpoint
        {
            get => _current.ApiEndpoint;
            set { _current.ApiEndpoint = value; Save(); }
        }

        public static string DefaultOutputFolder
        {
            get => _current.DefaultOutputFolder;
            set { _current.DefaultOutputFolder = value; Save(); }
        }

        static AppSettings()
        {
            Load();
        }

        public static void Load()
        {
            if (File.Exists(SettingsPath))
            {
                try
                {
                    string json = File.ReadAllText(SettingsPath);
                    _current = JsonSerializer.Deserialize<AppSettingsConfig>(json) ?? new AppSettingsConfig();
                }
                catch { }
            }
        }

        public static void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}
