using System.Globalization;
using System.Text;

namespace DotNetPerformanceLab;

public static class SvgChart
{
    public static async Task WriteAsync(
        string path,
        string title,
        string unit,
        IReadOnlyList<ProcessSample> samples,
        Func<ProcessSample, double?> selector,
        CancellationToken cancellationToken)
    {
        var points = samples.Select(sample => (sample.ElapsedSeconds, Value: selector(sample)))
            .Where(point => point.Value.HasValue)
            .Select(point => (point.ElapsedSeconds, Value: point.Value!.Value))
            .ToArray();
        if (points.Length == 0)
        {
            return;
        }

        const double width = 960;
        const double height = 360;
        const double left = 72;
        const double right = 24;
        const double top = 48;
        const double bottom = 52;
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;
        var maxX = Math.Max(1, points.Max(point => point.ElapsedSeconds));
        var minY = Math.Min(0, points.Min(point => point.Value));
        var maxY = Math.Max(1, points.Max(point => point.Value));
        if (Math.Abs(maxY - minY) < double.Epsilon)
        {
            maxY = minY + 1;
        }

        var builder = new StringBuilder();
        builder.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"960\" height=\"360\" viewBox=\"0 0 960 360\">");
        builder.AppendLine("<rect width=\"960\" height=\"360\" fill=\"#0d1117\" rx=\"12\"/>");
        builder.Append("<text x=\"24\" y=\"30\" fill=\"#f0f6fc\" font-family=\"sans-serif\" font-size=\"18\" font-weight=\"600\">")
            .Append(Escape(title)).AppendLine("</text>");

        for (var index = 0; index <= 4; index++)
        {
            var fraction = index / 4d;
            var y = top + (plotHeight * fraction);
            var value = maxY - ((maxY - minY) * fraction);
            builder.AppendFormat(CultureInfo.InvariantCulture, "<line x1=\"{0}\" y1=\"{1:0.##}\" x2=\"{2}\" y2=\"{1:0.##}\" stroke=\"#30363d\"/>\n", left, y, width - right);
            builder.AppendFormat(CultureInfo.InvariantCulture, "<text x=\"{0}\" y=\"{1:0.##}\" text-anchor=\"end\" fill=\"#8b949e\" font-family=\"monospace\" font-size=\"11\">{2:0.##} {3}</text>\n", left - 8, y + 4, value, Escape(unit));
        }

        var polyline = string.Join(' ', points.Select(point =>
        {
            var x = left + (point.ElapsedSeconds / maxX * plotWidth);
            var y = top + ((maxY - point.Value) / (maxY - minY) * plotHeight);
            return FormattableString.Invariant($"{x:0.##},{y:0.##}");
        }));
        builder.Append("<polyline fill=\"none\" stroke=\"#58a6ff\" stroke-width=\"2\" points=\"")
            .Append(polyline).AppendLine("\"/>");
        builder.AppendFormat(CultureInfo.InvariantCulture, "<text x=\"{0}\" y=\"{1}\" fill=\"#8b949e\" font-family=\"monospace\" font-size=\"11\">0s</text>\n", left, height - 20);
        builder.AppendFormat(CultureInfo.InvariantCulture, "<text x=\"{0}\" y=\"{1}\" text-anchor=\"end\" fill=\"#8b949e\" font-family=\"monospace\" font-size=\"11\">{2:0.#}s</text>\n", width - right, height - 20, maxX);
        builder.AppendLine("</svg>");

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
