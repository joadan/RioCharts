namespace RioCharts.Core.Series;

/// <summary>Resolves <see cref="SeriesOptions.ColorBands"/>: the color of a value, and the value ranges each color covers.</summary>
internal static class ColorBands
{
    /// <summary>The color of a value: the last band that contains it, or null (the series color).</summary>
    public static string? ColorOf(IReadOnlyList<ColorBand>? bands, double value)
    {
        if (bands is null || !double.IsFinite(value))
        {
            return null;
        }

        string? color = null;
        foreach (var band in bands)
        {
            if (Contains(band, value))
            {
                color = band.Color;
            }
        }

        return color;
    }

    /// <summary>
    /// The value axis cut into ranges of one color each, low to high: (from, to, color), with
    /// infinite ends; <paramref name="fallback"/> where no band applies. Neighbours with the same
    /// color are joined.
    /// </summary>
    public static List<(double From, double To, string Color)> Ranges(IReadOnlyList<ColorBand> bands, string fallback)
    {
        var cuts = bands.SelectMany(b => new[] { b.Above, b.Below }).OfType<double>().Where(double.IsFinite).Distinct().Order().ToList();
        var edges = cuts.Prepend(double.NegativeInfinity).Append(double.PositiveInfinity).ToList();
        var ranges = new List<(double From, double To, string Color)>();
        for (var i = 0; i < edges.Count - 1; i++)
        {
            var (from, to) = (edges[i], edges[i + 1]);
            var probe = double.IsInfinity(from) ? (double.IsInfinity(to) ? 0 : to - 1) : double.IsInfinity(to) ? from + 1 : (from + to) / 2;
            var color = ColorOf(bands, probe) ?? fallback;
            if (ranges.Count > 0 && ranges[^1].Color == color)
            {
                ranges[^1] = (ranges[^1].From, to, color);
            }
            else
            {
                ranges.Add((from, to, color));
            }
        }

        return ranges;
    }

    private static bool Contains(ColorBand band, double value) =>
        !string.IsNullOrWhiteSpace(band.Color) && (band.Above is not { } above || value > above) && (band.Below is not { } below || value < below);
}
