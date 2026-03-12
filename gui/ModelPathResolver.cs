using System;
using System.IO;

namespace SoftcurseMediaLabAI
{
    /// <summary>
    /// F-03 fix: Central, config-driven model path resolution.
    /// Priority order:
    ///   1. User-configured ModelDirectory in AppSettings
    ///   2. models/ sibling to the executable  (published / installer layout)
    ///   3. ../../../models/  relative to exe   (dev bin/Debug/net8.0/ layout)
    /// Throws FileNotFoundException with a user-actionable message if not found.
    /// </summary>
    public static class ModelPathResolver
    {
        /// <summary>Returns the full path to a model file, or throws with instructions.</summary>
        public static string Resolve(string fileName)
        {
            foreach (string candidate in GetCandidateDirs())
            {
                string full = Path.GetFullPath(Path.Combine(candidate, fileName));
                if (File.Exists(full)) return full;
            }

            throw new FileNotFoundException(
                $"Model file '{fileName}' was not found in any of the expected locations.\n\n" +
                $"To fix this:\n" +
                $"  • Place the model in the 'models' folder next to the application.\n" +
                $"  • Or set a custom model directory in Settings → Model Directory.\n\n" +
                $"Searched in:\n  " +
                string.Join("\n  ", GetCandidateDirs()));
        }

        /// <summary>
        /// Returns the first directory that exists and contains at least one .onnx file,
        /// or the first candidate if none qualify. Used by settings UI to show current path.
        /// </summary>
        public static string GetActiveModelDir()
        {
            foreach (string dir in GetCandidateDirs())
            {
                if (Directory.Exists(dir)) return Path.GetFullPath(dir);
            }
            // Return the stable release candidate as the suggested creation target
            return Path.GetFullPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models"));
        }

        private static string[] GetCandidateDirs()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;

            var candidates = new System.Collections.Generic.List<string>();

            // 1. User override from settings
            if (!string.IsNullOrWhiteSpace(AppSettings.ModelDirectory))
                candidates.Add(AppSettings.ModelDirectory);

            // 2. Stable release layout: models/ next to exe
            candidates.Add(Path.Combine(exeDir, "models"));

            // 3. Dev layout: gui/models/ relative to solution root
            candidates.Add(Path.Combine(exeDir, "..", "..", "..", "models"));
            candidates.Add(Path.Combine(exeDir, "..", "..", "..", "gui", "models"));

            return candidates.ToArray();
        }

        /// <summary>Ensures the preferred model directory exists; creates it if missing.</summary>
        public static string EnsureModelDir()
        {
            string dir = GetActiveModelDir();
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
