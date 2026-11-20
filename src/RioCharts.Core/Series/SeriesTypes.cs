using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Paths;
using RioCharts.Core.Scene;

namespace RioCharts.Core.Series;

public enum CoordinateSystem { Cartesian, Polar }

/// <summary>
/// What a series type contributes: its coordinate system, extent rules, layout needs and
/// scene nodes. Internal for now (no public plugin API), but every type goes through it.
/// </summary>
internal interface ISeriesType
{
    SeriesType Type { get; }
    CoordinateSystem CoordinateSystem { get; }

    /// <summary>Default draw order: areas (0), bars (1), lines (2).</summary>
    int DefaultLayer { get; }

    /// <summary>Bars and areas always start at zero.</summary>
    bool IncludesZero { get; }

    /// <summary>True when the type needs a slot in the x band (grouped bars).</summary>
    bool UsesSlots { get; }

    void Build(SeriesBuildContext context);

    static ISeriesType For(SeriesType type) => type switch
    {
        SeriesType.Area => AreaSeriesType.Instance,
        SeriesType.Bar => BarSeriesType.Instance,
        _ => LineSeriesType.Instance,
    };
}

/// <summary>Everything a series type needs to draw itself.</summary>
internal sealed class SeriesBuildContext
{
    public required SeriesSpec Spec { get; init; }
    public required SeriesData Data { get; init; }
    public required string Color { get; init; }
    public required int ColorIndex { get; init; }
    public required StyleTable Styles { get; init; }
    public required RectF Plot { get; init; }

    /// <summary>X pixel for each data point (NaN when it cannot be placed).</summary>
    public required double[] PixelX { get; init; }

    public required Func<double, double> MapY { get; init; }

    /// <summary>Pixel y of the zero line, clamped to the plot.</summary>
    public required double ZeroY { get; init; }

    /// <summary>Width available per x position for bars.</summary>
    public double SlotWidth { get; init; }

    public int SlotIndex { get; init; }
    public int SlotCount { get; init; } = 1;
    public int DownsampleThreshold { get; init; }

    public List<Node> Output { get; } = [];
    public HitTestIndex.SeriesHits? Hits { get; set; }

    public double StrokeWidth => Spec.StrokeWidth ?? 2;
}

internal sealed class LineSeriesType : ISeriesType
{
    public static LineSeriesType Instance { get; } = new();
    public SeriesType Type => SeriesType.Line;
    public CoordinateSystem CoordinateSystem => CoordinateSystem.Cartesian;
    public int DefaultLayer => 2;
    public bool IncludesZero => false;
    public bool UsesSlots => false;

    public void Build(SeriesBuildContext c)
    {
        var (xs, ys, indices) = DrawnPoints(c);
        var style = c.Styles.Add($"series-{c.ColorIndex}-line", new Style { Stroke = c.Color, StrokeWidth = (float)c.StrokeWidth, Fill = "none" });
        c.Output.Add(new PathNode(LineGenerator.Line(xs, ys, c.Spec.Curve), style) { Class = "rio-line" });
        AddLonePoints(c, xs, ys);
        c.Hits = Hits(c, xs, ys, indices);
    }

    /// <summary>
    /// Markers stay hidden, except for points with no neighbour to connect to (a single value,
    /// or one value between gaps), which would otherwise be invisible.
    /// </summary>
    internal static void AddLonePoints(SeriesBuildContext c, double[] xs, double[] ys)
    {
        var builder = new PathBuilder();
        var any = false;
        var radius = Math.Max(2.5, c.StrokeWidth * 1.25);
        foreach (var (start, end) in LineGenerator.Segments(xs, ys))
        {
            if (end - start == 1)
            {
                builder.MoveTo(xs[start], ys[start] - radius).Arc(xs[start], ys[start], radius, 0, Math.PI * 2).Close();
                any = true;
            }
        }

        if (any)
        {
            var style = c.Styles.Add($"series-{c.ColorIndex}-point", new Style { Fill = c.Color });
            c.Output.Add(new PathNode(builder.Build(), style) { Class = "rio-point" });
        }
    }

    /// <summary>Pixel coordinates of the points to draw, downsampled with LTTB when there are many.</summary>
    internal static (double[] X, double[] Y, int[] Indices) DrawnPoints(SeriesBuildContext c)
    {
        var data = c.Data;
        var indices = c.DownsampleThreshold > 0 && data.Count > c.DownsampleThreshold
            ? Lttb.Downsample(data.X, data.Y, 0, data.Count, c.DownsampleThreshold)
            : Enumerable.Range(0, data.Count).ToArray();

        var xs = new double[indices.Length];
        var ys = new double[indices.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            var j = indices[i];
            xs[i] = c.PixelX[j];
            ys[i] = double.IsFinite(data.Y[j]) ? c.MapY(data.Y[j]) : double.NaN;
        }

        return (xs, ys, indices);
    }

    internal static HitTestIndex.SeriesHits Hits(SeriesBuildContext c, double[] xs, double[] ys, int[] indices)
    {
        var hx = new List<float>(xs.Length);
        var hy = new List<float>(xs.Length);
        var hi = new List<int>(xs.Length);
        var hs = new List<int>(xs.Length);
        for (var i = 0; i < xs.Length; i++)
        {
            if (double.IsFinite(xs[i]) && double.IsFinite(ys[i]))
            {
                hx.Add((float)xs[i]);
                hy.Add((float)ys[i]);
                hi.Add(indices[i]);
                hs.Add(c.Data.ToSourceIndex(indices[i]));
            }
        }

        return new HitTestIndex.SeriesHits(c.Spec.Id, [.. hx], [.. hy], [.. hi], [.. hs]);
    }
}

internal sealed class AreaSeriesType : ISeriesType
{
    public static AreaSeriesType Instance { get; } = new();
    public SeriesType Type => SeriesType.Area;
    public CoordinateSystem CoordinateSystem => CoordinateSystem.Cartesian;
    public int DefaultLayer => 0;
    public bool IncludesZero => true;
    public bool UsesSlots => false;

    public void Build(SeriesBuildContext c)
    {
        var (xs, ys, indices) = LineSeriesType.DrawnPoints(c);
        var fill = c.Styles.Add($"series-{c.ColorIndex}-area", new Style { Fill = c.Color, FillOpacity = 0.18f });
        var line = c.Styles.Add($"series-{c.ColorIndex}-line", new Style { Stroke = c.Color, StrokeWidth = (float)c.StrokeWidth, Fill = "none" });
        c.Output.Add(new PathNode(LineGenerator.Area(xs, ys, c.ZeroY, c.Spec.Curve), fill) { Class = "rio-area" });
        c.Output.Add(new PathNode(LineGenerator.Line(xs, ys, c.Spec.Curve), line) { Class = "rio-line" });
        LineSeriesType.AddLonePoints(c, xs, ys);
        c.Hits = LineSeriesType.Hits(c, xs, ys, indices);
    }
}

internal sealed class BarSeriesType : ISeriesType
{
    public static BarSeriesType Instance { get; } = new();
    public SeriesType Type => SeriesType.Bar;
    public CoordinateSystem CoordinateSystem => CoordinateSystem.Cartesian;
    public int DefaultLayer => 1;
    public bool IncludesZero => true;
    public bool UsesSlots => true;

    public void Build(SeriesBuildContext c)
    {
        var style = c.Styles.Add($"series-{c.ColorIndex}-bar", new Style { Fill = c.Color });
        var slot = c.SlotWidth / Math.Max(1, c.SlotCount);
        var gap = slot > 8 ? 2 : 0;
        var width = Math.Max(1, slot - gap);
        var radius = (float)Math.Min(3, width / 4);

        var hx = new List<float>();
        var hy = new List<float>();
        var hi = new List<int>();
        var hs = new List<int>();
        for (var i = 0; i < c.Data.Count; i++)
        {
            var value = c.Data.Y[i];
            var center = c.PixelX[i];
            if (!double.IsFinite(value) || !double.IsFinite(center))
            {
                continue;
            }

            var left = center - c.SlotWidth / 2 + c.SlotIndex * slot + gap / 2.0;
            var y = c.MapY(value);
            var top = Math.Min(y, c.ZeroY);
            var height = Math.Max(Math.Abs(y - c.ZeroY), value == 0 ? 0 : 1); // non-zero values stay visible
            c.Output.Add(new RectNode(new RectF((float)left, (float)top, (float)width, (float)height), style)
            {
                CornerRadius = radius,
                Class = "rio-bar",
            });

            hx.Add((float)(left + width / 2));
            hy.Add((float)y);
            hi.Add(i);
            hs.Add(c.Data.ToSourceIndex(i));
        }

        c.Hits = new HitTestIndex.SeriesHits(c.Spec.Id, [.. hx], [.. hy], [.. hi], [.. hs]);
    }
}
