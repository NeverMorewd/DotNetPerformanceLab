using System.Text;
using System.Text.Json;

namespace DotNetPerformanceLab;

public static class PlotlyReportExporter
{
    private const string PlotlyPackage = "plotly.js-basic-dist-min";

    public static async Task WriteAsync(
        string outputDirectory,
        PerformanceRunResult result,
        IReadOnlyList<MetricSample> metrics,
        CancellationToken cancellationToken,
        string? assetSourceDirectory = null)
    {
        var sourceDirectory = ResolveAssetSource(assetSourceDirectory);
        var siteDirectory = Path.Combine(outputDirectory, "web-report");
        var assetDirectory = Path.Combine(siteDirectory, "assets");
        var licenseDirectory = Path.Combine(assetDirectory, "licenses");
        Directory.CreateDirectory(licenseDirectory);

        File.Copy(Path.Combine(sourceDirectory, "dashboard.js"), Path.Combine(assetDirectory, "dashboard.js"));
        File.Copy(Path.Combine(sourceDirectory, "dashboard.css"), Path.Combine(assetDirectory, "dashboard.css"));
        File.Copy(
            Path.Combine(sourceDirectory, "node_modules", PlotlyPackage, "plotly-basic.min.js"),
            Path.Combine(assetDirectory, "plotly-basic.min.js"));
        File.Copy(
            Path.Combine(sourceDirectory, "node_modules", PlotlyPackage, "LICENSE"),
            Path.Combine(licenseDirectory, "plotly-js.txt"));

        var payload = new WebReportPayload(
            result.ReportLabel,
            result.GeneratedUtc,
            new WebEnvironmentPayload(
                result.Environment.OperatingSystem,
                result.Environment.Framework,
                result.Environment.ProcessArchitecture,
                result.Environment.LogicalProcessors,
                result.Environment.RuntimeIdentifier,
                result.Environment.KernelVersion,
                result.Environment.ProcessorModel,
                result.Environment.PhysicalCores,
                result.Environment.TotalMemoryBytes,
                result.Environment.TotalSwapBytes,
                result.Environment.Containerized),
            result.Settings,
            result.Counters,
            result.Trace,
            metrics);
        var json = JsonSerializer.Serialize(payload, LabJsonContext.Default.WebReportPayload);
        await File.WriteAllTextAsync(
            Path.Combine(assetDirectory, "data.js"),
            $"globalThis.DPL_REPORT = {json};{Environment.NewLine}",
            new UTF8Encoding(false),
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(siteDirectory, "index.html"),
            Html,
            new UTF8Encoding(false),
            cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveAssetSource(string? assetSourceDirectory)
    {
        var configured = Environment.GetEnvironmentVariable("DPL_WEB_ASSETS_DIRECTORY");
        var directory = !string.IsNullOrWhiteSpace(assetSourceDirectory)
            ? Path.GetFullPath(assetSourceDirectory)
            : string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.CurrentDirectory, "web")
                : Path.GetFullPath(configured);
        var required = new[]
        {
            Path.Combine(directory, "dashboard.js"),
            Path.Combine(directory, "dashboard.css"),
            Path.Combine(directory, "node_modules", PlotlyPackage, "plotly-basic.min.js"),
            Path.Combine(directory, "node_modules", PlotlyPackage, "LICENSE")
        };
        var missing = required.FirstOrDefault(path => !File.Exists(path));
        return missing is null
            ? directory
            : throw new InvalidOperationException(
                $"Plotly report assets are missing: {missing}. Run 'npm ci --prefix web --ignore-scripts' before analysis.");
    }

    private const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="color-scheme" content="dark">
          <title>.NET Performance Analysis</title>
          <link rel="stylesheet" href="assets/dashboard.css">
        </head>
        <body>
          <header class="page-header">
            <div class="eyebrow">.NET PERFORMANCE LAB</div>
            <h1 id="target">.NET Performance Analysis</h1>
            <p class="subtitle">Explore synchronized process, host, runtime, and application telemetry.</p>
          </header>
          <main>
            <section class="summary" aria-label="Report summary">
              <article class="card"><div class="label">Generated</div><div class="value" id="generated">—</div></article>
              <article class="card"><div class="label">Environment</div><div class="value" id="environment">—</div></article>
              <article class="card"><div class="label">Baseline</div><div class="value" id="baseline">—</div></article>
              <article class="card"><div class="label">Runtime diagnostics</div><div class="value" id="diagnostics">—</div></article>
            </section>
            <section class="workspace" aria-label="Interactive metric explorer">
              <nav id="scopes" class="scope-tabs" aria-label="Metric scope"></nav>
              <div class="toolbar" aria-label="Chart controls">
                <label for="metric">Metric</label>
                <select id="metric"></select>
              </div>
              <div class="chart-heading">
                <div>
                  <div class="label" id="scope-label">Metric</div>
                  <h2 id="metric-title">—</h2>
                  <p id="metric-code" class="metric-code"></p>
                </div>
                <div class="selection-status" id="status">—</div>
              </div>
              <div class="runtime-note" id="runtime-note" hidden>
                Runtime counters are collected in a separate diagnostic pass. Zero is a valid observation; JIT metrics are expected to remain zero for Native AOT applications.
              </div>
              <div class="panel"><div id="chart" role="img" aria-label="Selected performance metric chart"></div></div>
            </section>
            <footer>Generated by DotNetPerformanceLab. Plotly.js is distributed under the MIT License.</footer>
          </main>
          <script src="assets/plotly-basic.min.js"></script>
          <script src="assets/data.js"></script>
          <script src="assets/dashboard.js"></script>
        </body>
        </html>
        """;
}

public sealed record WebReportPayload(
    string Target,
    DateTimeOffset GeneratedUtc,
    WebEnvironmentPayload Environment,
    RunSettingsSnapshot Settings,
    DiagnosticArtifact Counters,
    DiagnosticArtifact Trace,
    IReadOnlyList<MetricSample> Metrics);

public sealed record WebEnvironmentPayload(
    string OperatingSystem,
    string Framework,
    string ProcessArchitecture,
    int LogicalProcessors,
    string? RuntimeIdentifier,
    string? KernelVersion,
    string? ProcessorModel,
    int? PhysicalCores,
    long? TotalMemoryBytes,
    long? TotalSwapBytes,
    bool? Containerized);
