namespace DotNetPerformanceLab.Tests;

public sealed class RuntimeCounterParserTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"dpl-counters-{Guid.NewGuid():N}.json");

    [Fact]
    public void ParseSummarizesIncludedRuntimeMetrics()
    {
        File.WriteAllText(_path, """
            {
              "Events": [
                { "name": "dotnet.gc.heap.total_allocated (By / 1 sec)", "tags": "", "value": 1048576 },
                { "name": "dotnet.gc.heap.total_allocated (By / 1 sec)", "tags": "", "value": 2097152 },
                { "name": "dotnet.process.memory.working_set (By)", "tags": "", "value": 999 },
                { "name": "custom.unconfigured.metric", "tags": "", "value": 1 }
              ]
            }
            """);

        var result = RuntimeCounterParser.Parse(_path);

        Assert.Equal(2, result.Count);
        var metric = Assert.Single(result, item => item.Name == "dotnet.gc.heap.total_allocated");
        Assert.Equal("dotnet.gc.heap.total_allocated", metric.Name);
        Assert.Equal("MB/s", metric.Unit);
        Assert.Equal(1572864, metric.Mean);
        Assert.Equal(2, metric.Samples);
        Assert.Contains(result, item => item.Name == "dotnet.process.memory.working_set" && item.Unit == "MB");
    }

    [Fact]
    public void ParseSamplesPreservesTimestampTagsUnitAndAllRuntimeMetrics()
    {
        File.WriteAllText(_path, """
            {
              "Events": [
                { "timestamp": "2026-09-01T12:00:00Z", "name": "dotnet.gc.last_collection.heap.size (By)", "tags": "gc.heap.generation=loh", "value": 1024 },
                { "timestamp": "2026-09-01T12:00:01Z", "name": "dotnet.process.cpu.count ({cpu})", "tags": "", "value": 8 },
                { "timestamp": "2026-09-01T12:00:01Z", "name": "custom.metric (1)", "tags": "", "value": 4 }
              ]
            }
            """);

        var result = RuntimeCounterParser.ParseSamples(_path);

        Assert.Equal(2, result.Count);
        Assert.All(result, sample => Assert.Equal(MetricScope.Runtime, sample.Scope));
        Assert.Equal("By", result[0].Unit);
        Assert.Equal("loh", result[0].Tags["gc.heap.generation"]);
        Assert.Equal(1, result[1].ElapsedSeconds);
    }

    public void Dispose()
    {
        File.Delete(_path);
    }
}
