using System.Globalization;
using System.Text;

namespace DotNetPerformanceLab;

public static class MarkdownReport
{
    private const double BytesPerMegabyte = 1024 * 1024;

    public static async Task WriteAsync(
        string path,
        PerformanceRunResult result,
        MarkdownReportTarget target,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# .NET Performance Analysis Report").AppendLine();
        builder.Append("**Target:** ").Append(Escape(result.ReportLabel)).AppendLine("  ");
        builder.Append("**Executable:** `").Append(EscapeCode(result.TargetPath)).AppendLine("`  ");
        builder.Append("**Generated:** ").Append(result.GeneratedUtc.ToString("u", CultureInfo.InvariantCulture)).AppendLine().AppendLine();
        if (target == MarkdownReportTarget.DownloadableArtifact)
        {
            builder.AppendLine("[Open the interactive Plotly report](web-report/index.html)").AppendLine();
        }

        builder.AppendLine("## Executive summary").AppendLine();
        builder.AppendLine("| Metric | Median | P95 | Maximum | Final | Growth/min |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|");
        AppendAggregateRow(builder, "CPU, core equivalent", result.Iterations.Select(item => item.CpuCorePercent), "%", 1);
        AppendAggregateRow(builder, "CPU, machine normalized", result.Iterations.Select(item => item.CpuMachinePercent), "%", 2);
        AppendAggregateRow(builder, "Working set", result.Iterations.Select(item => item.WorkingSetBytes), " MB", 2, BytesPerMegabyte);
        AppendAggregateRow(builder, "Private memory", result.Iterations.Select(item => item.PrivateMemoryBytes), " MB", 2, BytesPerMegabyte);
        AppendSampleAggregateRow(builder, "Host CPU", result, MetricNames.HostCpuUsage, "%", 1);
        AppendSampleAggregateRow(builder, "Host memory used", result, MetricNames.HostMemoryUsed, " MB", 2, BytesPerMegabyte);
        builder.AppendLine();

        builder.AppendLine("CPU core equivalent can exceed 100% when the process uses more than one logical processor. Machine-normalized CPU divides this value by the runner logical processor count.").AppendLine();

        builder.AppendLine("## Iterations").AppendLine();
        builder.AppendLine("| Iteration | Samples | CPU median | Working-set median | Working-set P95 | Private-memory median | CV |");
        builder.AppendLine("|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var iteration in result.Iterations)
        {
            builder.Append("| ").Append(iteration.Iteration)
                .Append(" | ").Append(iteration.Samples.Count)
                .Append(" | ").Append(Format(iteration.CpuCorePercent?.Median, "%", 1))
                .Append(" | ").Append(Format(iteration.WorkingSetBytes.Median / BytesPerMegabyte, " MB", 2))
                .Append(" | ").Append(Format(iteration.WorkingSetBytes.Percentile95 / BytesPerMegabyte, " MB", 2))
                .Append(" | ").Append(Format(iteration.PrivateMemoryBytes?.Median / BytesPerMegabyte, " MB", 2))
                .Append(" | ").Append(Format(iteration.WorkingSetBytes.CoefficientOfVariationPercent, "%", 2))
                .AppendLine(" |");
        }

        builder.AppendLine().AppendLine("## Environment").AppendLine();
        builder.AppendLine("| Property | Value |").AppendLine("|---|---|");
        AppendProperty(builder, "Operating system", result.Environment.OperatingSystem);
        AppendProperty(builder, ".NET framework", result.Environment.Framework);
        AppendProperty(builder, "OS architecture", result.Environment.Architecture);
        AppendProperty(builder, "Process architecture", result.Environment.ProcessArchitecture);
        AppendProperty(builder, "Logical processors", result.Environment.LogicalProcessors.ToString(CultureInfo.InvariantCulture));
        AppendProperty(builder, "Machine", result.Environment.MachineName);
        AppendProperty(builder, "Runner", result.Environment.RunnerName);
        AppendProperty(builder, "Runner OS", result.Environment.RunnerOs);
        AppendProperty(builder, "Runner architecture", result.Environment.RunnerArchitecture);

        builder.AppendLine().AppendLine("### Collector capabilities").AppendLine();
        builder.AppendLine("| Collector | Scope | Status | Reason |").AppendLine("|---|---|---|---|");
        foreach (var capability in result.Capabilities ?? [])
        {
            builder.Append("| ").Append(Escape(capability.Collector))
                .Append(" | ").Append(capability.Scope)
                .Append(" | ").Append(capability.Availability)
                .Append(" | ").Append(Escape(capability.Reason ?? string.Empty))
                .AppendLine(" |");
        }

        builder.AppendLine().AppendLine("## Test configuration").AppendLine();
        builder.AppendLine("| Property | Value |").AppendLine("|---|---:|");
        AppendProperty(builder, "Iterations", result.Settings.Iterations.ToString(CultureInfo.InvariantCulture));
        AppendProperty(builder, "Warm-up", $"{result.Settings.WarmupSeconds} seconds");
        AppendProperty(builder, "Measurement", $"{result.Settings.MeasurementSeconds} seconds");
        AppendProperty(builder, "Sample interval", $"{result.Settings.SampleIntervalMilliseconds} ms");
        AppendProperty(builder, "Cooldown", $"{result.Settings.CooldownSeconds} seconds");

        builder.AppendLine().AppendLine("## Managed diagnostics").AppendLine();
        AppendDiagnostic(builder, result.Counters);
        AppendDiagnostic(builder, result.Trace);

        if (result.RuntimeMetrics.Count > 0)
        {
            builder.AppendLine().AppendLine("### Runtime counter summary").AppendLine();
            builder.AppendLine("| Metric | Tags | Mean | Maximum | Final | Samples |");
            builder.AppendLine("|---|---|---:|---:|---:|---:|");
            foreach (var metric in result.RuntimeMetrics)
            {
                var divisor = metric.Unit.StartsWith("MB", StringComparison.Ordinal) ? BytesPerMegabyte : metric.Unit.StartsWith("ms", StringComparison.Ordinal) ? 0.001 : 1;
                builder.Append("| ").Append(Escape(metric.Name))
                    .Append(" | ").Append(Escape(metric.Tags))
                    .Append(" | ").Append(Format(metric.Mean / divisor, MetricSuffix(metric.Unit), 2))
                    .Append(" | ").Append(Format(metric.Maximum / divisor, MetricSuffix(metric.Unit), 2))
                    .Append(" | ").Append(Format(metric.Final / divisor, MetricSuffix(metric.Unit), 2))
                    .Append(" | ").Append(metric.Samples)
                    .AppendLine(" |");
            }
        }

        builder.AppendLine().AppendLine("## Charts").AppendLine();
        if (target == MarkdownReportTarget.DownloadableArtifact)
        {
            builder.AppendLine("![CPU usage](charts/cpu.svg)").AppendLine();
            builder.AppendLine("![Working set](charts/working-set.svg)").AppendLine();
            builder.AppendLine("![Private memory](charts/private-memory.svg)").AppendLine();
            builder.AppendLine("![Host CPU](charts/host-cpu.svg)").AppendLine();
            builder.AppendLine("![Host memory](charts/host-memory.svg)").AppendLine();
        }
        else
        {
            builder.AppendLine("Download the performance report artifact from this workflow run to view the full-resolution SVG charts.").AppendLine();
        }

        builder.AppendLine("## Interpretation notes").AppendLine();
        builder.AppendLine("- Process metrics are suitable for comparisons only when runs use the same runner, operating system, power profile, workload, and measurement configuration.");
        builder.AppendLine("- Positive memory growth is a signal for further investigation, not proof of a memory leak.");
        builder.AppendLine("- EventPipe diagnostics run separately and do not contribute samples to the baseline summary.");
        builder.AppendLine("- Working-set and private-memory definitions vary by operating system.");

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    private static void AppendAggregateRow(
        StringBuilder builder,
        string name,
        IEnumerable<MetricStatistics?> statistics,
        string suffix,
        int decimals,
        double divisor = 1)
    {
        var items = statistics.Where(item => item is not null).Cast<MetricStatistics>().ToArray();
        if (items.Length == 0)
        {
            builder.Append("| ").Append(name).AppendLine(" | N/A | N/A | N/A | N/A | N/A |");
            return;
        }

        builder.Append("| ").Append(name)
            .Append(" | ").Append(Format(Statistics.Percentile(items.Select(item => item.Median).Order().ToArray(), 0.5) / divisor, suffix, decimals))
            .Append(" | ").Append(Format(Statistics.Percentile(items.Select(item => item.Percentile95).Order().ToArray(), 0.5) / divisor, suffix, decimals))
            .Append(" | ").Append(Format(items.Max(item => item.Maximum) / divisor, suffix, decimals))
            .Append(" | ").Append(Format(Statistics.Percentile(items.Select(item => item.Final).Order().ToArray(), 0.5) / divisor, suffix, decimals))
            .Append(" | ").Append(Format(Statistics.Percentile(items.Select(item => item.GrowthPerMinute).Order().ToArray(), 0.5) / divisor, suffix, decimals))
            .AppendLine(" |");
    }

    private static void AppendSampleAggregateRow(
        StringBuilder builder,
        string label,
        PerformanceRunResult result,
        string metricName,
        string suffix,
        int decimals,
        double divisor = 1)
    {
        var values = result.Iterations
            .SelectMany(iteration => iteration.Metrics ?? [])
            .Where(metric => metric.Name == metricName && metric.Value.HasValue)
            .Select(metric => (metric.ElapsedSeconds, metric.Value!.Value))
            .ToArray();
        if (values.Length == 0)
        {
            builder.Append("| ").Append(label).AppendLine(" | N/A | N/A | N/A | N/A | N/A |");
            return;
        }

        var statistics = Statistics.Calculate(values);
        AppendAggregateRow(builder, label, [statistics], suffix, decimals, divisor);
    }

    private static void AppendProperty(StringBuilder builder, string name, string value) =>
        builder.Append("| ").Append(Escape(name)).Append(" | ").Append(Escape(value)).AppendLine(" |");

    private static void AppendDiagnostic(StringBuilder builder, DiagnosticArtifact artifact)
    {
        var state = !artifact.Requested ? "Not requested" : artifact.Collected ? "Collected" : "Unavailable";
        builder.Append("- **").Append(Escape(artifact.Name)).Append(":** ").Append(state);
        if (!string.IsNullOrWhiteSpace(artifact.File))
        {
            builder.Append(" (`").Append(EscapeCode(artifact.File)).Append("`)");
        }

        if (!string.IsNullOrWhiteSpace(artifact.Message))
        {
            builder.Append(" — ").Append(Escape(artifact.Message));
        }

        builder.AppendLine();
    }

    private static string Format(double? value, string suffix, int decimals) =>
        value.HasValue ? value.Value.ToString($"F{decimals}", CultureInfo.InvariantCulture) + suffix : "N/A";

    private static string MetricSuffix(string unit) => string.IsNullOrEmpty(unit) ? string.Empty : $" {unit}";

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string EscapeCode(string value) => value.Replace("`", "'", StringComparison.Ordinal);
}

public enum MarkdownReportTarget
{
    DownloadableArtifact,
    GitHubJobSummary
}
