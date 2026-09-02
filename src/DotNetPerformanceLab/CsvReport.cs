using System.Globalization;
using System.Text;

namespace DotNetPerformanceLab;

public static class CsvReport
{
    public static async Task WriteSamplesAsync(
        string path,
        IReadOnlyList<ProcessSample> samples,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("iteration,timestamp_utc,elapsed_seconds,cpu_core_percent,cpu_machine_percent,cpu_user_percent,cpu_system_percent,working_set_bytes,private_memory_bytes,virtual_memory_bytes,peak_working_set_bytes,peak_paged_memory_bytes,thread_count,handle_count,read_operation_count,write_operation_count,read_bytes,write_bytes,host_cpu_percent,host_memory_total_bytes,host_memory_available_bytes,host_swap_total_bytes,host_swap_used_bytes,host_network_received_bytes,host_network_transmitted_bytes,host_process_count,host_load_1m,host_load_5m,host_load_15m");
        foreach (var sample in samples)
        {
            builder.Append(sample.Iteration).Append(',')
                .Append(sample.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(Format(sample.ElapsedSeconds)).Append(',')
                .Append(Format(sample.CpuCorePercent)).Append(',')
                .Append(Format(sample.CpuMachinePercent)).Append(',')
                .Append(Format(sample.CpuUserPercent)).Append(',')
                .Append(Format(sample.CpuSystemPercent)).Append(',')
                .Append(sample.WorkingSetBytes).Append(',')
                .Append(sample.PrivateMemoryBytes?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.VirtualMemoryBytes?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.PeakWorkingSetBytes?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.PeakPagedMemoryBytes?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.ThreadCount?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.HandleCount?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.ReadOperationCount?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.WriteOperationCount?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.ReadBytes?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.WriteBytes?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Format(sample.Host?.CpuUsagePercent)).Append(',')
                .Append(sample.Host?.TotalMemoryBytes?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.Host?.AvailableMemoryBytes?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.Host?.TotalSwapBytes?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.Host?.UsedSwapBytes?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.Host?.NetworkReceivedBytes?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.Host?.NetworkTransmittedBytes?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.Host?.ProcessCount?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Format(sample.Host?.LoadAverageOneMinute)).Append(',')
                .Append(Format(sample.Host?.LoadAverageFiveMinutes)).Append(',')
                .Append(Format(sample.Host?.LoadAverageFifteenMinutes)).AppendLine();
        }

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    private static string Format(double? value) =>
        value?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
}
