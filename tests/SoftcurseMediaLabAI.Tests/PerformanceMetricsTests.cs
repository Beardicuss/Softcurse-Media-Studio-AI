using SoftcurseMediaLabAI;
using Xunit;

namespace SoftcurseMediaLabAI.Tests;

public sealed class PerformanceMetricsTests
{
    [Fact]
    public void StartupAndVideoBudgetsAreExplicitAndPositive()
    {
        Assert.True(PerformanceMetrics.StartupBudget > TimeSpan.Zero);
        Assert.True(PerformanceMetrics.VideoThroughputBudgetFps > 0);
    }

    [Fact]
    public void StartupMeasurementIsCapturedOnlyOnce()
    {
        PerformanceMetrics.BeginStartup();

        StartupPerformanceSnapshot first = PerformanceMetrics.MarkUiReady();
        StartupPerformanceSnapshot second = PerformanceMetrics.MarkUiReady();

        Assert.Equal(first, second);
        Assert.True(first.Elapsed >= TimeSpan.Zero);
        Assert.True(first.PrivateMemoryBytes > 0);
    }
}
