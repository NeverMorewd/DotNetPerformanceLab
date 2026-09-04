using System.Net;
using System.Text;
using System.Text.Json;

namespace DotNetPerformanceLab.Tests;

public sealed class ComparisonTests : IDisposable
{
    private readonly string _outputDirectory = Path.Combine(Path.GetTempPath(), $"dpl-comparison-{Guid.NewGuid():N}");

    [Fact]
    public void ExporterCreatesStableStatisticsAndDirections()
    {
        var data = ComparisonDataExporter.Create(CreateResult(),
        [
            Sample(MetricScope.Process, MetricNames.ProcessMemoryWorkingSet, 10, "By"),
            Sample(MetricScope.Process, MetricNames.ProcessMemoryWorkingSet, 20, "By"),
            Sample(MetricScope.Application, "requests.completed", 100, "{request}"),
            Sample(MetricScope.Host, MetricNames.HostMemoryAvailable, 1_000, "By")
        ]);

        Assert.Equal(1, data.SchemaVersion);
        var memory = Assert.Single(data.Metrics, metric => metric.Name == MetricNames.ProcessMemoryWorkingSet);
        Assert.Equal(MetricOptimizationDirection.Lower, memory.Direction);
        Assert.Equal(10, memory.Statistics.Minimum);
        Assert.Equal(15, memory.Statistics.Mean);
        Assert.Equal(20, memory.Statistics.Maximum);
        Assert.Equal(MetricOptimizationDirection.Higher, Assert.Single(data.Metrics, metric => metric.Name == "requests.completed").Direction);
        Assert.Equal(MetricOptimizationDirection.Neutral, Assert.Single(data.Metrics, metric => metric.Scope == MetricScope.Host).Direction);
    }

    [Fact]
    public async Task BuildAsyncCreatesCompleteSingleTableComparison()
    {
        var first = CreateComparison("Before", 10, 120);
        var second = CreateComparison("After", 20, 150);
        using var client = new HttpClient(new SourceHandler(first, second));
        var sources = new[]
        {
            new ComparisonSource("Before", new Uri("https://reports.test/before.json")),
            new ComparisonSource("After", new Uri("https://reports.test/after.json"))
        };

        var exitCode = await ComparisonRunner.BuildAsync(client, sources, _outputDirectory, "Before vs After", TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var html = await File.ReadAllTextAsync(Path.Combine(_outputDirectory, "web-report", "index.html"), TestContext.Current.CancellationToken);
        Assert.Contains("Before vs After", html, StringComparison.Ordinal);
        Assert.Contains(MetricNames.ProcessMemoryWorkingSet, html, StringComparison.Ordinal);
        Assert.Contains("requests.completed", html, StringComparison.Ordinal);
        Assert.Contains("class=\"best\">10", html, StringComparison.Ordinal);
        Assert.Contains("class=\"best\">150", html, StringComparison.Ordinal);
        var markdown = await File.ReadAllTextAsync(Path.Combine(_outputDirectory, "comparison.md"), TestContext.Current.CancellationToken);
        Assert.Contains("Minimum / mean / maximum / P95 / final", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("🔴", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsyncRejectsNonHttpsAndDuplicateLabels()
    {
        using var client = new HttpClient(new SourceHandler(CreateComparison("A", 1, 1), CreateComparison("B", 2, 2)));
        await Assert.ThrowsAsync<ArgumentException>(() => ComparisonRunner.BuildAsync(client,
        [
            new ComparisonSource("Same", new Uri("https://reports.test/a.json")),
            new ComparisonSource("Same", new Uri("http://reports.test/b.json"))
        ], _outputDirectory, "Comparison", TestContext.Current.CancellationToken));
    }

    private static ComparisonData CreateComparison(string label, double memory, double completed) => new(1, label, DateTimeOffset.UnixEpoch, "Test OS", "X64",
    [
        Metric(MetricScope.Process, MetricNames.ProcessMemoryWorkingSet, "By", MetricOptimizationDirection.Lower, memory),
        Metric(MetricScope.Application, "requests.completed", "{request}", MetricOptimizationDirection.Higher, completed)
    ]);

    private static ComparisonMetricData Metric(MetricScope scope, string name, string unit, MetricOptimizationDirection direction, double value) =>
        new(scope, name, string.Empty, unit, direction, new MetricStatistics(value, value, value, value, value, 0, 0, value, 0), 1);

    private static MetricSample Sample(MetricScope scope, string name, double value, string unit) =>
        new(1, DateTimeOffset.UnixEpoch, 0, scope, name, value, unit, new Dictionary<string, string>(), MetricAvailability.Available);

    private static PerformanceRunResult CreateResult() => new(3, "test", "Test", new RunSettingsSnapshot(1, 1, 1000, 0, 1, false, 1, false, 1),
        new EnvironmentSnapshot("Test OS", ".NET", "X64", "X64", 8, "machine", "runner", "Test", "X64", DateTimeOffset.UnixEpoch), [],
        new DiagnosticArtifact("Counters", false, false, null, null), new DiagnosticArtifact("Trace", false, false, null, null), [], DateTimeOffset.UnixEpoch);

    public void Dispose() { if (Directory.Exists(_outputDirectory)) Directory.Delete(_outputDirectory, true); }

    private sealed class SourceHandler(ComparisonData first, ComparisonData second) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var data = request.RequestUri!.AbsolutePath.Contains("before", StringComparison.Ordinal) ? first : second;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") });
        }
    }
}
