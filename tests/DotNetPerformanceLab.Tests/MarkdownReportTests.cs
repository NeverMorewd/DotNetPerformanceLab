namespace DotNetPerformanceLab.Tests;

public sealed class MarkdownReportTests
{
    [Fact]
    public async Task DownloadableReportReferencesPackagedCharts()
    {
        var report = await WriteReportAsync(MarkdownReportTarget.DownloadableArtifact);

        Assert.Contains("![CPU usage](charts/cpu.svg)", report, StringComparison.Ordinal);
        Assert.Contains("![Working set](charts/working-set.svg)", report, StringComparison.Ordinal);
        Assert.Contains("![Private memory](charts/private-memory.svg)", report, StringComparison.Ordinal);
        Assert.Contains("![Host CPU](charts/host-cpu.svg)", report, StringComparison.Ordinal);
        Assert.Contains("![Host memory](charts/host-memory.svg)", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobSummaryDoesNotReferenceArtifactRelativePaths()
    {
        var report = await WriteReportAsync(MarkdownReportTarget.GitHubJobSummary);

        Assert.DoesNotContain("charts/", report, StringComparison.Ordinal);
        Assert.Contains("Download the performance report artifact", report, StringComparison.Ordinal);
    }

    private static async Task<string> WriteReportAsync(MarkdownReportTarget target)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotnet-performance-report-{Guid.NewGuid():N}.md");
        try
        {
            await MarkdownReport.WriteAsync(path, CreateResult(), target, TestContext.Current.CancellationToken);
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
            "test-app",
            "Test application",
            new RunSettingsSnapshot(1, 5, 1000, 0, 1, false, 30, false, 10),
            new EnvironmentSnapshot("Test OS", ".NET", "X64", "X64", 8, "test", "test", "Test OS", "X64", DateTimeOffset.UnixEpoch),
            [new IterationResult(1, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, false, [], statistics, statistics, statistics, statistics, "samples.csv")],
            new DiagnosticArtifact("Runtime counters", false, false, null, null),
            new DiagnosticArtifact("EventPipe trace", false, false, null, null),
            [],
            DateTimeOffset.UnixEpoch);
    }
}
