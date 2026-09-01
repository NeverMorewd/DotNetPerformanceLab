namespace DotNetPerformanceLab;

public static class Statistics
{
    public static MetricStatistics Calculate(IReadOnlyList<(double TimeSeconds, double Value)> observations)
    {
        if (observations.Count == 0)
        {
            throw new ArgumentException("At least one observation is required.", nameof(observations));
        }

        var values = observations.Select(item => item.Value).Order().ToArray();
        var mean = values.Average();
        var variance = values.Select(value => Math.Pow(value - mean, 2)).Average();
        var standardDeviation = Math.Sqrt(variance);

        return new MetricStatistics(
            Minimum: values[0],
            Median: Percentile(values, 0.50),
            Percentile95: Percentile(values, 0.95),
            Maximum: values[^1],
            Mean: mean,
            StandardDeviation: standardDeviation,
            CoefficientOfVariationPercent: Math.Abs(mean) < double.Epsilon ? 0 : standardDeviation / Math.Abs(mean) * 100,
            Final: observations[^1].Value,
            GrowthPerMinute: LinearSlope(observations) * 60);
    }

    public static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(sortedValues));
        }

        if (percentile is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }

        var position = (sortedValues.Count - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        var fraction = position - lower;
        return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * fraction);
    }

    public static double LinearSlope(IReadOnlyList<(double TimeSeconds, double Value)> observations)
    {
        if (observations.Count < 2)
        {
            return 0;
        }

        var meanX = observations.Average(item => item.TimeSeconds);
        var meanY = observations.Average(item => item.Value);
        var numerator = observations.Sum(item => (item.TimeSeconds - meanX) * (item.Value - meanY));
        var denominator = observations.Sum(item => Math.Pow(item.TimeSeconds - meanX, 2));
        return Math.Abs(denominator) < double.Epsilon ? 0 : numerator / denominator;
    }
}
