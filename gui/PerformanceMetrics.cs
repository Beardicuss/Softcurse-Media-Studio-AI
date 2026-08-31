using System;
using System.Diagnostics;

namespace SoftcurseMediaLabAI
{
    public readonly record struct StartupPerformanceSnapshot(
        TimeSpan Elapsed, long PrivateMemoryBytes, bool WithinBudget);

    public static class PerformanceMetrics
    {
        public static readonly TimeSpan StartupBudget = TimeSpan.FromSeconds(2);
        public const double VideoThroughputBudgetFps = 24;

        private static readonly object Sync = new();
        private static Stopwatch? _startupWatch;
        private static StartupPerformanceSnapshot? _startupSnapshot;

        public static StartupPerformanceSnapshot? StartupSnapshot
        {
            get { lock (Sync) return _startupSnapshot; }
        }

        public static void BeginStartup()
        {
            lock (Sync)
            {
                _startupSnapshot = null;
                _startupWatch = Stopwatch.StartNew();
            }
        }

        public static StartupPerformanceSnapshot MarkUiReady()
        {
            lock (Sync)
            {
                if (_startupSnapshot is StartupPerformanceSnapshot existing) return existing;
                _startupWatch ??= Stopwatch.StartNew();
                _startupWatch.Stop();
                var snapshot = new StartupPerformanceSnapshot(
                    _startupWatch.Elapsed,
                    Process.GetCurrentProcess().PrivateMemorySize64,
                    _startupWatch.Elapsed <= StartupBudget);
                _startupSnapshot = snapshot;
                Debug.WriteLine(
                    $"[Performance] UI ready in {snapshot.Elapsed.TotalMilliseconds:F0} ms; " +
                    $"private memory {snapshot.PrivateMemoryBytes / 1024d / 1024d:F1} MB; " +
                    $"budget {(snapshot.WithinBudget ? "met" : "exceeded")}.");
                return snapshot;
            }
        }
    }
}
