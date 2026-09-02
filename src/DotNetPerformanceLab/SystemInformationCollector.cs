using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DotNetPerformanceLab;

public static class SystemInformationCollector
{
    public static EnvironmentSnapshot Capture(IHostMetricCollector hostMetrics)
    {
        var host = hostMetrics.Capture();
        return new EnvironmentSnapshot(
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            Environment.MachineName,
            Environment.GetEnvironmentVariable("RUNNER_NAME") ?? "Local",
            Environment.GetEnvironmentVariable("RUNNER_OS") ?? RuntimeInformation.OSDescription,
            Environment.GetEnvironmentVariable("RUNNER_ARCH") ?? RuntimeInformation.OSArchitecture.ToString(),
            DateTimeOffset.UtcNow,
            RuntimeInformation.RuntimeIdentifier,
            Environment.OSVersion.VersionString,
            ReadProcessorModel(),
            ReadPhysicalCoreCount(),
            host.TotalMemoryBytes,
            host.TotalSwapBytes,
            DetectContainer());
    }

    private static string? ReadProcessorModel()
    {
        if (OperatingSystem.IsLinux())
        {
            return ReadLinuxValue("/proc/cpuinfo", "model name") ?? ReadLinuxValue("/proc/cpuinfo", "Hardware");
        }

        if (OperatingSystem.IsMacOS())
        {
            return RunSysctl("machdep.cpu.brand_string");
        }

        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
    }

    private static int? ReadPhysicalCoreCount()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var physicalId = string.Empty;
                var coreId = string.Empty;
                var cores = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in File.ReadLines("/proc/cpuinfo").Append(string.Empty))
                {
                    if (line.StartsWith("physical id", StringComparison.Ordinal)) physicalId = Value(line);
                    else if (line.StartsWith("core id", StringComparison.Ordinal)) coreId = Value(line);
                    else if (line.Length == 0 && coreId.Length > 0)
                    {
                        cores.Add($"{physicalId}:{coreId}");
                        physicalId = coreId = string.Empty;
                    }
                }

                return cores.Count > 0 ? cores.Count : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        if (OperatingSystem.IsMacOS() && int.TryParse(RunSysctl("hw.physicalcpu"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            return count;
        }

        return null;
    }

    private static string? ReadLinuxValue(string path, string key)
    {
        try
        {
            var line = File.ReadLines(path).FirstOrDefault(item => item.StartsWith(key, StringComparison.OrdinalIgnoreCase));
            return line is null ? null : Value(line);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string Value(string line)
    {
        var separator = line.IndexOf(':');
        return separator < 0 ? string.Empty : line[(separator + 1)..].Trim();
    }

    private static string? RunSysctl(string name)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/sbin/sysctl",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                ArgumentList = { "-n", name }
            });
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static bool? DetectContainer()
    {
        if (!OperatingSystem.IsLinux()) return null;
        if (File.Exists("/.dockerenv")) return true;
        try
        {
            return File.ReadLines("/proc/1/cgroup").Any(line =>
                line.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("kubepods", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("containerd", StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return null;
        }
    }
}
