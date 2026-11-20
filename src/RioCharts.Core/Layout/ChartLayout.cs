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
}

public sealed record LegendEntry(string SeriesId, string Label, string Color, SeriesType Type, bool Visible, bool IsEmpty);

public sealed class ChartLayoutResult
{
    public required Scene.Scene Scene { get; init; }
    public required RectF Plot { get; init; }
    public required HitTestIndex HitTest { get; init; }
    public required IReadOnlyList<LegendEntry> Legend { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required XKind XKind { get; init; }

    /// <summary>X scale for numeric and time axes (time in milliseconds); null for category axes.</summary>
    public LinearScale? XScale { get; init; }

    /// <summary>X scale for category axes.</summary>
    public BandScale? XBand { get; init; }

    public required LinearScale YScale { get; init; }
    public required bool HasData { get; init; }
}

/// <summary>
/// The layout pass: resolves domains from series extents, picks ticks that fit the measured
/// labels, places legend and plot, and emits the scene. Pure: no UI or browser dependency.
/// </summary>
public static class ChartLayout
{
    private const double PadTop = 8, PadRight = 12, PadBottom = 6, PadLeft = 6;
    private const double LabelGap = 8;

    private sealed record Resolved(SeriesSpec Spec, SeriesData Data, int ColorIndex, string Color, ISeriesType Type, bool Visible, bool Empty);

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

        // ---------- Resolve series ----------
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
            var visible = options.HiddenSeries?.Contains(s.Id) != true;
            var empty = !d.Y.Any(double.IsFinite);
            resolved.Add(new Resolved(s, d, i, s.Color ?? ThemeTokens.Series(i), ISeriesType.For(s.Type), visible, empty));
        }

        var xKind = ResolveXKind(spec.X.Scale, resolved);
        foreach (var r in resolved.Where(r => r.Data.Count > 0 && r.Data.Kind != xKind))
        {
            warnings.Add($"series '{r.Spec.Id}': x values are {r.Data.Kind} but the axis is {xKind}; the series is skipped.");
        }

        var drawable = resolved.Where(r => r.Visible && !r.Empty && (r.Data.Kind == xKind || r.Data.Count == 0)).ToList();
        var hasData = drawable.Count > 0;

        // ---------- Domains ----------
        var categories = xKind == XKind.Category ? MergeCategories(resolved) : [];
        var (yMin, yMax) = YExtent(drawable, spec.Y);
        var hasBars = drawable.Any(r => r.Type.UsesSlots);
        var (xMin, xMax, barSpacing) = xKind == XKind.Category ? (0, 0, 0) : XExtent(drawable, spec.X, xKind, hasBars);

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
        var xLabelHeight = lineHeight + 6;
        var xTitleHeight = string.IsNullOrWhiteSpace(spec.X.Title) ? 0 : lineHeight + 4;
        // The Y title sits above the axis labels, horizontally (easier to read than rotated text).
        var yTitleHeight = string.IsNullOrWhiteSpace(spec.Y.Title) ? 0 : lineHeight + 2;
        var plotTop = y0 + legendTop + yTitleHeight + font / 2; // room for the top label, centered on its grid line
        var plotBottom = height - PadBottom - legendBottom - xTitleHeight - xLabelHeight;
        var plotHeight = Math.Max(10, plotBottom - plotTop);
        plotBottom = plotTop + plotHeight;

        // ---------- Y ticks (they decide the left margin) ----------
        var yTarget = Math.Clamp((int)(plotHeight / 42), 2, 10);
        var (yDomainMin, yDomainMax, yStep) = YDomain(yMin, yMax, spec.Y, yTarget);
        var yTicks = NiceTicks.Ticks(yDomainMin, yDomainMax, yStep);
        var yFormat = NumberFormatter.ForTicks(yTicks, yStep, culture, spec.Y.Format);
        var yLabels = yTicks.Select(yFormat).ToList();
        var yLabelWidth = hasData ? yLabels.Select(l => measure.Measure(l, font)).DefaultIfEmpty(0).Max() : 0;
        var plotLeft = PadLeft + yLabelWidth + (hasData ? LabelGap : 0);
        var plotRight = width - PadRight - legendRight;
        var plotWidth = Math.Max(10, plotRight - plotLeft);
        var plot = new RectF((float)plotLeft, (float)plotTop, (float)plotWidth, (float)plotHeight);

        var yScale = new LinearScale(yDomainMin, yDomainMax, plotBottom, plotTop);

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
                xGrid = AddContinuousXLabels(xScale, xKind, spec.X, culture, measure, font, plotBottom, width, xTickNodes);
            }
        }

        // ---------- Static layer ----------
        var statics = new List<Node>();
        statics.AddRange(titleNodes);

        var grid = new List<Node>();
        var zeroInDomain = yDomainMin < 0 && yDomainMax > 0;
        if (spec.Y.GridLines ?? true)
        {
            foreach (var t in yTicks)
            {
                if (hasData && t == 0 && zeroInDomain)
                {
                    continue; // the zero line is drawn as the baseline
                }

                var py = (float)yScale.Map(t);
                grid.Add(new PathNode(new PathBuilder().MoveTo(plotLeft, py).LineTo(plotLeft + plotWidth, py).Build(), new StyleRef("grid")));
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

        var baselineY = zeroInDomain ? yScale.Map(0) : plotBottom;
        statics.Add(new PathNode(new PathBuilder().MoveTo(plotLeft, baselineY).LineTo(plotLeft + plotWidth, baselineY).Build(), new StyleRef("axis"))
        {
            Class = "rio-baseline",
        });

        if (hasData)
        {
            var yLabelNodes = new List<Node>();
            for (var i = 0; i < yTicks.Count; i++)
            {
                yLabelNodes.Add(new TextNode(yLabels[i], new PointF((float)(plotLeft - LabelGap), (float)yScale.Map(yTicks[i])), new TextStyle("axis-label"), Anchor.End));
            }

            statics.Add(new GroupNode(yLabelNodes) { Class = "rio-axis-y", AriaLabel = AxisLabel("Y axis", spec.Y.Title, yLabels) });
            statics.Add(new GroupNode(xTickNodes) { Class = "rio-axis-x", AriaLabel = "X axis" + (spec.X.Title is { } xt ? ": " + xt : "") });
        }

        if (!string.IsNullOrWhiteSpace(spec.X.Title))
        {
            statics.Add(new TextNode(spec.X.Title!, new PointF((float)(plotLeft + plotWidth / 2), (float)(plotBottom + xLabelHeight + xTitleHeight / 2)),
                new TextStyle("axis-title"), Anchor.Middle) { Class = "rio-axis-title" });
        }

        if (!string.IsNullOrWhiteSpace(spec.Y.Title))
        {
            var ty = (float)(plotTop - font / 2 - yTitleHeight / 2 - 2);
            statics.Add(new TextNode(spec.Y.Title!, new PointF((float)PadLeft, ty), new TextStyle("axis-title"), Anchor.Start)
            {
                Class = "rio-axis-title",
            });
        }

        if (showLegend)
        {
            statics.Add(BuildLegend(legendEntries, legendItems, legendRows, legendPosition, plot, y0, plotBottom + xLabelHeight + xTitleHeight + 8,
                width, lineHeight, font, styles));
        }

        // ---------- Series layer ----------
        var zeroY = Math.Clamp(yScale.Map(0), plotTop, plotBottom);
        var barSeries = drawable.Where(r => r.Type.UsesSlots).ToList();
        var slotWidth = xKind == XKind.Category
            ? xBand!.Bandwidth
            : barSpacing > 0 ? Math.Abs(xScale!.Map(xScale.DomainMin + barSpacing) - xScale.Map(xScale.DomainMin)) * 0.8 : plotWidth * 0.1;
        var threshold = options.DownsampleThreshold ?? Math.Max(500, (int)(plotWidth * 2));
        var categoryIndex = categories.Select((c, i) => (c, i)).ToDictionary(t => t.c, t => t.i, StringComparer.Ordinal);

        var hitTest = new HitTestIndex();
        var seriesNodes = new List<(int Order, int Index, Node Node)>();
        foreach (var r in drawable)
        {
            var context = new SeriesBuildContext
            {
                Spec = r.Spec,
                Data = r.Data,
                Color = r.Color,
                ColorIndex = r.ColorIndex,
                Styles = styles,
                Plot = plot,
                PixelX = PixelX(r.Data, xKind, xScale, xBand, categoryIndex),
                MapY = yScale.Map,
                ZeroY = zeroY,
                SlotWidth = slotWidth,
                SlotIndex = Math.Max(0, barSeries.IndexOf(r)),
                SlotCount = Math.Max(1, barSeries.Count),
                DownsampleThreshold = threshold,
            };
            r.Type.Build(context);
            if (context.Hits is { } hits)
            {
                hitTest.Add(hits);
            }

            var order = r.Spec.ZIndex ?? r.Type.DefaultLayer * 1000;
            seriesNodes.Add((order, r.ColorIndex, new GroupNode(context.Output)
            {
                Key = "series:" + r.Spec.Id,
                Class = "rio-series rio-series-" + r.Spec.Type.ToString().ToLowerInvariant(),
                AriaLabel = $"{r.Spec.DisplayName}: {r.Data.Y.Count(double.IsFinite)} data points",
            }));
        }

        var seriesLayer = new GroupNode(seriesNodes.OrderBy(t => t.Order).ThenBy(t => t.Index).Select(t => t.Node).ToList(), Clip: Inflate(plot, 2))
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

        return new ChartLayoutResult
        {
            Scene = scene,
            Plot = plot,
            HitTest = hitTest,
            Legend = legendEntries,
            Warnings = warnings,
            XKind = xKind,
            XScale = xScale,
            XBand = xBand,
            YScale = yScale,
            HasData = hasData,
        };
    }

    // ---------- Styles ----------

    private static StyleTable CreateStyles(LayoutOptions options)
    {
        var styles = new StyleTable();
        styles.Add("grid", new Style { Stroke = ThemeTokens.Grid, StrokeWidth = 1 });
        styles.Add("axis", new Style { Stroke = ThemeTokens.Axis, StrokeWidth = 1 });
        styles.Add("axis-label", new TextStyleDef { FontSize = (float)options.FontSize, Fill = ThemeTokens.TextMuted });
        styles.Add("axis-title", new TextStyleDef { FontSize = (float)options.FontSize, FontWeight = 500, Fill = ThemeTokens.TextMuted });
        styles.Add("legend-label", new TextStyleDef { FontSize = (float)options.FontSize, Fill = ThemeTokens.Text });
        styles.Add("title", new TextStyleDef { FontSize = (float)options.TitleFontSize, FontWeight = 600, Fill = ThemeTokens.Text });
        styles.Add("empty", new TextStyleDef { FontSize = (float)options.FontSize + 1, Fill = ThemeTokens.TextMuted });
        styles.Add("legend-hit", new Style { Fill = "transparent" });
        return styles;
    }

    // ---------- Domains ----------

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

    private static (double Min, double Max) YExtent(List<Resolved> series, AxisSpec axis)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        var includeZero = false;
        foreach (var r in series)
        {
            includeZero |= r.Type.IncludesZero;
            foreach (var y in r.Data.Y)
            {
                if (double.IsFinite(y))
                {
                    min = Math.Min(min, y);
                    max = Math.Max(max, y);
                }
            }
        }

        if (!double.IsFinite(min))
        {
            (min, max) = (0, 1);
        }

        if (includeZero)
        {
            min = Math.Min(min, 0);
            max = Math.Max(max, 0);
        }

        return (axis.Min ?? min, axis.Max ?? max);
    }

    private static (double Min, double Max, double Step) YDomain(double min, double max, AxisSpec axis, int target)
    {
        if (max < min)
        {
            (min, max) = (max, min);
        }

        if (max == min)
        {
            // All values equal: pad so the axis does not collapse.
            var pad = min == 0 ? 1 : Math.Abs(min) * 0.1;
            if (axis.Min is null)
            {
                min -= pad;
            }

            if (axis.Max is null)
            {
                max += pad;
            }

            if (max <= min)
            {
                max = min + 1;
            }
        }

        var (niceMin, niceMax, step) = NiceTicks.NiceDomain(min, max, target);
        return (axis.Min ?? niceMin, axis.Max ?? niceMax, step);
    }

    private static (double Min, double Max, double BarSpacing) XExtent(List<Resolved> series, AxisSpec axis, XKind kind, bool hasBars)
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

    // ---------- X labels ----------

    /// <summary>Adds numeric or time tick labels, choosing the densest ticks whose labels do not overlap.</summary>
    private static List<double> AddContinuousXLabels(LinearScale scale, XKind kind, AxisSpec axis, CultureInfo culture,
        ITextMeasurer measure, double font, double plotBottom, double chartWidth, List<Node> output)
    {
        var plotWidth = scale.RangeEnd - scale.RangeStart;
        List<double> ticks = [];
        List<string> labels = [];

        if (kind == XKind.Time)
        {
            var maxCount = Math.Max(2, (int)(plotWidth / 50));
            foreach (var interval in TimeTicks.FromSmallest(scale.DomainMin, scale.DomainMax, maxCount))
            {
                ticks = TimeTicks.Generate(scale.DomainMin, scale.DomainMax, interval);
                labels = ticks.Select(t => TimeFormatter.Format(t, interval, culture, axis.Format)).ToList();
                if (Fits(ticks, labels, scale, measure, font, chartWidth))
                {
                    break;
                }
            }
        }
        else
        {
            for (var target = Math.Clamp((int)(plotWidth / 70), 2, 12); target >= 1; target--)
            {
                var step = NiceTicks.Step(scale.DomainMax - scale.DomainMin, target);
                ticks = NiceTicks.Ticks(scale.DomainMin, scale.DomainMax, step);
                var format = NumberFormatter.ForTicks(ticks, step, culture, axis.Format);
                labels = ticks.Select(format).ToList();
                if (Fits(ticks, labels, scale, measure, font, chartWidth))
                {
                    break;
                }
            }
        }

        var pixels = ticks.Select(scale.Map).ToList();
        foreach (var label in PlaceLabels(ticks, labels, scale, measure, font, chartWidth))
        {
            if (label.Skip)
            {
                continue; // no room at all
            }

            output.Add(new TextNode(label.Text, new PointF((float)label.X, (float)(plotBottom + 6)), new TextStyle("axis-label"), label.Anchor)
            {
                Baseline = Baseline.Top,
            });
        }

        return pixels;
    }

    private readonly record struct PlacedLabel(string Text, double X, Anchor Anchor, double Left, double Right, bool Skip);

    /// <summary>
    /// Positions labels under their ticks. Labels at the plot edges align to their tick instead
    /// of centering, so they stay clear of the Y labels and inside the chart.
    /// </summary>
    private static List<PlacedLabel> PlaceLabels(List<double> ticks, List<string> labels, LinearScale scale, ITextMeasurer measure,
        double font, double chartWidth)
    {
        var placed = new List<PlacedLabel>(ticks.Count);
        for (var i = 0; i < ticks.Count; i++)
        {
            var px = scale.Map(ticks[i]);
            var width = measure.Measure(labels[i], font);
            var anchor = Anchor.Middle;
            var (left, right) = (px - width / 2, px + width / 2);
            if (left < scale.RangeStart - 4)
            {
                (anchor, left, right) = (Anchor.Start, px, px + width);
            }
            else if (right > Math.Min(chartWidth, scale.RangeEnd + 12))
            {
                (anchor, left, right) = (Anchor.End, px - width, px);
            }

            var skip = left < scale.RangeStart - 4 || right > chartWidth;
            placed.Add(new PlacedLabel(labels[i], px, anchor, left, right, skip));
        }

        return placed;
    }

    private static bool Fits(List<double> ticks, List<string> labels, LinearScale scale, ITextMeasurer measure, double font, double chartWidth)
    {
        var placed = PlaceLabels(ticks, labels, scale, measure, font, chartWidth).Where(p => !p.Skip).ToList();
        for (var i = 1; i < placed.Count; i++)
        {
            if (placed[i].Left - placed[i - 1].Right < 12)
            {
                return false;
            }
        }

        return true;
    }

    private static void AddCategoryLabels(BandScale band, ITextMeasurer measure, double font, double plotBottom, List<Node> output)
    {
        if (band.Categories.Count == 0)
        {
            return;
        }

        // Show every k-th label when they do not all fit.
        var widest = band.Categories.Max(c => measure.Measure(c, font));
        var every = Math.Max(1, (int)Math.Ceiling((widest + 8) / Math.Max(1, band.Step)));
        for (var i = 0; i < band.Categories.Count; i += every)
        {
            output.Add(new TextNode(band.Categories[i], new PointF((float)band.Center(i), (float)(plotBottom + 6)), new TextStyle("axis-label"), Anchor.Middle)
            {
                Baseline = Baseline.Top,
            });
        }
    }

    // ---------- Legend ----------

    private const double SwatchWidth = 12;
    private const double SwatchGap = 6;
    private const double ItemGap = 16;

    private static List<(LegendEntry Entry, double Width)> MeasureLegend(List<LegendEntry> entries, ITextMeasurer measure, double font) =>
        entries.Select(e => (e, SwatchWidth + SwatchGap + measure.Measure(e.Label, font))).ToList();

    private static List<List<(LegendEntry Entry, double Width)>> WrapRows(List<(LegendEntry Entry, double Width)> items, double maxWidth)
    {
        var rows = new List<List<(LegendEntry, double)>>();
        var row = new List<(LegendEntry, double)>();
        double used = 0;
        foreach (var item in items)
        {
            var add = row.Count == 0 ? item.Width : item.Width + ItemGap;
            if (row.Count > 0 && used + add > maxWidth)
            {
                rows.Add(row);
                row = [];
                used = 0;
                add = item.Width;
            }

            row.Add(item);
            used += add;
        }

        if (row.Count > 0)
        {
            rows.Add(row);
        }

        return rows;
    }

    private static GroupNode BuildLegend(List<LegendEntry> entries, List<(LegendEntry Entry, double Width)> items,
        List<List<(LegendEntry Entry, double Width)>> rows, LegendPosition position, RectF plot, double top, double bottomTop,
        double chartWidth, double lineHeight, double font, StyleTable styles)
    {
        var nodes = new List<Node>();
        var rowHeight = lineHeight + 4;
        if (position == LegendPosition.Right)
        {
            var x = plot.Right + 16;
            var y = plot.Y;
            foreach (var (entry, w) in items)
            {
                nodes.Add(LegendItem(entry, x, y, w, rowHeight, styles));
                y += (float)rowHeight;
            }
        }
        else
        {
            var y = position == LegendPosition.Top ? top : bottomTop;
            foreach (var row in rows)
            {
                double x = PadLeft;
                foreach (var (entry, w) in row)
                {
                    nodes.Add(LegendItem(entry, x, y, w, rowHeight, styles));
                    x += w + ItemGap;
                }

                y += rowHeight;
            }
        }

        return new GroupNode(nodes) { Class = "rio-legend", AriaLabel = "Legend" };
    }

    private static GroupNode LegendItem(LegendEntry entry, double x, double y, double width, double height, StyleTable styles)
    {
        var cy = y + height / 2;
        var swatchStyle = styles.Add("swatch-" + entry.SeriesId, new Style { Fill = entry.Color, Stroke = entry.Color, StrokeWidth = 2 });
        Node swatch = entry.Type == SeriesType.Line
            ? new PathNode(new PathBuilder().MoveTo(x, cy).LineTo(x + SwatchWidth, cy).Build(), swatchStyle) { Class = "rio-swatch rio-swatch-line" }
            : new RectNode(new RectF((float)x, (float)(cy - 5), (float)SwatchWidth, 10), swatchStyle) { CornerRadius = 2, Class = "rio-swatch" };

        var state = !entry.Visible ? ", hidden" : entry.IsEmpty ? ", no data" : "";
        return new GroupNode(
        [
            new RectNode(new RectF((float)(x - 3), (float)y, (float)(width + 6), (float)height), new StyleRef("legend-hit")) { CornerRadius = 3, Class = "rio-legend-hit" },
            swatch,
            new TextNode(entry.Label, new PointF((float)(x + SwatchWidth + SwatchGap), (float)cy), new TextStyle("legend-label"), Anchor.Start),
        ])
        {
            Key = "legend:" + entry.SeriesId,
            Class = "rio-legend-item" + (entry.Visible ? "" : " rio-legend-off") + (entry.IsEmpty ? " rio-legend-empty" : ""),
            Interaction = new NodeInteraction("legend", entry.SeriesId) { Pressed = entry.Visible },
            AriaLabel = entry.Label + state,
        };
    }

    // ---------- Helpers ----------

    private static RectF Inflate(RectF r, float by) => new(r.X - by, r.Y - by, r.Width + 2 * by, r.Height + 2 * by);

    private static string AxisLabel(string name, string? title, List<string> labels) =>
        (title is null ? name : $"{name}: {title}") + (labels.Count > 1 ? $", from {labels[0]} to {labels[^1]}" : "");

    private static string EmptyMessage(List<Resolved> series) =>
        series.Count > 0 && series.All(s => !s.Visible) ? "All series are hidden" : "No data available";
}
