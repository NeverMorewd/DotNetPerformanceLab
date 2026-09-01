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
}
