using System.Diagnostics;

namespace DotNetPerformanceLab;

public sealed class ProcessSampler
{
    private readonly TimeProvider _timeProvider;

    public ProcessSampler(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IterationResult> RunIterationAsync(
        RunSettings settings,
        int iteration,
        CancellationToken cancellationToken)
    {
        await using var target = TargetProcess.Start(settings);
        var process = target.Process;
        var startedUtc = _timeProvider.GetUtcNow();

        await DelayWhileRunningAsync(process, settings.Warmup, cancellationToken).ConfigureAwait(false);
        if (process.HasExited)
        {
            throw new InvalidOperationException($"Target exited during warm-up with code {process.ExitCode}.");
        }

        var measurementStarted = _timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        var samples = new List<ProcessSample>();
        var previousCpu = process.TotalProcessorTime;
        var previousElapsed = stopwatch.Elapsed;
        var unexpectedExit = false;

        while (stopwatch.Elapsed < settings.Measurement)
        {
            await Task.Delay(settings.SampleInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
            if (process.HasExited)
            {
                unexpectedExit = true;
                break;
            }

            process.Refresh();
            var elapsed = stopwatch.Elapsed;
            var totalCpu = process.TotalProcessorTime;
            var elapsedDelta = elapsed - previousElapsed;
            var cpuDelta = totalCpu - previousCpu;
            double? corePercent = elapsedDelta.TotalMilliseconds <= 0
                ? null
                : Math.Max(0, cpuDelta.TotalMilliseconds / elapsedDelta.TotalMilliseconds * 100);

            samples.Add(new ProcessSample(
                iteration,
                _timeProvider.GetUtcNow(),
                elapsed.TotalSeconds,
                corePercent,
                corePercent / Environment.ProcessorCount,
                process.WorkingSet64,
                ReadPrivateMemory(process),
                ReadThreadCount(process)));

            previousCpu = totalCpu;
            previousElapsed = elapsed;
        }

        var measurementEnded = _timeProvider.GetUtcNow();
        int? exitCode = process.HasExited ? process.ExitCode : null;
        if (samples.Count == 0)
        {
            throw new InvalidOperationException("The target produced no performance samples.");
        }

        Directory.CreateDirectory(settings.OutputDirectory);
        var samplesFile = Path.Combine(settings.OutputDirectory, $"samples-iteration-{iteration}.csv");
        await CsvReport.WriteSamplesAsync(samplesFile, samples, cancellationToken).ConfigureAwait(false);

        var result = CreateResult(
            iteration,
            process.Id,
            startedUtc,
            measurementStarted,
            measurementEnded,
            exitCode,
            unexpectedExit,
            samples,
            Path.GetFileName(samplesFile));

        if (unexpectedExit && settings.FailOnTargetExit)
        {
            throw new InvalidOperationException($"Target exited during measurement with code {exitCode}.");
        }

        return result;
    }

    private static IterationResult CreateResult(
        int iteration,
        int processId,
        DateTimeOffset startedUtc,
        DateTimeOffset measurementStarted,
        DateTimeOffset measurementEnded,
        int? exitCode,
        bool unexpectedExit,
        IReadOnlyList<ProcessSample> samples,
        string samplesFile)
    {
        var cpuCore = samples.Where(sample => sample.CpuCorePercent.HasValue)
            .Select(sample => (sample.ElapsedSeconds, sample.CpuCorePercent!.Value)).ToArray();
        var cpuMachine = samples.Where(sample => sample.CpuMachinePercent.HasValue)
            .Select(sample => (sample.ElapsedSeconds, sample.CpuMachinePercent!.Value)).ToArray();
        var workingSet = samples.Select(sample => (sample.ElapsedSeconds, (double)sample.WorkingSetBytes)).ToArray();
        var privateMemory = samples.Where(sample => sample.PrivateMemoryBytes.HasValue)
            .Select(sample => (sample.ElapsedSeconds, (double)sample.PrivateMemoryBytes!.Value)).ToArray();

        return new IterationResult(
            iteration,
            processId,
            startedUtc,
            measurementStarted,
            measurementEnded,
            exitCode,
            unexpectedExit,
            samples,
            cpuCore.Length == 0 ? null : Statistics.Calculate(cpuCore),
            cpuMachine.Length == 0 ? null : Statistics.Calculate(cpuMachine),
            Statistics.Calculate(workingSet),
            privateMemory.Length == 0 ? null : Statistics.Calculate(privateMemory),
            samplesFile);
    }

    private async Task DelayWhileRunningAsync(Process process, TimeSpan duration, CancellationToken cancellationToken)
    {
        var deadline = _timeProvider.GetUtcNow() + duration;
        while (_timeProvider.GetUtcNow() < deadline)
        {
            if (process.HasExited)
            {
                return;
            }

            var remaining = deadline - _timeProvider.GetUtcNow();
            await Task.Delay(remaining > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remaining, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static long? ReadPrivateMemory(Process process)
    {
        try
        {
            return process.PrivateMemorySize64;
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static int? ReadThreadCount(Process process)
    {
        try
        {
            return process.Threads.Count;
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException)
        {
            return null;
        }
    }
}
