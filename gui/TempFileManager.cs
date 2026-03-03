using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

namespace GeminiWatermarkRemover
{
    public static class TempFileManager
    {
        private static readonly ConcurrentBag<string> tempFiles = new ConcurrentBag<string>();

        public static void RegisterTempFile(string filePath)
        {
            if (!string.IsNullOrWhiteSpace(filePath) && !tempFiles.Contains(filePath))
            {
                tempFiles.Add(filePath);
            }
        }

        public static void CleanupAll()
        {
            foreach (var file in tempFiles)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Optionally log silent failures, but do not interrupt the shutdown process
                }
            }
        }
    }
}
