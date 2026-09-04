using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DotNetPerformanceLab;

public static class ComparisonRunner
{
    private const int MaximumSources = 12;
    private const int MaximumSourceBytes = 16 * 1024 * 1024;

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var sourcesJson = Require("DPL_COMPARISON_SOURCES_JSON");
        var outputDirectory = Path.GetFullPath(Require("DPL_COMPARISON_OUTPUT_DIRECTORY"));
        var title = Environment.GetEnvironmentVariable("DPL_COMPARISON_TITLE")?.Trim();
        var sources = JsonSerializer.Deserialize(sourcesJson, LabJsonContext.Default.IReadOnlyListComparisonSource)
            ?? throw new InvalidDataException("Comparison sources JSON is invalid.");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        return await BuildAsync(client, sources, outputDirectory, string.IsNullOrWhiteSpace(title) ? ".NET Performance Comparison" : title, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<int> BuildAsync(HttpClient client, IReadOnlyList<ComparisonSource> sources, string outputDirectory, string title, CancellationToken cancellationToken)
    {
        ValidateSources(sources);
        PrepareOutputDirectory(outputDirectory);
        var reports = new List<(ComparisonSource Source, ComparisonData Data)>(sources.Count);
        foreach (var source in sources)
        {
            using var response = await client.GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumSourceBytes) throw new InvalidDataException($"Comparison source '{source.Label}' exceeds {MaximumSourceBytes} bytes.");
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var bounded = new MemoryStream();
            await responseStream.CopyToAsync(bounded, cancellationToken).ConfigureAwait(false);
            if (bounded.Length > MaximumSourceBytes) throw new InvalidDataException($"Comparison source '{source.Label}' exceeds {MaximumSourceBytes} bytes.");
            bounded.Position = 0;
            var data = await JsonSerializer.DeserializeAsync(bounded, LabJsonContext.Default.ComparisonData, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException($"Comparison source '{source.Label}' is invalid.");
            if (data.SchemaVersion != 1) throw new InvalidDataException($"Comparison source '{source.Label}' uses unsupported schema version {data.SchemaVersion}.");
            reports.Add((source, data));
        }

        var summary = new ComparisonReportSummary(1, title, DateTimeOffset.UtcNow, sources.Select(source => source.Label).ToArray(), BuildRows(reports).Count);
        Directory.CreateDirectory(Path.Combine(outputDirectory, "web-report"));
        await WriteJsonAsync(Path.Combine(outputDirectory, "comparison-summary.json"), summary, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "comparison.md"), BuildMarkdown(title, reports), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "job-summary.md"), BuildJobSummary(title, reports), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "web-report", "index.html"), BuildHtml(title, reports), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static IReadOnlyList<ComparisonRow> BuildRows(IReadOnlyList<(ComparisonSource Source, ComparisonData Data)> reports)
    {
        var keys = reports.SelectMany(report => report.Data.Metrics)
            .Select(metric => new MetricKey(metric.Scope, metric.Name, metric.Tags, metric.Unit))
            .Distinct().OrderBy(key => key.Scope).ThenBy(key => key.Name, StringComparer.Ordinal).ThenBy(key => key.Tags, StringComparer.Ordinal);
        return keys.Select(key =>
        {
            var values = reports.Select(report => report.Data.Metrics.FirstOrDefault(metric => metric.Scope == key.Scope && metric.Name == key.Name && metric.Tags == key.Tags && metric.Unit == key.Unit)).ToArray();
            var directions = values.Where(value => value is not null).Select(value => value!.Direction).Distinct().ToArray();
            return new ComparisonRow(key, directions.Length == 1 ? directions[0] : MetricOptimizationDirection.Neutral, values);
        }).ToArray();
    }

    private static string BuildMarkdown(string title, IReadOnlyList<(ComparisonSource Source, ComparisonData Data)> reports)
    {
        var builder = new StringBuilder().Append("# ").Append(EscapeMarkdown(title)).AppendLine().AppendLine();
        builder.AppendLine("Values are minimum / mean / maximum / P95 / final. 🔴 marks the best comparable value; neutral metrics are never ranked.").AppendLine();
        builder.Append("| Scope | Metric | Direction | Unit |");
        foreach (var report in reports) builder.Append(' ').Append(EscapeMarkdown(report.Source.Label)).Append(" |");
        builder.AppendLine().Append("|---|---|---|---|");
        foreach (var _ in reports) builder.Append("---|");
        builder.AppendLine();
        foreach (var row in BuildRows(reports))
        {
            builder.Append("| ").Append(row.Key.Scope).Append(" | ").Append(EscapeMarkdown(DisplayMetric(row.Key))).Append(" | ").Append(row.Direction).Append(" | ").Append(EscapeMarkdown(row.Key.Unit)).Append(" |");
            for (var index = 0; index < reports.Count; index++) builder.Append(' ').Append(MarkdownStatistics(row, index)).Append(" |");
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string BuildJobSummary(string title, IReadOnlyList<(ComparisonSource Source, ComparisonData Data)> reports)
    {
        var url = Environment.GetEnvironmentVariable("DPL_COMPARISON_REPORT_URL");
        var builder = new StringBuilder().Append("# ").Append(EscapeMarkdown(title)).AppendLine().AppendLine();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) builder.Append("[Open the comparison dashboard](").Append(uri.AbsoluteUri).AppendLine(")").AppendLine();
        builder.Append("Compared ").Append(reports.Count).Append(" reports across ").Append(BuildRows(reports).Count).AppendLine(" compatible metric series.").AppendLine();
        foreach (var report in reports) builder.Append("- **").Append(EscapeMarkdown(report.Source.Label)).Append(":** ").Append(EscapeMarkdown(report.Data.Label)).Append(" · ").Append(report.Data.OperatingSystem).Append(" · ").Append(report.Data.GeneratedUtc.ToString("u", CultureInfo.InvariantCulture)).AppendLine();
        builder.AppendLine().AppendLine("The downloadable artifact contains the complete single-table comparison and offline HTML report.");
        return builder.ToString();
    }

    private static string BuildHtml(string title, IReadOnlyList<(ComparisonSource Source, ComparisonData Data)> reports)
    {
        var rows = new StringBuilder();
        foreach (var row in BuildRows(reports))
        {
            rows.Append("<tr><th><span class=scope>").Append(Html(row.Key.Scope.ToString())).Append("</span><strong>").Append(Html(DisplayMetric(row.Key))).Append("</strong><code>").Append(Html(row.Key.Name)).Append("</code></th><td>").Append(row.Direction).Append("</td><td>").Append(Html(row.Key.Unit)).Append("</td>");
            for (var index = 0; index < reports.Count; index++) rows.Append("<td>").Append(HtmlStatistics(row, index)).Append("</td>");
            rows.AppendLine("</tr>");
        }
        var headers = string.Concat(reports.Select(report => $"<th>{Html(report.Source.Label)}<small>{Html(report.Data.OperatingSystem)} · {Html(report.Data.Architecture)}</small></th>"));
        return $$$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="dark"><title>{{{Html(title)}}}</title>
        <style>:root{font-family:Inter,system-ui,sans-serif;color:#edf4ff;background:#080d16}*{box-sizing:border-box}body{margin:0}main{width:min(1800px,calc(100% - 32px));margin:auto;padding:36px 0}h1{font-size:clamp(26px,3vw,42px);margin:0}.intro{color:#91a5c5;margin:8px 0 24px}.table-wrap{overflow:auto;border:1px solid #223451;border-radius:14px;background:#0d1625}table{border-collapse:separate;border-spacing:0;width:100%;min-width:1000px}th,td{padding:12px 14px;text-align:left;vertical-align:top;border:0;border-bottom:1px solid #1d2c46}thead th{position:sticky;top:0;background:#111d30;color:#93a9ca;z-index:2;font-size:12px;text-transform:uppercase;letter-spacing:.05em}tbody th{min-width:290px}tbody tr:last-child>*{border-bottom:0}strong,code,small{display:block}code{color:#7890b6;font-size:11px;margin-top:4px}.scope{color:#65c8ff;font-size:10px;text-transform:uppercase}.stat{display:grid;grid-template-columns:44px 1fr;gap:3px 8px;min-width:140px;font-variant-numeric:tabular-nums}.stat span:nth-child(odd){color:#7890b6;font-size:11px}.best{color:#ff5e6c;font-weight:750}.best:before{content:'● ';font-size:9px}small{color:#7890b6;margin-top:4px;text-transform:none;letter-spacing:0}@media(max-width:700px){main{width:calc(100% - 20px);padding:22px 0}th,td{padding:10px}}</style></head>
        <body><main><h1>{{{Html(title)}}}</h1><p class="intro">Minimum, mean, maximum, P95 and final values for every compatible metric series. Red dots identify the best value where direction is meaningful.</p><div class="table-wrap"><table><thead><tr><th>Metric</th><th>Better</th><th>Unit</th>{{{headers}}}</tr></thead><tbody>{{{rows}}}</tbody></table></div></main></body></html>
        """;
    }

    private static string MarkdownStatistics(ComparisonRow row, int index)
    {
        var metric = row.Values[index];
        if (metric is null) return "—";
        var values = GetValues(metric.Statistics);
        return string.Join(" / ", values.Select((value, statistic) => $"{(IsBest(row, index, statistic) ? "🔴 " : string.Empty)}{Format(value)}"));
    }

    private static string HtmlStatistics(ComparisonRow row, int index)
    {
        var metric = row.Values[index];
        if (metric is null) return "—";
        var labels = new[] { "Min", "Mean", "Max", "P95", "Final" };
        var values = GetValues(metric.Statistics);
        return "<div class=stat>" + string.Concat(values.Select((value, statistic) => $"<span>{labels[statistic]}</span><span class=\"{(IsBest(row, index, statistic) ? "best" : string.Empty)}\">{Format(value)}</span>")) + "</div>";
    }

    private static double[] GetValues(MetricStatistics statistics) => [statistics.Minimum, statistics.Mean, statistics.Maximum, statistics.Percentile95, statistics.Final];

    private static bool IsBest(ComparisonRow row, int index, int statistic)
    {
        if (row.Direction == MetricOptimizationDirection.Neutral || row.Values[index] is null) return false;
        var values = row.Values.Select(value => value is null ? (double?)null : GetValues(value.Statistics)[statistic]).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        if (values.Length < 2) return false;
        var candidate = GetValues(row.Values[index]!.Statistics)[statistic];
        var best = row.Direction == MetricOptimizationDirection.Lower ? values.Min() : values.Max();
        return Math.Abs(candidate - best) <= Math.Max(1e-9, Math.Abs(best) * 1e-9);
    }

    private static string DisplayMetric(MetricKey key) => string.IsNullOrWhiteSpace(key.Tags) ? key.Name : $"{key.Name} [{key.Tags}]";
    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Html(string value) => WebUtility.HtmlEncode(value);
    private static string EscapeMarkdown(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
    private static string Require(string name) => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)) ? throw new ArgumentException($"Environment variable {name} is required.") : Environment.GetEnvironmentVariable(name)!.Trim();
    private static async Task WriteJsonAsync(string path, ComparisonReportSummary value, CancellationToken token) { await using var stream = File.Create(path); await JsonSerializer.SerializeAsync(stream, value, LabJsonContext.Default.ComparisonReportSummary, token).ConfigureAwait(false); }
    private static void PrepareOutputDirectory(string path) { if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()) throw new InvalidOperationException($"The comparison output directory must be empty: {path}"); Directory.CreateDirectory(path); }
    private static void ValidateSources(IReadOnlyList<ComparisonSource> sources)
    {
        if (sources.Count is < 2 or > MaximumSources) throw new ArgumentOutOfRangeException(nameof(sources), $"Provide between 2 and {MaximumSources} comparison sources.");
        if (sources.Any(source => string.IsNullOrWhiteSpace(source.Label) || source.Url.Scheme != Uri.UriSchemeHttps)) throw new ArgumentException("Every comparison source requires a label and an HTTPS URL.", nameof(sources));
        if (sources.Select(source => source.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count) throw new ArgumentException("Comparison source labels must be unique.", nameof(sources));
    }

    private sealed record MetricKey(MetricScope Scope, string Name, string Tags, string Unit);
    private sealed record ComparisonRow(MetricKey Key, MetricOptimizationDirection Direction, IReadOnlyList<ComparisonMetricData?> Values);
}
