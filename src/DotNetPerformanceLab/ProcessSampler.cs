using System.Diagnostics;
using System.Collections.Frozen;

namespace DotNetPerformanceLab;

public sealed class ProcessSampler
{
    private readonly TimeProvider _timeProvider;
    private readonly IHostMetricCollector _hostMetrics;
    private readonly IProcessIoCollector _processIo;

    public ProcessSampler(TimeProvider? timeProvider = null, IHostMetricCollector? hostMetrics = null, IProcessIoCollector? processIo = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _hostMetrics = hostMetrics ?? HostMetricCollector.Create();
        _processIo = processIo ?? ProcessIoCollector.Create();
    }

    public async Task<IterationResult> RunIterationAsync(
        RunSettings settings,
        int iteration,
        CancellationToken cancellationToken,
        ILiveMetricPublisher? livePublisher = null)
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
        var previousUserCpu = ReadTime(process, static item => item.UserProcessorTime);
        var previousSystemCpu = ReadTime(process, static item => item.PrivilegedProcessorTime);
        var previousElapsed = stopwatch.Elapsed;
        var unexpectedExit = false;

        _hostMetrics.Capture();

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

            var userCpu = ReadTime(process, static item => item.UserProcessorTime);
            var systemCpu = ReadTime(process, static item => item.PrivilegedProcessorTime);
            var userPercent = Percentage(userCpu, previousUserCpu, elapsedDelta);
            var systemPercent = Percentage(systemCpu, previousSystemCpu, elapsedDelta);
            var host = _hostMetrics.Capture();
            var io = _processIo.Capture(process);

            samples.Add(new ProcessSample(
                iteration,
                _timeProvider.GetUtcNow(),
                elapsed.TotalSeconds,
                corePercent,
                corePercent / Environment.ProcessorCount,
                process.WorkingSet64,
                ReadPrivateMemory(process),
                ReadThreadCount(process),
                userPercent,
                systemPercent,
                ReadLong(process, static item => item.VirtualMemorySize64),
                ReadLong(process, static item => item.PeakWorkingSet64),
                ReadLong(process, static item => item.PeakPagedMemorySize64),
                ReadInt(process, static item => item.HandleCount),
                host,
                io.ReadOperationCount,
                io.WriteOperationCount,
                io.ReadBytes,
                io.WriteBytes));
            livePublisher?.TryPublish(CreateMetricSamples([samples[^1]]));

            previousCpu = totalCpu;
            previousUserCpu = userCpu;
            previousSystemCpu = systemCpu;
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
            samplesFile,
            CreateMetricSamples(samples));
    }

    private static IReadOnlyList<MetricSample> CreateMetricSamples(IReadOnlyList<ProcessSample> samples)
    {
        var metrics = new List<MetricSample>(samples.Count * 18);
        foreach (var sample in samples)
        {
            Add(metrics, sample, MetricScope.Process, MetricNames.ProcessCpuCore, sample.CpuCorePercent, "%");
            Add(metrics, sample, MetricScope.Process, MetricNames.ProcessCpuMachine, sample.CpuMachinePercent, "%");
            Add(metrics, sample, MetricScope.Process, MetricNames.ProcessCpuUser, sample.CpuUserPercent, "%");
            Add(metrics, sample, MetricScope.Process, MetricNames.ProcessCpuSystem, sample.CpuSystemPercent, "%");
            Add(metrics, sample, MetricScope.Process, MetricNames.ProcessMemoryWorkingSet, sample.WorkingSetBytes, "By");
            Add(metrics, sample, MetricScope.Process, MetricNames.ProcessMemoryPrivate, sample.PrivateMemoryBytes, "By");
            Add(metrics, sample, MetricScope.Process, MetricNames.ProcessMemoryVirtual, sample.VirtualMemoryBytes, "By");
            Add(metrics, sample, MetricScope.Process, MetricNames.ProcessThreads, sample.ThreadCount, "{thread}");
            Add(metrics, sample, MetricScope.Process, MetricNames.ProcessHandles, sample.HandleCount, "{handle}");
            Add(metrics, sample, MetricScope.Process, MetricNames.ProcessIoReadOperations, sample.ReadOperationCount, "{operation}");
            Add(metrics, sample, MetricScope.Process, MetricNames.ProcessIoWriteOperations, sample.WriteOperationCount, "{operation}");
            Add(metrics, sample, MetricScope.Process, MetricNames.ProcessIoReadBytes, sample.ReadBytes, "By");
            Add(metrics, sample, MetricScope.Process, MetricNames.ProcessIoWriteBytes, sample.WriteBytes, "By");

            if (sample.Host is not { } host)
            {
                continue;
            }

            Add(metrics, sample, MetricScope.Host, MetricNames.HostCpuUsage, host.CpuUsagePercent, "%");
            Add(metrics, sample, MetricScope.Host, MetricNames.HostMemoryTotal, host.TotalMemoryBytes, "By");
            Add(metrics, sample, MetricScope.Host, MetricNames.HostMemoryAvailable, host.AvailableMemoryBytes, "By");
            Add(metrics, sample, MetricScope.Host, MetricNames.HostMemoryUsed,
                host.TotalMemoryBytes.HasValue && host.AvailableMemoryBytes.HasValue ? host.TotalMemoryBytes - host.AvailableMemoryBytes : null, "By");
            Add(metrics, sample, MetricScope.Host, MetricNames.HostSwapTotal, host.TotalSwapBytes, "By");
            Add(metrics, sample, MetricScope.Host, MetricNames.HostSwapUsed, host.UsedSwapBytes, "By");
            Add(metrics, sample, MetricScope.Host, MetricNames.HostNetworkReceive, host.NetworkReceivedBytes, "By");
            Add(metrics, sample, MetricScope.Host, MetricNames.HostNetworkTransmit, host.NetworkTransmittedBytes, "By");
            Add(metrics, sample, MetricScope.Host, MetricNames.HostProcessCount, host.ProcessCount, "{process}");
            Add(metrics, sample, MetricScope.Host, MetricNames.HostLoadOne, host.LoadAverageOneMinute, "1");
            Add(metrics, sample, MetricScope.Host, MetricNames.HostLoadFive, host.LoadAverageFiveMinutes, "1");
            Add(metrics, sample, MetricScope.Host, MetricNames.HostLoadFifteen, host.LoadAverageFifteenMinutes, "1");
        }

        return metrics;
    }

    private static void Add(
        ICollection<MetricSample> metrics,
        ProcessSample sample,
        MetricScope scope,
        string name,
        double? value,
        string unit)
    {
        metrics.Add(new MetricSample(
            sample.Iteration,
            sample.TimestampUtc,
            sample.ElapsedSeconds,
            scope,
            name,
            value,
            unit,
            FrozenDictionary<string, string>.Empty,
            value.HasValue ? MetricAvailability.Available : MetricAvailability.Unavailable,
            value.HasValue ? null : "The metric was unavailable for this sample."));
    }

    private static double? Percentage(TimeSpan? current, TimeSpan? previous, TimeSpan elapsed) =>
        current.HasValue && previous.HasValue && elapsed.TotalMilliseconds > 0
            ? Math.Max(0, (current.Value - previous.Value).TotalMilliseconds / elapsed.TotalMilliseconds * 100)
            : null;

    private static TimeSpan? ReadTime(Process process, Func<Process, TimeSpan> read)
    {
        try
        {
            return read(process);
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static long? ReadLong(Process process, Func<Process, long> read)
    {
        try
        {
            return read(process);
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static int? ReadInt(Process process, Func<Process, int> read)
    {
        try
        {
            return read(process);
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException)
        {
            return null;
        }
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
