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

        Directory.CreateDirectory(outputDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false
        };
        Add(startInfo, "publish", projectPath, "--configuration", configuration, "--runtime", runtimeIdentifier,
            "--self-contained", selfContained.ToString().ToLowerInvariant(), "--output", outputDirectory,
            "--locked-mode", $"-p:PublishAot={publishAot}", $"-p:PublishTrimmed={publishTrimmed}");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet publish.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static void Add(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
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
