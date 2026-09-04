using System.Text.Json;

namespace DotNetPerformanceLab;

public static class ComparisonDataExporter
{
    public static ComparisonData Create(PerformanceRunResult result, IReadOnlyList<MetricSample> metrics)
    {
        var aggregates = metrics
            .Where(sample => sample.Availability == MetricAvailability.Available && sample.Value is not null && double.IsFinite(sample.Value.Value))
            .GroupBy(sample => new MetricKey(sample.Scope, sample.Name, sample.Unit, FormatTags(sample.Tags)))
            .Select(group =>
            {
                var ordered = group.OrderBy(sample => sample.TimestampUtc).ToArray();
                var startedUtc = ordered[0].TimestampUtc;
                var observations = ordered.Select(sample => (TimeSeconds: (sample.TimestampUtc - startedUtc).TotalSeconds, Value: sample.Value!.Value)).ToArray();
                return new ComparisonMetricData(
                    group.Key.Scope,
                    group.Key.Name,
                    group.Key.Tags,
                    group.Key.Unit,
                    MetricDirectionPolicy.Classify(group.Key.Scope, group.Key.Name),
                    Statistics.Calculate(observations),
                    observations.Length);
            })
            .OrderBy(metric => metric.Scope)
            .ThenBy(metric => metric.Name, StringComparer.Ordinal)
            .ThenBy(metric => metric.Tags, StringComparer.Ordinal)
            .ToArray();

        return new ComparisonData(1, result.ReportLabel, result.GeneratedUtc, result.Environment.OperatingSystem, result.Environment.ProcessArchitecture, aggregates);
    }

    public static async Task WriteAsync(string outputDirectory, ComparisonData data, CancellationToken cancellationToken)
    {
        foreach (var path in new[] { Path.Combine(outputDirectory, "comparison-data.json"), Path.Combine(outputDirectory, "web-report", "comparison-data.json") })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, data, LabJsonContext.Default.ComparisonData, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string FormatTags(IReadOnlyDictionary<string, string> tags) =>
        string.Join(",", tags.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));

    private sealed record MetricKey(MetricScope Scope, string Name, string Unit, string Tags);
}

public static class MetricDirectionPolicy
{
    private static readonly string[] HigherTerms = ["throughput", "completed", "success"];
    private static readonly string[] LowerTerms = [
        "cpu", "memory", "working_set", "private", "virtual", "thread", "handle", "latency", "duration",
        "pause", "allocation", "heap", "collection", "exception", "error", "failure", "contention", "queue", "load", "swap.used"];

    public static MetricOptimizationDirection Classify(MetricScope scope, string name)
    {
        if (scope == MetricScope.Host) return MetricOptimizationDirection.Neutral;
        var normalized = name.ToLowerInvariant();
        if (HigherTerms.Any(normalized.Contains)) return MetricOptimizationDirection.Higher;
        if (LowerTerms.Any(normalized.Contains)) return MetricOptimizationDirection.Lower;
        if (scope == MetricScope.Process && (normalized.Contains("io.") || normalized.Contains("network"))) return MetricOptimizationDirection.Lower;
        return MetricOptimizationDirection.Neutral;
    }
}
