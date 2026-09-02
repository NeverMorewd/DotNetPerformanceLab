using System.Text.Json;

namespace DotNetPerformanceLab;

public static class RuntimeCounterParser
{
    private static readonly string[] IncludedMetrics =
    [
        "dotnet.gc.heap.total_allocated",
        "dotnet.gc.last_collection.heap.size",
        "dotnet.gc.last_collection.memory.committed_size",
        "dotnet.gc.heap.fragmentation.size",
        "dotnet.gc.pause.time",
        "dotnet.gc.collections",
        "dotnet.thread_pool.thread.count",
        "dotnet.thread_pool.queue.length",
        "dotnet.thread_pool.work_item.count",
        "dotnet.monitor.lock_contentions",
        "dotnet.exception.count",
        "dotnet.timer.count",
        "dotnet.jit.compilation.time",
        "dotnet.jit.compiled_methods",
        "dotnet.jit.compiled_il.size",
        "dotnet.process.cpu.time",
        "dotnet.process.memory.working_set",
        "dotnet.assembly.count"
    ];

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
                !IncludedMetrics.Any(metric => name.StartsWith(metric, StringComparison.Ordinal)))
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
}
