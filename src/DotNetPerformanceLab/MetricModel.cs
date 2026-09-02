using System.Text.Json.Serialization;

namespace DotNetPerformanceLab;

[JsonConverter(typeof(JsonStringEnumConverter<MetricScope>))]
public enum MetricScope
{
    Process,
    Host,
    Runtime,
    Application
}

[JsonConverter(typeof(JsonStringEnumConverter<MetricAvailability>))]
public enum MetricAvailability
{
    Available,
    Unsupported,
    Unavailable
}

public sealed record MetricSample(
    int Iteration,
    DateTimeOffset TimestampUtc,
    double ElapsedSeconds,
    MetricScope Scope,
    string Name,
    double? Value,
    string Unit,
    IReadOnlyDictionary<string, string> Tags,
    MetricAvailability Availability,
    string? UnavailableReason = null);

public sealed record CollectorCapability(
    string Collector,
    MetricScope Scope,
    MetricAvailability Availability,
    IReadOnlyList<string> Metrics,
    string? Reason = null);

public static class MetricNames
{
    public const string ProcessCpuCore = "process.cpu.core_equivalent";
    public const string ProcessCpuMachine = "process.cpu.machine_normalized";
    public const string ProcessCpuUser = "process.cpu.user";
    public const string ProcessCpuSystem = "process.cpu.system";
    public const string ProcessMemoryWorkingSet = "process.memory.working_set";
    public const string ProcessMemoryPrivate = "process.memory.private";
    public const string ProcessMemoryVirtual = "process.memory.virtual";
    public const string ProcessThreads = "process.thread.count";
    public const string ProcessHandles = "process.handle.count";
    public const string ProcessIoReadOperations = "process.io.read.operations";
    public const string ProcessIoWriteOperations = "process.io.write.operations";
    public const string ProcessIoReadBytes = "process.io.read.bytes";
    public const string ProcessIoWriteBytes = "process.io.write.bytes";
    public const string HostCpuUsage = "host.cpu.usage";
    public const string HostMemoryTotal = "host.memory.total";
    public const string HostMemoryAvailable = "host.memory.available";
    public const string HostMemoryUsed = "host.memory.used";
    public const string HostSwapTotal = "host.swap.total";
    public const string HostSwapUsed = "host.swap.used";
    public const string HostNetworkReceive = "host.network.receive";
    public const string HostNetworkTransmit = "host.network.transmit";
    public const string HostProcessCount = "host.process.count";
    public const string HostLoadOne = "host.load.1m";
    public const string HostLoadFive = "host.load.5m";
    public const string HostLoadFifteen = "host.load.15m";
}
