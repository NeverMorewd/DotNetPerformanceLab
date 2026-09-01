using System.Diagnostics;

namespace DotNetPerformanceLab;

public static class RepositoryPublisher
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var projectPath = Require("DPL_PROJECT_PATH");
        var runtimeIdentifier = Require("DPL_RUNTIME_IDENTIFIER");
        var outputDirectory = Require("DPL_PUBLISH_DIRECTORY");
        var configuration = Get("DPL_CONFIGURATION", "Release");
        var selfContained = GetBoolean("DPL_SELF_CONTAINED", true);
        var publishAot = GetBoolean("DPL_PUBLISH_AOT", false);
        var publishTrimmed = GetBoolean("DPL_PUBLISH_TRIMMED", publishAot);
        var lockedRestore = GetBoolean("DPL_LOCKED_RESTORE", true);

        Directory.CreateDirectory(outputDirectory);
        var publishProperties = new[] { $"-p:PublishAot={publishAot}", $"-p:PublishTrimmed={publishTrimmed}" };
        if (lockedRestore)
        {
            var lockedRestoreExitCode = await RunDotNetAsync(
                ["restore", projectPath, "--locked-mode"],
                cancellationToken).ConfigureAwait(false);
            if (lockedRestoreExitCode != 0)
            {
                return lockedRestoreExitCode;
            }
        }

        var restoreExitCode = await RunDotNetAsync(
            ["restore", projectPath, "--runtime", runtimeIdentifier, .. publishProperties],
            cancellationToken).ConfigureAwait(false);
        if (restoreExitCode != 0)
        {
            return restoreExitCode;
        }

        return await RunDotNetAsync(
            ["publish", projectPath, "--configuration", configuration, "--runtime", runtimeIdentifier,
                "--self-contained", selfContained.ToString().ToLowerInvariant(), "--output", outputDirectory,
                "--no-restore", .. publishProperties],
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> RunDotNetAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo { FileName = "dotnet", UseShellExecute = false };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet CLI.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static string Require(string name) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
            ? throw new ArgumentException($"Environment variable {name} is required.")
            : Environment.GetEnvironmentVariable(name)!.Trim();

    private static string Get(string name, string fallback) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
            ? fallback
            : Environment.GetEnvironmentVariable(name)!.Trim();

    private static bool GetBoolean(string name, bool fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : bool.TryParse(value, out var parsed)
                ? parsed
                : throw new ArgumentException($"{name} must be true or false.", name);
    }
}
