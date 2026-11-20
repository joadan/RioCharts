using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;
using RioCharts.Core.Series;
using RioCharts.Core.Text;

namespace RioCharts.Core.Layout;

public sealed record LayoutOptions
{
    public double Width { get; init; } = 640;
    public double Height { get; init; } = 300;
    public ITextMeasurer Measurer { get; init; } = ApproximateTextMeasurer.Instance;
    public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;

    /// <summary>Ids of series hidden through the legend (they keep their color and legend item).</summary>
    public IReadOnlySet<string>? HiddenSeries { get; init; }

    public double FontSize { get; init; } = 12;
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
}

public sealed record LegendEntry(string SeriesId, string Label, string Color, SeriesType Type, bool Visible, bool IsEmpty);

public sealed class ChartLayoutResult
{
    public required Scene.Scene Scene { get; init; }
    public required RectF Plot { get; init; }
    public required HitTestIndex HitTest { get; init; }
    public required TooltipModel Tooltip { get; init; }
    public required IReadOnlyList<LegendEntry> Legend { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required XKind XKind { get; init; }

    /// <summary>X scale for numeric and time axes (time in milliseconds); null for category axes.</summary>
    public LinearScale? XScale { get; init; }

    /// <summary>X scale for category axes.</summary>
    public BandScale? XBand { get; init; }

    /// <summary>The full x range of the data (numeric and time axes), the bounds for zoom and pan.</summary>
    public (double Min, double Max)? XExtent { get; init; }

    /// <summary>True when <see cref="LayoutOptions.XRange"/> narrowed the x axis.</summary>
    public bool Zoomed { get; init; }

    /// <summary>The display time zone conversion, or null when times are shown as given.</summary>
    public TimeConversion? Time { get; init; }

    /// <summary>Scale of the primary vertical axis (the one with the grid lines).</summary>
    public required LinearScale YScale { get; init; }

    /// <summary>Scales of all vertical axes by axis id, including hidden ones.</summary>
    public required IReadOnlyDictionary<string, LinearScale> YScales { get; init; }

    public required bool HasData { get; init; }

    /// <summary>Draggable reference lines (for the interactive layer).</summary>
    public IReadOnlyList<DragHandle> DragHandles { get; init; } = [];
}

/// <summary>
/// The layout pass: resolves domains from series extents (stacked where asked), picks ticks
/// that fit the measured labels, places legend, axes and plot, and emits the scene. Pure: no
/// UI or browser dependency.
/// </summary>
public static partial class ChartLayout
{
    private const double PadTop = 8, PadRight = 12, PadBottom = 6, PadLeft = 6;
    private const double LabelGap = 8;
    private const double AxisGap = 14;

    private sealed class Resolved
    {
        public required SeriesSpec Spec { get; init; }
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
    }

    public static ChartLayoutResult Compute(ChartSpec spec, IReadOnlyDictionary<string, SeriesData> data, LayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        options ??= new LayoutOptions();
        var warnings = new List<string>();
        var culture = options.Culture;
        var measure = options.Measurer;
        var width = Math.Max(50, options.Width);
        var height = Math.Max(50, options.Height);
        var font = options.FontSize;
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
            };
            r.Axis = FindAxis(axes, s.YAxisId, $"series[{i}].yAxisId", warnings) ?? primary;
            if ((d.Kind == XKind.Category || r.Type.UsesSlots) && RepeatsX(d) is { } repeated)
            {
                warnings.Add($"series '{s.Id}': x value {repeated} appears more than once; the points are drawn on top of each other. Combine them with an aggregate (Aggregate or YAggregate on a series component, 'aggregate' in a spec).");
            }

            resolved.Add(r);
        }

        if (resolved.Any(r => r.Type.CoordinateSystem == CoordinateSystem.Polar))
        {
            return ComputePolar(spec, resolved, options, warnings, styles);
        }

        var xKind = ResolveXKind(spec.X.Scale, resolved);
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
        var (xMin, xMax, barSpacing) = xKind == XKind.Category
            ? (0, 0, 0)
            : XExtent(drawable, spec.X, xKind, hasBars, annotations.Where(a => a.ExpandsDomain));
        (double Min, double Max)? fullX = xKind == XKind.Category ? null : (xMin, xMax);
        (double Min, double Max)? window = null;
        if (fullX is not null && options.XRange is { } range && range.Max > range.Min && double.IsFinite(range.Min) && double.IsFinite(range.Max))
        {
            (xMin, xMax) = range;
            window = range;
        }

        foreach (var axis in axes)
        {
            YExtent(axis, annotations.Where(a => a.ExpandsDomain && a.Axis == axis), window);
        }

        // ---------- Title and legend ----------
        var y0 = PadTop;
        var titleNodes = new List<Node>();
        if (!string.IsNullOrWhiteSpace(spec.Title))
        {
            var titleHeight = options.TitleFontSize * 1.3;
            titleNodes.Add(new TextNode(spec.Title!, new PointF((float)PadLeft, (float)(y0 + titleHeight / 2)), new TextStyle("title"), Anchor.Start)
            {
                Class = "rio-title",
            });
            y0 += titleHeight + 6;
        }

        var legendEntries = resolved
            .Select(r => new LegendEntry(r.Spec.Id, r.Spec.DisplayName, r.Color, r.Spec.Type, r.Visible, r.Empty))
            .ToList();
        var showLegend = spec.Legend.Mode switch
        {
            LegendMode.Show => legendEntries.Count > 0,
            LegendMode.Hide => false,
            _ => legendEntries.Count > 1,
        };

        var legendPosition = spec.Legend.Position;
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
                if (legendPosition == LegendPosition.Top)
                {
                    legendTop = legendHeight + 8;
                }
                else
                {
                    legendBottom = legendHeight + 8;
                }
            }
        }

        // ---------- Vertical layout ----------
        var visibleAxes = axes.Where(a => a.Visible).ToList();
        var xLabelHeight = lineHeight + 6;
        var xTitleHeight = string.IsNullOrWhiteSpace(spec.X.Title) ? 0 : lineHeight + 4;

        // Y titles sit above the axis labels, horizontally (easier to read than rotated text).
        var yTitleHeight = visibleAxes.Any(a => !string.IsNullOrWhiteSpace(a.Spec.Title)) || (!hasData && !string.IsNullOrWhiteSpace(primary.Spec.Title))
            ? lineHeight + 2
            : 0;
        var plotTop = y0 + legendTop + yTitleHeight + font / 2; // room for the top label, centered on its grid line
        var plotBottom = height - PadBottom - legendBottom - xTitleHeight - xLabelHeight;
        var plotHeight = Math.Max(10, plotBottom - plotTop);
        plotBottom = plotTop + plotHeight;

        // ---------- Y ticks (they decide the side margins) ----------
        var yTarget = Math.Clamp((int)(plotHeight / 42), 2, 10);
        // The first visible axis owns the grid and the zero line; the others align to its ticks.
        var gridAxis = visibleAxes.FirstOrDefault() ?? primary;
        ComputeYTicks(gridAxis, yTarget, culture, measure, font);
        foreach (var axis in axes.Where(a => a != gridAxis))
        {
            ComputeYTicks(axis, yTarget, culture, measure, font, axis.Visible ? gridAxis : null);
        }

        var left = visibleAxes.Where(a => !a.Right).ToList();
        var right = visibleAxes.Where(a => a.Right).ToList();
        var plotLeft = PadLeft + (left.Count > 0 ? LabelGap + left.Sum(a => a.LabelWidth) + (left.Count - 1) * AxisGap : 0);
        var plotRight = width - PadRight - legendRight - (right.Count > 0 ? LabelGap + right.Sum(a => a.LabelWidth) + (right.Count - 1) * AxisGap : 0);
        var plotWidth = Math.Max(10, plotRight - plotLeft);
        plotRight = plotLeft + plotWidth;
        var plot = new RectF((float)plotLeft, (float)plotTop, (float)plotWidth, (float)plotHeight);

        PlaceAxisColumns(left, plotLeft - LabelGap, -1);
        PlaceAxisColumns(right, plotRight + LabelGap, 1);
        foreach (var axis in axes)
        {
            axis.Scale = new LinearScale(axis.DomainMin, axis.DomainMax, plotBottom, plotTop);
        }


        // ---------- X scale and ticks ----------
        LinearScale? xScale = null;
        BandScale? xBand = null;
        var xTickNodes = new List<Node>();
        var xGrid = new List<double>();
        if (xKind == XKind.Category)
        {
            // Bars get padded bands; lines on categories use points half a step from the edges.
            xBand = new BandScale(categories, plotLeft, plotLeft + plotWidth, hasBars ? 0.2 : 0, hasBars ? 0.1 : 0);
            if (hasData)
            {
                AddCategoryLabels(xBand, measure, font, plotBottom, xTickNodes);
            }
        }
        else
        {
            xScale = new LinearScale(xMin, xMax, plotLeft, plotLeft + plotWidth);
            if (hasData)
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
        if (gridAxis.Spec.GridLines ?? true)
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

        var baselineY = zeroInDomain ? gridScale.Map(0) : plotBottom;
        statics.Add(new PathNode(new PathBuilder().MoveTo(plotLeft, baselineY).LineTo(plotRight, baselineY).Build(), new StyleRef("axis"))
        {
            Class = "rio-baseline",
        });

        if (hasData)
        {
            foreach (var axis in visibleAxes)
            {
                var nodes = new List<Node>();
                for (var i = 0; i < axis.Ticks.Count; i++)
                {
                    nodes.Add(new TextNode(axis.Labels[i], new PointF((float)axis.LabelX, (float)axis.Scale.Map(axis.Ticks[i])), new TextStyle("axis-label"),
                        axis.Right ? Anchor.Start : Anchor.End));
                }

                statics.Add(new GroupNode(nodes)
                {
                    Class = axis.Right ? "rio-axis-y rio-axis-y-right" : "rio-axis-y",
                    Key = "axis:" + axis.Id,
                    AriaLabel = AxisLabel(visibleAxes.Count > 1 ? $"Y axis {axis.Id}" : "Y axis", axis.Spec.Title, axis.Labels),
                });
            }

            statics.Add(new GroupNode(xTickNodes) { Class = "rio-axis-x", AriaLabel = "X axis" + (spec.X.Title is { } xt ? ": " + xt : "") });
        }

        if (!string.IsNullOrWhiteSpace(spec.X.Title))
        {
            statics.Add(new TextNode(spec.X.Title!, new PointF((float)(plotLeft + plotWidth / 2), (float)(plotBottom + xLabelHeight + xTitleHeight / 2)),
                new TextStyle("axis-title"), Anchor.Middle) { Class = "rio-axis-title" });
        }

        var titleY = (float)(plotTop - font / 2 - yTitleHeight / 2 - 2);
        foreach (var axis in hasData ? visibleAxes : [primary])
        {
            if (!string.IsNullOrWhiteSpace(axis.Spec.Title))
            {
                var (tx, anchor) = !hasData ? (PadLeft, Anchor.Start)
                    : axis.Right ? (axis.LabelX + axis.LabelWidth, Anchor.End)
                    : (axis.LabelX - axis.LabelWidth, Anchor.Start);
                statics.Add(new TextNode(axis.Spec.Title!, new PointF((float)tx, titleY), new TextStyle("axis-title"), anchor) { Class = "rio-axis-title" });
            }
        }

        if (showLegend)
        {
            statics.Add(BuildLegend(legendItems, legendRows, legendPosition, width - PadRight - legendRight + 16, plot, y0,
                plotBottom + xLabelHeight + xTitleHeight + 8, lineHeight, styles));
        }

        // ---------- Series ----------
        var slotKeys = drawable.Where(r => r.Type.UsesSlots).Select(SlotKey).Distinct().ToList();
        var slotWidth = xKind == XKind.Category
            ? xBand!.Bandwidth
            : barSpacing > 0 ? Math.Abs(xScale!.Map(xScale.DomainMin + barSpacing) - xScale.Map(xScale.DomainMin)) * 0.8 : plotWidth * 0.1;
        var threshold = options.DownsampleThreshold ?? Math.Max(500, (int)(plotWidth * 2));

        var hitTest = new HitTestIndex();
        var tooltipSeries = new List<TooltipSeries>();
        var seriesNodes = new List<(int Order, int Index, Node Node)>();
        var labelAnchors = new List<(Resolved Series, List<LabelAnchor> Anchors)>();
        var formatX = TimeFormat(xKind, drawable.SelectMany(r => r.Data.X).Take(5000), culture, spec.X.Format, time, options.ViewerTimeZone);
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
                PixelX = PixelX(r.Data, xKind, xScale, xBand, categoryIndex),
                Upper = r.Upper,
                Lower = r.Lower,
                MapY = axisScale.Map,
                ZeroY = Math.Clamp(axisScale.Map(0), plotTop, plotBottom),
                SlotWidth = slotWidth,
                SlotIndex = r.Type.UsesSlots ? slotKeys.IndexOf(SlotKey(r)) : 0,
                SlotCount = Math.Max(1, slotKeys.Count),
                DownsampleThreshold = threshold,
                XWindow = window,
            };
            r.Type.Build(context);
            if (context.Hits is { } hits)
            {
                hitTest.Add(hits);
                tooltipSeries.Add(TooltipFor(r, hitTest.Series[^1], context, formatX, culture, xKind, xBand, plotWidth));
            }

            if (context.Labels.Count > 0)
            {
                labelAnchors.Add((r, context.Labels));
            }

            var order = r.Spec.ZIndex ?? r.Type.DefaultLayer * 1000;
            seriesNodes.Add((order, r.ColorIndex, new GroupNode(context.Output)
            {
                Key = "series:" + r.Spec.Id,
                Class = "rio-series rio-series-" + r.Spec.Type.ToString().ToLowerInvariant(),
                AriaLabel = $"{r.Spec.DisplayName}: {r.Data.Y.Count(double.IsFinite)} data points",
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
            BuildAnnotations(annotations, plot, xKind, xScale, xBand, culture, measure, font, styles, options.InteractiveAnnotations,
                below, above, placedLabels, dragHandles, spec.X.Format, time);
            labelNodes.AddRange(BuildDataLabels(labelAnchors, plot, culture, measure, font, styles, placedLabels, warnings));
        }

        var plotNodes = new List<Node>(below);
        plotNodes.AddRange(seriesNodes.OrderBy(t => t.Order).ThenBy(t => t.Index).Select(t => t.Node));
        plotNodes.AddRange(above);
        var seriesLayer = new GroupNode(
        [
            new GroupNode(plotNodes, Clip: Inflate(plot, 2)) { Class = "rio-plot" },
            new GroupNode(labelNodes) { Class = "rio-labels" },
        ])
        {
            Class = "rio-series-layer",
        };

        // ---------- Overlay ----------
        var overlay = new List<Node>();
        if (!hasData)
        {
            overlay.Add(new TextNode(EmptyMessage(resolved), new PointF(plot.CenterX, plot.CenterY), new TextStyle("empty"), Anchor.Middle) { Class = "rio-empty" });
        }

        var scene = new Scene.Scene(new GroupNode(statics) { Class = "rio-static" }, seriesLayer, new GroupNode(overlay) { Class = "rio-overlay" })
        {
            Width = (float)width,
            Height = (float)height,
            Styles = styles,
            Description = ChartDescriber.Describe(spec, resolved.Select(r => (r.Spec, r.Data, r.Visible)).ToList(), xKind, culture),
        };

        var bandWidth = xKind == XKind.Category ? xBand!.Step : hasBars ? slotWidth / 0.8 : 0;
        return new ChartLayoutResult
        {
            Scene = scene,
            Plot = plot,
            HitTest = hitTest,
            Tooltip = new TooltipModel(plot, spec.Tooltip.Enabled && hasData, spec.Tooltip.Shared, (float)bandWidth, tooltipSeries),
            Legend = legendEntries,
            Warnings = warnings,
            XKind = xKind,
            XScale = xScale,
            XBand = xBand,
            XExtent = fullX,
            Zoomed = window is not null,
            Time = time,
            YScale = gridAxis.Scale,
            YScales = axes.ToDictionary(a => a.Id, a => a.Scale, StringComparer.Ordinal),
            HasData = hasData,
            DragHandles = dragHandles,
        };
    }

    private static string SlotKey(Resolved r) => r.Spec.Stack is { Length: > 0 } stack ? $"stack|{r.Axis.Id}|{stack}" : "series|" + r.Spec.Id;

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
        styles.Add("annotation-line", new Style { Stroke = ThemeTokens.Annotation, StrokeWidth = 1.25f, DashArray = "4 3" });
        styles.Add("annotation-band", new Style { Fill = ThemeTokens.Annotation, FillOpacity = 0.1f });
        styles.Add("annotation-label", new TextStyleDef { FontSize = small, Fill = ThemeTokens.Annotation });
        styles.Add("annotation-grip", new Style { Stroke = "transparent", StrokeWidth = 12 });
        styles.Add("callout-label", new TextStyleDef { FontSize = small, Fill = ThemeTokens.Text });
        return styles;
    }

    // ---------- Series helpers ----------

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
            .Where(r => r.Spec.Stack is { Length: > 0 })
            .GroupBy(r => (r.Axis.Id, r.Spec.Stack, r.Type.UsesSlots));
        foreach (var group in groups)
        {
            var positive = new Dictionary<double, double>();
            var negative = new Dictionary<double, double>();
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
                    if (!double.IsFinite(v) || !double.IsFinite(key))
                    {
                        upper[i] = lower[i] = double.NaN;
                        continue;
                    }

                    var sums = v >= 0 ? positive : negative;
                    sums.TryGetValue(key, out var start);
                    lower[i] = start;
                    upper[i] = start + v;
                    sums[key] = start + v;
                }

                r.Upper = upper;
                r.Lower = lower;
            }
        }
    }

    private static (double Min, double Max, double BarSpacing) XExtent(List<Resolved> series, AxisSpec axis, XKind kind, bool hasBars,
        IEnumerable<ResolvedAnnotation> annotations)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        var spacing = double.PositiveInfinity;
        foreach (var r in series)
        {
            var d = r.Data;
            for (var i = 0; i < d.Count; i++)
            {
                if (double.IsFinite(d.Y[i]))
                {
                    min = Math.Min(min, d.X[i]);
                    max = Math.Max(max, d.X[i]);
                }
            }

            if (r.Type.UsesSlots)
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

        if (max == min)
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

        return (axis.Min ?? min, axis.Max ?? max, spacing);
    }

    private static double[] PixelX(SeriesData data, XKind kind, LinearScale? scale, BandScale? band, Dictionary<string, int> categoryIndex)
    {
        var px = new double[data.Count];
        for (var i = 0; i < px.Length; i++)
        {
            if (kind == XKind.Category)
            {
                var local = (int)data.X[i];
                px[i] = local >= 0 && local < data.Categories.Count && categoryIndex.TryGetValue(data.Categories[local], out var global)
                    ? band!.Center(global)
                    : double.NaN;
            }
            else
            {
                px[i] = scale!.Map(data.X[i]);
            }
        }

        return px;
    }

    private static TooltipSeries TooltipFor(Resolved r, HitTestIndex.SeriesHits hits, SeriesBuildContext context, Func<double, string> formatX,
        CultureInfo culture, XKind kind, BandScale? band, double plotWidth)
    {
        var n = hits.X.Length;
        var xLabels = new string[n];
        var values = new string[n];
        var format = r.Spec.Format ?? r.Axis.Spec.Format;
        for (var i = 0; i < n; i++)
        {
            var j = hits.Index[i];
            var x = r.Data.X[j];
            xLabels[i] = kind == XKind.Category
                ? ((int)x is var c && c >= 0 && c < r.Data.Categories.Count ? r.Data.Categories[c] : "")
                : formatX(x);
            values[i] = ValueFormatter.Format(r.Data.Y[j], culture, format);
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
            hits.X, hits.Y, hits.Index, xLabels, values);
    }

    // ---------- Helpers ----------

    private static RectF Inflate(RectF r, float by) => new(r.X - by, r.Y - by, r.Width + 2 * by, r.Height + 2 * by);

    private static string AxisLabel(string name, string? title, List<string> labels) =>
        (title is null ? name : $"{name}: {title}") + (labels.Count > 1 ? $", from {labels[0]} to {labels[^1]}" : "");

    private static string EmptyMessage(List<Resolved> series) =>
        series.Count > 0 && series.All(s => !s.Visible) ? "All series are hidden" : "No data available";
}
