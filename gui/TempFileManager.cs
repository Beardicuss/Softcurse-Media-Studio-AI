using System;
using System.Collections.Concurrent;
using System.IO;

namespace SoftcurseMediaLabAI
{
    public static class TempFileManager
    {
        private static readonly ConcurrentDictionary<string, byte> Files =
            new(StringComparer.OrdinalIgnoreCase);

        public static string TempDirectory { get; } =
            Path.Combine(Path.GetTempPath(), "SoftcurseMediaLabAI");

        public static string CreateTempPath(string prefix, string extension)
        {
            Directory.CreateDirectory(TempDirectory);
            string safePrefix = string.IsNullOrWhiteSpace(prefix) ? "temp" : Sanitize(prefix);
            string safeExtension = extension.StartsWith('.') ? extension : "." + extension;
            string path = Path.Combine(TempDirectory, $"{safePrefix}_{Guid.NewGuid():N}{safeExtension}");
            RegisterTempFile(path);
            return path;
        }

        public static void RegisterTempFile(string filePath)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
                Files.TryAdd(Path.GetFullPath(filePath), 0);
        }

        public static int CleanupStale(TimeSpan maxAge)
        {
            if (maxAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxAge));
            if (!Directory.Exists(TempDirectory)) return 0;
            int deleted = 0;
            DateTime cutoff = DateTime.UtcNow - maxAge;
            foreach (string file in Directory.EnumerateFiles(TempDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                        Files.TryRemove(file, out _);
                        deleted++;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return deleted;
        }

        public static void CleanupAll()
        {
            foreach (string file in Files.Keys)
            {
                try { if (File.Exists(file)) File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            Files.Clear();
            try
            {
                if (Directory.Exists(TempDirectory) &&
                    !Directory.EnumerateFileSystemEntries(TempDirectory).GetEnumerator().MoveNext())
                    Directory.Delete(TempDirectory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string Sanitize(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value;
        }
    }
}
