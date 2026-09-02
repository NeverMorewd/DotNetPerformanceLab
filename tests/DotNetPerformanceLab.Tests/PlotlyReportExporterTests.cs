namespace DotNetPerformanceLab.Tests;

public sealed class PlotlyReportExporterTests : IDisposable
{
    private readonly string _outputDirectory = Path.Combine(Path.GetTempPath(), $"dpl-web-report-{Guid.NewGuid():N}");

    [Fact]
    public async Task WriteAsyncCreatesOfflineReportWithPinnedAssets()
    {
        var assets = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "web"));
        var result = CreateResult();
        var metrics = new[]
        {
            new MetricSample(1, DateTimeOffset.UnixEpoch, 1, MetricScope.Process, MetricNames.ProcessCpuCore, 12.5, "%", new Dictionary<string, string>(), MetricAvailability.Available)
        };

        await PlotlyReportExporter.WriteAsync(_outputDirectory, result, metrics, TestContext.Current.CancellationToken, assets);

        var report = Path.Combine(_outputDirectory, "web-report");
        Assert.True(File.Exists(Path.Combine(report, "index.html")));
        Assert.True(File.Exists(Path.Combine(report, "assets", "plotly-basic.min.js")));
        Assert.True(File.Exists(Path.Combine(report, "assets", "licenses", "plotly-js.txt")));
        var data = await File.ReadAllTextAsync(Path.Combine(report, "assets", "data.js"), TestContext.Current.CancellationToken);
        Assert.Contains(MetricNames.ProcessCpuCore, data, StringComparison.Ordinal);
    }

    private static PerformanceRunResult CreateResult() => new(
        2,
        "test-app",
        "Test application",
        new RunSettingsSnapshot(1, 5, 1000, 0, 1, false, 30, false, 10),
        new EnvironmentSnapshot("Test OS", ".NET", "X64", "X64", 8, "test", "test", "Test OS", "X64", DateTimeOffset.UnixEpoch),
        [],
        new DiagnosticArtifact("Runtime counters", false, false, null, null),
        new DiagnosticArtifact("EventPipe trace", false, false, null, null),
        [],
        DateTimeOffset.UnixEpoch,
        []);

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, true);
        }
    }
}
