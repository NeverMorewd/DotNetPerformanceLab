namespace DotNetPerformanceLab.Tests;

public sealed class HostMetricCollectorTests
{
    [Fact]
    public void CurrentPlatformCollectorCapturesWithoutThrowing()
    {
        var collector = HostMetricCollector.Create();

        _ = collector.Capture();
        var sample = collector.Capture();

        Assert.Equal(MetricScope.Host, collector.Capability.Scope);
        Assert.NotEmpty(collector.Capability.Metrics);
        Assert.True(sample.ProcessCount is null or > 0);
        Assert.True(sample.CpuUsagePercent is null or >= 0 and <= 100);
        Assert.True(sample.TotalMemoryBytes is null or > 0);
    }
}
