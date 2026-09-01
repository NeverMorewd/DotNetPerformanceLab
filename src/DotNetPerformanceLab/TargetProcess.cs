using System.Diagnostics;

namespace DotNetPerformanceLab;

public sealed class TargetProcess : IAsyncDisposable
{
    private readonly Process _process;

    private TargetProcess(Process process)
    {
        _process = process;
    }

    public Process Process => _process;

    public static TargetProcess Start(RunSettings settings)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = settings.TargetPath,
            WorkingDirectory = settings.WorkingDirectory,
            UseShellExecute = false
        };

        foreach (var argument in settings.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"Failed to start target: {settings.TargetPath}");
        }

        return new TargetProcess(process);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _process.Dispose();
        }
    }
}
