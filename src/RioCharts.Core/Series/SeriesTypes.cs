using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Paths;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;

namespace RioCharts.Core.Series;

internal enum CoordinateSystem { Cartesian, Polar, SpaceFilling }

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
        SeriesType.RangeBar or SeriesType.Bullet or SeriesType.Funnel or SeriesType.Calendar => new PolarSeriesType(type, CoordinateSystem.Cartesian), // laid out by their own ChartLayout.Compute*
        SeriesType.RangeArea => RangeAreaSeriesType.Instance,
        SeriesType.Dumbbell => DumbbellSeriesType.Instance,
        SeriesType.BoxPlot => BoxPlotSeriesType.Instance,
        SeriesType.Candlestick => CandlestickSeriesType.Instance,
        SeriesType.Waterfall => WaterfallSeriesType.Instance,
        SeriesType.Histogram => HistogramSeriesType.Instance,
        SeriesType.Pie or SeriesType.RadialBar or SeriesType.Radar or SeriesType.Gauge => new PolarSeriesType(type),
        SeriesType.Treemap => new PolarSeriesType(type, CoordinateSystem.SpaceFilling),
        SeriesType.Map or SeriesType.MapBubble => new PolarSeriesType(type, CoordinateSystem.SpaceFilling),
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
    private readonly ContinuousScale? _scale;
    private readonly double[]? _categories;

    public PixelPositions(SeriesData data, XKind kind, ContinuousScale? scale, BandScale? band, IReadOnlyDictionary<string, int> categoryIndex)
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
    public required SeriesOptions Spec { get; init; }
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

    /// <summary>The size scale all bubble series in the chart share; null when no series has sizes.</summary>
    public BubbleScale? Bubbles { get; init; }

    /// <summary>The display zone; null when times are shown as given. Merged candles follow its calendar.</summary>
    public TimeConversion? Time { get; init; }

    /// <summary>Bubbles: the drawn radius of each hit point (same order as <see cref="Hits"/>); null for other series.</summary>
    public float[]? HitRadii { get; set; }

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

    /// <summary>A point's own color: from the data (<see cref="SeriesData.Colors"/>), else its value's color band; null keeps the series color.</summary>
    public string? PointColor(int i) => Data.Colors?[i] ?? ColorBands.ColorOf(Spec.ColorBands, Data.Y[i]);

    /// <summary>
    /// Adds a line or area path once per color band, each clipped to its value range, so the color
    /// changes exactly where it crosses a band edge. Without bands: once, in the series color.
    /// </summary>
    public void AddBanded(PathData path, string name, Func<string, Style> style, string cssClass)
    {
        if (Spec.ColorBands is not { Count: > 0 } bands)
        {
            Output.Add(new PathNode(path, Styles.Add($"series-{ColorIndex}-{name}", style(Color))) { Class = cssClass });
            return;
        }

        const float margin = 8; // room for the stroke at the plot edges (the plot clip still applies)
        foreach (var (from, to, color) in ColorBands.Ranges(bands, Color))
        {
            var y0 = double.IsPositiveInfinity(to) ? Plot.Y - margin : MapY(to);
            var y1 = double.IsNegativeInfinity(from) ? Plot.Bottom + margin : MapY(from);
            var (top, bottom) = (Math.Max(Plot.Y - margin, Math.Min(y0, y1)), Math.Min(Plot.Bottom + margin, Math.Max(y0, y1)));
            if (bottom - top < 0.01)
            {
                continue; // a range outside the axis
            }

            var styleRef = color == Color ? Styles.Add($"series-{ColorIndex}-{name}", style(color)) : Styles.Add($"{name}-{color}", style(color));
            Output.Add(new GroupNode([new PathNode(path, styleRef) { Class = cssClass }],
                Clip: new RectF(Plot.X - margin, (float)top, Plot.Width + 2 * margin, (float)(bottom - top)))
            {
                Class = "rio-color-band",
            });
        }
    }

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
        c.AddBanded(LineGenerator.Line(xs, ys, c.Spec.Curve), "line", color => new Style { Stroke = color, StrokeWidth = (float)c.StrokeWidth, Fill = "none" }, "rio-line");
        AddLonePoints(c, xs, ys);
        AddHighlights(c);
        AddColoredPoints(c, xs, ys, indices);
        c.Hits = Hits(c, xs, ys, indices);
        AddLabels(c, xs, ys, indices);
    }

    /// <summary>
    /// Points with their own color (<see cref="SeriesData.Colors"/>): a dot in that color, ringed in
    /// the surface color; the line keeps the series color.
    /// </summary>
    internal static void AddColoredPoints(SeriesBuildContext c, double[] xs, double[] ys, int[] indices)
    {
        if (c.Data.Colors is not { } colors)
        {
            return;
        }

        var radius = Math.Max(3, c.StrokeWidth * 1.75);
        var paths = new Dictionary<string, PathBuilder>(StringComparer.Ordinal);
        for (var k = 0; k < indices.Length; k++)
        {
            if (indices[k] < colors.Length && colors[indices[k]] is { } color && double.IsFinite(ys[k]))
            {
                var path = System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(paths, color, out _) ??= new PathBuilder();
                path.MoveTo(xs[k], ys[k] - radius).Arc(xs[k], ys[k], radius, 0, Math.PI * 2).Close();
            }
        }

        foreach (var (color, path) in paths)
        {
            c.Output.Add(new PathNode(path.Build(), c.Styles.Add($"point-{color}", new Style { Fill = color, Stroke = ThemeTokens.Surface, StrokeWidth = 1.5f }))
            {
                Class = "rio-point rio-point-colored",
            });
        }
    }

    /// <summary>
    /// Dots on the first, last, lowest and highest value (<see cref="SeriesOptions.Highlight"/>), found
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
/// The size scale of the bubbles in a chart: a bubble's area is proportional to its size, and the
/// largest size in the chart gets each series' largest radius (from <see cref="SeriesOptions.MarkerSize"/>,
/// else from the chart size), so bubbles of all series compare.
/// </summary>
/// <param name="MaxSize">The largest positive size of all bubble series.</param>
/// <param name="ChartWidth">The chart's width in pixels.</param>
/// <param name="ChartHeight">The chart's height in pixels.</param>
internal readonly record struct BubbleScale(double MaxSize, double ChartWidth, double ChartHeight)
{
    /// <summary>The largest bubble's radius for a series.</summary>
    public double MaxRadius(SeriesOptions spec) => spec.MarkerSize is > 0 and var d
        ? Math.Clamp(d / 2, 2, 100)
        : Math.Clamp(Math.Min(ChartWidth, ChartHeight) * 0.06, 4, 28);

    /// <summary>A bubble's radius in pixels; NaN when it is not drawn (no size, or zero or less).</summary>
    public double Radius(SeriesOptions spec, double size) => size > 0 && double.IsFinite(size) && MaxSize > 0
        ? Math.Max(1.5, MaxRadius(spec) * Math.Sqrt(size / MaxSize))
        : double.NaN;

    /// <summary>The scale for the bubble series among <paramref name="series"/>; null when there are none.</summary>
    public static BubbleScale? For(IEnumerable<(SeriesOptions Spec, SeriesData Data)> series, double width, double height)
    {
        var max = 0.0;
        var any = false;
        foreach (var (spec, data) in series)
        {
            if (spec.Type != SeriesType.Scatter || data.Sizes is not { } sizes)
            {
                continue;
            }

            any = true;
            foreach (var v in sizes)
            {
                if (v > max && double.IsFinite(v))
                {
                    max = v;
                }
            }
        }

        return any ? new BubbleScale(max, width, height) : null;
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

    /// <summary>Marker radius from <see cref="SeriesOptions.MarkerSize"/> (a diameter, default 5 px).</summary>
    /// <summary>Most markers drawn in one square cell a marker radius wide, once a series is thinned.</summary>
    internal const int MarkersPerCell = 3;

    internal static double Radius(SeriesOptions spec) => Math.Clamp((spec.MarkerSize is > 0 and var size ? size : 5) / 2, 1, 30);

    public void Build(SeriesBuildContext c)
    {
        var data = c.Data;
        var (start, end) = c.XWindow is { } w && data.Kind != XKind.Category
            ? Layout.ChartLayout.VisibleIndexes(data.X, w.Min, w.Max, 0)
            : (0, data.Count);
        if (data.Sizes is { } sizes && c.Bubbles is { } scale)
        {
            BuildBubbles(c, sizes, scale, start, end);
            return;
        }

        var r = Radius(c.Spec);
        var xs = new List<double>();
        var ys = new List<double>();
        var indices = new List<int>();
        var builder = new PathBuilder();
        var colored = c.Data.Colors is null && c.Spec.ColorBands is null ? null : new Dictionary<string, PathBuilder>(StringComparer.Ordinal);

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

            // A point with its own color goes in a path of that color.
            var target = colored is not null && c.PointColor(i) is { } own
                ? System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(colored, own, out _) ??= new PathBuilder()
                : builder;
            target.MoveTo(px, py - r).Arc(px, py, r, 0, Math.PI * 2).Close();
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
        foreach (var (color, path) in colored ?? [])
        {
            c.Output.Add(new PathNode(path.Build(), c.Styles.Add($"marker-{color}", new Style { Fill = color, FillOpacity = 0.75f })) { Class = "rio-marker" });
        }

        c.Hits = LineSeriesType.Hits(c, [.. xs], [.. ys], [.. indices]);
        LineSeriesType.AddLabels(c, [.. xs], [.. ys], [.. indices]);
    }

    /// <summary>
    /// Bubbles: a circle per point with its area proportional to its size, largest first so small
    /// bubbles stay on top. Translucent with a solid outline, so overlaps show. Never thinned: each
    /// bubble carries its own size.
    /// </summary>
    private static void BuildBubbles(SeriesBuildContext c, double[] sizes, BubbleScale scale, int start, int end)
    {
        var points = new List<(int Index, double X, double Y, double R)>();
        for (var i = start; i < end; i++)
        {
            var (px, y) = (c.PixelX[i], c.Upper[i]);
            var r = scale.Radius(c.Spec, sizes[i]);
            if (double.IsFinite(px) && double.IsFinite(y) && double.IsFinite(r))
            {
                points.Add((i, px, c.MapY(y), r));
            }
        }

        var builder = new PathBuilder();
        var colored = c.Data.Colors is null && c.Spec.ColorBands is null ? null : new Dictionary<string, PathBuilder>(StringComparer.Ordinal);
        foreach (var p in points.OrderByDescending(p => p.R).ThenBy(p => p.Index))
        {
            var target = colored is not null && c.PointColor(p.Index) is { } own
                ? System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(colored, own, out _) ??= new PathBuilder()
                : builder;
            target.MoveTo(p.X, p.Y - p.R).Arc(p.X, p.Y, p.R, 0, Math.PI * 2).Close();
        }

        static Style BubbleStyle(string color) => new() { Fill = color, FillOpacity = 0.5f, Stroke = color, StrokeWidth = 1 };
        c.Output.Add(new PathNode(builder.Build(), c.Styles.Add($"series-{c.ColorIndex}-bubble", BubbleStyle(c.Color))) { Class = "rio-marker rio-bubble" });
        foreach (var (color, path) in colored ?? [])
        {
            c.Output.Add(new PathNode(path.Build(), c.Styles.Add($"bubble-{color}", BubbleStyle(color))) { Class = "rio-marker rio-bubble" });
        }

        // Hits stay in x order (the tooltip scans them by x).
        double[] xs = [.. points.Select(p => p.X)], ys = [.. points.Select(p => p.Y)];
        int[] indices = [.. points.Select(p => p.Index)];
        c.Hits = LineSeriesType.Hits(c, xs, ys, indices);
        c.HitRadii = [.. points.Where(p => c.InPlotX(p.X)).Select(p => (float)p.R)];
        if (c.Spec.TrendLine && c.Data.Kind != XKind.Category)
        {
            AddTrendLine(c, start, end);
        }

        LineSeriesType.AddLabels(c, xs, ys, indices);
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
        var opacity = c.Stacked ? 0.32f : 0.18f;
        var area = y0 is null ? LineGenerator.Area(xs, ys, c.ZeroY, c.Spec.Curve) : LineGenerator.AreaBetween(xs, ys, y0, c.Spec.Curve);
        c.AddBanded(area, "area", color => new Style { Fill = color, FillOpacity = opacity }, "rio-area");
        c.AddBanded(LineGenerator.Line(xs, ys, c.Spec.Curve), "line", color => new Style { Stroke = color, StrokeWidth = (float)c.StrokeWidth, Fill = "none" }, "rio-line");
        LineSeriesType.AddLonePoints(c, xs, ys);
        LineSeriesType.AddHighlights(c);
        LineSeriesType.AddColoredPoints(c, xs, ys, indices);
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
/// Dumbbells: per point a line from its first value (<see cref="SeriesBuildContext.Lower"/>, the
/// data's low column) to its second (<see cref="SeriesBuildContext.Upper"/>), a hollow dot at the first
/// and a filled dot at the second. Several dumbbell series share the category's middle.
/// </summary>
internal sealed class DumbbellSeriesType : ISeriesType
{
    /// <summary>The dots' radius in pixels.</summary>
    public const double Radius = 5;

    public static DumbbellSeriesType Instance { get; } = new();
    public SeriesType Type => SeriesType.Dumbbell;
    public CoordinateSystem CoordinateSystem => CoordinateSystem.Cartesian;
    public int DefaultLayer => 2;
    public bool IncludesZero => false;
    public bool UsesSlots => false;

    public void Build(SeriesBuildContext c)
    {
        var connectors = new PathBuilder();
        var froms = new PathBuilder();
        var tos = new Dictionary<string, PathBuilder>(StringComparer.Ordinal);
        var hx = new List<float>();
        var hy = new List<float>();
        var hi = new List<int>();
        var hs = new List<int>();
        for (var i = 0; i < c.Data.Count; i++)
        {
            var x = c.PixelX[i];
            var to = c.Upper[i];
            var from = c.Lower is { } lower ? lower[i] : double.NaN;
            if (!double.IsFinite(x) || !c.InPlotX(x) || (!double.IsFinite(to) && !double.IsFinite(from)))
            {
                continue;
            }

            var (y0, y1) = (c.MapY(from), c.MapY(to));
            if (double.IsFinite(from) && double.IsFinite(to))
            {
                connectors.MoveTo(x, y0).LineTo(x, y1);
            }

            if (double.IsFinite(from))
            {
                Dot(froms, x, y0, Radius - 1);
            }

            if (double.IsFinite(to))
            {
                var color = c.PointColor(i) ?? c.Color;
                Dot(tos.TryGetValue(color, out var path) ? path : tos[color] = new PathBuilder(), x, y1, Radius);
            }

            hx.Add((float)x);
            hy.Add((float)(double.IsFinite(to) ? y1 : y0));
            hi.Add(i);
            hs.Add(c.Data.ToSourceIndex(i));
            if (c.Spec.DataLabels == true)
            {
                if (double.IsFinite(from))
                {
                    c.Labels.Add(new LabelAnchor(from, x, y0, double.NaN, 0, false));
                }

                if (double.IsFinite(to))
                {
                    c.Labels.Add(new LabelAnchor(to, x, y1, double.NaN, 0, false));
                }
            }
        }

        c.Output.Add(new PathNode(connectors.Build(), c.Styles.Add($"series-{c.ColorIndex}-dumbbell", new Style { Stroke = c.Color, StrokeWidth = 3, Opacity = 0.45f }))
        {
            Class = "rio-dumbbell-line",
        });
        c.Output.Add(new PathNode(froms.Build(), c.Styles.Add($"series-{c.ColorIndex}-from", new Style { Fill = ThemeTokens.Surface, Stroke = c.Color, StrokeWidth = 2 }))
        {
            Class = "rio-dumbbell-from",
        });
        foreach (var (color, path) in tos)
        {
            var style = color == c.Color ? c.Styles.Add($"series-{c.ColorIndex}-to", new Style { Fill = color }) : c.Styles.Add($"dot-{color}", new Style { Fill = color });
            c.Output.Add(new PathNode(path.Build(), style) { Class = "rio-dumbbell-to" });
        }

        c.Hits = new HitTestIndex.SeriesHits(c.Spec.Id, [.. hx], [.. hy], [.. hi], [.. hs]);
    }

    /// <summary>A circle as a closed path.</summary>
    internal static void Dot(PathBuilder path, double x, double y, double r) => path.MoveTo(x, y - r).Arc(x, y, r, 0, Math.PI * 2).Close();

    /// <summary>The tooltip's value: "58 → 66 (+8)", or one end alone when the other is missing.</summary>
    internal static string Text(double from, double to, System.Globalization.CultureInfo culture, string? format)
    {
        string F(double v) => Formatting.ValueFormatter.Format(v, culture, format);
        if (!double.IsFinite(from))
        {
            return F(to);
        }

        if (!double.IsFinite(to))
        {
            return F(from) + " →";
        }

        var change = to - from;
        return $"{F(from)} → {F(to)} ({(change > 0 ? "+" : "")}{F(change)})";
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
/// Candlesticks: a thin wick from low to high and a body from open to close, lightly filled when the
/// close is at or above the open and solid below, in the series color (or with
/// <see cref="SeriesOptions.DirectionColors"/>, rising in the positive and falling in the negative color). Several
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

    /// <summary>A rising body: a light fill with its outline (the wick does not cross it).</summary>
    internal static Style UpStyle(string color) => new() { Fill = color, FillOpacity = 0.22f, Stroke = color, StrokeWidth = 1f };

    /// <summary>A falling body: solid.</summary>
    internal static Style DownStyle(string color) => new() { Fill = color, Stroke = color, StrokeWidth = 1f };

    /// <summary>The rising and falling colors of a series: the theme's positive and negative colors, or its own.</summary>
    internal static (string Up, string Down) Colors(SeriesOptions series, string color) =>
        series.DirectionColors ? (ThemeTokens.Positive, ThemeTokens.Negative) : (color, color);

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

        var (upColor, downColor) = Colors(c.Spec, c.Color);
        var upBody = c.Styles.Add($"candle-up-{upColor}", UpStyle(upColor));
        var downBody = c.Styles.Add($"candle-down-{downColor}", DownStyle(downColor));
        var upWick = c.Styles.Add($"candle-up-wick-{upColor}", new Style { Stroke = upColor, StrokeWidth = 1f, Fill = "none" });
        var downWick = c.Styles.Add($"candle-down-wick-{downColor}", new Style { Stroke = downColor, StrokeWidth = 1f, Fill = "none" });

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
            // With a display zone x is in instants: merge on that zone's calendar.
            var zone = c.Time;
            key = calendar is { } bucket
                ? zone is null
                    ? t => TimeValue.FromDateTime((DateTime)TimeBuckets.Floor(TimeValue.ToDateTime(t), bucket))
                    : t => zone.FromDisplay(TimeValue.FromDateTime((DateTime)TimeBuckets.Floor(TimeValue.ToDateTime(zone.ToDisplay(t)), bucket)))
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

            var own = c.PointColor(i) is { } color ? c.Styles.Add($"bar-{color}", new Style { Fill = color }) : style;
            c.Output.Add(new RectNode(new RectF((float)left, (float)top, (float)width, (float)height), own)
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
/// Histogram: a bar per bin from its lower to its upper edge on a numeric x axis, with a hairline
/// between bins instead of a gap (the bins touch: x is continuous). Several histograms overlay, more
/// see-through, instead of sharing the slot like grouped bars. X is each bin's middle, y its count.
/// </summary>
internal sealed class HistogramSeriesType : ISeriesType
{
    public static HistogramSeriesType Instance { get; } = new();
    public SeriesType Type => SeriesType.Histogram;
    public CoordinateSystem CoordinateSystem => CoordinateSystem.Cartesian;
    public int DefaultLayer => 1;
    public bool IncludesZero => true;
    public bool UsesSlots => true;

    /// <summary>The bin width in x units: the series' <see cref="SeriesOptions.BinWidth"/>, else the smallest distance between bins; NaN when unknown.</summary>
    internal static double WidthOf(SeriesOptions series, SeriesData data)
    {
        if (series.BinWidth is > 0 and var width && double.IsFinite(width))
        {
            return width;
        }

        var smallest = double.PositiveInfinity;
        for (var i = 1; i < data.Count; i++)
        {
            if (data.X[i] - data.X[i - 1] is > 0 and var dx && dx < smallest)
            {
                smallest = dx;
            }
        }

        return double.IsFinite(smallest) ? smallest : double.NaN;
    }

    public void Build(SeriesBuildContext c)
    {
        // The x axis keeps a bin width per slot (80 % of it, for bars): the full width is the bin.
        var binPx = c.SlotWidth / 0.8;
        var gap = binPx > 6 ? 1 : 0;
        var width = Math.Max(1, binPx - gap);
        c.BarWidth = width;
        c.BarOffset = 0;
        var overlaid = c.SlotCount > 1;
        var style = c.Styles.Add($"series-{c.ColorIndex}-histogram", new Style { Fill = c.Color, FillOpacity = overlaid ? 0.55f : null });

        var hx = new List<float>();
        var hy = new List<float>();
        var hi = new List<int>();
        var hs = new List<int>();
        for (var i = 0; i < c.Data.Count; i++)
        {
            var value = c.Upper[i];
            var center = c.PixelX[i];
            if (!double.IsFinite(value) || !double.IsFinite(center) || !c.InPlotX(center))
            {
                continue;
            }

            var y = c.MapY(value);
            var top = Math.Min(y, c.ZeroY);
            var height = Math.Abs(y - c.ZeroY);
            if (height > 0)
            {
                var own = c.PointColor(i) is { } color ? c.Styles.Add($"histogram-{color}", new Style { Fill = color, FillOpacity = overlaid ? 0.55f : null }) : style;
                c.Output.Add(new RectNode(new RectF((float)(center - width / 2), (float)top, (float)width, (float)height), own) { Class = "rio-bin" });
            }

            // Empty bins take hovers too: the tooltip says the range had no values.
            hx.Add((float)center);
            hy.Add((float)y);
            hi.Add(i);
            hs.Add(c.Data.ToSourceIndex(i));
            if (c.Spec.DataLabels == true && value != 0)
            {
                c.Labels.Add(new LabelAnchor(value, center, y, c.ZeroY, width, false));
            }
        }

        c.Hits = new HitTestIndex.SeriesHits(c.Spec.Id, [.. hx], [.. hy], [.. hi], [.. hs]);
    }
}

/// <summary>
/// Waterfall: how a start value becomes an end value. Each change is a bar floating from the running
/// total before it to the one after, in the positive theme color when it rises and the negative
/// color when it falls; a total (<see cref="SeriesData.Totals"/>) is a bar from zero to the running
/// total, in the series color. Thin connector lines carry each bar's end level over to the next bar.
/// Several waterfall series share the x band like grouped bars; gaps change nothing.
/// </summary>
internal sealed class WaterfallSeriesType : ISeriesType
{
    public static WaterfallSeriesType Instance { get; } = new();
    public SeriesType Type => SeriesType.Waterfall;
    public CoordinateSystem CoordinateSystem => CoordinateSystem.Cartesian;
    public int DefaultLayer => 1;
    public bool IncludesZero => true;
    public bool UsesSlots => true;

    /// <summary>
    /// Each point's bar: from <c>Lower</c> (the running total before it, or zero for a total) to
    /// <c>Upper</c> (the running total after it). NaN for gaps.
    /// </summary>
    internal static (double[] Upper, double[] Lower) Steps(SeriesData data)
    {
        var upper = new double[data.Count];
        var lower = new double[data.Count];
        var running = 0.0;
        for (var i = 0; i < data.Count; i++)
        {
            if (data.Totals is { } totals && totals[i])
            {
                (lower[i], upper[i]) = (0, running);
            }
            else if (double.IsFinite(data.Y[i]))
            {
                lower[i] = running;
                running += data.Y[i];
                upper[i] = running;
            }
            else
            {
                upper[i] = lower[i] = double.NaN;
            }
        }

        return (upper, lower);
    }

    /// <summary>A bar's color: its own, else the series color for a total, the positive color for a rise and the negative color for a fall.</summary>
    internal static string ColorOf(SeriesData data, int i, string seriesColor) =>
        data.Colors?[i] ?? (data.Totals is { } totals && totals[i] ? seriesColor : data.Y[i] < 0 ? ThemeTokens.Negative : ThemeTokens.Positive);

    public void Build(SeriesBuildContext c)
    {
        if (c.Lower is not { } lower)
        {
            return;
        }

        var slot = c.SlotWidth / Math.Max(1, c.SlotCount);
        var gap = slot > 8 ? 2 : 0;
        var width = Math.Max(1, slot - gap);
        var radius = (float)Math.Min(2, width / 6);
        c.BarWidth = width;
        c.BarOffset = -c.SlotWidth / 2 + c.SlotIndex * slot + gap / 2.0 + width / 2;

        var connectors = new PathBuilder();
        var hasConnectors = false;
        (double Right, double Y)? previous = null;
        var bars = new List<Node>();
        var hx = new List<float>();
        var hy = new List<float>();
        var hi = new List<int>();
        var hs = new List<int>();
        for (var i = 0; i < c.Data.Count; i++)
        {
            var center = c.PixelX[i];
            var (top, bottom) = (c.Upper[i], lower[i]);
            if (!double.IsFinite(center) || !double.IsFinite(top) || !double.IsFinite(bottom))
            {
                continue; // a gap: the next bar's connector comes from the last bar drawn
            }

            var left = center + c.BarOffset - width / 2;
            var (y, y0) = (c.MapY(top), c.MapY(bottom));
            if (previous is { } p && c.InPlotX(center))
            {
                connectors.MoveTo(p.Right, p.Y).LineTo(left, p.Y);
                hasConnectors = true;
            }

            previous = (left + width, y);
            if (!c.InPlotX(center))
            {
                continue;
            }

            var color = ColorOf(c.Data, i, c.Color);
            var total = c.Data.Totals is { } totals && totals[i];
            bars.Add(new RectNode(new RectF((float)left, (float)Math.Min(y, y0), (float)width, (float)Math.Max(Math.Abs(y - y0), top == bottom ? 0 : 1)),
                c.Styles.Add($"bar-{color}", new Style { Fill = color }))
            {
                CornerRadius = radius,
                Class = total ? "rio-waterfall rio-waterfall-total" : "rio-waterfall",
            });

            // Hits at the x position (shared by grouped series), at the running total after the bar.
            hx.Add((float)center);
            hy.Add((float)y);
            hi.Add(i);
            hs.Add(c.Data.ToSourceIndex(i));
            if (c.Spec.DataLabels == true)
            {
                // A change shows itself (above a rise, below a fall); a total its value.
                c.Labels.Add(new LabelAnchor(total ? top : c.Data.Y[i], left + width / 2, total ? y : c.Data.Y[i] >= 0 ? Math.Min(y, y0) : Math.Max(y, y0),
                    total ? y0 : c.Data.Y[i] >= 0 ? Math.Max(y, y0) : Math.Min(y, y0), width, false));
            }
        }

        c.Output.AddRange(bars);
        if (hasConnectors)
        {
            var style = c.Styles.Add("waterfall-connector", new Style { Stroke = ThemeTokens.Axis, StrokeWidth = 1f, Fill = "none" });
            c.Output.Add(new PathNode(connectors.Build(), style) { Class = "rio-connectors" });
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

/// <summary>Types laid out by their own pass (pies, radial bars, radar, treemap): nothing to build per series.</summary>
internal sealed class PolarSeriesType(SeriesType type, CoordinateSystem coordinates = CoordinateSystem.Polar) : ISeriesType
{
    public SeriesType Type => type;
    public CoordinateSystem CoordinateSystem => coordinates;
    public int DefaultLayer => 1;
    public bool IncludesZero => true;
    public bool UsesSlots => false;

    public void Build(SeriesBuildContext context)
    {
    }
}
