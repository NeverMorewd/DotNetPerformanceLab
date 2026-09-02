using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace DotNetPerformanceLab;

public interface IHostMetricCollector
{
    CollectorCapability Capability { get; }

    HostSnapshot Capture();
}

public sealed record HostSnapshot(
    double? CpuUsagePercent,
    long? TotalMemoryBytes,
    long? AvailableMemoryBytes,
    long? TotalSwapBytes,
    long? UsedSwapBytes,
    long? NetworkReceivedBytes,
    long? NetworkTransmittedBytes,
    int? ProcessCount,
    double? LoadAverageOneMinute,
    double? LoadAverageFiveMinutes,
    double? LoadAverageFifteenMinutes);

public static class HostMetricCollector
{
    public static IHostMetricCollector Create() =>
        OperatingSystem.IsWindows() ? new WindowsHostMetricCollector() :
        OperatingSystem.IsLinux() ? new LinuxHostMetricCollector() :
        OperatingSystem.IsMacOS() ? new MacOsHostMetricCollector() :
        new UnsupportedHostMetricCollector();
}

internal abstract class HostMetricCollectorBase : IHostMetricCollector
{
    private long? _previousNetworkReceived;
    private long? _previousNetworkTransmitted;

    public abstract CollectorCapability Capability { get; }

    public HostSnapshot Capture()
    {
        var platform = CapturePlatform();
        var (received, transmitted) = ReadNetworkTotals();
        var networkReceived = Delta(received, ref _previousNetworkReceived);
        var networkTransmitted = Delta(transmitted, ref _previousNetworkTransmitted);
        return platform with
        {
            NetworkReceivedBytes = networkReceived,
            NetworkTransmittedBytes = networkTransmitted,
            ProcessCount = ReadProcessCount()
        };
    }

    protected abstract HostSnapshot CapturePlatform();

    private static (long? Received, long? Transmitted) ReadNetworkTotals()
    {
        try
        {
            long received = 0;
            long transmitted = 0;
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback || adapter.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                var statistics = adapter.GetIPStatistics();
                received += statistics.BytesReceived;
                transmitted += statistics.BytesSent;
            }

            return (received, transmitted);
        }
        catch (NetworkInformationException)
        {
            return (null, null);
        }
    }

    private static long? Delta(long? current, ref long? previous)
    {
        var result = current.HasValue && previous.HasValue && current >= previous ? current - previous : null;
        previous = current;
        return result;
    }

    private static int? ReadProcessCount()
    {
        try
        {
            return Process.GetProcesses().Length;
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    protected static CollectorCapability CreateCapability(string name, params string[] metrics) =>
        new(name, MetricScope.Host, MetricAvailability.Available, metrics);

    protected static HostSnapshot Empty => new(null, null, null, null, null, null, null, null, null, null, null);
}

internal sealed class WindowsHostMetricCollector : HostMetricCollectorBase
{
    private ulong? _previousIdle;
    private ulong? _previousTotal;

    public override CollectorCapability Capability { get; } = CreateCapability(
        "Windows host metrics",
        MetricNames.HostCpuUsage,
        MetricNames.HostMemoryTotal,
        MetricNames.HostMemoryAvailable,
        MetricNames.HostSwapTotal,
        MetricNames.HostSwapUsed,
        MetricNames.HostNetworkReceive,
        MetricNames.HostNetworkTransmit,
        MetricNames.HostProcessCount);

    protected override HostSnapshot CapturePlatform()
    {
        double? cpu = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var idleValue = idle.Value;
            var totalValue = kernel.Value + user.Value;
            if (_previousIdle.HasValue && _previousTotal.HasValue && totalValue > _previousTotal)
            {
                var totalDelta = totalValue - _previousTotal.Value;
                var idleDelta = idleValue - _previousIdle.Value;
                cpu = Math.Clamp((totalDelta - Math.Min(idleDelta, totalDelta)) * 100d / totalDelta, 0, 100);
            }

            _previousIdle = idleValue;
            _previousTotal = totalValue;
        }

        var memory = new MemoryStatusEx();
        long? totalMemory = null;
        long? availableMemory = null;
        long? totalSwap = null;
        long? usedSwap = null;
        if (GlobalMemoryStatusEx(memory))
        {
            totalMemory = checked((long)memory.TotalPhysical);
            availableMemory = checked((long)memory.AvailablePhysical);
            var totalPageFile = checked((long)memory.TotalPageFile);
            var availablePageFile = checked((long)memory.AvailablePageFile);
            totalSwap = Math.Max(0, totalPageFile - totalMemory.Value);
            usedSwap = Math.Max(0, totalPageFile - availablePageFile - (totalMemory.Value - availableMemory.Value));
        }

        return Empty with
        {
            CpuUsagePercent = cpu,
            TotalMemoryBytes = totalMemory,
            AvailableMemoryBytes = availableMemory,
            TotalSwapBytes = totalSwap,
            UsedSwapBytes = usedSwap
        };
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}

internal sealed class LinuxHostMetricCollector : HostMetricCollectorBase
{
    private long? _previousIdle;
    private long? _previousTotal;

    public override CollectorCapability Capability { get; } = CreateCapability(
        "Linux host metrics",
        MetricNames.HostCpuUsage,
        MetricNames.HostMemoryTotal,
        MetricNames.HostMemoryAvailable,
        MetricNames.HostSwapTotal,
        MetricNames.HostSwapUsed,
        MetricNames.HostLoadOne,
        MetricNames.HostLoadFive,
        MetricNames.HostLoadFifteen,
        MetricNames.HostNetworkReceive,
        MetricNames.HostNetworkTransmit,
        MetricNames.HostProcessCount);

    protected override HostSnapshot CapturePlatform()
    {
        var (cpu, _) = ReadCpu();
        var memory = ReadMemory();
        var load = ReadLoadAverage();
        return Empty with
        {
            CpuUsagePercent = cpu,
            TotalMemoryBytes = memory.Total,
            AvailableMemoryBytes = memory.Available,
            TotalSwapBytes = memory.SwapTotal,
            UsedSwapBytes = memory.SwapUsed,
            LoadAverageOneMinute = load.One,
            LoadAverageFiveMinutes = load.Five,
            LoadAverageFifteenMinutes = load.Fifteen
        };
    }

    private (double? Usage, bool Success) ReadCpu()
    {
        try
        {
            var values = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                .Select(value => long.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (values.Length < 4)
            {
                return (null, false);
            }

            var idle = values[3] + (values.Length > 4 ? values[4] : 0);
            var total = values.Sum();
            double? usage = null;
            if (_previousIdle.HasValue && _previousTotal.HasValue && total > _previousTotal)
            {
                var totalDelta = total - _previousTotal.Value;
                usage = Math.Clamp((totalDelta - (idle - _previousIdle.Value)) * 100d / totalDelta, 0, 100);
            }

            _previousIdle = idle;
            _previousTotal = total;
            return (usage, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            return (null, false);
        }
    }

    private static (long? Total, long? Available, long? SwapTotal, long? SwapUsed) ReadMemory()
    {
        try
        {
            var values = File.ReadLines("/proc/meminfo")
                .Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => ParseKibibytes(parts[1]), StringComparer.Ordinal);
            var total = values.GetValueOrDefault("MemTotal");
            var available = values.GetValueOrDefault("MemAvailable");
            var swapTotal = values.GetValueOrDefault("SwapTotal");
            var swapFree = values.GetValueOrDefault("SwapFree");
            return (total, available, swapTotal, swapTotal.HasValue && swapFree.HasValue ? swapTotal - swapFree : null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            return (null, null, null, null);
        }
    }

    private static (double? One, double? Five, double? Fifteen) ReadLoadAverage()
    {
        try
        {
            var values = File.ReadAllText("/proc/loadavg").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return values.Length >= 3
                ? (ParseDouble(values[0]), ParseDouble(values[1]), ParseDouble(values[2]))
                : (null, null, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, null, null);
        }
    }

    private static long? ParseKibibytes(string text)
    {
        var value = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? checked(parsed * 1024)
            : null;
    }

    private static double? ParseDouble(string text) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
}

internal sealed class MacOsHostMetricCollector : HostMetricCollectorBase
{
    public override CollectorCapability Capability { get; } = CreateCapability(
        "macOS host metrics",
        MetricNames.HostMemoryTotal,
        MetricNames.HostLoadOne,
        MetricNames.HostLoadFive,
        MetricNames.HostLoadFifteen,
        MetricNames.HostNetworkReceive,
        MetricNames.HostNetworkTransmit,
        MetricNames.HostProcessCount);

    protected override HostSnapshot CapturePlatform()
    {
        var memory = ReadSysctlInt64("hw.memsize");
        var load = new double[3];
        var loadCount = GetLoadAverage(load, load.Length);
        return Empty with
        {
            TotalMemoryBytes = memory,
            LoadAverageOneMinute = loadCount > 0 ? load[0] : null,
            LoadAverageFiveMinutes = loadCount > 1 ? load[1] : null,
            LoadAverageFifteenMinutes = loadCount > 2 ? load[2] : null
        };
    }

    private static long? ReadSysctlInt64(string name)
    {
        nuint length = sizeof(long);
        long value = 0;
        return SysctlByName(name, ref value, ref length, IntPtr.Zero, 0) == 0 ? value : null;
    }

    [DllImport("libSystem.dylib", EntryPoint = "sysctlbyname")]
    private static extern int SysctlByName(string name, ref long oldValue, ref nuint oldLength, IntPtr newValue, nuint newLength);

    [DllImport("libSystem.dylib", EntryPoint = "getloadavg")]
    private static extern int GetLoadAverage([Out] double[] loadAverage, int count);
}

internal sealed class UnsupportedHostMetricCollector : IHostMetricCollector
{
    public CollectorCapability Capability { get; } = new(
        "Host metrics",
        MetricScope.Host,
        MetricAvailability.Unsupported,
        [],
        "The current operating system is not supported.");

    public HostSnapshot Capture() => new(null, null, null, null, null, null, null, null, null, null, null);
}
