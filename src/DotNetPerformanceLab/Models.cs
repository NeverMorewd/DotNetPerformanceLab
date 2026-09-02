using System.Text.Json.Serialization;

namespace DotNetPerformanceLab;

public sealed record RunSettings(
    string TargetPath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string OutputDirectory,
    string ReportLabel,
    TimeSpan Warmup,
    TimeSpan Measurement,
    TimeSpan SampleInterval,
    TimeSpan Cooldown,
    int Iterations,
    bool CollectCounters,
    TimeSpan CounterDuration,
    bool CollectTrace,
    TimeSpan TraceDuration,
    bool FailOnTargetExit,
    IReadOnlyList<string> AllowedRoots,
    string ToolDirectory);

public sealed record ProcessSample(
    int Iteration,
    DateTimeOffset TimestampUtc,
    double ElapsedSeconds,
    double? CpuCorePercent,
    double? CpuMachinePercent,
    long WorkingSetBytes,
    long? PrivateMemoryBytes,
    int? ThreadCount,
    double? CpuUserPercent = null,
    double? CpuSystemPercent = null,
    long? VirtualMemoryBytes = null,
    long? PeakWorkingSetBytes = null,
    long? PeakPagedMemoryBytes = null,
    int? HandleCount = null,
    HostSnapshot? Host = null);

public sealed record MetricStatistics(
    double Minimum,
    double Median,
    double Percentile95,
    double Maximum,
    double Mean,
    double StandardDeviation,
    double CoefficientOfVariationPercent,
    double Final,
    double GrowthPerMinute);

public sealed record IterationResult(
    int Iteration,
    int ProcessId,
    DateTimeOffset StartedUtc,
    DateTimeOffset MeasurementStartedUtc,
    DateTimeOffset MeasurementEndedUtc,
    int? ExitCode,
    bool ExitedUnexpectedly,
    IReadOnlyList<ProcessSample> Samples,
    MetricStatistics? CpuCorePercent,
    MetricStatistics? CpuMachinePercent,
    MetricStatistics WorkingSetBytes,
    MetricStatistics? PrivateMemoryBytes,
    string SamplesFile,
    IReadOnlyList<MetricSample>? Metrics = null);

public sealed record DiagnosticArtifact(
    string Name,
    bool Requested,
    bool Collected,
    string? File,
    string? Message);

public sealed record RuntimeMetricSummary(
    string Name,
    string Tags,
    string Unit,
    double Mean,
    double Maximum,
    double Final,
    double Sum,
    int Samples);

public sealed record EnvironmentSnapshot(
    string OperatingSystem,
    string Framework,
    string Architecture,
    string ProcessArchitecture,
    int LogicalProcessors,
    string MachineName,
    string RunnerName,
    string RunnerOs,
    string RunnerArchitecture,
    DateTimeOffset CapturedUtc);

public sealed record PerformanceRunResult(
    int SchemaVersion,
    string TargetPath,
    string ReportLabel,
    RunSettingsSnapshot Settings,
    EnvironmentSnapshot Environment,
    IReadOnlyList<IterationResult> Iterations,
    DiagnosticArtifact Counters,
    DiagnosticArtifact Trace,
    IReadOnlyList<RuntimeMetricSummary> RuntimeMetrics,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<CollectorCapability>? Capabilities = null);

public sealed record RunSettingsSnapshot(
    int WarmupSeconds,
    int MeasurementSeconds,
    int SampleIntervalMilliseconds,
    int CooldownSeconds,
    int Iterations,
    bool CollectCounters,
    int CounterDurationSeconds,
    bool CollectTrace,
    int TraceDurationSeconds);

[JsonSerializable(typeof(PerformanceRunResult))]
[JsonSerializable(typeof(IReadOnlyList<MetricSample>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
internal sealed partial class LabJsonContext : JsonSerializerContext;
