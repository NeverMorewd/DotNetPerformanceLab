using System.Diagnostics;

namespace DotNetPerformanceLab;

public sealed class DiagnosticCollector
{
    public async Task<(DiagnosticArtifact Counters, DiagnosticArtifact Trace)> CollectAsync(
        RunSettings settings,
        CancellationToken cancellationToken)
    {
        var counters = settings.CollectCounters
            ? await CollectOneAsync(settings, "Runtime counters", "dotnet-counters", settings.CounterDuration, "runtime-counters.json", BuildCountersArguments, cancellationToken).ConfigureAwait(false)
            : new DiagnosticArtifact("Runtime counters", false, false, null, null);

        var trace = settings.CollectTrace
            ? await CollectOneAsync(settings, "EventPipe trace", "dotnet-trace", settings.TraceDuration, "runtime.nettrace", BuildTraceArguments, cancellationToken).ConfigureAwait(false)
            : new DiagnosticArtifact("EventPipe trace", false, false, null, null);

        return (counters, trace);
    }

    private static async Task<DiagnosticArtifact> CollectOneAsync(
        RunSettings settings,
        string displayName,
        string toolName,
        TimeSpan duration,
        string outputFileName,
        Func<int, TimeSpan, string, IReadOnlyList<string>> argumentsFactory,
        CancellationToken cancellationToken)
    {
        await using var target = TargetProcess.Start(settings);
        await DelayWhileRunningAsync(target.Process, settings.Warmup, cancellationToken).ConfigureAwait(false);
        if (target.Process.HasExited)
        {
            return new DiagnosticArtifact(displayName, true, false, null, $"Target exited during diagnostic warm-up with code {target.Process.ExitCode}.");
        }

        Directory.CreateDirectory(settings.OutputDirectory);
        var outputPath = Path.Combine(settings.OutputDirectory, outputFileName);
        var logPath = Path.Combine(settings.OutputDirectory, $"{toolName}.log");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = settings.ToolDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("tool");
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(toolName);
        startInfo.ArgumentList.Add("--");
        foreach (var argument in argumentsFactory(target.Process.Id, duration, outputPath))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var tool = new Process { StartInfo = startInfo };
        try
        {
            tool.Start();
            var stdout = tool.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = tool.StandardError.ReadToEndAsync(cancellationToken);
            await tool.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var log = (await stdout.ConfigureAwait(false)) + Environment.NewLine + (await stderr.ConfigureAwait(false));
            await File.WriteAllTextAsync(logPath, log, cancellationToken).ConfigureAwait(false);

            if (tool.ExitCode != 0 || !File.Exists(outputPath))
            {
                return new DiagnosticArtifact(displayName, true, false, Path.GetFileName(logPath), SummarizeFailure(log, tool.ExitCode));
            }

            return new DiagnosticArtifact(displayName, true, true, Path.GetFileName(outputPath), null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new DiagnosticArtifact(displayName, true, false, null, exception.Message);
        }
    }

    private static IReadOnlyList<string> BuildCountersArguments(int processId, TimeSpan duration, string outputPath) =>
    [
        "collect",
        "--process-id", processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--duration", Duration(duration),
        "--refresh-interval", "1",
        "--format", "json",
        "--output", outputPath,
        "--counters", "System.Runtime"
    ];

    private static IReadOnlyList<string> BuildTraceArguments(int processId, TimeSpan duration, string outputPath) =>
    [
        "collect",
        "--process-id", processId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--duration", Duration(duration),
        "--profile", "dotnet-common,dotnet-sampled-thread-time",
        "--output", outputPath
    ];

    private static string Duration(TimeSpan duration) => duration.ToString(@"dd\:hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task DelayWhileRunningAsync(Process process, TimeSpan duration, CancellationToken cancellationToken)
    {
        var delay = Task.Delay(duration, cancellationToken);
        var exit = process.WaitForExitAsync(cancellationToken);
        await Task.WhenAny(delay, exit).ConfigureAwait(false);
    }

    private static string SummarizeFailure(string log, int exitCode)
    {
        var line = log.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        return string.IsNullOrWhiteSpace(line) ? $"Diagnostic tool exited with code {exitCode}." : line;
    }
}
