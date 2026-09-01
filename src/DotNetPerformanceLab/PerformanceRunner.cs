using System.Runtime.InteropServices;
using System.Text.Json;

namespace DotNetPerformanceLab;

public sealed class PerformanceRunner
{
    private readonly ProcessSampler _sampler;
    private readonly DiagnosticCollector _diagnostics;

    public PerformanceRunner(ProcessSampler? sampler = null, DiagnosticCollector? diagnostics = null)
    {
        _sampler = sampler ?? new ProcessSampler();
        _diagnostics = diagnostics ?? new DiagnosticCollector();
    }

    public async Task<PerformanceRunResult> RunAsync(RunSettings settings, CancellationToken cancellationToken)
    {
        settings = TargetValidator.Validate(settings);
        PrepareOutputDirectory(settings.OutputDirectory);
        var iterations = new List<IterationResult>(settings.Iterations);

        for (var iteration = 1; iteration <= settings.Iterations; iteration++)
        {
            Console.WriteLine($"Starting baseline iteration {iteration} of {settings.Iterations}.");
            iterations.Add(await _sampler.RunIterationAsync(settings, iteration, cancellationToken).ConfigureAwait(false));

            if (iteration < settings.Iterations && settings.Cooldown > TimeSpan.Zero)
            {
                await Task.Delay(settings.Cooldown, cancellationToken).ConfigureAwait(false);
            }
        }

        var diagnostics = await _diagnostics.CollectAsync(settings, cancellationToken).ConfigureAwait(false);
        var runtimeMetrics = diagnostics.Counters.Collected && diagnostics.Counters.File is not null
            ? RuntimeCounterParser.Parse(Path.Combine(settings.OutputDirectory, diagnostics.Counters.File))
            : [];
        var result = new PerformanceRunResult(
            SchemaVersion: 1,
            TargetPath: settings.TargetPath,
            ReportLabel: settings.ReportLabel,
            Settings: new RunSettingsSnapshot(
                (int)settings.Warmup.TotalSeconds,
                (int)settings.Measurement.TotalSeconds,
                (int)settings.SampleInterval.TotalMilliseconds,
                (int)settings.Cooldown.TotalSeconds,
                settings.Iterations,
                settings.CollectCounters,
                (int)settings.CounterDuration.TotalSeconds,
                settings.CollectTrace,
                (int)settings.TraceDuration.TotalSeconds),
            Environment: CaptureEnvironment(),
            Iterations: iterations,
            Counters: diagnostics.Counters,
            Trace: diagnostics.Trace,
            RuntimeMetrics: runtimeMetrics,
            GeneratedUtc: DateTimeOffset.UtcNow);

        await WriteOutputsAsync(settings.OutputDirectory, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static void PrepareOutputDirectory(string outputDirectory)
    {
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }

        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(Path.Combine(outputDirectory, "charts"));
    }

    private static async Task WriteOutputsAsync(string outputDirectory, PerformanceRunResult result, CancellationToken cancellationToken)
    {
        var jsonPath = Path.Combine(outputDirectory, "summary.json");
        await using (var stream = File.Create(jsonPath))
        {
            await JsonSerializer.SerializeAsync(stream, result, LabJsonContext.Default.PerformanceRunResult, cancellationToken).ConfigureAwait(false);
        }

        await MarkdownReport.WriteAsync(Path.Combine(outputDirectory, "report.md"), result, cancellationToken).ConfigureAwait(false);
        var chartSamples = CreateChartTimeline(result.Iterations);
        var chartDirectory = Path.Combine(outputDirectory, "charts");
        await SvgChart.WriteAsync(Path.Combine(chartDirectory, "cpu.svg"), "CPU core equivalent", "%", chartSamples, sample => sample.CpuCorePercent, cancellationToken).ConfigureAwait(false);
        await SvgChart.WriteAsync(Path.Combine(chartDirectory, "working-set.svg"), "Working set", "MB", chartSamples, sample => sample.WorkingSetBytes / 1024d / 1024d, cancellationToken).ConfigureAwait(false);
        await SvgChart.WriteAsync(Path.Combine(chartDirectory, "private-memory.svg"), "Private memory", "MB", chartSamples, sample => sample.PrivateMemoryBytes / 1024d / 1024d, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<ProcessSample> CreateChartTimeline(IReadOnlyList<IterationResult> iterations)
    {
        var samples = new List<ProcessSample>();
        var offset = 0d;
        foreach (var iteration in iterations)
        {
            foreach (var sample in iteration.Samples)
            {
                samples.Add(sample with { ElapsedSeconds = sample.ElapsedSeconds + offset });
            }

            if (iteration.Samples.Count > 0)
            {
                offset = samples[^1].ElapsedSeconds + 1;
            }
        }

        return samples;
    }

    private static EnvironmentSnapshot CaptureEnvironment() => new(
        RuntimeInformation.OSDescription,
        RuntimeInformation.FrameworkDescription,
        RuntimeInformation.OSArchitecture.ToString(),
        RuntimeInformation.ProcessArchitecture.ToString(),
        Environment.ProcessorCount,
        Environment.MachineName,
        Environment.GetEnvironmentVariable("RUNNER_NAME") ?? "Local",
        Environment.GetEnvironmentVariable("RUNNER_OS") ?? RuntimeInformation.OSDescription,
        Environment.GetEnvironmentVariable("RUNNER_ARCH") ?? RuntimeInformation.OSArchitecture.ToString(),
        DateTimeOffset.UtcNow);
}
