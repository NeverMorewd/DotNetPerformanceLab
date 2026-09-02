using System.Text.Json;

namespace DotNetPerformanceLab;

public static class RuntimeCounterParser
{
    public static IReadOnlyList<MetricSample> ParseSamples(string path)
    {
        if (!File.Exists(path)) return [];
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!document.RootElement.TryGetProperty("Events", out var events) || events.ValueKind != JsonValueKind.Array) return [];

        var raw = events.EnumerateArray()
            .Select(item => TryReadSample(item, out var sample) ? sample : null)
            .Where(sample => sample is not null)
            .Cast<RawRuntimeSample>()
            .Where(sample => sample.Name.StartsWith("dotnet.", StringComparison.Ordinal) || sample.Provider.Length > 0)
            .OrderBy(sample => sample.TimestampUtc)
            .ToArray();
        if (raw.Length == 0) return [];

        var startedUtc = raw[0].TimestampUtc;
        return raw.Select(sample => new MetricSample(
            0,
            sample.TimestampUtc,
            Math.Max(0, (sample.TimestampUtc - startedUtc).TotalSeconds),
            sample.Provider == "System.Runtime" || sample.Name.StartsWith("dotnet.", StringComparison.Ordinal) ? MetricScope.Runtime : MetricScope.Application,
            DisplayName(sample.Name),
            sample.Value,
            RawUnit(sample.Name),
            ParseTags(sample.Tags),
            MetricAvailability.Available)).ToArray();
    }

    public static IReadOnlyList<RuntimeMetricSummary> Parse(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!document.RootElement.TryGetProperty("Events", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var observations = new Dictionary<(string Name, string Tags), List<double>>();
        foreach (var item in events.EnumerateArray())
        {
            if (!TryRead(item, out var name, out var tags, out var value) ||
                (!name.StartsWith("dotnet.", StringComparison.Ordinal) && !HasProvider(item)))
            {
                continue;
            }

            var key = (name, tags);
            if (!observations.TryGetValue(key, out var values))
            {
                values = [];
                observations.Add(key, values);
            }

            values.Add(value);
        }

        return observations
            .OrderBy(pair => pair.Key.Name, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Tags, StringComparer.Ordinal)
            .Select(pair => new RuntimeMetricSummary(
                DisplayName(pair.Key.Name),
                pair.Key.Tags,
                Unit(pair.Key.Name),
                pair.Value.Average(),
                pair.Value.Max(),
                pair.Value[^1],
                pair.Value.Sum(),
                pair.Value.Count))
            .ToArray();
    }

    private static bool TryRead(JsonElement item, out string name, out string tags, out double value)
    {
        name = string.Empty;
        tags = string.Empty;
        value = 0;
        if (!item.TryGetProperty("name", out var nameElement) ||
            !item.TryGetProperty("value", out var valueElement) ||
            valueElement.ValueKind != JsonValueKind.Number ||
            !valueElement.TryGetDouble(out value))
        {
            return false;
        }

        name = nameElement.GetString() ?? string.Empty;
        if (item.TryGetProperty("tags", out var tagsElement))
        {
            tags = tagsElement.GetString() ?? string.Empty;
        }

        return name.Length > 0;
    }

    private static bool TryReadSample(JsonElement item, out RawRuntimeSample? sample)
    {
        sample = null;
        if (!TryRead(item, out var name, out var tags, out var value) ||
            !item.TryGetProperty("timestamp", out var timestampElement) ||
            !timestampElement.TryGetDateTimeOffset(out var timestamp))
        {
            return false;
        }

        var provider = item.TryGetProperty("provider", out var providerElement) ? providerElement.GetString() ?? string.Empty : string.Empty;
        sample = new RawRuntimeSample(timestamp, provider, name, tags, value);
        return true;
    }

    private static IReadOnlyDictionary<string, string> ParseTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, string>();
        return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && parts[0].Length > 0)
            .GroupBy(parts => parts[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last()[1], StringComparer.Ordinal);
    }

    private static string RawUnit(string name)
    {
        var start = name.IndexOf(" (", StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var end = name.IndexOf(')', start + 2);
        if (end < 0) return string.Empty;
        var unit = name[(start + 2)..end];
        var rate = unit.IndexOf(" / ", StringComparison.Ordinal);
        return rate < 0 ? unit : unit[..rate];
    }

    private static string DisplayName(string name)
    {
        var unitStart = name.IndexOf(" (", StringComparison.Ordinal);
        return unitStart < 0 ? name : name[..unitStart];
    }

    private static string Unit(string name)
    {
        if (name.Contains(" (By", StringComparison.Ordinal))
        {
            return name.Contains("/ 1 sec", StringComparison.Ordinal) ? "MB/s" : "MB";
        }

        if (name.Contains(" (s / 1 sec)", StringComparison.Ordinal))
        {
            return "ms/s";
        }

        return name.Contains("/ 1 sec", StringComparison.Ordinal) ? "/s" : string.Empty;
    }

    private static bool HasProvider(JsonElement item) =>
        item.TryGetProperty("provider", out var provider) && !string.IsNullOrWhiteSpace(provider.GetString());

    private sealed record RawRuntimeSample(DateTimeOffset TimestampUtc, string Provider, string Name, string Tags, double Value);
}
