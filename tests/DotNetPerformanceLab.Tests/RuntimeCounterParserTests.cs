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

    public void Dispose()
    {
        File.Delete(_path);
    }
}
