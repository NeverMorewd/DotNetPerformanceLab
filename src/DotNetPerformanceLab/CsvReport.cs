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
        builder.AppendLine("iteration,timestamp_utc,elapsed_seconds,cpu_core_percent,cpu_machine_percent,working_set_bytes,private_memory_bytes,thread_count");
        foreach (var sample in samples)
        {
            builder.Append(sample.Iteration).Append(',')
                .Append(sample.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(Format(sample.ElapsedSeconds)).Append(',')
                .Append(Format(sample.CpuCorePercent)).Append(',')
                .Append(Format(sample.CpuMachinePercent)).Append(',')
                .Append(sample.WorkingSetBytes).Append(',')
                .Append(sample.PrivateMemoryBytes?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.ThreadCount?.ToString(CultureInfo.InvariantCulture)).AppendLine();
        }

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    private static string Format(double? value) =>
        value?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
}
