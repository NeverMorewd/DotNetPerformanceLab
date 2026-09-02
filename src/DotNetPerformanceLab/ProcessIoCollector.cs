using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DotNetPerformanceLab;

public sealed record ProcessIoSnapshot(
    long? ReadOperationCount,
    long? WriteOperationCount,
    long? ReadBytes,
    long? WriteBytes);

public interface IProcessIoCollector
{
    CollectorCapability Capability { get; }
    ProcessIoSnapshot Capture(Process process);
}

public static class ProcessIoCollector
{
    public static IProcessIoCollector Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsProcessIoCollector();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxProcessIoCollector();
        }

        return UnsupportedProcessIoCollector.Instance;
    }

    private sealed class WindowsProcessIoCollector : IProcessIoCollector
    {
        public CollectorCapability Capability { get; } = AvailableCapability("Windows GetProcessIoCounters");

        public ProcessIoSnapshot Capture(Process process)
        {
            try
            {
                if (!GetProcessIoCounters(process.Handle, out var counters))
                {
                    return Empty;
                }

                return new ProcessIoSnapshot(
                    CheckedLong(counters.ReadOperationCount),
                    CheckedLong(counters.WriteOperationCount),
                    CheckedLong(counters.ReadTransferCount),
                    CheckedLong(counters.WriteTransferCount));
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return Empty;
            }
        }

        private static long? CheckedLong(ulong value) => value <= long.MaxValue ? (long)value : null;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessIoCounters(IntPtr processHandle, out IoCounters counters);

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }
    }

    private sealed class LinuxProcessIoCollector : IProcessIoCollector
    {
        public CollectorCapability Capability { get; } = AvailableCapability("Linux procfs process I/O");

        public ProcessIoSnapshot Capture(Process process)
        {
            try
            {
                long? readOperations = null;
                long? writeOperations = null;
                long? readBytes = null;
                long? writeBytes = null;
                foreach (var line in File.ReadLines($"/proc/{process.Id}/io"))
                {
                    var separator = line.IndexOf(':');
                    if (separator < 1 || !long.TryParse(line[(separator + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    {
                        continue;
                    }

                    switch (line[..separator])
                    {
                        case "syscr": readOperations = value; break;
                        case "syscw": writeOperations = value; break;
                        case "read_bytes": readBytes = value; break;
                        case "write_bytes": writeBytes = value; break;
                    }
                }

                return new ProcessIoSnapshot(readOperations, writeOperations, readBytes, writeBytes);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Empty;
            }
        }
    }

    private sealed class UnsupportedProcessIoCollector : IProcessIoCollector
    {
        public static UnsupportedProcessIoCollector Instance { get; } = new();
        public CollectorCapability Capability { get; } = new(
            "Process I/O",
            MetricScope.Process,
            MetricAvailability.Unsupported,
            IoMetricNames,
            "Portable per-process I/O counters are not available on this operating system.");
        public ProcessIoSnapshot Capture(Process process) => Empty;
    }

    private static readonly string[] IoMetricNames =
    [
        MetricNames.ProcessIoReadOperations,
        MetricNames.ProcessIoWriteOperations,
        MetricNames.ProcessIoReadBytes,
        MetricNames.ProcessIoWriteBytes
    ];

    private static CollectorCapability AvailableCapability(string collector) => new(
        collector,
        MetricScope.Process,
        MetricAvailability.Available,
        IoMetricNames);

    private static ProcessIoSnapshot Empty { get; } = new(null, null, null, null);
}
