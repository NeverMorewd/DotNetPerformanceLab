namespace DotNetPerformanceLab.Tests;

public sealed class SettingsLoaderTests
{
    [Fact]
    public void ParsesArgumentsAsJsonWithoutShellInterpretation()
    {
        var values = new Dictionary<string, string?>
        {
            ["DPL_TARGET_PATH"] = Path.Combine(Path.GetTempPath(), "target"),
            ["DPL_ARGUMENTS_JSON"] = "[\"--name\",\"value with spaces\",\"; echo unsafe\"]",
            ["DPL_COLLECT_COUNTERS"] = "false"
        };

        var result = SettingsLoader.FromEnvironment(name => values.GetValueOrDefault(name));

        Assert.Equal(["--name", "value with spaces", "; echo unsafe"], result.Arguments);
        Assert.False(result.CollectCounters);
    }

    [Fact]
    public void RejectsOutOfRangeMeasurementDuration()
    {
        var values = new Dictionary<string, string?>
        {
            ["DPL_TARGET_PATH"] = "target",
            ["DPL_MEASUREMENT_SECONDS"] = "1"
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => SettingsLoader.FromEnvironment(name => values.GetValueOrDefault(name)));
    }

    [Fact]
    public void ParsesLivePublisherAndAdditionalMeters()
    {
        var values = new Dictionary<string, string?>
        {
            ["DPL_TARGET_PATH"] = "target",
            ["DPL_METERS_JSON"] = "[\"System.Net.Http\",\"Sample.Application\"]",
            ["DPL_LIVE_ENDPOINT"] = "https://metrics.example.test/api",
            ["DPL_LIVE_TOKEN"] = "secret",
            ["DPL_LIVE_RUN_ID"] = "run-42"
        };

        var result = SettingsLoader.FromEnvironment(name => values.GetValueOrDefault(name));

        Assert.Equal(["System.Net.Http", "Sample.Application"], result.Meters);
        Assert.Equal(new Uri("https://metrics.example.test/api"), result.LiveEndpoint);
        Assert.Equal("run-42", result.LiveRunId);
    }

    [Fact]
    public void RejectsInsecureLiveEndpoint()
    {
        var values = new Dictionary<string, string?>
        {
            ["DPL_TARGET_PATH"] = "target",
            ["DPL_LIVE_ENDPOINT"] = "http://metrics.example.test/api",
            ["DPL_LIVE_TOKEN"] = "secret"
        };

        Assert.Throws<ArgumentException>(() => SettingsLoader.FromEnvironment(name => values.GetValueOrDefault(name)));
    }
}
