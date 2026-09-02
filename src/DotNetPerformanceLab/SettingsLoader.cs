using System.Globalization;
using System.Text.Json;

namespace DotNetPerformanceLab;

public static class SettingsLoader
{
    public static RunSettings FromEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;

        var target = Require(read, "DPL_TARGET_PATH");
        var workingDirectory = Get(read, "DPL_WORKING_DIRECTORY", Path.GetDirectoryName(target) ?? Environment.CurrentDirectory);
        var outputDirectory = Get(read, "DPL_OUTPUT_DIRECTORY", Path.Combine(Environment.CurrentDirectory, "artifacts", "performance"));
        var arguments = ParseStringArray(read("DPL_ARGUMENTS_JSON"), "DPL_ARGUMENTS_JSON");
        var allowedRoots = ParseStringArray(read("DPL_ALLOWED_ROOTS_JSON"), "DPL_ALLOWED_ROOTS_JSON").ToList();
        var singleAllowedRoot = read("DPL_ALLOWED_ROOT");
        if (!string.IsNullOrWhiteSpace(singleAllowedRoot))
        {
            allowedRoots.Add(singleAllowedRoot.Trim());
        }

        var liveEndpoint = OptionalUri(read("DPL_LIVE_ENDPOINT"), "DPL_LIVE_ENDPOINT");
        return new RunSettings(
            TargetPath: target,
            Arguments: arguments,
            WorkingDirectory: workingDirectory,
            OutputDirectory: outputDirectory,
            ReportLabel: Get(read, "DPL_REPORT_LABEL", Path.GetFileNameWithoutExtension(target)),
            Warmup: Seconds(read, "DPL_WARMUP_SECONDS", 60, 0, 3600),
            Measurement: Seconds(read, "DPL_MEASUREMENT_SECONDS", 300, 5, 86400),
            SampleInterval: Milliseconds(read, "DPL_SAMPLE_INTERVAL_MS", 1000, 100, 60000),
            Cooldown: Seconds(read, "DPL_COOLDOWN_SECONDS", 10, 0, 600),
            Iterations: Integer(read, "DPL_ITERATIONS", 3, 1, 20),
            CollectCounters: Boolean(read, "DPL_COLLECT_COUNTERS", true),
            CounterDuration: Seconds(read, "DPL_COUNTER_DURATION_SECONDS", 30, 5, 3600),
            CollectTrace: Boolean(read, "DPL_COLLECT_TRACE", false),
            TraceDuration: Seconds(read, "DPL_TRACE_DURATION_SECONDS", 30, 5, 3600),
            FailOnTargetExit: Boolean(read, "DPL_FAIL_ON_TARGET_EXIT", true),
            AllowedRoots: allowedRoots,
            ToolDirectory: Get(read, "DPL_TOOL_DIRECTORY", Environment.CurrentDirectory),
            Meters: ParseStringArray(read("DPL_METERS_JSON"), "DPL_METERS_JSON")
                .Where(meter => !string.IsNullOrWhiteSpace(meter))
                .Select(meter => meter.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            LiveEndpoint: liveEndpoint,
            LiveToken: liveEndpoint is null ? null : Require(read, "DPL_LIVE_TOKEN"),
            LiveRunId: liveEndpoint is null ? null : Get(read, "DPL_LIVE_RUN_ID", Guid.NewGuid().ToString("N")));
    }

    private static string Require(Func<string, string?> read, string name) =>
        string.IsNullOrWhiteSpace(read(name))
            ? throw new ArgumentException($"Environment variable {name} is required.")
            : read(name)!.Trim();

    private static string Get(Func<string, string?> read, string name, string fallback) =>
        string.IsNullOrWhiteSpace(read(name)) ? fallback : read(name)!.Trim();

    private static int Integer(Func<string, string?> read, string name, int fallback, int minimum, int maximum)
    {
        var text = read(name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be an integer from {minimum} to {maximum}.");
        }

        return value;
    }

    private static TimeSpan Seconds(Func<string, string?> read, string name, int fallback, int minimum, int maximum) =>
        TimeSpan.FromSeconds(Integer(read, name, fallback, minimum, maximum));

    private static TimeSpan Milliseconds(Func<string, string?> read, string name, int fallback, int minimum, int maximum) =>
        TimeSpan.FromMilliseconds(Integer(read, name, fallback, minimum, maximum));

    private static bool Boolean(Func<string, string?> read, string name, bool fallback)
    {
        var text = read(name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        return bool.TryParse(text, out var value)
            ? value
            : throw new ArgumentException($"{name} must be true or false.", name);
    }

    private static IReadOnlyList<string> ParseStringArray(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(json, LabJsonContext.Default.IReadOnlyListString) ?? [];
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"{name} must be a JSON array of strings.", name, exception);
        }
    }

    private static Uri? OptionalUri(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException($"{name} must be an absolute HTTPS URL.", name);
        }

        return uri;
    }
}
