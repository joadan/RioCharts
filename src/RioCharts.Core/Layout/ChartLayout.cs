using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;
using RioCharts.Core.Series;
using RioCharts.Core.Text;

namespace RioCharts.Core.Layout;

/// <summary>Size, fonts and environment for a layout pass (<see cref="ChartLayout.Compute"/>).</summary>
internal sealed record LayoutOptions
{
    /// <summary>Chart width in pixels. Default 640.</summary>
    public double Width { get; init; } = 640;

    /// <summary>Chart height in pixels. Default 300.</summary>
    public double Height { get; init; } = 300;

    /// <summary>Measures label text to fit ticks and margins. Default: <see cref="ApproximateTextMeasurer"/>.</summary>
    public ITextMeasurer Measurer { get; init; } = ApproximateTextMeasurer.Instance;

    /// <summary>Culture for numbers and dates in labels. Default: the current culture.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;

    /// <summary>The chart's own texts (empty states, descriptions, screen reader names). Null: <see cref="ChartTexts.For"/> the culture.</summary>
    public ChartTexts? Texts { get; init; }

    /// <summary>Ids of series hidden through the legend (they keep their color and legend item).</summary>
    public IReadOnlySet<string>? HiddenSeries { get; init; }

    /// <summary>Font size of labels in pixels. Default 12.</summary>
    public double FontSize { get; init; } = 12;

    /// <summary>Font size of the title in pixels. Default 15.</summary>
    public double TitleFontSize { get; init; } = 15;

    /// <summary>Line/area series with more points than this are downsampled. Null: twice the plot width (at least 500).</summary>
    public int? DownsampleThreshold { get; init; }

    /// <summary>Make annotations clickable (focusable buttons in the scene).</summary>
    public bool InteractiveAnnotations { get; init; }

    /// <summary>
    /// Visible x range (zoom/pan) in data units: numbers, or milliseconds for time axes. Null shows
    /// all data. The y axes fit the visible data. Ignored for category axes.
    /// </summary>
    public (double Min, double Max)? XRange { get; init; }

    /// <summary>The viewer's IANA time zone (from the browser), used for TimeZone "local" and to show the zone when it differs.</summary>
    public string? ViewerTimeZone { get; init; }

    /// <summary>
    /// Space to keep at least on each side of the plot, in pixels, so linked charts (e.g. a
    /// Blazor <c>Group</c>) line their plots up although their axis labels differ in width.
    /// Null: only what the chart's own labels need. Cartesian charts with vertical value axes only.
    /// </summary>
    public PlotInsets? MinInsets { get; init; }

    /// <summary>
    /// The selected x range in data units (numbers, or milliseconds for time axes), drawn as a
    /// shaded band behind the series. Null: none. Ignored for category axes.
    /// </summary>
    public (double Min, double Max)? SelectedRange { get; init; }

    /// <summary>
    /// Selected points per series id, as indexes into that series' <see cref="SeriesData"/>. Drawn
    /// as markers on lines, areas and scatter points, and as outlines around bars. Null: none.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyCollection<int>>? SelectedPoints { get; init; }

    /// <summary>
    /// Treemaps: the group to show (drill-down), as a path with the series' separator, e.g. "Asia" or
    /// "Asia/India". Null, or a path that is not there: the whole tree.
    /// </summary>
    public string? TreemapPath { get; init; }

    /// <summary>Treemaps: make groups and the path bar clickable (scene interactions of kind "treemap", with the group's path).</summary>
    public bool InteractiveTreemap { get; init; }

    /// <summary>
    /// Treemaps with <see cref="InteractiveTreemap"/>: items do not take clicks themselves (no point
    /// clicks or selection), so a click anywhere in a group, items included, drills one level into
    /// it. Off: only a group's header and frame drill (items keep their clicks).
    /// </summary>
    public bool TreemapDrillOnItems { get; init; }

    /// <summary>
    /// Maps: the area to zoom into: a continent by English name (e.g. "Europe"), or a country by ISO
    /// alpha-2 or alpha-3 code or English name (e.g. "SE"). Null, or one that is not on the map: the whole world.
    /// </summary>
    public string? MapRegion { get; init; }

    /// <summary>Maps: make countries and the path bar clickable (scene interactions of kind "map", with the continent's name or country's code to zoom to; "" for the world).</summary>
    public bool InteractiveMap { get; init; }
}

/// <summary>The space between the chart's edges and its plot, in pixels.</summary>
/// <param name="Left">From the left edge to the plot.</param>
/// <param name="Right">From the plot to the right edge.</param>
internal readonly record struct PlotInsets(double Left, double Right);

/// <summary>A legend item. <paramref name="Tooltip"/>: a summary shown on hover (lines separated by \n).</summary>
internal sealed record LegendEntry(string SeriesId, string Label, string Color, SeriesType Type, bool Visible, bool IsEmpty, string? Tooltip = null)
{
    /// <summary>Candlesticks colored by direction (<see cref="SeriesOptions.DirectionColors"/>): the swatch shows the rising and falling colors.</summary>
    public bool DirectionColors { get; init; }
}

/// <summary>The output of <see cref="ChartLayout.Compute"/>: the scene plus what interaction needs.</summary>
internal sealed class ChartLayoutResult
{
    /// <summary>The nodes to draw.</summary>
    public required Scene.Scene Scene { get; init; }

    /// <summary>The plot area in pixels (inside axes, legend and title).</summary>
    public required RectF Plot { get; init; }

    /// <summary>Pixel positions of drawn points, for finding the point under the pointer.</summary>
    public required HitTestIndex HitTest { get; init; }

    /// <summary>What the interactive layer needs for tooltips, crosshair and point clicks.</summary>
    public required TooltipModel Tooltip { get; init; }

    /// <summary>The legend items, in order.</summary>
    public required IReadOnlyList<LegendEntry> Legend { get; init; }

    /// <summary>Problems found in the definition or data, for developers (English).</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>The kind of x axis used.</summary>
    public required XKind XKind { get; init; }

    /// <summary>X scale for numeric and time axes (time in milliseconds); null for category axes.</summary>
    public ContinuousScale? XScale { get; init; }

    /// <summary>X scale for category axes.</summary>
    public BandScale? XBand { get; init; }

    /// <summary>The full x range of the data (numeric and time axes), the bounds for zoom and pan.</summary>
    public (double Min, double Max)? XExtent { get; init; }

    /// <summary>
    /// False for layouts that have an x scale (for coordinates) but ignore <see cref="LayoutOptions.XRange"/>, such as
    /// funnels and bullets: no zoom, pan or range selection there.
    /// </summary>
    public bool ZoomsX { get; init; } = true;

    /// <summary>True when <see cref="LayoutOptions.XRange"/> narrowed the x axis.</summary>
    public bool Zoomed { get; init; }

    /// <summary>
    /// The free area at the chart's top right, above the plot: right of the title, a top legend and
    /// the y-axis titles, and above the top y label. Renderers put buttons such as Reset zoom there
    /// when they fit, so they do not cover the data. Null when nothing is above the plot (no title,
    /// legend or y-axis title) or a compact chart.
    /// </summary>
    public RectF? TopRowFree { get; init; }

    /// <summary>The display time zone conversion, or null when times are shown as given.</summary>
    public TimeConversion? Time { get; init; }

    /// <summary>Scale of the primary vertical axis (the one with the grid lines).</summary>
    public required ContinuousScale YScale { get; init; }

    /// <summary>Scales of all vertical axes by axis id, including hidden ones.</summary>
    public required IReadOnlyDictionary<string, ContinuousScale> YScales { get; init; }

    /// <summary>True when there was something to draw; false when the chart shows its empty state.</summary>
    public required bool HasData { get; init; }

    /// <summary>Draggable reference lines (for the interactive layer).</summary>
    public IReadOnlyList<DragHandle> DragHandles { get; init; } = [];

    /// <summary>
    /// The insets the chart's own axis labels and legend need, before <see cref="LayoutOptions.MinInsets"/>.
    /// Linked charts share the largest of these. Null for charts that cannot line up (polar,
    /// horizontal bars, heatmaps, compact charts).
    /// </summary>
    public PlotInsets? NeededInsets { get; init; }

    /// <summary>Treemaps: the group shown (<see cref="LayoutOptions.TreemapPath"/>), or null when the whole tree is shown.</summary>
    public string? TreemapPath { get; init; }

    /// <summary>Maps: the area zoomed into, a country's <see cref="Geo.GeoRegion.Code"/> or a continent's name; null when the whole world is shown.</summary>
    public string? MapRegion { get; init; }
}

/// <summary>
/// The layout pass: resolves domains from series extents (stacked where asked), picks ticks
/// that fit the measured labels, places legend, axes and plot, and emits the scene. Pure: no
/// UI or browser dependency.
/// </summary>
internal static partial class ChartLayout
{
    private const double PadTop = 8, PadRight = 12, PadBottom = 6, PadLeft = 6;
    private const double LabelGap = 8;
    private const double AxisGap = 14;

    private sealed class Resolved
    {
        public required SeriesOptions Spec { get; init; }
        public required SeriesData Data { get; init; }
        public required int ColorIndex { get; init; }
        public required string Color { get; init; }
        public required ISeriesType Type { get; init; }
        public required bool Visible { get; init; }
        public required bool Empty { get; init; }
        public YAxisLayout Axis { get; set; } = null!;

        /// <summary>Values to draw: the data, or stack tops.</summary>
        public double[] Upper { get; set; } = [];

        /// <summary>Stack bases; null when not stacked.</summary>
        public double[]? Lower { get; set; }

        /// <summary>
        /// The stack the series is in: its stack name, "" for series a percent axis stacks without a name, or null
        /// when not stacked. Set once the axis is known.
        /// </summary>
        public string? StackGroup { get; set; }

        /// <summary>Stacked to 100 % (<see cref="StackMode.Percent"/>): <see cref="Upper"/> and <see cref="Lower"/> are shares.</summary>
        public bool Percent => StackGroup is not null && Axis.Spec.Stacking == StackMode.Percent;
    }

    /// <summary>Lays out a chart. Never throws for empty or odd data; it renders an empty state instead.</summary>
    /// <param name="spec">The chart.</param>
    /// <param name="data">Data per series id.</param>
    /// <param name="options">Size, fonts and culture; null for the defaults.</param>
    public static ChartLayoutResult Compute(ChartDefinition spec, IReadOnlyDictionary<string, SeriesData> data, LayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        options ??= new LayoutOptions();
        var warnings = new List<string>();
        var culture = options.Culture;
        var texts = options.Texts ?? ChartTexts.For(culture);
        var measure = options.Measurer;
        var compact = spec.Compact || AutoCompacts(spec, options);
        var width = Math.Max(compact ? 8 : 50, options.Width);
        var height = Math.Max(compact ? 8 : 50, options.Height);
        var font = options.FontSize;
        var chrome = FitChrome(spec.AutoCompact && !compact, width, height, font);
        var lineHeight = font * 1.3;
        var styles = CreateStyles(options);

        // ---------- Axes and series ----------
        var axes = ResolveAxes(spec, warnings);
        var time = ResolveTime(spec, data, options, warnings);
        var primary = axes[0];
        var resolved = new List<Resolved>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < spec.Series.Count; i++)
        {
            var s = spec.Series[i];
            if (!seenIds.Add(s.Id))
            {
                warnings.Add($"series[{i}].id: duplicate id '{s.Id}'; the series is skipped.");
                continue;
            }

            var d = data.TryGetValue(s.Id, out var found) ? found : SeriesData.Empty;
            if (time is not null && d.Kind == XKind.Time)
            {
                d = time.Convert(d); // plot on instants
            }

            var r = new Resolved
            {
                Spec = s,
                Data = d,
                ColorIndex = i,
                Color = s.Color ?? ThemeTokens.Series(i),
                Type = ISeriesType.For(s.Type),
                Visible = options.HiddenSeries?.Contains(s.Id) != true,
                Empty = !d.Y.Any(double.IsFinite),
                Upper = d.Y,
                Lower = s.Type is SeriesType.RangeArea or SeriesType.Dumbbell ? d.Low : null,
            };
            if (s.Type == SeriesType.Waterfall)
            {
                (r.Upper, r.Lower) = WaterfallSeriesType.Steps(d); // each bar from the running total before it to the one after
            }

            if (s.Type == SeriesType.Dumbbell && d.Low is null && d.Count > 0)
            {
                warnings.Add($"series '{s.Id}': a dumbbell needs its first values (FromValue, or \"from\" in a definition); only the second are drawn.");
            }

            if (s.Type == SeriesType.RangeArea && d.Low is null && d.Count > 0)
            {
                warnings.Add($"series '{s.Id}': a range area needs low values (LowValue, or \"low\" in a definition); nothing is drawn.");
            }

            if (s.Type == SeriesType.Candlestick && d.Candles is null && d.Count > 0)
            {
                warnings.Add($"series '{s.Id}': a candlestick needs candles (CandlestickSeries, or SeriesData.WithCandles); nothing is drawn.");
            }

            if (s.Type == SeriesType.BoxPlot && d.Box is null && d.Count > 0)
            {
                warnings.Add($"series '{s.Id}': a box plot needs boxes (BoxPlotSeries, SeriesData.WithBox or FromBoxGroups); nothing is drawn.");
            }

            r.Axis = FindAxis(axes, s.YAxisId, $"series[{i}].yAxisId", warnings) ?? primary;
            r.StackGroup = StackGroupOf(r, i, warnings);
            if ((d.Kind == XKind.Category || r.Type.UsesSlots) && s.Type is not (SeriesType.Treemap or SeriesType.Map or SeriesType.MapBubble) && RepeatsX(d) is { } repeated)
            {
                warnings.Add($"series '{s.Id}': x value {repeated} appears more than once; the points are drawn on top of each other. Combine them with an aggregate (Aggregate or YAggregate on a series component, 'aggregate' in a definition).");
            }

            resolved.Add(r);
        }

        if (resolved.Any(r => r.Spec.Type == SeriesType.Funnel))
        {
            return ComputeFunnel(spec, resolved, options, warnings, styles);
        }

        if (resolved.Any(r => r.Spec.Type == SeriesType.Calendar))
        {
            return ComputeCalendar(spec, resolved, options, warnings, styles, time);
        }

        if (resolved.Any(r => r.Spec.Type == SeriesType.Bullet))
        {
            return ComputeBullets(spec, resolved, options, warnings, styles);
        }

        if (resolved.Any(r => r.Spec.Type == SeriesType.RangeBar))
        {
            return ComputeRangeBars(spec, resolved, options, warnings, styles, time);
        }

        if (spec.Horizontal && resolved.Any(r => DrawsHorizontally(r.Spec.Type)) && !resolved.Any(r => r.Spec.Type == SeriesType.Heatmap))
        {
            return ComputeHorizontal(spec, resolved, options, warnings, styles, time);
        }

        if (resolved.Any(r => r.Spec.Type == SeriesType.Heatmap))
        {
            return ComputeHeatmap(spec, resolved, options, warnings, styles, time);
        }

        if (resolved.Any(r => r.Spec.Type is SeriesType.Map or SeriesType.MapBubble))
        {
            return ComputeMap(spec, resolved, options, warnings, styles);
        }

        if (resolved.Any(r => r.Spec.Type == SeriesType.Treemap))
        {
            return ComputeTreemap(spec, resolved, options, warnings, styles);
        }

        if (resolved.Any(r => r.Spec.Type == SeriesType.Radar))
        {
            return ComputeRadar(spec, resolved, options, warnings, styles);
        }

        if (resolved.Any(r => r.Type.CoordinateSystem == CoordinateSystem.Polar))
        {
            return ComputePolar(spec, resolved, options, warnings, styles);
        }

        var xKind = ResolveXKind(spec.X.Scale, resolved);
        if (spec.X.Scale == ScaleType.Log && xKind != XKind.Numeric)
        {
            warnings.Add($"x.scale: a log scale needs numbers, but the x values are {xKind}; the x axis is not logarithmic.");
        }

        // Counts, extremes and trend per series for the screen reader texts, computed once: the
        // legend, the series' labels and the chart description all use them.
        var factsCache = new Dictionary<SeriesData, SeriesFacts>();
        SeriesFacts Facts(SeriesData d)
        {
            if (!factsCache.TryGetValue(d, out var f))
            {
                factsCache[d] = f = ChartDescriber.Facts(d, xKind);
            }

            return f;
        }
        foreach (var r in resolved.Where(r => r.Data.Count > 0 && r.Data.Kind != xKind))
        {
            warnings.Add($"series '{r.Spec.Id}': x values are {r.Data.Kind} but the axis is {xKind}; the series is skipped.");
        }

        var drawable = resolved.Where(r => r.Visible && !r.Empty && (r.Data.Kind == xKind || r.Data.Count == 0)).ToList();
        var hasData = drawable.Count > 0;
        foreach (var r in drawable)
        {
            r.Axis.Series.Add(r);
        }

        foreach (var axis in axes)
        {
            axis.Visible = axis.Series.Count > 0;
        }

        // ---------- Stacking, annotations and domains ----------
        var categories = xKind == XKind.Category ? MergeCategories(resolved) : [];
        var categoryIndex = categories.Select((c, i) => (c, i)).ToDictionary(t => t.c, t => t.i, StringComparer.Ordinal);
        ApplyStacking(drawable, xKind, categoryIndex);

        var annotations = ResolveAnnotations(spec, axes, xKind, categories, warnings, time);
        var hasBars = drawable.Any(r => r.Type.UsesSlots);

        // A log x axis: numbers only, and not under bars (their slots need even spacing).
        var xLog = spec.X.Scale == ScaleType.Log && xKind == XKind.Numeric;
        if (xLog && hasBars)
        {
            warnings.Add("x.scale: a log x axis cannot be used with bar-like series (bars, histograms, box plots, candlesticks, waterfalls); the x axis is linear.");
            xLog = false;
        }

        LeaveOutNonPositive(drawable, annotations, warnings, xLog);
        var (xMin, xMax, barSpacing) = xKind == XKind.Category
            ? (0, 0, 0)
            : xLog ? LogXExtent(drawable, spec.X, annotations.Where(a => a.ExpandsDomain))
            : XExtent(drawable, spec.X, xKind, hasBars, annotations.Where(a => a.ExpandsDomain));
        var scatter = drawable.Where(r => r.Spec.Type == SeriesType.Scatter).ToList();
        var bubbles = BubbleScale.For(drawable.Select(r => (r.Spec, r.Data)), width, height);
        var sizeLegend = MeasureSizeLegend(spec, drawable, bubbles, compact, chrome.Legend, width, culture, texts, measure, font, lineHeight);
        if (bubbles is { } bubbleScale && xKind != XKind.Category && options.XRange is null && !xLog)
        {
            // Room for the bubbles at the ends, about as much as their radius on a plot of this size.
            (xMin, xMax) = PadForBubbles(scatter, bubbleScale, r => r.Data.X, xMin, xMax, (width - (sizeLegend?.Width + 16 ?? 0)) * 0.85,
                spec.X.Min is null, spec.X.Max is null);
        }

        if (scatter.Count > 0 && xKind == XKind.Numeric && options.XRange is null && !xLog)
        {
            // Scatter: a nice x range too, so points at the extremes are not on the plot edge.
            var nice = NiceTicks.NiceDomain(xMin, xMax, 10);
            (xMin, xMax) = (spec.X.Min ?? nice.Min, spec.X.Max ?? nice.Max);
        }
        (double Min, double Max)? fullX = xKind == XKind.Category ? null : (xMin, xMax);
        (double Min, double Max)? window = null;
        if (fullX is not null && options.XRange is { } range && range.Max > range.Min && double.IsFinite(range.Min) && double.IsFinite(range.Max)
            && (!xLog || range.Min > 0))
        {
            (xMin, xMax) = range;
            window = range;
        }

        foreach (var axis in axes)
        {
            YExtent(axis, annotations.Where(a => a.ExpandsDomain && a.Axis == axis), window, compact);
            if (bubbles is { } yBubbles && !axis.Log && axis.Series.Any(r => r.Data.Sizes is not null))
            {
                (axis.ExtentMin, axis.ExtentMax) = PadForBubbles(axis.Series.Where(r => r.Spec.Type == SeriesType.Scatter).ToList(), yBubbles, r => r.Upper,
                    axis.ExtentMin, axis.ExtentMax, height * 0.7, axis.Spec.Min is null, axis.Spec.Max is null);
            }
        }

        // ---------- Title and legend ----------
        var y0 = PadTop;
        var titleNodes = new List<Node>();
        if (!compact && chrome.Title && !string.IsNullOrWhiteSpace(spec.Title))
        {
            var titleHeight = options.TitleFontSize * 1.3;
            titleNodes.Add(new TextNode(spec.Title!, new PointF((float)PadLeft, (float)(y0 + titleHeight / 2)), new TextStyle("title"), Anchor.Start)
            {
                Class = "rio-title",
            });
            y0 += titleHeight + 6;
        }

        var legendEntries = resolved
            .Select(r => new LegendEntry(r.Spec.Id, r.Spec.DisplayName, r.Color, r.Spec.Type, r.Visible, r.Empty,
                ChartDescriber.DescribeSeries(spec, r.Spec, r.Data, xKind, culture, texts, Facts(r.Data)) ?? $"{r.Spec.DisplayName}: {texts.NoData}")
            {
                DirectionColors = r.Spec.Type == SeriesType.Candlestick && r.Spec.DirectionColors,
            })
            .ToList();
        var showLegend = !compact && spec.Legend.Mode switch
        {
            LegendMode.Show => legendEntries.Count > 0,
            LegendMode.Hide => false,
            _ => legendEntries.Count > 1 && chrome.Legend,
        };

        var legendPosition = FitLegendPosition(spec.Legend.Position, chrome);
        var legendItems = showLegend ? MeasureLegend(legendEntries, measure, font) : [];
        double legendTop = 0, legendBottom = 0, legendRight = 0;
        List<List<(LegendEntry Entry, double Width)>> legendRows = [];
        if (showLegend)
        {
            if (legendPosition == LegendPosition.Right)
            {
                legendRight = legendItems.Max(i => i.Width) + 16;
            }
            else
            {
                legendRows = WrapRows(legendItems, width - PadLeft - PadRight);
                var legendHeight = legendRows.Count * (lineHeight + 4);
                if (LegendTooTall(spec, legendHeight, height))
                {
                    showLegend = false;
                    legendRows = [];
                }
                else if (legendPosition == LegendPosition.Top)
                {
                    legendTop = legendHeight + 8;
                }
                else
                {
                    legendBottom = legendHeight + 8;
                }
            }
        }

        // Bubbles: their size legend sits right of the plot, under a right legend.
        if (sizeLegend is not null)
        {
            legendRight = Math.Max(legendRight, sizeLegend.Width + 16);
        }

        // ---------- Vertical layout ----------
        var visibleAxes = axes.Where(a => a.Visible).ToList();
        var xLabelHeight = compact || !chrome.XLabels ? 0 : lineHeight + 6;
        var xTitleHeight = compact || !chrome.XTitle || string.IsNullOrWhiteSpace(spec.X.Title) ? 0 : lineHeight + 4;

        // Y titles sit above the axis labels, horizontally (easier to read than rotated text).
        var yTitleHeight = !compact && chrome.YTitles && (visibleAxes.Any(a => !string.IsNullOrWhiteSpace(a.Spec.Title)) || (!hasData && !string.IsNullOrWhiteSpace(primary.Spec.Title)))
            ? lineHeight + 2
            : 0;

        // Compact: the series fill the box, inset just enough for strokes and highlight dots.
        var inset = CompactInset(drawable);
        // The first visible axis owns the grid and the zero line; the others align to its ticks.
        var gridAxis = visibleAxes.FirstOrDefault() ?? primary;
        var showYTitles = !compact && chrome.YTitles && hasData;
        double plotTop, plotBottom, plotHeight, plotLeft, plotRight, plotWidth;
        List<YAxisLayout> left, right;
        PlotInsets neededInsets;
        var labelRows = xKind == XKind.Category && hasData && !compact && chrome.XLabels;
        double? xLabelAngle = null;

        // Two passes at most: category labels that need more than one line of height (rotated, see PlanCategoryLabels)
        // only know so once the plot width is known, and the taller label row then shortens the plot.
        for (var pass = 0; ; pass++)
        {
            plotTop = compact ? inset : y0 + legendTop + yTitleHeight + font / 2; // room for the top label, centered on its grid line
            plotBottom = compact ? height - inset : height - PadBottom - legendBottom - xTitleHeight - xLabelHeight;
            plotHeight = Math.Max(compact ? 2 : 10, plotBottom - plotTop);
            plotBottom = plotTop + plotHeight;

            // ---------- Y ticks (they decide the side margins) ----------
            var yTarget = Math.Clamp((int)(plotHeight / 42), 2, 10);
            if (compact)
            {
                foreach (var axis in axes)
                {
                    TightDomain(axis);
                }
            }
            else
            {
                ComputeYTicks(gridAxis, yTarget, culture, measure, font, plotHeight);
                foreach (var axis in axes.Where(a => a != gridAxis))
                {
                    ComputeYTicks(axis, yTarget, culture, measure, font, plotHeight, axis.Visible ? gridAxis : null);
                }
            }

            left = compact || !chrome.YLabels ? [] : visibleAxes.Where(a => !a.Right).ToList();
            right = compact || !chrome.YLabels ? [] : visibleAxes.Where(a => a.Right).ToList();
            SizeAxisColumns(left, showYTitles, width * 0.2, measure, font);
            SizeAxisColumns(right, showYTitles, width * 0.2, measure, font);
            plotLeft = compact ? inset : PadLeft + ColumnsWidth(left);
            plotRight = compact ? width - inset : width - PadRight - legendRight - ColumnsWidth(right);
            neededInsets = new PlotInsets(plotLeft, width - plotRight);
            if (!compact && options.MinInsets is { } min)
            {
                plotLeft = Math.Max(plotLeft, min.Left);
                plotRight = Math.Min(plotRight, width - min.Right);
            }

            plotWidth = Math.Max(compact ? 2 : 10, plotRight - plotLeft);
            plotRight = plotLeft + plotWidth;

            if (!labelRows || pass > 0)
            {
                break;
            }

            var plan = PlanCategoryLabels(new BandScale(categories, plotLeft, plotLeft + plotWidth, hasBars ? 0.2 : 0, hasBars ? 0.1 : 0),
                spec.X.LabelRotation, measure, font, lineHeight, height);
            xLabelAngle = plan.Angle;
            if (plan.Height <= xLabelHeight)
            {
                break;
            }

            xLabelHeight = plan.Height;
        }

        var plot = new RectF((float)plotLeft, (float)plotTop, (float)plotWidth, (float)plotHeight);

        PlaceAxisColumns(left, plotLeft - LabelGap, -1);
        PlaceAxisColumns(right, plotRight + LabelGap, 1);
        foreach (var axis in axes)
        {
            axis.Scale = axis.Log
                ? ContinuousScale.Log(axis.DomainMin, axis.DomainMax, plotBottom, plotTop)
                : new ContinuousScale(axis.DomainMin, axis.DomainMax, plotBottom, plotTop);
        }


        // ---------- X scale and ticks ----------
        ContinuousScale? xScale = null;
        BandScale? xBand = null;
        var xTickNodes = new List<Node>();
        var xGrid = new List<double>();
        if (xKind == XKind.Category)
        {
            // Bars get padded bands; lines on categories use points half a step from the edges.
            xBand = new BandScale(categories, plotLeft, plotLeft + plotWidth, hasBars ? 0.2 : 0, hasBars ? 0.1 : 0);
            if (hasData && !compact)
            {
                AddCategoryLabels(PlanCategoryLabels(xBand, xLabelAngle ?? spec.X.LabelRotation, measure, font, lineHeight, height), xBand, plotBottom, xTickNodes);
            }
        }
        else
        {
            xScale = xLog ? ContinuousScale.Log(xMin, xMax, plotLeft, plotLeft + plotWidth) : new ContinuousScale(xMin, xMax, plotLeft, plotLeft + plotWidth);
            if (hasData && !compact)
            {
                xGrid = AddContinuousXLabels(xScale, xKind, spec.X, culture, measure, font, plotBottom, width, xTickNodes, time);
            }
        }

        // ---------- Static layer ----------
        var statics = new List<Node>();
        statics.AddRange(titleNodes);

        var grid = new List<Node>();
        var gridScale = gridAxis.Scale;
        var zeroInDomain = gridAxis.DomainMin < 0 && gridAxis.DomainMax > 0;
        if (gridAxis.Spec.GridLines ?? !compact)
        {
            foreach (var t in gridAxis.Ticks)
            {
                if (hasData && t == 0 && zeroInDomain)
                {
                    continue; // the zero line is drawn as the baseline
                }

                var py = (float)gridScale.Map(t);
                grid.Add(new PathNode(new PathBuilder().MoveTo(plotLeft, py).LineTo(plotRight, py).Build(), new StyleRef("grid")));
            }
        }

        foreach (var axis in visibleAxes.Where(a => a != gridAxis && a.Spec.GridLines == true))
        {
            foreach (var t in axis.Ticks)
            {
                var py = (float)axis.Scale.Map(t);
                grid.Add(new PathNode(new PathBuilder().MoveTo(plotLeft, py).LineTo(plotRight, py).Build(), new StyleRef("grid")));
            }
        }

        if (spec.X.GridLines == true)
        {
            foreach (var px in xGrid)
            {
                grid.Add(new PathNode(new PathBuilder().MoveTo(px, plotTop).LineTo(px, plotBottom).Build(), new StyleRef("grid")));
            }
        }

        statics.Add(new GroupNode(grid) { Class = "rio-grid" });

        // Compact charts have no baseline, except a zero line for bars that go negative.
        var baselineY = zeroInDomain ? gridScale.Map(0) : plotBottom;
        if (!compact || (hasBars && zeroInDomain))
        {
            statics.Add(new PathNode(new PathBuilder().MoveTo(plotLeft, baselineY).LineTo(plotRight, baselineY).Build(), new StyleRef("axis"))
            {
                Class = "rio-baseline",
            });
        }

        // An axis' labels and title in its Color, or its first visible series' color (SeriesColor); else the muted axis style.
        TextStyle AxisTextStyle(YAxisLayout axis, string part)
        {
            var color = axis.Spec.Color ?? (axis.Spec.SeriesColor ? axis.Series.FirstOrDefault(r => r.Visible && !r.Empty)?.Color : null);
            return color is null ? new TextStyle("axis-" + part)
                : styles.Add($"axis-{axis.Id}-{part}", new TextStyleDef { FontSize = (float)options.FontSize, FontWeight = part == "title" ? 500 : 400, Fill = color });
        }

        if (hasData && !compact)
        {
            foreach (var axis in chrome.YLabels ? visibleAxes : [])
            {
                var nodes = new List<Node>();
                for (var i = 0; i < axis.Ticks.Count; i++)
                {
                    nodes.Add(new TextNode(axis.Labels[i], new PointF((float)axis.LabelX, (float)axis.Scale.Map(axis.Ticks[i])), AxisTextStyle(axis, "label"),
                        axis.Right ? Anchor.Start : Anchor.End));
                }

                statics.Add(new GroupNode(nodes)
                {
                    Class = axis.Right ? "rio-axis-y rio-axis-y-right" : "rio-axis-y",
                    Key = "axis:" + axis.Id,
                    AriaLabel = AxisLabel(texts, culture, visibleAxes.Count > 1 ? $"{texts.YAxis} {axis.Id}" : texts.YAxis, axis.Spec.Title, axis.Labels),
                });
            }

            statics.Add(new GroupNode(chrome.XLabels ? xTickNodes : []) { Class = "rio-axis-x", AriaLabel = texts.XAxis + (spec.X.Title is { } xt ? ": " + xt : "") });
        }

        if (!compact && chrome.XTitle && !string.IsNullOrWhiteSpace(spec.X.Title))
        {
            statics.Add(new TextNode(spec.X.Title!, new PointF((float)(plotLeft + plotWidth / 2), (float)(plotBottom + xLabelHeight + xTitleHeight / 2)),
                new TextStyle("axis-title"), Anchor.Middle) { Class = "rio-axis-title" });
        }

        var titleY = (float)(plotTop - font / 2 - yTitleHeight / 2 - 2);
        double topRowUsed = 0;
        foreach (var axis in compact || !chrome.YTitles ? [] : hasData ? left.Concat(right) : [primary])
        {
            // Without data the primary axis' title shows on its own; with data, the axes with labels show theirs.
            var title = hasData ? axis.ShownTitle : axis.Spec.Title;
            if (!string.IsNullOrWhiteSpace(title))
            {
                var (tx, anchor) = !hasData ? (PadLeft, Anchor.Start)
                    : axis.Right ? (axis.LabelX + axis.ColumnWidth, Anchor.End)
                    : (axis.LabelX - axis.ColumnWidth, Anchor.Start);
                statics.Add(new TextNode(title, new PointF((float)tx, titleY), AxisTextStyle(axis, "title"), anchor) { Class = "rio-axis-title" });
                topRowUsed = Math.Max(topRowUsed, anchor == Anchor.End ? tx : tx + measure.Measure(title, font));
            }
        }

        // The free area at the top right (for the Reset zoom button): right of what sits above the plot.
        if (titleNodes.Count > 0)
        {
            topRowUsed = Math.Max(topRowUsed, PadLeft + measure.Measure(spec.Title!, options.TitleFontSize));
        }

        if (showLegend && legendPosition == LegendPosition.Top)
        {
            topRowUsed = Math.Max(topRowUsed, legendRows.Select(row => PadLeft + row.Sum(i => i.Width) + (row.Count - 1) * ItemGap).DefaultIfEmpty(0).Max());
        }

        var topRowBottom = plotTop - font / 2 - 1; // above the top y label, centered on the plot's top edge
        RectF? topRowFree = compact || plotTop - PadTop - font / 2 <= 1 || topRowBottom <= 0 || width - topRowUsed - 8 <= 0
            ? null
            : new RectF((float)(topRowUsed + 8), 0, (float)(width - topRowUsed - 8), (float)topRowBottom);

        if (showLegend)
        {
            statics.Add(BuildLegend(legendItems, legendRows, legendPosition, width - PadRight - legendRight + 16, plot, y0,
                plotBottom + xLabelHeight + xTitleHeight + 8, lineHeight, styles, texts));
        }

        if (sizeLegend is not null)
        {
            var sizeTop = plot.Y + (showLegend && legendPosition == LegendPosition.Right ? legendItems.Count * (lineHeight + 4) + 12 : 0);
            if (sizeTop + sizeLegend.Height <= plot.Bottom + xLabelHeight)
            {
                statics.Add(BuildSizeLegend(sizeLegend, width - PadRight - legendRight + 16, sizeTop, styles));
            }
        }

        // ---------- Series ----------
        var slotKeys = drawable.Where(r => r.Type.UsesSlots).Select(SlotKey).Distinct().ToList();
        var fullSlotWidth = xKind == XKind.Category
            ? xBand!.Bandwidth
            : barSpacing > 0 ? Math.Abs(xScale!.Map(xScale.DomainMin + barSpacing) - xScale.Map(xScale.DomainMin)) * 0.8 : plotWidth * 0.1;
        var slotWidth = SlotGroupWidth(fullSlotWidth, plotWidth, slotKeys.Count, drawable.Where(r => r.Type.UsesSlots));
        var threshold = options.DownsampleThreshold ?? Math.Max(500, (int)(plotWidth * 2));

        var hitTest = new HitTestIndex();
        var tooltipSeries = new List<TooltipSeries>();
        var seriesNodes = new List<(int Order, int Index, Node Node)>();
        var selectedMarks = new List<Node>();
        var labelAnchors = new List<(Resolved Series, List<LabelAnchor> Anchors)>();
        var formatX = TimeFormat(xKind, drawable.SelectMany(r => r.Data.X).Take(5000), culture, spec.X.TooltipFormat ?? spec.X.Format, time, options.ViewerTimeZone,
            showOffset: spec.X.TooltipFormat is null);
        // Series with the same x values (common: several measurements per timestamp) share their
        // tooltip x labels by point index, so each date is formatted once, not once per series. Not
        // for large series: they are downsampled per series (few shared points), and the array would
        // land on the large object heap.
        var withData = drawable.Where(r => r.Data.Count > 0).ToList();
        var sharedXLabels = xKind != XKind.Category && withData.Count > 1 && withData[0].Data.Count <= 10_000
            && withData.All(r => r.Data.X.AsSpan().SequenceEqual(withData[0].Data.X))
            ? new string?[withData[0].Data.Count]
            : null;
        foreach (var r in drawable)
        {
            var axisScale = r.Axis.Scale;
            var context = new SeriesBuildContext
            {
                Spec = r.Spec,
                Data = r.Data,
                Color = r.Color,
                ColorIndex = r.ColorIndex,
                Styles = styles,
                Plot = plot,
                PixelX = new PixelPositions(r.Data, xKind, xScale, xBand, categoryIndex),
                Upper = r.Upper,
                Lower = r.Lower,
                MapY = axisScale.Map,
                LogAxes = xScale?.IsLog == true || axisScale.IsLog,
                LogY = axisScale.IsLog,
                ZeroY = Math.Clamp(axisScale.Map(0), plotTop, plotBottom),
                SlotWidth = slotWidth,
                SlotIndex = r.Type.UsesSlots ? slotKeys.IndexOf(SlotKey(r)) : 0,
                SlotCount = Math.Max(1, slotKeys.Count),
                DownsampleThreshold = threshold,
                XWindow = window,
                Bubbles = bubbles,
                Time = time,
            };
            r.Type.Build(context);
            if (context.Hits is { } drawnHits && HasErrors(r) && ErrorBars(r, context, drawnHits, styles) is { } errorBars)
            {
                context.Output.Add(errorBars);
            }
            else if (HasErrorData(r) && !HasErrors(r))
            {
                warnings.Add(r.Percent
                    ? $"series '{r.Spec.Id}': error bars are not drawn on series stacked to 100 %."
                    : $"series '{r.Spec.Id}': only bar, line and scatter series draw error bars; they are ignored.");
            }

            if (context.Hits is { } hits)
            {
                hitTest.Add(hits);
                tooltipSeries.Add(TooltipFor(r, hitTest.Series[^1], context, formatX, culture, texts, xKind, xBand, plotWidth, spec.X.Title, sharedXLabels));
            }

            if (context.Labels.Count > 0)
            {
                labelAnchors.Add((r, context.Labels));
            }

            if (options.SelectedPoints is { } selected && selected.TryGetValue(r.Spec.Id, out var picked) && picked.Count > 0)
            {
                selectedMarks.AddRange(SelectionMarks(r, context, picked, styles));
            }

            var order = r.Spec.ZIndex ?? r.Type.DefaultLayer * 1000;
            seriesNodes.Add((order, r.ColorIndex, new GroupNode(context.Output)
            {
                Key = "series:" + r.Spec.Id,
                Class = "rio-series rio-series-" + r.Spec.Type.ToString().ToLowerInvariant(),
                AriaLabel = texts.FillCount(nameof(ChartTexts.SeriesPoints), Facts(r.Data).Count, culture, r.Spec.DisplayName, Facts(r.Data).Count),
            }));
        }

        // ---------- Annotations and labels ----------
        var placedLabels = new List<RectF>();
        var dragHandles = new List<DragHandle>();
        var below = new List<Node>();
        var above = new List<Node>();
        var labelNodes = new List<Node>();
        if (hasData)
        {
            // Compact: annotations without their text (a band or line still shows the normal range).
            BuildAnnotations(compact ? annotations.Select(a => a.WithoutLabel()).ToList() : annotations, plot, xKind, xScale, xBand, culture, measure, font, styles,
                options.InteractiveAnnotations, below, above, placedLabels, dragHandles, spec.X.Format, time);
            if (!compact)
            {
                labelNodes.AddRange(BuildDataLabels(labelAnchors, plot, culture, measure, font, styles, placedLabels, warnings));
            }
        }

        var plotNodes = new List<Node>(below);
        if (hasData && xScale is not null && options.SelectedRange is { } selectedRange && SelectionBand(selectedRange, xScale, plot, styles) is { } band)
        {
            plotNodes.Add(band);
        }

        plotNodes.AddRange(seriesNodes.OrderBy(t => t.Order).ThenBy(t => t.Index).Select(t => t.Node));
        plotNodes.AddRange(above);
        if (selectedMarks.Count > 0)
        {
            plotNodes.Add(new GroupNode(selectedMarks) { Key = "selection:points", Class = "rio-selected-points" });
        }
        var seriesLayer = new GroupNode(
        [
            new GroupNode(plotNodes, Clip: Inflate(plot, (float)Math.Max(2, scatter.Count > 0
                ? scatter.Max(r => r.Data.Sizes is not null && bubbles is { } b ? b.MaxRadius(r.Spec) : ScatterSeriesType.Radius(r.Spec)) + 1
                : 0))) { Class = "rio-plot" },
            new GroupNode(labelNodes) { Class = "rio-labels" },
        ])
        {
            Class = "rio-series-layer",
        };

        // ---------- Overlay ----------
        var overlay = new List<Node>();
        if (!hasData && !compact)
        {
            overlay.Add(new TextNode(EmptyMessage(resolved, texts), new PointF(plot.CenterX, plot.CenterY), new TextStyle("empty"), Anchor.Middle) { Class = "rio-empty" });
        }

        var scene = new Scene.Scene(new GroupNode(statics) { Class = "rio-static" }, seriesLayer, new GroupNode(overlay) { Class = "rio-overlay" })
        {
            Width = (float)width,
            Height = (float)height,
            Styles = styles,
            Description = compact
                ? ChartDescriber.DescribeCompact(spec, resolved.Select(r => (r.Spec, r.Data, r.Visible)).ToList(), xKind, culture, texts, Facts)
                : ChartDescriber.Describe(spec, resolved.Select(r => (r.Spec, r.Data, r.Visible)).ToList(), xKind, culture, texts, Facts, time),
        };

        var bandWidth = xKind == XKind.Category ? xBand!.Step : hasBars ? fullSlotWidth / 0.8 : 0;
        return new ChartLayoutResult
        {
            Scene = scene,
            Plot = plot,
            HitTest = hitTest,
            // Scatter points are found in 2D (nearest point), not by x, so a chart with them is not shared.
            Tooltip = new TooltipModel(plot, spec.Tooltip.Enabled && hasData, spec.Tooltip.Shared && scatter.Count == 0, (float)bandWidth, tooltipSeries),
            Legend = legendEntries,
            Warnings = warnings,
            XKind = xKind,
            XScale = xScale,
            XBand = xBand,
            XExtent = fullX,
            Zoomed = window is not null,
            TopRowFree = topRowFree,
            Time = time,
            YScale = gridAxis.Scale,
            YScales = axes.ToDictionary(a => a.Id, a => a.Scale, StringComparer.Ordinal),
            HasData = hasData,
            DragHandles = dragHandles,
            NeededInsets = compact ? null : neededInsets,
        };
    }

    // ---------- Selection ----------

    /// <summary>The selected x range: a light band in the first series color with edge lines, like the zoom drag.</summary>
    private static GroupNode? SelectionBand((double Min, double Max) range, ContinuousScale x, RectF plot, StyleTable styles)
    {
        if (!double.IsFinite(range.Min) || !double.IsFinite(range.Max))
        {
            return null;
        }

        var x0 = Math.Clamp(x.Map(Math.Min(range.Min, range.Max)), plot.X, plot.Right);
        var x1 = Math.Clamp(x.Map(Math.Max(range.Min, range.Max)), plot.X, plot.Right);
        if (x1 - x0 < 0.5)
        {
            return null; // outside the visible range
        }

        var fill = styles.Add("selection", new Style { Fill = ThemeTokens.Series(0), FillOpacity = 0.1f });
        var edge = styles.Add("selection-edge", new Style { Stroke = ThemeTokens.Series(0), StrokeWidth = 1, Opacity = 0.6f });
        return new GroupNode(
        [
            new RectNode(new RectF((float)x0, plot.Y, (float)(x1 - x0), plot.Height), fill),
            new PathNode(new PathBuilder().MoveTo(x0, plot.Y).LineTo(x0, plot.Bottom).MoveTo(x1, plot.Y).LineTo(x1, plot.Bottom).Build(), edge),
        ])
        {
            Key = "selection:range",
            Class = "rio-selection",
        };
    }

    /// <summary>Selected points: a marker in the series color (ringed in the surface color), or an outline around a bar.</summary>
    private static IEnumerable<Node> SelectionMarks(Resolved r, SeriesBuildContext c, IReadOnlyCollection<int> picked, StyleTable styles)
    {
        var bar = r.Spec.Type is SeriesType.Bar or SeriesType.BoxPlot or SeriesType.Candlestick or SeriesType.Waterfall or SeriesType.Histogram;
        if (c.CandleGroups is { } merged && merged.Any(g => g.First != g.Last))
        {
            yield break; // merged candles (zoomed out): a single period has no candle of its own to outline
        }

        var style = bar
            ? styles.Add("selected-bar", new Style { Stroke = ThemeTokens.Text, StrokeWidth = 1.5f })
            : styles.Add($"series-{r.ColorIndex}-selected", new Style { Fill = r.Color, Stroke = ThemeTokens.Surface, StrokeWidth = 2 });
        foreach (var i in picked.Distinct().Order())
        {
            if (i < 0 || i >= r.Data.Count || i >= c.Upper.Length)
            {
                continue;
            }

            var x = c.PixelX[i];
            var top = c.Upper[i];
            if (!double.IsFinite(x) || !double.IsFinite(top))
            {
                continue; // a gap: nothing drawn there
            }

            var y = c.MapY(top);
            if (bar)
            {
                var y0 = r.Data.Box is { } box ? c.MapY(box.Q1[i]) : c.Lower is { } lower && double.IsFinite(lower[i]) ? c.MapY(lower[i]) : c.ZeroY;
                y = r.Data.Box is { } b ? c.MapY(b.Q3[i]) : y;
                if (r.Data.Candles is { } candle)
                {
                    (y, y0) = (c.MapY(candle.High[i]), c.MapY(candle.Low[i])); // the whole candle, wick included
                }
                var left = x + c.BarOffset - c.BarWidth / 2;
                yield return new RectNode(new RectF((float)(left - 1.5), (float)(Math.Min(y, y0) - 1.5), (float)(c.BarWidth + 3), (float)(Math.Abs(y0 - y) + 3)), style)
                {
                    CornerRadius = 3,
                    Class = "rio-selected-bar",
                };
            }
            else
            {
                yield return new PathNode(new PathBuilder().MoveTo(x, y - 5).Arc(x, y, 5, 0, Math.PI * 2).Close().Build(), style) { Class = "rio-selected-point" };
            }
        }
    }

    // Box plots, candlesticks and waterfalls are never stacked: each series gets its own slot even with a Stack name.
    private static string SlotKey(Resolved r) => r.StackGroup is { } stack
        ? $"stack|{r.Axis.Id}|{stack}"
        : "series|" + r.Spec.Id;

    /// <summary>
    /// The stack a series is in (see <see cref="Resolved.StackGroup"/>). Range areas, box plots, candlesticks and waterfalls are
    /// never stacked; on a percent axis, lines, areas and bars without a stack name share one.
    /// </summary>
    private static string? StackGroupOf(Resolved r, int index, List<string> warnings)
    {
        var percent = r.Axis.Spec.Stacking == StackMode.Percent;
        var named = r.Spec.Stack is { Length: > 0 } stack ? stack : null;
        if (r.Spec.Type is SeriesType.Line or SeriesType.Area or SeriesType.Bar)
        {
            return named ?? (percent ? "" : null);
        }

        var stackable = r.Spec.Type is not (SeriesType.RangeArea or SeriesType.Dumbbell or SeriesType.BoxPlot or SeriesType.Candlestick or SeriesType.Waterfall or SeriesType.Histogram);
        if (percent && (named is null || !stackable) && r.Spec.Type is SeriesType.Scatter or SeriesType.RangeArea or SeriesType.Dumbbell or SeriesType.BoxPlot or SeriesType.Candlestick or SeriesType.Waterfall or SeriesType.Histogram)
        {
            warnings.Add($"series[{index}]: a percent axis stacks line, area and bar series; this {r.Spec.Type} series is drawn with its own values.");
        }

        return stackable ? named : null;
    }

    // ---------- Styles ----------

    private static StyleTable CreateStyles(LayoutOptions options)
    {
        var small = (float)Math.Max(9, options.FontSize - 1);
        var styles = new StyleTable();
        styles.Add("grid", new Style { Stroke = ThemeTokens.Grid, StrokeWidth = 1 });
        styles.Add("axis", new Style { Stroke = ThemeTokens.Axis, StrokeWidth = 1 });
        styles.Add("axis-label", new TextStyleDef { FontSize = (float)options.FontSize, Fill = ThemeTokens.TextMuted });
        styles.Add("axis-title", new TextStyleDef { FontSize = (float)options.FontSize, FontWeight = 500, Fill = ThemeTokens.TextMuted });
        styles.Add("legend-label", new TextStyleDef { FontSize = (float)options.FontSize, Fill = ThemeTokens.Text });
        styles.Add("title", new TextStyleDef { FontSize = (float)options.TitleFontSize, FontWeight = 600, Fill = ThemeTokens.Text });
        styles.Add("empty", new TextStyleDef { FontSize = (float)options.FontSize + 1, Fill = ThemeTokens.TextMuted });
        styles.Add("legend-hit", new Style { Fill = "transparent" });
        styles.Add("data-label", new TextStyleDef { FontSize = small, Fill = ThemeTokens.Text });
        styles.Add("data-label-inverse", new TextStyleDef { FontSize = small, FontWeight = 500, Fill = ThemeTokens.DataLabelInverse });
        styles.Add("data-label-dark", new TextStyleDef { FontSize = small, FontWeight = 500, Fill = ThemeTokens.DataLabelDark });
        styles.Add("annotation-line", new Style { Stroke = ThemeTokens.Annotation, StrokeWidth = 1.25f, DashArray = "4 3" });
        styles.Add("annotation-band", new Style { Fill = ThemeTokens.Annotation, FillOpacity = 0.1f });
        styles.Add("annotation-label", new TextStyleDef { FontSize = small, Fill = ThemeTokens.Annotation });
        styles.Add("annotation-grip", new Style { Stroke = "transparent", StrokeWidth = 12 });
        styles.Add("callout-label", new TextStyleDef { FontSize = small, Fill = ThemeTokens.Text });
        return styles;
    }

    // ---------- Series helpers ----------

    /// <summary>
    /// Zero and negative values have no place on a log axis: series values become gaps (on copies; the data stays as
    /// it is) and lines and points of annotations are not drawn, each with one warning.
    /// </summary>
    private static void LeaveOutNonPositive(List<Resolved> drawable, List<ResolvedAnnotation> annotations, List<string> warnings, bool xLog)
    {
        if (xLog)
        {
            // Points at x zero or below have no place on a log x axis: gaps, like values of zero or below on a log y axis.
            foreach (var r in drawable)
            {
                var count = 0;
                double[]? upper = null, lower = null;
                for (var i = 0; i < r.Data.Count; i++)
                {
                    if (r.Data.X[i] <= 0 && double.IsFinite(r.Upper[i]))
                    {
                        upper ??= (double[])r.Upper.Clone();
                        upper[i] = double.NaN;
                        if (r.Lower is not null)
                        {
                            lower ??= (double[])r.Lower.Clone();
                            lower[i] = double.NaN;
                        }

                        count++;
                    }
                }

                if (count > 0)
                {
                    r.Upper = upper!;
                    r.Lower = lower ?? r.Lower;
                    warnings.Add($"series '{r.Spec.Id}': {count} point(s) at x zero or below are left out; a log x axis has no place for them.");
                }
            }

            foreach (var a in annotations.Where(a => (a.IsX || a.IsPoint) && (!(a.A > 0) || (a.IsBand && !(a.B > 0)))))
            {
                warnings.Add($"annotation '{a.Id}': an x value of zero or below has no place on a log x axis; it is not drawn.");
            }
        }

        foreach (var r in drawable.Where(r => r.Axis.Log))
        {
            var count = 0;
            r.Upper = Gaps(r.Upper);
            if (r.Spec.Type is SeriesType.RangeArea or SeriesType.Dumbbell or SeriesType.Waterfall && r.Lower is not null)
            {
                r.Lower = Gaps(r.Lower); // the low values; stack bases (zero for the first series) stay
            }

            if (count > 0)
            {
                warnings.Add($"series '{r.Spec.Id}': {count} value(s) of zero or below are left out; a log axis has no place for them.");
            }

            double[] Gaps(double[] values)
            {
                double[]? copy = null;
                for (var i = 0; i < values.Length; i++)
                {
                    if (values[i] <= 0)
                    {
                        copy ??= (double[])values.Clone();
                        copy[i] = double.NaN;
                        count++;
                    }
                }

                return copy ?? values;
            }
        }

        foreach (var a in annotations.Where(a => !a.IsX && a.Axis is { Log: true }))
        {
            if ((a.IsPoint && !(a.B > 0)) || (!a.IsPoint && !a.IsBand && !(a.A > 0)))
            {
                warnings.Add($"annotation '{a.Id}': a value of zero or below has no place on a log axis; it is not drawn.");
            }
        }
    }

    private static XKind ResolveXKind(ScaleType scale, List<Resolved> series) => scale switch
    {
        ScaleType.Linear => XKind.Numeric,
        ScaleType.Time => XKind.Time,
        ScaleType.Band => XKind.Category,
        _ => series.FirstOrDefault(r => r.Data.Count > 0)?.Data.Kind ?? XKind.Numeric,
    };

    private static List<string> MergeCategories(List<Resolved> series)
    {
        // All series (also hidden ones), so bands do not move when a series is toggled.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var r in series.Where(r => r.Data.Kind == XKind.Category))
        {
            foreach (var c in r.Data.Categories)
            {
                if (seen.Add(c))
                {
                    result.Add(c);
                }
            }
        }

        // A series with Sort orders the categories by its values (after aggregation, e.g. counts): the first such series
        // decides. Categories it has no value for keep their order after the others.
        if (series.FirstOrDefault(r => r.Spec.Sort is not null && r.Data.Kind == XKind.Category) is { } by)
        {
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            for (var i = 0; i < by.Data.Count; i++)
            {
                if ((int)by.Data.X[i] is var c && c >= 0 && c < by.Data.Categories.Count && double.IsFinite(by.Data.Y[i]))
                {
                    var name = by.Data.Categories[c];
                    values[name] = values.GetValueOrDefault(name) + by.Data.Y[i];
                }
            }

            var descending = by.Spec.Sort == ChartSort.Descending;
            result = [.. result
                .Select((c, i) => (c, i, Has: values.TryGetValue(c, out var v), V: v))
                .OrderBy(t => t.Has ? 0 : 1)
                .ThenBy(t => descending ? -t.V : t.V)
                .ThenBy(t => t.i)
                .Select(t => t.c)];
        }

        return result;
    }

    /// <summary>The first x value that appears more than once (for a warning), or null. Numbers and times are sorted.</summary>
    private static string? RepeatsX(SeriesData d)
    {
        if (d.Kind == XKind.Category)
        {
            if (d.Count <= d.Categories.Count)
            {
                return null;
            }

            var seen = new HashSet<double>();
            for (var i = 0; i < d.Count; i++)
            {
                if (!seen.Add(d.X[i]) && (int)d.X[i] is var c && c >= 0 && c < d.Categories.Count)
                {
                    return $"'{d.Categories[c]}'";
                }
            }

            return null;
        }

        for (var i = 1; i < d.Count; i++)
        {
            if (d.X[i] == d.X[i - 1])
            {
                return d.Kind == XKind.Time
                    ? TimeValue.ToDateTime(d.X[i]).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    : d.X[i].ToString(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    /// <summary>
    /// Stacks series that share a stack name, axis and kind (bars or not). Positive values stack
    /// up from zero and negative values down, per x position. Hidden series are left out.
    /// </summary>
    private static void ApplyStacking(List<Resolved> drawable, XKind kind, Dictionary<string, int> categoryIndex)
    {
        var groups = drawable
            .Where(r => r.StackGroup is not null)
            .GroupBy(r => (r.Axis.Id, r.StackGroup, r.Type.UsesSlots));
        foreach (var group in groups)
        {
            if (kind != XKind.Category && StackByIndex(group.ToList()))
            {
                continue;
            }

            // Totals per x position for a percent axis: the positive sum plus the size of the negative one.
            var totals = new Dictionary<double, double>();

            var positive = new Dictionary<double, double>();
            var negative = new Dictionary<double, double>();
            var broken = new HashSet<double>(); // x values where a lower line or area has a gap
            var breaks = !group.Key.UsesSlots; // bars: a missing value is an empty segment, the bars above stay
            foreach (var r in group)
            {
                var d = r.Data;
                var upper = new double[d.Count];
                var lower = new double[d.Count];
                for (var i = 0; i < d.Count; i++)
                {
                    var v = d.Y[i];
                    var key = kind == XKind.Category
                        ? (int)d.X[i] is var local && local >= 0 && local < d.Categories.Count && categoryIndex.TryGetValue(d.Categories[local], out var g) ? g : double.NaN
                        : d.X[i];
                    if (!double.IsFinite(key))
                    {
                        upper[i] = lower[i] = double.NaN;
                        continue;
                    }

                    // A gap leaves the series above it without a base there: they get a gap too.
                    if (!double.IsFinite(v) || broken.Contains(key))
                    {
                        upper[i] = lower[i] = double.NaN;
                        if (breaks)
                        {
                            broken.Add(key);
                        }

                        continue;
                    }

                    var sums = v >= 0 ? positive : negative;
                    sums.TryGetValue(key, out var start);
                    lower[i] = start;
                    upper[i] = start + v;
                    sums[key] = start + v;
                    totals[key] = totals.GetValueOrDefault(key) + Math.Abs(v);
                }

                r.Upper = upper;
                r.Lower = lower;
            }

            foreach (var r in group.Where(r => r.Percent))
            {
                for (var i = 0; i < r.Data.Count; i++)
                {
                    var key = kind == XKind.Category
                        ? (int)r.Data.X[i] is var local && local >= 0 && local < r.Data.Categories.Count && categoryIndex.TryGetValue(r.Data.Categories[local], out var g) ? g : double.NaN
                        : r.Data.X[i];
                    if (double.IsFinite(key) && totals.TryGetValue(key, out var total))
                    {
                        (r.Upper[i], r.Lower![i]) = (Share(r.Upper[i], total), Share(r.Lower![i], total));
                    }
                }
            }
        }
    }

    /// <summary>A stacked value as a share of its stack's total (0 when the total is 0), kept within -1 and 1.</summary>
    private static double Share(double value, double total) => !double.IsFinite(value) ? value : total > 0 ? Math.Clamp(value / total, -1, 1) : 0;

    /// <summary>
    /// The usual case: every series in the stack has the same x values (one row per x with a column
    /// per series), so the running sums are kept per index instead of per x value. False (nothing
    /// done) when the x values differ.
    /// </summary>
    private static bool StackByIndex(List<Resolved> group)
    {
        var x = group[0].Data.X;
        if (group.Any(r => r.Data.X.Length != x.Length || (!ReferenceEquals(r.Data.X, x) && !r.Data.X.AsSpan().SequenceEqual(x))))
        {
            return false;
        }

        var positive = new double[x.Length];
        var negative = new double[x.Length];
        var broken = new bool[x.Length]; // a lower line or area has a gap here
        var breaks = !group[0].Type.UsesSlots; // bars: a missing value is an empty segment, the bars above stay
        foreach (var r in group)
        {
            var y = r.Data.Y;
            var upper = new double[y.Length];
            var lower = new double[y.Length];
            for (var i = 0; i < y.Length; i++)
            {
                var v = y[i];
                if (!double.IsFinite(v) || broken[i])
                {
                    upper[i] = lower[i] = double.NaN;
                    broken[i] = breaks; // the lines and areas above have no base here
                    continue;
                }

                ref var sum = ref v >= 0 ? ref positive[i] : ref negative[i];
                lower[i] = sum;
                upper[i] = sum + v;
                sum += v;
            }

            r.Upper = upper;
            r.Lower = lower;
        }

        if (group[0].Percent)
        {
            foreach (var r in group)
            {
                for (var i = 0; i < x.Length; i++)
                {
                    var total = positive[i] - negative[i];
                    (r.Upper[i], r.Lower![i]) = (Share(r.Upper[i], total), Share(r.Lower![i], total));
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The domain of a log x axis: the positive x values with a value (and annotations there), rounded out to
    /// powers of ten unless Min or Max is set (and positive).
    /// </summary>
    private static (double Min, double Max, double BarSpacing) LogXExtent(List<Resolved> series, AxisOptions axis, IEnumerable<ResolvedAnnotation> annotations)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (var r in series)
        {
            for (var i = 0; i < r.Data.Count; i++)
            {
                if (r.Data.X[i] > 0 && double.IsFinite(r.Upper[i]))
                {
                    (min, max) = (Math.Min(min, r.Data.X[i]), Math.Max(max, r.Data.X[i]));
                }
            }
        }

        foreach (var v in annotations.SelectMany(a => a.XValues).Where(v => v > 0 && double.IsFinite(v)))
        {
            (min, max) = (Math.Min(min, v), Math.Max(max, v));
        }

        if (!double.IsFinite(min))
        {
            (min, max) = (1, 10);
        }

        var (niceMin, niceMax) = NiceTicks.LogDomain(min, max);
        var lo = axis.Min is > 0 and var fixedMin ? fixedMin : niceMin;
        var hi = axis.Max is > 0 and var fixedMax ? fixedMax : niceMax;
        return (lo, hi > lo ? hi : lo * 10, 0);
    }

    private static (double Min, double Max, double BarSpacing) XExtent(List<Resolved> series, AxisOptions axis, XKind kind, bool hasBars,
        IEnumerable<ResolvedAnnotation> annotations)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        var spacing = double.PositiveInfinity;
        foreach (var r in series)
        {
            // X is sorted: the extent runs from the first to the last point with a value.
            var d = r.Data;
            var first = 0;
            while (first < d.Count && !double.IsFinite(d.Y[first]))
            {
                first++;
            }

            if (first < d.Count)
            {
                var last = d.Count - 1;
                while (!double.IsFinite(d.Y[last]))
                {
                    last--;
                }

                min = Math.Min(min, d.X[first]);
                max = Math.Max(max, d.X[last]);
            }

            if (r.Spec.Type == SeriesType.Histogram && HistogramSeriesType.WidthOf(r.Spec, d) is > 0 and var binWidth)
            {
                spacing = Math.Min(spacing, binWidth); // the bins' edges, also for a single bin
            }
            else if (r.Type.UsesSlots)
            {
                for (var i = 1; i < d.Count; i++)
                {
                    var dx = d.X[i] - d.X[i - 1];
                    if (dx > 0)
                    {
                        spacing = Math.Min(spacing, dx);
                    }
                }
            }
        }

        if (double.IsFinite(min))
        {
            foreach (var a in annotations)
            {
                foreach (var v in a.XValues)
                {
                    min = Math.Min(min, v);
                    max = Math.Max(max, v);
                }
            }
        }

        if (!double.IsFinite(min))
        {
            (min, max) = kind == XKind.Time ? (0, 86_400_000) : (0, 1);
        }

        if (max == min && !(hasBars && spacing > 0 && double.IsFinite(spacing)))
        {
            var pad = kind == XKind.Time ? 3_600_000 : (min == 0 ? 1 : Math.Abs(min) * 0.1);
            min -= pad;
            max += pad;
        }

        if (!double.IsFinite(spacing))
        {
            spacing = 0;
        }

        if (hasBars && spacing > 0)
        {
            // Bars on a continuous axis: half a slot on each side so edge bars are not cut.
            min -= spacing / 2;
            max += spacing / 2;
        }

        // One end fixed and every value beyond it: the axis keeps the data's width from the fixed end, not an inverted range.
        var span = max > min ? max - min : 1;
        var (lo, hi) = (axis.Min ?? min, axis.Max ?? max);
        if (double.IsFinite(lo) && double.IsFinite(hi) && !(hi > lo))
        {
            (lo, hi) = axis.Max is null ? (lo, lo + span) : axis.Min is null ? (hi - span, hi) : (lo, hi);
        }

        return (lo, hi, spacing);
    }

    /// <summary>
    /// Widens a range so the bubbles at its ends fit: each bubble's radius in data units, on a plot
    /// about <paramref name="plotPixels"/> long. Only the ends that are not set explicitly move.
    /// </summary>
    private static (double Min, double Max) PadForBubbles(List<Resolved> series, BubbleScale scale, Func<Resolved, double[]> values,
        double min, double max, double plotPixels, bool padMin, bool padMax)
    {
        var span = max - min;
        if (!(span > 0) || !double.IsFinite(span) || plotPixels <= 0)
        {
            return (min, max);
        }

        var perPixel = span / plotPixels;
        var (newMin, newMax) = (min, max);
        foreach (var r in series)
        {
            if (r.Data.Sizes is not { } sizes)
            {
                continue;
            }

            var v = values(r);
            for (var i = 0; i < sizes.Length && i < v.Length; i++)
            {
                var radius = scale.Radius(r.Spec, sizes[i]);
                if (double.IsFinite(radius) && double.IsFinite(v[i]) && double.IsFinite(r.Upper[i]))
                {
                    var pad = (radius + 2) * perPixel;
                    newMin = Math.Min(newMin, v[i] - pad);
                    newMax = Math.Max(newMax, v[i] + pad);
                }
            }
        }

        return (padMin ? newMin : min, padMax ? newMax : max);
    }

    private static TooltipSeries TooltipFor(Resolved r, HitTestIndex.SeriesHits hits, SeriesBuildContext context, Func<double, string> formatX,
        CultureInfo culture, ChartTexts texts, XKind kind, BandScale? band, double plotWidth, string? xAxisTitle = null, string?[]? sharedXLabels = null)
    {
        var n = hits.X.Length;
        var xLabels = new string[n];
        var values = new string[n];
        var format = ValueFormat.Of(r.Spec.Format, r.Axis.Spec);
        var xTitle = r.Spec.Type == SeriesType.Scatter && !string.IsNullOrWhiteSpace(xAxisTitle) ? xAxisTitle : null;
        var boxParts = r.Spec.Type == SeriesType.BoxPlot && r.Data.Box is not null ? new string[n][] : null;
        var candleParts = r.Spec.Type == SeriesType.Candlestick && r.Data.Candles is not null && context.CandleGroups is { } cg && cg.Count == n ? new string[n][] : null;
        var candleColors = candleParts is not null && r.Spec.DirectionColors ? new string[n] : null; // the tooltip dot as the candle
        var waterfall = r.Spec.Type == SeriesType.Waterfall;
        var binWidth = r.Spec.Type == SeriesType.Histogram && kind != XKind.Category ? HistogramSeriesType.WidthOf(r.Spec, r.Data) : double.NaN;
        var waterfallParts = waterfall ? new string[n][] : null;
        var sizeParts = r.Spec.Type == SeriesType.Scatter && r.Data.Sizes is not null ? new string[n][] : null;
        var waterfallColors = waterfall ? new string[n] : null; // the tooltip dot as the bar
        for (var i = 0; i < n; i++)
        {
            var j = hits.Index[i];
            var x = r.Data.X[j];
            xLabels[i] = kind == XKind.Category
                ? ((int)x is var c && c >= 0 && c < r.Data.Categories.Count ? r.Data.Categories[c] : "")
                : sharedXLabels is not null ? sharedXLabels[j] ??= formatX(x) : formatX(x);
            if (xTitle is not null)
            {
                xLabels[i] = $"{xTitle}: {xLabels[i]}"; // scatter: say what the x value is
            }

            if (double.IsFinite(binWidth))
            {
                // A bin: from its lower to its upper edge.
                xLabels[i] = $"{formatX(Scales.NiceTicks.Clean(x - binWidth / 2))} – {formatX(Scales.NiceTicks.Clean(x + binWidth / 2))}";
            }
            values[i] = r.Spec.Type == SeriesType.RangeArea && r.Data.Low is { } low
                ? $"{ValueFormatter.Format(low[j], culture, format)} – {ValueFormatter.Format(r.Data.Y[j], culture, format)}"
                : r.Spec.Type == SeriesType.Dumbbell
                ? DumbbellSeriesType.Text(r.Data.Low?[j] ?? double.NaN, r.Data.Y[j], culture, format)
                : ValueFormatter.Format(r.Data.Y[j], culture, format);
            if (HasErrors(r))
            {
                values[i] += ErrorText(r, j, culture, format);
            }

            if (r.Percent && r.Lower is { } lower && double.IsFinite(r.Upper[j] - lower[j]))
            {
                values[i] += $" ({(r.Upper[j] - lower[j]).ToString("P0", culture)})"; // the value and its share
            }

            if (waterfallParts is not null)
            {
                // A change with its sign and the running total after it; a total bar shows the total alone.
                var total = r.Data.Totals is { } totals && totals[j];
                var change = r.Data.Y[j];
                values[i] = total ? ValueFormatter.Format(r.Upper[j], culture, format) : (change > 0 ? "+" : "") + ValueFormatter.Format(change, culture, format);
                waterfallParts[i] = [total ? "" : ValueFormatter.Format(r.Upper[j], culture, format)];
                waterfallColors![i] = WaterfallSeriesType.ColorOf(r.Data, j, r.Color);
            }

            if (sizeParts is not null)
            {
                sizeParts[i] = [ValueFormatter.Format(r.Data.Sizes![j], culture, r.Spec.SizeFormat)];
            }

            if (boxParts is not null && r.Data.Box is { } box)
            {
                boxParts[i] = BoxPartValues(box, j, r.Data.Y[j], culture, format);
            }

            if (candleParts is not null && r.Data.Candles is { } candles)
            {
                // A merged candle (zoomed out) stands for several periods: say from when to when.
                var (first, last) = context.CandleGroups![i];
                var candle = first == last ? candles.At(j, r.Data.Y[j])
                    : CandleSummary.Merge(Enumerable.Range(first, last - first + 1).Select(k => candles.At(k, r.Data.Y[k])));
                if (first != last && kind != XKind.Category)
                {
                    xLabels[i] = $"{formatX(r.Data.X[first])} – {xLabels[i]}";
                }

                values[i] = ValueFormatter.Format(candle.Close, culture, format);
                candleParts[i] = CandlePartValues(candle, culture, format, percent: r.Spec.DirectionColors);
                if (candleColors is not null)
                {
                    candleColors[i] = candle.Close >= candle.Open ? ThemeTokens.Positive : ThemeTokens.Negative;
                }
            }
        }

        float snap;
        if (kind == XKind.Category)
        {
            snap = (float)(band!.Step / 2);
        }
        else if (r.Type.UsesSlots)
        {
            snap = (float)Math.Max(1, context.SlotWidth / 0.8 / 2);
        }
        else if (n > 1)
        {
            var gaps = new float[n - 1];
            for (var i = 1; i < n; i++)
            {
                gaps[i - 1] = hits.X[i] - hits.X[i - 1];
            }

            Array.Sort(gaps);
            snap = Math.Max(1, gaps[gaps.Length / 2] / 2 + 0.5f);
        }
        else
        {
            snap = (float)plotWidth;
        }

        return new TooltipSeries(r.Spec.Id, r.Spec.DisplayName, r.Color, r.Spec.Type, snap, (float)context.BarWidth, (float)context.BarOffset,
            hits.X, hits.Y, hits.Index, xLabels, values)
        {
            Step = r.Spec.Curve == CurveType.Step && !r.Type.UsesSlots && kind != XKind.Category,
            PartLabels = boxParts is not null ? BoxPartLabels(texts) : candleParts is not null ? CandlePartLabels(texts) : waterfallParts is not null ? [texts.Total]
                : sizeParts is not null ? [string.IsNullOrWhiteSpace(r.Spec.SizeName) ? texts.Size : r.Spec.SizeName] : null,
            PartValues = boxParts ?? candleParts ?? waterfallParts ?? sizeParts,
            Radii = context.HitRadii,
            // Points with their own color show it in the tooltip too.
            PointColors = candleColors ?? waterfallColors ?? (r.Data.Colors is null && r.Spec.ColorBands is null
                ? null
                : [.. hits.Index.Select(j => r.Data.Colors?[j] ?? ColorBands.ColorOf(r.Spec.ColorBands, r.Data.Y[j]) ?? r.Color)]),
        };
    }

    /// <summary>The rows under a candle's close in its tooltip.</summary>
    private static string[] CandlePartLabels(ChartTexts texts) =>
        [texts.CandleOpen, texts.CandleHigh, texts.CandleLow, texts.CandleClose, texts.CandleChange];

    // The change in percent only with direction colors (prices): for values such as temperatures, a percentage depends
    // on where zero is and means nothing.
    private static string[] CandlePartValues(CandleSummary c, CultureInfo culture, ValueFormat format, bool percent)
    {
        var change = c.Close - c.Open;
        var sign = change > 0 ? "+" : "";
        var share = percent && c.Open != 0 ? $" ({sign}{(change / Math.Abs(c.Open)).ToString("0.0%", culture)})" : "";
        return
        [
            ValueFormatter.Format(c.Open, culture, format), ValueFormatter.Format(c.High, culture, format),
            ValueFormatter.Format(c.Low, culture, format), ValueFormatter.Format(c.Close, culture, format),
            sign + ValueFormatter.Format(change, culture, format) + share,
        ];
    }

    /// <summary>The rows under a box's median in its tooltip, highest first (as drawn).</summary>
    private static string[] BoxPartLabels(ChartTexts texts) =>
        [texts.BoxMax, texts.BoxUpperQuartile, texts.BoxMedian, texts.BoxLowerQuartile, texts.BoxMin, texts.BoxCount, texts.BoxOutliers];

    private static string[] BoxPartValues(BoxColumns box, int i, double median, CultureInfo culture, ValueFormat format)
    {
        string F(double v) => double.IsFinite(v) ? ValueFormatter.Format(v, culture, format) : "–";
        var outliers = box.OutliersOf(i);
        var shown = outliers.Length <= 5
            ? string.Join(", ", outliers.ToArray().Select(F))
            : string.Join(", ", outliers[..2].ToArray().Select(F)) + " … " + string.Join(", ", outliers[^2..].ToArray().Select(F));
        return
        [
            F(box.Max[i]), F(box.Q3[i]), F(median), F(box.Q1[i]), F(box.Min[i]),
            box.Count[i] > 0 ? box.Count[i].ToString("N0", culture) : "",
            outliers.Length == 0 ? "" : $"{outliers.Length.ToString("N0", culture)}: {shown}",
        ];
    }

    // ---------- Compact ----------

    /// <summary>Room around a compact plot: half the widest stroke, or a highlight dot with its outline.</summary>
    private static double CompactInset(IEnumerable<Resolved> series)
    {
        var inset = 1.5;
        foreach (var r in series)
        {
            var stroke = r.Spec.StrokeWidth ?? 2;
            inset = Math.Max(inset, r.Spec.Highlight != PointHighlight.None && !r.Type.UsesSlots ? HighlightRadius(r.Spec, stroke) + 1.5 : stroke / 2 + 0.5);
        }

        return inset;
    }

    /// <summary>Radius of a highlight dot for a line of the given width.</summary>
    internal static double HighlightRadius(double strokeWidth) => Math.Max(2.5, strokeWidth * 1.25);

    /// <summary>
    /// Radius of a line or area series' dots: half its <see cref="SeriesOptions.MarkerSize"/>, else from the line width.
    /// Dots on every point (<see cref="PointHighlight.All"/>) are larger by default (7 px across a 2 px line): they mark the
    /// data rather than accent one value, and must read as dots, not as a bump in the line.
    /// </summary>
    internal static double HighlightRadius(SeriesOptions spec, double strokeWidth) =>
        spec.MarkerSize is > 0 and var size ? Math.Clamp(size / 2, 1, 30)
        : spec.Highlight.HasFlag(PointHighlight.All) ? Math.Max(3.5, strokeWidth * 1.75)
        : HighlightRadius(strokeWidth);

    /// <summary>Bars are never wider than with this many categories: one bar does not fill the plot.</summary>
    private const double MinSlotCount = 4;

    /// <summary>
    /// Width of a band's group of bars (all slots; only when all are <see cref="SeriesType.Bar"/>): the band's, but no wider than a fifth of the plot (as with
    /// <see cref="MinSlotCount"/> categories), and with <see cref="SeriesOptions.MaxBarWidth"/> set, no wider than that per bar
    /// (plus the 2 px gap between bars). The group stays centered on its band.
    /// </summary>
    private static double SlotGroupWidth(double bandwidth, double plotLength, int slotCount, IEnumerable<Resolved> slotSeries)
    {
        // Only plain bars: histogram bins touch, and boxes and candles have widths of their own.
        var series = slotSeries.ToList();
        if (series.Count == 0 || series.Any(r => r.Spec.Type != SeriesType.Bar))
        {
            return bandwidth;
        }

        var width = Math.Min(bandwidth, plotLength * 0.8 / MinSlotCount);
        var max = series.Select(r => r.Spec.MaxBarWidth).Where(m => m > 0).DefaultIfEmpty().Min();
        if (max is > 0 and var perBar)
        {
            width = Math.Min(width, Math.Max(1, slotCount) * (perBar + 2));
        }

        return Math.Max(1, width);
    }

    /// <summary>Compact y domain: the data range as is (no nice rounding, no ticks); equal values sit in the middle.</summary>
    private static void TightDomain(YAxisLayout axis)
    {
        var (min, max) = axis.ExtentMin <= axis.ExtentMax ? (axis.ExtentMin, axis.ExtentMax) : (axis.ExtentMax, axis.ExtentMin);
        if (max == min)
        {
            var pad = min == 0 ? 1 : Math.Abs(min) * 0.1;
            (min, max) = (axis.Spec.Min ?? min - pad, axis.Spec.Max ?? max + pad);
            if (max <= min)
            {
                max = min + 1;
            }
        }

        axis.DomainMin = min;
        axis.DomainMax = max;
        axis.Ticks = [];
        axis.Labels = [];
        axis.LabelWidth = 0;
    }

    // ---------- Helpers ----------

    private static RectF Inflate(RectF r, float by) => new(r.X - by, r.Y - by, r.Width + 2 * by, r.Height + 2 * by);

    private static string AxisLabel(ChartTexts texts, CultureInfo culture, string name, string? title, List<string> labels)
    {
        var axis = title is null ? name : $"{name}: {title}";
        return labels.Count > 1 ? texts.Fill(nameof(ChartTexts.AxisRange), culture, axis, labels[0], labels[^1]) : axis;
    }

    private static string EmptyMessage(List<Resolved> series, ChartTexts texts) =>
        series.Count > 0 && series.All(s => !s.Visible) ? texts.AllSeriesHidden : texts.NoData;
}
