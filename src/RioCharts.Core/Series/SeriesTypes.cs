using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Paths;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;

namespace RioCharts.Core.Series;

internal enum CoordinateSystem { Cartesian, Polar }

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
        SeriesType.Scatter => ScatterSeriesType.Instance,
        SeriesType.Heatmap => HeatmapSeriesType.Instance,
        SeriesType.RangeArea => RangeAreaSeriesType.Instance,
        SeriesType.BoxPlot => BoxPlotSeriesType.Instance,
        SeriesType.Candlestick => CandlestickSeriesType.Instance,
        SeriesType.Pie or SeriesType.RadialBar => new PolarSeriesType(type),
        _ => LineSeriesType.Instance,
    };
}

/// <summary>Where a data label goes: the value, its point, and for bars the bar's extent.</summary>
/// <param name="Y0">Pixel y of the bar's base (NaN for points).</param>
/// <param name="Width">Bar width (0 for points).</param>
internal readonly record struct LabelAnchor(double Value, double X, double Y, double Y0, double Width, bool Stacked);

/// <summary>
/// The x pixel of each data point. Numbers and times are mapped when asked for, so a large series
/// that is drawn downsampled (or zoomed in) never maps every point.
/// </summary>
internal sealed class PixelPositions
{
    private readonly SeriesData _data;
    private readonly LinearScale? _scale;
    private readonly double[]? _categories;

    public PixelPositions(SeriesData data, XKind kind, LinearScale? scale, BandScale? band, IReadOnlyDictionary<string, int> categoryIndex)
    {
        _data = data;
        _scale = scale;
        if (kind == XKind.Category)
        {
            // Few points: look each category up once.
            _categories = new double[data.Count];
            for (var i = 0; i < data.Count; i++)
            {
                var local = (int)data.X[i];
                _categories[i] = band is not null && local >= 0 && local < data.Categories.Count && categoryIndex.TryGetValue(data.Categories[local], out var global)
                    ? band.Center(global)
                    : double.NaN;
            }
        }
    }

    public double this[int index] => _categories is not null ? _categories[index] : _scale?.Map(_data.X[index]) ?? double.NaN;
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

    /// <summary>X pixel for each data point (NaN when it cannot be placed), mapped on demand.</summary>
    public required PixelPositions PixelX { get; init; }

    /// <summary>Value to draw for each point: the data value, or the top of its stack segment.</summary>
    public required double[] Upper { get; init; }

    /// <summary>Base of each stack segment; null when not stacked (the base is zero).</summary>
    public double[]? Lower { get; init; }

    public bool Stacked => Lower is not null;

    public required Func<double, double> MapY { get; init; }

    /// <summary>Pixel y of the zero line, clamped to the plot.</summary>
    public required double ZeroY { get; init; }

    /// <summary>Width available per x position for bars.</summary>
    public double SlotWidth { get; init; }

    public int SlotIndex { get; init; }
    public int SlotCount { get; init; } = 1;
    public int DownsampleThreshold { get; init; }

    /// <summary>Visible x range when zoomed (data units).</summary>
    public (double Min, double Max)? XWindow { get; init; }

    /// <summary>True when a pixel x is inside the plot (hits and labels outside it are dropped).</summary>
    public bool InPlotX(double px) => px >= Plot.X - 0.5 && px <= Plot.Right + 0.5;

    public List<Node> Output { get; } = [];
    public HitTestIndex.SeriesHits? Hits { get; set; }

    /// <summary>Filled only when the series shows data labels.</summary>
    public List<LabelAnchor> Labels { get; } = [];

    /// <summary>Bar width and the bar center's offset from the x position (0 for points).</summary>
    public double BarWidth { get; set; }
    public double BarOffset { get; set; }

    public double StrokeWidth => Spec.StrokeWidth ?? 2;

    /// <summary>
    /// Candlesticks: the points each hit stands for (first to last data index), more than one when
    /// narrow candles were merged; null for other series.
    /// </summary>
    public List<(int First, int Last)>? CandleGroups { get; set; }
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
        var (xs, ys, _, indices) = DrawnPoints(c);
        var style = c.Styles.Add($"series-{c.ColorIndex}-line", new Style { Stroke = c.Color, StrokeWidth = (float)c.StrokeWidth, Fill = "none" });
        c.Output.Add(new PathNode(LineGenerator.Line(xs, ys, c.Spec.Curve), style) { Class = "rio-line" });
        AddLonePoints(c, xs, ys);
        AddHighlights(c);
        c.Hits = Hits(c, xs, ys, indices);
        AddLabels(c, xs, ys, indices);
    }

    /// <summary>
    /// Dots on the first, last, lowest and highest value (<see cref="SeriesSpec.Highlight"/>), found
    /// in the data (not the downsampled points) within the visible range, outlined in the surface color.
    /// </summary>
    internal static void AddHighlights(SeriesBuildContext c)
    {
        var highlight = c.Spec.Highlight;
        if (highlight == PointHighlight.None)
        {
            return;
        }

        var data = c.Data;
        var (start, end) = c.XWindow is { } w && data.Kind != XKind.Category
            ? Layout.ChartLayout.VisibleIndexes(data.X, w.Min, w.Max, 0)
            : (0, data.Count);
        int first = -1, last = -1, min = -1, max = -1;
        for (var i = start; i < end; i++)
        {
            var y = c.Upper[i];
            if (!double.IsFinite(y) || !double.IsFinite(c.PixelX[i]))
            {
                continue;
            }

            if (first < 0)
            {
                first = i;
            }

            last = i;
            if (min < 0 || y < c.Upper[min])
            {
                min = i;
            }

            if (max < 0 || y > c.Upper[max])
            {
                max = i;
            }
        }

        if (first < 0)
        {
            return;
        }

        var picked = new SortedSet<int>();
        if (highlight.HasFlag(PointHighlight.First)) picked.Add(first);
        if (highlight.HasFlag(PointHighlight.Last)) picked.Add(last);
        if (highlight.HasFlag(PointHighlight.Min)) picked.Add(min);
        if (highlight.HasFlag(PointHighlight.Max)) picked.Add(max);

        var radius = Layout.ChartLayout.HighlightRadius(c.StrokeWidth);
        var builder = new PathBuilder();
        foreach (var i in picked)
        {
            var (x, y) = (c.PixelX[i], c.MapY(c.Upper[i]));
            builder.MoveTo(x, y - radius).Arc(x, y, radius, 0, Math.PI * 2).Close();
        }

        var style = c.Styles.Add($"series-{c.ColorIndex}-highlight", new Style { Fill = c.Color, Stroke = ThemeTokens.Surface, StrokeWidth = 1.5f });
        c.Output.Add(new PathNode(builder.Build(), style) { Class = "rio-highlight" });
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

    /// <summary>
    /// Pixel coordinates of the points to draw (downsampled with LTTB when there are many):
    /// the line itself and, for stacked series, the base line.
    /// </summary>
    internal static (double[] X, double[] Y, double[]? Y0, int[] Indices) DrawnPoints(SeriesBuildContext c)
    {
        var data = c.Data;
        // Zoomed: only the visible points (plus one on each side, so lines reach the plot edge).
        var (start, end) = c.XWindow is { } w && data.Kind != XKind.Category
            ? Layout.ChartLayout.VisibleIndexes(data.X, w.Min, w.Max, 1)
            : (0, data.Count);
        var indices = c.DownsampleThreshold > 0 && end - start > c.DownsampleThreshold
            ? Lttb.Downsample(data.X, c.Upper, start, end, c.DownsampleThreshold)
            : Enumerable.Range(start, end - start).ToArray();

        var xs = new double[indices.Length];
        var ys = new double[indices.Length];
        var y0 = c.Lower is null ? null : new double[indices.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            var j = indices[i];
            if (j < 0)
            {
                // Gap marker from LTTB.
                xs[i] = ys[i] = double.NaN;
                if (y0 is not null)
                {
                    y0[i] = double.NaN;
                }

                continue;
            }

            xs[i] = c.PixelX[j];
            ys[i] = double.IsFinite(c.Upper[j]) ? c.MapY(c.Upper[j]) : double.NaN;
            if (y0 is not null)
            {
                y0[i] = double.IsFinite(c.Lower![j]) ? c.MapY(c.Lower[j]) : double.NaN;
            }
        }

        return (xs, ys, y0, indices);
    }

    internal static HitTestIndex.SeriesHits Hits(SeriesBuildContext c, double[] xs, double[] ys, int[] indices)
    {
        var hx = new List<float>(xs.Length);
        var hy = new List<float>(xs.Length);
        var hi = new List<int>(xs.Length);
        var hs = new List<int>(xs.Length);
        for (var i = 0; i < xs.Length; i++)
        {
            if (double.IsFinite(xs[i]) && double.IsFinite(ys[i]) && c.InPlotX(xs[i]))
            {
                hx.Add((float)xs[i]);
                hy.Add((float)ys[i]);
                hi.Add(indices[i]);
                hs.Add(c.Data.ToSourceIndex(indices[i]));
            }
        }

        return new HitTestIndex.SeriesHits(c.Spec.Id, [.. hx], [.. hy], [.. hi], [.. hs]);
    }

    internal static void AddLabels(SeriesBuildContext c, double[] xs, double[] ys, int[] indices)
    {
        if (c.Spec.DataLabels != true)
        {
            return;
        }

        for (var i = 0; i < xs.Length; i++)
        {
            if (double.IsFinite(xs[i]) && double.IsFinite(ys[i]) && c.InPlotX(xs[i]))
            {
                c.Labels.Add(new LabelAnchor(c.Data.Y[indices[i]], xs[i], ys[i], double.NaN, 0, c.Stacked));
            }
        }
    }
}

/// <summary>
/// A marker per point, slightly translucent so overlapping points show density, and optionally a
/// least-squares trend line. Every visible point is drawn (no downsampling: each point matters).
/// </summary>
internal sealed class ScatterSeriesType : ISeriesType
{
    public static ScatterSeriesType Instance { get; } = new();
    public SeriesType Type => SeriesType.Scatter;
    public CoordinateSystem CoordinateSystem => CoordinateSystem.Cartesian;
    public int DefaultLayer => 3;
    public bool IncludesZero => false;
    public bool UsesSlots => false;

    /// <summary>Marker radius from <see cref="SeriesSpec.MarkerSize"/> (a diameter, default 5 px).</summary>
    /// <summary>Most markers drawn in one square cell a marker radius wide, once a series is thinned.</summary>
    internal const int MarkersPerCell = 3;

    internal static double Radius(SeriesSpec spec) => Math.Clamp((spec.MarkerSize is > 0 and var size ? size : 5) / 2, 1, 30);

    public void Build(SeriesBuildContext c)
    {
        var data = c.Data;
        var (start, end) = c.XWindow is { } w && data.Kind != XKind.Category
            ? Layout.ChartLayout.VisibleIndexes(data.X, w.Min, w.Max, 0)
            : (0, data.Count);
        var r = Radius(c.Spec);
        var xs = new List<double>();
        var ys = new List<double>();
        var indices = new List<int>();
        var builder = new PathBuilder();

        // Many points: at most MarkersPerCell markers in each square one radius wide. Three markers at
        // 0.75 opacity already cover about 98%, so the picture is the same while dense clouds draw far
        // fewer markers; sparse areas, isolated points and outliers are all kept. Hover and the
        // tooltip use the drawn points.
        var occupied = c.DownsampleThreshold > 0 && end - start > c.DownsampleThreshold ? new Dictionary<long, int>() : null;
        for (var i = start; i < end; i++)
        {
            var (px, y) = (c.PixelX[i], c.Upper[i]);
            if (!double.IsFinite(px) || !double.IsFinite(y))
            {
                continue;
            }

            var py = c.MapY(y);
            if (occupied is not null)
            {
                ref var count = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(
                    occupied, ((long)Math.Floor(px / r) << 32) ^ (uint)(int)Math.Floor(py / r), out _);
                if (++count > MarkersPerCell)
                {
                    continue;
                }
            }

            builder.MoveTo(px, py - r).Arc(px, py, r, 0, Math.PI * 2).Close();
            xs.Add(px);
            ys.Add(py);
            indices.Add(i);
        }

        if (c.Spec.TrendLine && data.Kind != XKind.Category)
        {
            AddTrendLine(c, start, end);
        }

        var style = c.Styles.Add($"series-{c.ColorIndex}-marker", new Style { Fill = c.Color, FillOpacity = 0.75f });
        c.Output.Add(new PathNode(builder.Build(), style) { Class = "rio-marker" });
        c.Hits = LineSeriesType.Hits(c, [.. xs], [.. ys], [.. indices]);
        LineSeriesType.AddLabels(c, [.. xs], [.. ys], [.. indices]);
    }

    /// <summary>
    /// Least squares over all visible points (also those left out of a dense drawing), from the first
    /// to the last point's x.
    /// </summary>
    private static void AddTrendLine(SeriesBuildContext c, int start, int end)
    {
        bool Usable(int i) => double.IsFinite(c.Upper[i]) && double.IsFinite(c.PixelX[i]);
        int n = 0, first = -1, last = -1;
        double mx = 0, my = 0;
        for (var i = start; i < end; i++)
        {
            if (Usable(i))
            {
                (n, mx, my, last) = (n + 1, mx + c.Data.X[i], my + c.Upper[i], i);
                first = first < 0 ? i : first;
            }
        }

        if (n < 2)
        {
            return;
        }

        mx /= n;
        my /= n;
        double sxy = 0, sxx = 0;
        for (var i = start; i < end; i++)
        {
            if (Usable(i))
            {
                sxy += (c.Data.X[i] - mx) * (c.Upper[i] - my);
                sxx += (c.Data.X[i] - mx) * (c.Data.X[i] - mx);
            }
        }

        if (sxx <= 0)
        {
            return;
        }

        var slope = sxy / sxx; // first and last: x ascending
        double Y(int i) => c.MapY(my + slope * (c.Data.X[i] - mx));
        var style = c.Styles.Add($"series-{c.ColorIndex}-trend",
            new Style { Stroke = c.Color, StrokeWidth = 1.5f, Fill = "none", DashArray = "6 4", Opacity = 0.85f });
        c.Output.Add(new PathNode(new PathBuilder().MoveTo(c.PixelX[first], Y(first)).LineTo(c.PixelX[last], Y(last)).Build(), style)
        {
            Class = "rio-trend",
        });
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
        var (xs, ys, y0, indices) = LineSeriesType.DrawnPoints(c);

        // Stacked areas do not overlap, so they can be more opaque.
        var fill = c.Styles.Add($"series-{c.ColorIndex}-area", new Style { Fill = c.Color, FillOpacity = c.Stacked ? 0.32f : 0.18f });
        var line = c.Styles.Add($"series-{c.ColorIndex}-line", new Style { Stroke = c.Color, StrokeWidth = (float)c.StrokeWidth, Fill = "none" });
        var area = y0 is null ? LineGenerator.Area(xs, ys, c.ZeroY, c.Spec.Curve) : LineGenerator.AreaBetween(xs, ys, y0, c.Spec.Curve);
        c.Output.Add(new PathNode(area, fill) { Class = "rio-area" });
        c.Output.Add(new PathNode(LineGenerator.Line(xs, ys, c.Spec.Curve), line) { Class = "rio-line" });
        LineSeriesType.AddLonePoints(c, xs, ys);
        LineSeriesType.AddHighlights(c);
        c.Hits = LineSeriesType.Hits(c, xs, ys, indices);
        LineSeriesType.AddLabels(c, xs, ys, indices);
    }
}

/// <summary>
/// A band between each point's low and high value: a light fill without an outline, drawn below lines
/// (it usually sits behind a mean or median line). Points without a low value are gaps.
/// </summary>
internal sealed class RangeAreaSeriesType : ISeriesType
{
    public static RangeAreaSeriesType Instance { get; } = new();
    public SeriesType Type => SeriesType.RangeArea;
    public CoordinateSystem CoordinateSystem => CoordinateSystem.Cartesian;
    public int DefaultLayer => 0;
    public bool IncludesZero => false;
    public bool UsesSlots => false;

    public void Build(SeriesBuildContext c)
    {
        var (xs, ys, y0, indices) = LineSeriesType.DrawnPoints(c);
        if (y0 is null)
        {
            return; // no low values: nothing to fill (the layout warns)
        }

        // A gap where either end is missing.
        for (var i = 0; i < xs.Length; i++)
        {
            if (!double.IsFinite(y0[i]))
            {
                ys[i] = double.NaN;
            }
            else if (!double.IsFinite(ys[i]))
            {
                y0[i] = double.NaN;
            }
        }

        var fill = c.Styles.Add($"series-{c.ColorIndex}-range", new Style { Fill = c.Color, FillOpacity = 0.2f });
        c.Output.Add(new PathNode(LineGenerator.AreaBetween(xs, ys, y0, c.Spec.Curve), fill) { Class = "rio-range" });
        c.Hits = LineSeriesType.Hits(c, xs, ys, indices);
    }
}

/// <summary>
/// Box plots: per point a box from the lower to the upper quartile with a stronger median line,
/// thin whiskers to the whisker ends (no caps) and outliers as small hollow dots. Several box
/// series share the x band like grouped bars. Points without a box are gaps.
/// </summary>
internal sealed class BoxPlotSeriesType : ISeriesType
{
    /// <summary>Boxes wider than this look like bars; they stay centered in their slot.</summary>
    private const double MaxWidth = 56;
    private const double OutlierRadius = 2.5;

    public static BoxPlotSeriesType Instance { get; } = new();
    public SeriesType Type => SeriesType.BoxPlot;
    public CoordinateSystem CoordinateSystem => CoordinateSystem.Cartesian;
    public int DefaultLayer => 1;
    public bool IncludesZero => false;
    public bool UsesSlots => true;

    public void Build(SeriesBuildContext c)
    {
        if (c.Data.Box is not { } box)
        {
            return; // no boxes (the layout warns)
        }

        var slot = c.SlotWidth / Math.Max(1, c.SlotCount);
        var width = Math.Clamp(slot - (slot > 12 ? 6 : 0), 1, MaxWidth);
        c.BarWidth = width;
        c.BarOffset = -c.SlotWidth / 2 + c.SlotIndex * slot + slot / 2;

        var boxStyle = c.Styles.Add($"series-{c.ColorIndex}-box", new Style { Fill = c.Color, FillOpacity = 0.22f, Stroke = c.Color, StrokeWidth = 1f });
        var whiskerStyle = c.Styles.Add($"series-{c.ColorIndex}-whisker", new Style { Stroke = c.Color, StrokeWidth = 1f, Fill = "none" });
        var medianStyle = c.Styles.Add($"series-{c.ColorIndex}-median", new Style { Stroke = c.Color, StrokeWidth = 1.75f, Fill = "none" });
        var outlierStyle = c.Styles.Add($"series-{c.ColorIndex}-outlier", new Style { Fill = ThemeTokens.Surface, Stroke = c.Color, StrokeWidth = 1f });

        var boxes = new List<Node>();
        var whiskers = new PathBuilder();
        var medians = new PathBuilder();
        var outliers = new PathBuilder();
        var (hasWhiskers, hasMedians, hasOutliers) = (false, false, false);
        var hx = new List<float>();
        var hy = new List<float>();
        var hi = new List<int>();
        var hs = new List<int>();
        for (var i = 0; i < c.Data.Count; i++)
        {
            var center = c.PixelX[i];
            var (median, q1, q3) = (c.Data.Y[i], box.Q1[i], box.Q3[i]);
            if (!double.IsFinite(center) || !double.IsFinite(median) || !double.IsFinite(q1) || !double.IsFinite(q3) || !c.InPlotX(center))
            {
                continue;
            }

            var x = center + c.BarOffset;
            var left = x - width / 2;
            var (y1, y3, ym) = (c.MapY(q1), c.MapY(q3), c.MapY(median));
            var top = Math.Min(y1, y3);
            boxes.Add(new RectNode(new RectF((float)left, (float)top, (float)width, (float)Math.Max(1, Math.Abs(y1 - y3))), boxStyle)
            {
                CornerRadius = (float)Math.Min(2, width / 6),
                Class = "rio-box",
            });
            medians.MoveTo(left, ym).LineTo(left + width, ym);
            hasMedians = true;

            // Whiskers from the box edges to the whisker ends.
            if (double.IsFinite(box.High[i]) && box.High[i] > q3)
            {
                whiskers.MoveTo(x, y3).LineTo(x, c.MapY(box.High[i]));
                hasWhiskers = true;
            }

            if (double.IsFinite(box.Low[i]) && box.Low[i] < q1)
            {
                whiskers.MoveTo(x, y1).LineTo(x, c.MapY(box.Low[i]));
                hasWhiskers = true;
            }

            foreach (var v in box.OutliersOf(i))
            {
                var oy = c.MapY(v);
                outliers.MoveTo(x, oy - OutlierRadius).Arc(x, oy, OutlierRadius, 0, Math.PI * 2).Close();
                hasOutliers = true;
            }

            // Hits at the x position (shared by grouped series), at the median.
            hx.Add((float)center);
            hy.Add((float)ym);
            hi.Add(i);
            hs.Add(c.Data.ToSourceIndex(i));
        }

        if (hasWhiskers)
        {
            c.Output.Add(new PathNode(whiskers.Build(), whiskerStyle) { Class = "rio-whiskers" });
        }

        c.Output.AddRange(boxes);
        if (hasMedians)
        {
            c.Output.Add(new PathNode(medians.Build(), medianStyle) { Class = "rio-medians" });
        }

        if (hasOutliers)
        {
            c.Output.Add(new PathNode(outliers.Build(), outlierStyle) { Class = "rio-outliers" });
        }

        c.Hits = new HitTestIndex.SeriesHits(c.Spec.Id, [.. hx], [.. hy], [.. hi], [.. hs]);
    }
}

/// <summary>
/// Candlesticks: a thin wick from low to high and a body from open to close, lightly filled in the
/// rising color when the close is at or above the open, solid in the falling color below. Several
/// series share the x band like grouped bars. When candles would be too narrow to see (zoomed out),
/// they merge into longer periods: on time axes calendar periods (2, 3, 6 or 12 hours, a day, a
/// week, a month, ...) aligned to the clock, otherwise groups of consecutive candles. A merged
/// candle keeps the first open, the highest high, the lowest low and the last close.
/// </summary>
internal sealed class CandlestickSeriesType : ISeriesType
{
    /// <summary>Candles narrower than this (their slot, in pixels) merge into longer periods.</summary>
    internal const double MergeBelow = 5;

    /// <summary>Merged candles get at least this much room each.</summary>
    internal const double MergedSlot = 12;

    private const double MaxWidth = 32;
    private const double Minute = 60_000, Hour = 60 * Minute, Day = 24 * Hour;

    /// <summary>Periods to merge into on time axes, shortest first (weeks and longer are calendar periods).</summary>
    private static readonly (double Ms, TimeBucket? Calendar)[] Periods =
    [
        (Minute, null), (2 * Minute, null), (5 * Minute, null), (10 * Minute, null), (15 * Minute, null), (30 * Minute, null),
        (Hour, null), (2 * Hour, null), (3 * Hour, null), (6 * Hour, null), (12 * Hour, null),
        (Day, null), (7 * Day, TimeBucket.Week), (30.44 * Day, TimeBucket.Month), (91.31 * Day, TimeBucket.Quarter), (365.25 * Day, TimeBucket.Year),
    ];

    public static CandlestickSeriesType Instance { get; } = new();
    public SeriesType Type => SeriesType.Candlestick;
    public CoordinateSystem CoordinateSystem => CoordinateSystem.Cartesian;
    public int DefaultLayer => 1;
    public bool IncludesZero => false;
    public bool UsesSlots => true;

    /// <summary>A rising body: a light fill in the rising color with its outline (the wick does not cross it).</summary>
    internal static Style UpStyle => new() { Fill = ThemeTokens.Positive, FillOpacity = 0.22f, Stroke = ThemeTokens.Positive, StrokeWidth = 1f };

    /// <summary>A falling body: solid.</summary>
    internal static Style DownStyle => new() { Fill = ThemeTokens.Negative, Stroke = ThemeTokens.Negative, StrokeWidth = 1f };

    public void Build(SeriesBuildContext c)
    {
        if (c.Data.Candles is not { } candles)
        {
            return; // no candles (the layout warns)
        }

        var count = Math.Max(1, c.SlotCount);
        var (groups, band) = Groups(c, c.SlotWidth / count, count);

        // Merged candles share their period's width (most of it) with the other candle series.
        band ??= c.SlotWidth;
        var span = band.Value / count;
        var width = Math.Clamp(span - (span > 6 ? Math.Max(1.5, span * 0.3) : 0), 1, MaxWidth);
        c.BarWidth = width;
        c.BarOffset = -band.Value / 2 + c.SlotIndex * span + span / 2;

        var upBody = c.Styles.Add("candle-up", UpStyle);
        var downBody = c.Styles.Add("candle-down", DownStyle);
        var upWick = c.Styles.Add("candle-up-wick", new Style { Stroke = ThemeTokens.Positive, StrokeWidth = 1f, Fill = "none" });
        var downWick = c.Styles.Add("candle-down-wick", new Style { Stroke = ThemeTokens.Negative, StrokeWidth = 1f, Fill = "none" });

        var up = new PathBuilder();
        var down = new PathBuilder();
        var (hasUp, hasDown) = (false, false);
        var bodies = new List<Node>();
        var hx = new List<float>();
        var hy = new List<float>();
        var hi = new List<int>();
        var hs = new List<int>();
        var hitGroups = new List<(int, int)>();
        foreach (var (first, last, center) in groups)
        {
            var candle = first == last
                ? candles.At(first, c.Data.Y[first])
                : CandleSummary.Merge(Enumerable.Range(first, last - first + 1).Select(i => candles.At(i, c.Data.Y[i])));
            if (!candle.IsComplete || !double.IsFinite(center) || !c.InPlotX(center))
            {
                continue;
            }

            var x = center + c.BarOffset;
            var rising = candle.Close >= candle.Open;
            var (yo, yc) = (c.MapY(candle.Open), c.MapY(candle.Close));
            var (top, bottom) = (Math.Min(yo, yc), Math.Max(yo, yc));

            // The wick above and below the body, not through it (the rising body is see-through).
            var wick = rising ? up : down;
            var (yh, yl) = (c.MapY(candle.High), c.MapY(candle.Low));
            if (yh < top)
            {
                wick.MoveTo(x, yh).LineTo(x, top);
            }

            if (yl > bottom)
            {
                wick.MoveTo(x, bottom).LineTo(x, yl);
            }

            (hasUp, hasDown) = (hasUp || rising, hasDown || !rising);
            bodies.Add(new RectNode(new RectF((float)(x - width / 2), (float)top, (float)width, (float)Math.Max(1, bottom - top)), rising ? upBody : downBody)
            {
                Class = rising ? "rio-candle rio-candle-up" : "rio-candle rio-candle-down",
            });

            // The hit stands for the group: at its center, at the last close.
            hx.Add((float)center);
            hy.Add((float)yc);
            hi.Add(last);
            hs.Add(c.Data.ToSourceIndex(last));
            hitGroups.Add((first, last));
        }

        if (hasUp)
        {
            c.Output.Add(new PathNode(up.Build(), upWick) { Class = "rio-wicks rio-wicks-up" });
        }

        if (hasDown)
        {
            c.Output.Add(new PathNode(down.Build(), downWick) { Class = "rio-wicks rio-wicks-down" });
        }

        c.Output.AddRange(bodies);
        c.CandleGroups = hitGroups;
        c.Hits = new HitTestIndex.SeriesHits(c.Spec.Id, [.. hx], [.. hy], [.. hi], [.. hs]);
    }

    /// <summary>
    /// The candles to draw: each one's first and last point and its center pixel, and when merged
    /// the width of the band the candle series share per period (null: the usual slot).
    /// </summary>
    private static (List<(int First, int Last, double Center)> Groups, double? Band) Groups(SeriesBuildContext c, double slot, int seriesCount)
    {
        var data = c.Data;
        var n = data.Count;
        var groups = new List<(int, int, double)>(n);
        var pxPerUnit = n > 1 && data.X[^1] > data.X[0] ? (c.PixelX[n - 1] - c.PixelX[0]) / (data.X[^1] - data.X[0]) : 0;
        if (slot >= MergeBelow || n < 2 || data.Kind == XKind.Category || !(pxPerUnit > 0))
        {
            for (var i = 0; i < n; i++)
            {
                groups.Add((i, i, c.PixelX[i]));
            }

            return (groups, null);
        }

        // The usual distance between candles.
        var gaps = new double[n - 1];
        for (var i = 1; i < n; i++)
        {
            gaps[i - 1] = data.X[i] - data.X[i - 1];
        }

        Array.Sort(gaps);
        var spacing = Math.Max(gaps[gaps.Length / 2], double.Epsilon);

        Func<double, double> key;
        double period;
        if (data.Kind == XKind.Time)
        {
            // The shortest calendar period longer than one candle that gives each merged candle room.
            var (ms, calendar) = Periods.FirstOrDefault(p => p.Ms > spacing * 1.01 && p.Ms * pxPerUnit / seriesCount >= MergedSlot);
            if (ms == 0)
            {
                (ms, calendar) = Periods[^1];
            }

            period = ms;
            key = calendar is { } bucket
                ? t => TimeValue.FromDateTime((DateTime)TimeBuckets.Floor(TimeValue.ToDateTime(t), bucket))
                : t => Math.Floor(t / ms) * ms;
        }
        else
        {
            // Numbers: groups of k spacings, at multiples of k from zero (stable while panning).
            period = Math.Ceiling(MergedSlot * seriesCount / (spacing * pxPerUnit)) * spacing;
            key = t => Math.Floor(t / period) * period;
        }

        for (var first = 0; first < n;)
        {
            var start = key(data.X[first]);
            var last = first;
            while (last + 1 < n && key(data.X[last + 1]) == start)
            {
                last++;
            }

            // Centered on its period (a day's candle at noon), not on the points it happens to have.
            groups.Add((first, last, c.PixelX[first] + (start + period / 2 - data.X[first]) * pxPerUnit));
            first = last + 1;
        }

        return (groups, period * pxPerUnit * 0.8);
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

        // Stacked segments are square, with a 1px gap between them; single bars get round corners.
        var radius = c.Stacked ? 0 : (float)Math.Min(3, width / 4);
        c.BarWidth = width;
        c.BarOffset = -c.SlotWidth / 2 + c.SlotIndex * slot + gap / 2.0 + width / 2;

        var hx = new List<float>();
        var hy = new List<float>();
        var hi = new List<int>();
        var hs = new List<int>();
        for (var i = 0; i < c.Data.Count; i++)
        {
            var value = c.Data.Y[i];
            var center = c.PixelX[i];
            if (!double.IsFinite(value) || !double.IsFinite(center) || !double.IsFinite(c.Upper[i]) || !c.InPlotX(center))
            {
                continue;
            }

            var left = center + c.BarOffset - width / 2;
            var y = c.MapY(c.Upper[i]);
            var y0 = c.Lower is { } lower ? c.MapY(lower[i]) : c.ZeroY;
            var top = Math.Min(y, y0);
            var height = Math.Max(Math.Abs(y - y0), value == 0 ? 0 : 1); // non-zero values stay visible
            if (c.Stacked && height > 3)
            {
                // Leave a hairline gap on the side away from zero so segments read separately.
                height -= 1;
                if (value > 0)
                {
                    top += 1;
                }
            }

            c.Output.Add(new RectNode(new RectF((float)left, (float)top, (float)width, (float)height), style)
            {
                CornerRadius = radius,
                Class = "rio-bar",
            });

            // Hits sit at the x position (shared by grouped bars), so a shared tooltip lines them up.
            hx.Add((float)center);
            hy.Add((float)y);
            hi.Add(i);
            hs.Add(c.Data.ToSourceIndex(i));
            if (c.Spec.DataLabels == true)
            {
                c.Labels.Add(new LabelAnchor(value, left + width / 2, y, y0, width, c.Stacked));
            }
        }

        c.Hits = new HitTestIndex.SeriesHits(c.Spec.Id, [.. hx], [.. hy], [.. hi], [.. hs]);
    }
}

/// <summary>
/// Pie, donut and radial bar. They need the whole chart in polar coordinates, so the layout
/// draws them in its polar pass instead of through <see cref="Build"/>.
/// </summary>
/// <summary>Heatmap rows are laid out together by <c>ChartLayout.ComputeHeatmap</c>, not one by one.</summary>
internal sealed class HeatmapSeriesType : ISeriesType
{
    public static HeatmapSeriesType Instance { get; } = new();
    public SeriesType Type => SeriesType.Heatmap;
    public CoordinateSystem CoordinateSystem => CoordinateSystem.Cartesian;
    public int DefaultLayer => 0;
    public bool IncludesZero => false;
    public bool UsesSlots => false;

    public void Build(SeriesBuildContext context)
    {
    }
}

internal sealed class PolarSeriesType(SeriesType type) : ISeriesType
{
    public SeriesType Type => type;
    public CoordinateSystem CoordinateSystem => CoordinateSystem.Polar;
    public int DefaultLayer => 1;
    public bool IncludesZero => true;
    public bool UsesSlots => false;

    public void Build(SeriesBuildContext context)
    {
    }
}
