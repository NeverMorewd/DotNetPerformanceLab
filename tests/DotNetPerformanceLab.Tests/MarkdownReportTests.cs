namespace DotNetPerformanceLab.Tests;

public sealed class MarkdownReportTests
{
    [Fact]
    public async Task DownloadableReportLinksInteractiveDashboardWithoutEmbeddingSvgCharts()
    {
        var report = await WriteReportAsync(MarkdownReportTarget.DownloadableArtifact);

        Assert.Contains("[Open the interactive performance dashboard](web-report/index.html)", report, StringComparison.Ordinal);
        Assert.DoesNotContain("charts/", report, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/runner/", report, StringComparison.Ordinal);
        Assert.Contains("**No activity observed:** dotnet.jit.compiled_methods.", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobSummaryRejectsNonHttpsReportUrl()
    {
        var report = await WriteReportAsync(MarkdownReportTarget.GitHubJobSummary, "http://example.test/performance/");

        Assert.DoesNotContain("example.test", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobSummaryLinksConfiguredPagesReport()
    {
        var report = await WriteReportAsync(MarkdownReportTarget.GitHubJobSummary, "https://example.github.io/performance/");

        Assert.DoesNotContain("charts/", report, StringComparison.Ordinal);
        Assert.Contains("[Open the interactive performance dashboard](https://example.github.io/performance/)", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Download the performance report artifact", report, StringComparison.Ordinal);
    }

    private static async Task<string> WriteReportAsync(MarkdownReportTarget target, string? interactiveReportUrl = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotnet-performance-report-{Guid.NewGuid():N}.md");
        try
        {
            await MarkdownReport.WriteAsync(path, CreateResult(), target, TestContext.Current.CancellationToken, interactiveReportUrl);
            return await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static PerformanceRunResult CreateResult()
    {
        var statistics = new MetricStatistics(1, 2, 3, 4, 2.5, 1, 10, 4, 0.5);
        return new PerformanceRunResult(
            1,
            "/home/runner/test-app",
            "Test application",
            new RunSettingsSnapshot(1, 5, 1000, 0, 1, false, 30, false, 10),
            new EnvironmentSnapshot("Test OS", ".NET", "X64", "X64", 8, "test", "test", "Test OS", "X64", DateTimeOffset.UnixEpoch),
            [new IterationResult(1, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, false, [], statistics, statistics, statistics, statistics, "samples.csv")],
            new DiagnosticArtifact("Runtime counters", false, false, null, null),
            new DiagnosticArtifact("EventPipe trace", false, false, null, null),
            [
                new RuntimeMetricSummary("dotnet.assembly.count", string.Empty, string.Empty, 42, 42, 42, 126, 3),
                new RuntimeMetricSummary("dotnet.jit.compiled_methods", string.Empty, "/s", 0, 0, 0, 0, 3)
            ],
            DateTimeOffset.UnixEpoch);
    }
}
