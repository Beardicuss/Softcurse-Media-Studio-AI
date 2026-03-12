using System;
using System.Collections.Concurrent;
using System.IO;

namespace SoftcurseMediaLabAI
{
    /// <summary>
    /// F-12 fix: Replace ConcurrentBag (O(n) Contains) with ConcurrentDictionary (O(1) lookup).
    /// </summary>
    public static class TempFileManager
    {
        // byte value is unused — the key IS the value (set semantics)
        private static readonly ConcurrentDictionary<string, byte> _files =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        public static void RegisterTempFile(string filePath)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
                _files.TryAdd(filePath, 0);
        }

        public static void CleanupAll()
        {
            foreach (string file in _files.Keys)
            {
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
                catch
                {
                    // Best-effort cleanup on shutdown — don't interrupt exit
                }
            }
            _files.Clear();
        }
    }
}
