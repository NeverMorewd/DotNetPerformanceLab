using System.Text.Json.Serialization;

namespace DotNetPerformanceLab;

[JsonConverter(typeof(JsonStringEnumConverter<MetricOptimizationDirection>))]
public enum MetricOptimizationDirection
{
    Lower,
    Higher,
    Neutral
}

public sealed record ComparisonMetricData(
    MetricScope Scope,
    string Name,
    string Tags,
    string Unit,
    MetricOptimizationDirection Direction,
    MetricStatistics Statistics,
    int Samples);

public sealed record ComparisonData(
    int SchemaVersion,
    string Label,
    DateTimeOffset GeneratedUtc,
    string OperatingSystem,
    string Architecture,
    IReadOnlyList<ComparisonMetricData> Metrics);

public sealed record ComparisonSource(string Label, Uri Url);

public sealed record ComparisonReportSummary(
    int SchemaVersion,
    string Title,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<string> Sources,
    int Metrics);

