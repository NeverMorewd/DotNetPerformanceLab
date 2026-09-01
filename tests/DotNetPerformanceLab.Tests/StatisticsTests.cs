namespace DotNetPerformanceLab.Tests;

public sealed class StatisticsTests
{
    [Fact]
    public void PercentileInterpolatesBetweenAdjacentValues()
    {
        var result = Statistics.Percentile([1, 2, 3, 4], 0.5);

        Assert.Equal(2.5, result);
    }

    [Fact]
    public void CalculateReturnsExpectedSummaryAndGrowth()
    {
        var result = Statistics.Calculate([(0, 10), (30, 20), (60, 30)]);

        Assert.Equal(10, result.Minimum);
        Assert.Equal(20, result.Median);
        Assert.Equal(30, result.Maximum);
        Assert.Equal(30, result.Final);
        Assert.Equal(20, result.GrowthPerMinute, precision: 6);
    }

    [Fact]
    public void ZeroMeanProducesFiniteCoefficientOfVariation()
    {
        var result = Statistics.Calculate([(0, 0), (1, 0)]);

        Assert.Equal(0, result.CoefficientOfVariationPercent);
    }
}
