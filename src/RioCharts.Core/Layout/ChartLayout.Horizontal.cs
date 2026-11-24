using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;
using RioCharts.Core.Text;

namespace RioCharts.Core.Layout;

public static partial class ChartLayout
{
    /// <summary>
    /// Horizontal dumbbells: in the middle of each row, a line from the first value to the second, a
    /// hollow dot at the first and a filled dot at the second; data labels outside the dots.
    /// </summary>
    private static void HorizontalDumbbells(Resolved r, (double From, double To, int Index)[] segments, Func<SeriesData, int, int> rowOf, BandScale band,
        Scales.ContinuousScale valueScale, RectF plot, bool compact, Text.ITextMeasurer measure, double font, CultureInfo culture, ValueFormat format, StyleTable styles,
        List<Node> nodes, List<Node> labelNodes, List<(float X, float Y, float Width, int Index, int Row, double Share)> points)
    {
        const double radius = Series.DumbbellSeriesType.Radius;
        var connectors = new PathBuilder();
        var froms = new PathBuilder();
        var tos = new Dictionary<string, PathBuilder>(StringComparer.Ordinal);
        foreach (var (from, to, i) in segments)
        {
            var row = rowOf(r.Data, i);
            var cy = band.Center(row);
            var (x0, x1) = (valueScale.Map(from), valueScale.Map(to));
            if (double.IsFinite(from) && double.IsFinite(to))
            {
                connectors.MoveTo(x0, cy).LineTo(x1, cy);
            }

            if (double.IsFinite(from))
            {
                Series.DumbbellSeriesType.Dot(froms, x0, cy, radius - 1);
            }

            if (double.IsFinite(to))
            {
                var color = r.Data.Colors?[i] ?? Series.ColorBands.ColorOf(r.Spec.ColorBands, to) ?? r.Color;
                Series.DumbbellSeriesType.Dot(tos.TryGetValue(color, out var path) ? path : tos[color] = new PathBuilder(), x1, cy, radius);
            }

            var ends = new[] { x0, x1 }.Where(double.IsFinite).ToList();
            var (left, right) = (ends.Min(), ends.Max());
            points.Add(((float)((left + right) / 2), (float)cy, (float)(right - left + radius * 2 + 6), i, row, 0));

            if (r.Spec.DataLabels == true && !compact)
            {
                // The lower value left of its dot, the higher right of its dot.
                foreach (var (value, x) in new[] { (from, x0), (to, x1) }.Where(e => double.IsFinite(e.Item1)))
                {
                    var leftSide = ends.Count == 2 && x == left && left < right;
                    var text = ValueFormatter.Format(value, culture, format);
                    var at = leftSide ? x - radius - 4 : x + radius + 4;
                    var w = measure.Measure(text, font - 1);
                    if (leftSide ? at - w >= plot.X - 4 : at + w <= plot.Right + 8)
                    {
                        labelNodes.Add(new TextNode(text, new PointF((float)at, (float)cy), new TextStyle("data-label"), leftSide ? Anchor.End : Anchor.Start)
                        {
                            Class = "rio-data-label",
                        });
                    }
                }
            }
        }

        nodes.Add(new PathNode(connectors.Build(), styles.Add($"series-{r.ColorIndex}-dumbbell", new Style { Stroke = r.Color, StrokeWidth = 3, Opacity = 0.45f }))
        {
            Class = "rio-dumbbell-line",
        });
        nodes.Add(new PathNode(froms.Build(), styles.Add($"series-{r.ColorIndex}-from", new Style { Fill = ThemeTokens.Surface, Stroke = r.Color, StrokeWidth = 2 }))
        {
            Class = "rio-dumbbell-from",
        });
        foreach (var (color, path) in tos)
        {
            var style = color == r.Color ? styles.Add($"series-{r.ColorIndex}-to", new Style { Fill = color }) : styles.Add($"dot-{color}", new Style { Fill = color });
            nodes.Add(new PathNode(path.Build(), style) { Class = "rio-dumbbell-to" });
        }
    }

    /// <summary>Series types a horizontal chart draws: bars, box plots, waterfalls and dumbbells.</summary>
    private static bool DrawsHorizontally(SeriesType type) => type is SeriesType.Bar or SeriesType.BoxPlot or SeriesType.Waterfall or SeriesType.Dumbbell;

    /// <summary>A horizontal box's height in its slot: as for upright boxes, at most 56 px.</summary>
    private static double BoxHeight(double slot) => Math.Clamp(slot - (slot > 12 ? 6 : 0), 1, 56);

    /// <summary>
    /// Horizontal box plots: in each row's slot a box from the lower to the upper quartile with a stronger median
    /// line, thin whiskers out to the whisker ends and outliers as small hollow dots, as for upright boxes.
    /// </summary>
    private static void HorizontalBoxes(Resolved r, (double Q1, double Q3, int Index)[] boxes, Func<SeriesData, int, int> rowOf, BandScale band, int slot,
        double thickness, double groupOffset, Scales.ContinuousScale valueScale, StyleTable styles, List<Node> nodes,
        List<(float X, float Y, float Width, int Index, int Row, double Share)> points)
    {
        const double outlierRadius = 2.5;
        if (r.Data.Box is not { } box)
        {
            return;
        }

        var h = BoxHeight(thickness);
        var boxStyle = styles.Add($"series-{r.ColorIndex}-box", new Style { Fill = r.Color, FillOpacity = 0.22f, Stroke = r.Color, StrokeWidth = 1f });
        var whiskers = new PathBuilder();
        var medians = new PathBuilder();
        var outliers = new PathBuilder();
        var (hasWhiskers, hasOutliers) = (false, false);
        var rects = new List<Node>();
        foreach (var (q1, q3, i) in boxes)
        {
            var row = rowOf(r.Data, i);
            var cy = band.Start(row) + groupOffset + slot * thickness + thickness / 2;
            var (x1, x3, xm) = (valueScale.Map(q1), valueScale.Map(q3), valueScale.Map(r.Data.Y[i]));
            var (left, right) = (Math.Min(x1, x3), Math.Max(x1, x3));
            rects.Add(new RectNode(new RectF((float)left, (float)(cy - h / 2), (float)Math.Max(1, right - left), (float)h), boxStyle)
            {
                CornerRadius = (float)Math.Min(2, h / 6),
                Class = "rio-box",
            });
            medians.MoveTo(xm, cy - h / 2).LineTo(xm, cy + h / 2);
            if (double.IsFinite(box.High[i]) && box.High[i] > q3)
            {
                whiskers.MoveTo(x3, cy).LineTo(valueScale.Map(box.High[i]), cy);
                hasWhiskers = true;
            }

            if (double.IsFinite(box.Low[i]) && box.Low[i] < q1)
            {
                whiskers.MoveTo(x1, cy).LineTo(valueScale.Map(box.Low[i]), cy);
                hasWhiskers = true;
            }

            foreach (var v in box.OutliersOf(i))
            {
                var ox = valueScale.Map(v);
                outliers.MoveTo(ox, cy - outlierRadius).Arc(ox, cy, outlierRadius, 0, Math.PI * 2).Close();
                hasOutliers = true;
            }

            points.Add(((float)((left + right) / 2), (float)cy, (float)Math.Max(6, right - left), i, row, 0));
        }

        if (hasWhiskers)
        {
            nodes.Add(new PathNode(whiskers.Build(), styles.Add($"series-{r.ColorIndex}-whisker", new Style { Stroke = r.Color, StrokeWidth = 1f, Fill = "none" }))
            {
                Class = "rio-whiskers",
            });
        }

        nodes.AddRange(rects);
        if (rects.Count > 0)
        {
            nodes.Add(new PathNode(medians.Build(), styles.Add($"series-{r.ColorIndex}-median", new Style { Stroke = r.Color, StrokeWidth = 1.75f, Fill = "none" }))
            {
                Class = "rio-medians",
            });
        }

        if (hasOutliers)
        {
            nodes.Add(new PathNode(outliers.Build(), styles.Add($"series-{r.ColorIndex}-outlier", new Style { Fill = ThemeTokens.Surface, Stroke = r.Color, StrokeWidth = 1f }))
            {
                Class = "rio-outliers",
            });
        }
    }

    /// <summary>
    /// Horizontal bars (<see cref="ChartDefinition.Horizontal"/>): categories down the left side, first on
    /// top, values along the bottom (<see cref="ChartDefinition.Y"/> formats them). Several bar series are
    /// grouped side by side in each row, or stacked with <see cref="SeriesOptions.Stack"/>. Box plots and
    /// waterfalls take a slot like a bar series; a waterfall runs down the rows. Value lines and
    /// bands from the annotations are drawn across the rows.
    /// </summary>
    private static ChartLayoutResult ComputeHorizontal(ChartDefinition spec, List<Resolved> resolved, LayoutOptions options, List<string> warnings, StyleTable styles,
        TimeConversion? time)
    {
        var culture = options.Culture;
        var texts = options.Texts ?? ChartTexts.For(culture);
        var measure = options.Measurer;
        var compact = spec.Compact || AutoCompacts(spec, options);
        var width = Math.Max(compact ? 8 : 50, options.Width);
        var height = Math.Max(compact ? 8 : 50, options.Height);
        var font = options.FontSize;
        var lineHeight = font * 1.3;
        var chrome = FitChrome(spec.AutoCompact && !compact, width, height, font);

        foreach (var r in resolved.Where(r => !DrawsHorizontally(r.Spec.Type)))
        {
            warnings.Add($"series '{r.Spec.Id}': {r.Spec.Type} series cannot be drawn in a horizontal chart; the series is skipped.");
        }

        // Bars, box plots, waterfalls and dumbbells; dumbbells sit in the middle of the row, the others share it in slots.
        var bars = resolved.Where(r => DrawsHorizontally(r.Spec.Type)).ToList();
        static bool IsDumbbell(Resolved r) => r.Spec.Type == SeriesType.Dumbbell;
        static bool IsBox(Resolved r) => r.Spec.Type == SeriesType.BoxPlot;
        static bool IsWaterfall(Resolved r) => r.Spec.Type == SeriesType.Waterfall;
        static bool IsBar(Resolved r) => r.Spec.Type == SeriesType.Bar;
        var xKind = ResolveXKind(spec.X.Scale, bars);

        // ---------- Rows: categories, or the distinct x values as labels ----------
        // The row labels are also the tooltip headers, so TooltipFormat does not apply here.
        var formatX = TimeFormat(xKind, bars.SelectMany(r => r.Data.X).Take(5000), culture, spec.X.Format, time, options.ViewerTimeZone);
        List<string> rows;
        Func<SeriesData, int, int> rowOf;
        if (xKind == XKind.Category)
        {
            rows = MergeCategories(bars);
            var index = rows.Select((c, i) => (c, i)).ToDictionary(t => t.c, t => t.i, StringComparer.Ordinal);
            rowOf = (d, i) => (int)d.X[i] is var c && c >= 0 && c < d.Categories.Count && index.TryGetValue(d.Categories[c], out var row) ? row : -1;
        }
        else
        {
            var xs = bars.SelectMany(r => r.Data.X.Where((_, i) => i < r.Data.Count)).Distinct().Order().ToList();
            var index = xs.Select((x, i) => (x, i)).ToDictionary(t => t.x, t => t.i);
            rows = xs.Select(formatX).ToList();
            rowOf = (d, i) => index.TryGetValue(d.X[i], out var row) ? row : -1;
        }

        var drawn = bars.Where(r => r.Visible && !r.Empty && (r.Data.Kind == xKind || r.Data.Count == 0)).ToList();
        var hasData = drawn.Count > 0 && rows.Count > 0;

        // ---------- Values, stacks and the value range ----------
        static string KeyOf(Resolved r) => r.StackGroup is { } s && IsBar(r) ? "stack|" + s : "series|" + r.Spec.Id;
        var slotKeys = drawn.Where(r => !IsDumbbell(r)).Select(KeyOf).Distinct().ToList();
        var segments = new Dictionary<Resolved, (double Start, double End, int Index)[]>();
        var stacks = new Dictionary<(string Key, int Row, bool Positive), double>();
        var totals = new Dictionary<(string Key, int Row), double>(); // per stack and row, for percent
        foreach (var r in drawn)
        {
            var key = KeyOf(r);
            var stacked = r.StackGroup is not null && IsBar(r);
            var list = new List<(double, double, int)>();
            for (var i = 0; i < r.Data.Count; i++)
            {
                var v = r.Data.Y[i];
                var row = rowOf(r.Data, i);
                if (IsBox(r))
                {
                    // From the lower to the upper quartile; the whiskers and outliers come from the box.
                    if (row >= 0 && r.Data.Box is { } box && double.IsFinite(v) && double.IsFinite(box.Q1[i]) && double.IsFinite(box.Q3[i]))
                    {
                        list.Add((box.Q1[i], box.Q3[i], i));
                    }

                    continue;
                }

                if (IsWaterfall(r))
                {
                    // From the running total before the change to the one after (a total from zero).
                    if (row >= 0 && r.Lower is { } lower && double.IsFinite(r.Upper[i]) && double.IsFinite(lower[i]))
                    {
                        list.Add((lower[i], r.Upper[i], i));
                    }

                    continue;
                }

                if (IsDumbbell(r))
                {
                    // From its first value to its second; either may be missing.
                    var from = r.Data.Low?[i] ?? double.NaN;
                    if (row >= 0 && (double.IsFinite(v) || double.IsFinite(from)))
                    {
                        list.Add((from, v, i));
                    }

                    continue;
                }

                if (!double.IsFinite(v) || row < 0)
                {
                    continue;
                }

                var start = 0d;
                if (stacked)
                {
                    var k = (key, row, v >= 0);
                    start = stacks.GetValueOrDefault(k);
                    stacks[k] = start + v;
                    totals[(key, row)] = totals.GetValueOrDefault((key, row)) + Math.Abs(v);
                }

                list.Add((start, start + v, i));
            }

            segments[r] = [.. list];
        }

        // Bars and waterfalls start at zero; box plots and dumbbells alone fit their values.
        var withZero = drawn.Any(r => IsBar(r) || IsWaterfall(r)) || drawn.Count == 0;
        double vMin = withZero ? 0 : double.PositiveInfinity, vMax = withZero ? 0 : double.NegativeInfinity;
        foreach (var r in drawn)
        {
            var list = segments[r];
            if (IsDumbbell(r))
            {
                foreach (var (from, to, _) in list)
                {
                    foreach (var v in new[] { from, to }.Where(double.IsFinite))
                    {
                        (vMin, vMax) = (Math.Min(vMin, v), Math.Max(vMax, v));
                    }
                }

                continue;
            }

            if (IsBox(r) && r.Data.Box is { } box)
            {
                // The extremes, outliers included.
                foreach (var (_, _, i) in list)
                {
                    foreach (var v in new[] { box.Min[i], box.Low[i], box.Q1[i], box.Q3[i], box.High[i], box.Max[i] }.Where(double.IsFinite))
                    {
                        (vMin, vMax) = (Math.Min(vMin, v), Math.Max(vMax, v));
                    }
                }

                continue;
            }

            for (var n = 0; n < list.Length; n++)
            {
                if (r.Percent && IsBar(r) && totals.TryGetValue((KeyOf(r), rowOf(r.Data, list[n].Index)), out var total))
                {
                    list[n] = (Share(list[n].Start, total), Share(list[n].End, total), list[n].Index);
                }

                vMin = Math.Min(vMin, Math.Min(list[n].Start, list[n].End));
                vMax = Math.Max(vMax, Math.Max(list[n].Start, list[n].End));
                if (HasErrors(r))
                {
                    // The error bars fit on the axis too, moved with their stack segment.
                    var (low, high) = RawErrorAt(r, list[n].Index);
                    var shift = list[n].End - r.Data.Y[list[n].Index];
                    if (double.IsFinite(low))
                    {
                        vMin = Math.Min(vMin, low + shift);
                        vMax = Math.Max(vMax, high + shift);
                    }
                }
            }
        }

        var percent = drawn.Any(r => r.Percent && IsBar(r));
        var valueFormat = ValueFormat.Of(spec.Y, percent ? "P0" : null);

        var lines = new List<(Annotation Spec, double A, double B)>();
        foreach (var (a, i) in spec.Annotations.Select((a, i) => (a, i)))
        {
            if (a.Axis != AnnotationAxis.Y || a.Type is not (AnnotationType.Line or AnnotationType.Band))
            {
                warnings.Add($"annotations[{i}]: horizontal bar charts show value lines and bands only; the annotation is skipped.");
                continue;
            }

            var first = AxisValue.ToDouble(a.Type == AnnotationType.Band ? a.From : a.Value, XKind.Numeric, []);
            var second = a.Type == AnnotationType.Band ? AxisValue.ToDouble(a.To, XKind.Numeric, []) : double.NaN;
            if (!double.IsFinite(first) || (a.Type == AnnotationType.Band && !double.IsFinite(second)))
            {
                warnings.Add($"annotations[{i}]: needs a number; the annotation is skipped.");
                continue;
            }

            lines.Add((a, first, second));
            if (a.Type == AnnotationType.Line && (a.ExpandsDomain ?? true))
            {
                vMin = Math.Min(vMin, first);
                vMax = Math.Max(vMax, first);
            }
        }

        if (!double.IsFinite(vMin))
        {
            (vMin, vMax) = (0, 1);
        }

        if (vMax == vMin)
        {
            vMax = vMin + 1;
        }

        // ---------- Title and legend ----------
        var statics = new List<Node>();
        var y0 = PadTop;
        if (!compact && chrome.Title && !string.IsNullOrWhiteSpace(spec.Title))
        {
            var titleHeight = options.TitleFontSize * 1.3;
            statics.Add(new TextNode(spec.Title!, new PointF((float)PadLeft, (float)(y0 + titleHeight / 2)), new TextStyle("title"), Anchor.Start) { Class = "rio-title" });
            y0 += titleHeight + 6;
        }

        var legendEntries = bars
            .Select(r => new LegendEntry(r.Spec.Id, r.Spec.DisplayName, r.Color, r.Spec.Type, r.Visible, r.Empty,
                ChartDescriber.DescribeSeries(spec, r.Spec, r.Data, xKind, culture, texts) ?? $"{r.Spec.DisplayName}: {texts.NoData}"))
            .ToList();
        var showLegend = !compact && spec.Legend.Mode switch
        {
            LegendMode.Show => legendEntries.Count > 0,
            LegendMode.Hide => false,
            _ => legendEntries.Count > 1 && chrome.Legend,
        };
        List<List<(LegendEntry Entry, double Width)>> legendRows = [];
        var legendItems = showLegend ? MeasureLegend(legendEntries, measure, font) : [];
        if (showLegend)
        {
            legendRows = WrapRows(legendItems, width - PadLeft - PadRight);
            var legendHeight = legendRows.Count * (lineHeight + 4);
            if (LegendTooTall(spec, legendHeight, height))
            {
                showLegend = false;
            }
            else
            {
                y0 += legendHeight + 8;
            }
        }

        // ---------- Plot ----------
        var showRowLabels = !compact && chrome.YLabels;
        var maxLabelWidth = width * 0.45;
        var rowLabels = rows.Select(l => Shorten(l, maxLabelWidth, measure, font)).ToList();
        var labelWidth = showRowLabels && rowLabels.Count > 0 ? rowLabels.Max(l => measure.Measure(l, font)) : 0;
        var valueLabelHeight = compact || !chrome.XLabels ? 0 : lineHeight + 6;
        var valueTitleHeight = compact || !chrome.XTitle || string.IsNullOrWhiteSpace(spec.Y.Title) ? 0 : lineHeight + 4;
        var plotLeft = compact ? 1 : PadLeft + (showRowLabels ? labelWidth + LabelGap : 0);
        var plotRight = compact ? width - 1 : width - PadRight - 8; // room for the last tick label's half
        // Labels of value lines and bands go in a strip above the rows, not over the first bar.
        var lineLabels = !compact && lines.Any(l => !string.IsNullOrWhiteSpace(l.Spec.Label)) ? lineHeight + 2 : 0;
        var plotTop = compact ? 1 : y0 + lineLabels;
        var plotBottom = compact ? height - 1 : height - PadBottom - valueTitleHeight - valueLabelHeight;
        var plot = new RectF((float)plotLeft, (float)plotTop, (float)Math.Max(2, plotRight - plotLeft), (float)Math.Max(2, plotBottom - plotTop));

        var (niceMin, niceMax, step) = NiceTicks.NiceDomain(vMin, vMax, Math.Clamp((int)(plot.Width / 90), 2, 10), NumberFormatter.MinStep(valueFormat, culture) ?? 0);
        var valueScale = new ContinuousScale(spec.Y.Min ?? niceMin, spec.Y.Max ?? niceMax, plot.X, plot.Right);
        var ticks = NiceTicks.Ticks(valueScale.DomainMin, valueScale.DomainMax, step);
        var tickFormat = NumberFormatter.ForTicks(ticks, step, culture, valueFormat);
        var band = new BandScale(rows, plot.Y, plot.Bottom, 0.25, 0.1);
        var slotCount = Math.Max(1, slotKeys.Count);
        // Bars no thicker than with four rows (or MaxBarWidth), centered on their row.
        var groupHeight = SlotGroupWidth(band.Bandwidth, plot.Height, slotCount, drawn.Where(r => !IsDumbbell(r)));
        var groupOffset = (band.Bandwidth - groupHeight) / 2;
        var thickness = groupHeight / slotCount;
        var gap = thickness > 8 ? 2 : 0;

        // ---------- Grid, zero line, value labels ----------
        if (hasData && (spec.Y.GridLines ?? !compact))
        {
            var grid = ticks.Where(t => t != 0)
                .Select(t => (Node)new PathNode(new PathBuilder().MoveTo(valueScale.Map(t), plot.Y).LineTo(valueScale.Map(t), plot.Bottom).Build(), new StyleRef("grid")))
                .ToList();
            statics.Add(new GroupNode(grid) { Class = "rio-grid" });
        }

        if (hasData)
        {
            var zero = valueScale.Map(Math.Clamp(0, valueScale.DomainMin, valueScale.DomainMax));
            statics.Add(new PathNode(new PathBuilder().MoveTo(zero, plot.Y).LineTo(zero, plot.Bottom).Build(), new StyleRef("axis")) { Class = "rio-baseline" });
        }

        if (hasData && valueLabelHeight > 0)
        {
            var labels = ticks.Select(t => (Node)new TextNode(tickFormat(t), new PointF((float)valueScale.Map(t), (float)(plot.Bottom + 4 + lineHeight / 2)),
                new TextStyle("axis-label"), Anchor.Middle)).ToList();
            statics.Add(new GroupNode(labels)
            {
                Class = "rio-axis-x",
                AriaLabel = AxisLabel(texts, culture, spec.Y.Title is { } vt ? $"{texts.XAxis}: {vt}" : texts.XAxis, null, ticks.Select(tickFormat).ToList()),
            });
        }

        if (valueTitleHeight > 0)
        {
            statics.Add(new TextNode(spec.Y.Title!, new PointF(plot.CenterX, (float)(plot.Bottom + valueLabelHeight + valueTitleHeight / 2)), new TextStyle("axis-title"),
                Anchor.Middle) { Class = "rio-axis-title" });
        }

        if (showRowLabels && hasData)
        {
            var labels = rowLabels.Select((l, i) => (Node)new TextNode(l, new PointF((float)(plot.X - LabelGap), (float)(band.Start(i) + band.Bandwidth / 2)),
                new TextStyle("axis-label"), Anchor.End) { Class = "rio-row-label" }).ToList();
            statics.Add(new GroupNode(labels) { Class = "rio-axis-y", AriaLabel = texts.YAxis + (spec.X.Title is { } ct ? ": " + ct : "") });
        }

        if (showLegend)
        {
            statics.Add(BuildLegend(legendItems, legendRows, LegendPosition.Top, 0, plot, y0 - legendRows.Count * (lineHeight + 4) - 8, 0, lineHeight, styles,
                texts.Legend));
        }

        // ---------- Bars ----------
        var seriesNodes = new List<Node>();
        var selectedMarks = new List<Node>();
        var labelNodes = new List<Node>();
        var hitTest = new HitTestIndex();
        var tooltipSeries = new List<TooltipSeries>();
        foreach (var r in drawn)
        {
            var slot = slotKeys.IndexOf(KeyOf(r));
            var stacked = r.StackGroup is not null && IsBar(r);
            var style = styles.Add($"series-{r.ColorIndex}-bar", new Style { Fill = r.Color });
            var format = ValueFormat.Of(r.Spec.Format, spec.Y);
            var nodes = new List<Node>();
            var points = new List<(float X, float Y, float Width, int Index, int Row, double Share)>();
            var errors = HasErrors(r) ? new PathBuilder() : null;
            var anyError = false;
            if (IsDumbbell(r))
            {
                HorizontalDumbbells(r, segments[r], rowOf, band, valueScale, plot, compact, measure, font, culture, format, styles, nodes, labelNodes, points);
            }
            else if (IsBox(r))
            {
                HorizontalBoxes(r, segments[r], rowOf, band, slot, thickness, groupOffset, valueScale, styles, nodes, points);
            }

            var connectors = IsWaterfall(r) ? new PathBuilder() : null;
            var hasConnectors = false;
            (double X, double Bottom)? previous = null;
            foreach (var (start, end, i) in IsDumbbell(r) || IsBox(r) ? [] : segments[r])
            {
                var row = rowOf(r.Data, i);
                var top = band.Start(row) + groupOffset + slot * thickness + gap / 2.0;
                var h = Math.Max(1, thickness - gap);
                var (x0, x1) = (valueScale.Map(Math.Min(start, end)), valueScale.Map(Math.Max(start, end)));
                if (IsWaterfall(r))
                {
                    // Rises in the positive color, falls in the negative one, totals in the series color; a thin
                    // line carries each bar's end down to the next bar.
                    var color = Series.WaterfallSeriesType.ColorOf(r.Data, i, r.Color);
                    var total = r.Data.Totals is { } flags && flags[i];
                    nodes.Add(new RectNode(new RectF((float)x0, (float)top, (float)Math.Max(start == end ? 0 : 1, x1 - x0), (float)h),
                        styles.Add($"bar-{color}", new Style { Fill = color }))
                    {
                        CornerRadius = (float)Math.Min(2, h / 6),
                        Class = total ? "rio-waterfall rio-waterfall-total" : "rio-waterfall",
                    });
                    if (previous is { } p)
                    {
                        connectors!.MoveTo(p.X, p.Bottom).LineTo(p.X, top);
                        hasConnectors = true;
                    }

                    previous = (valueScale.Map(end), top + h);
                }
                else
                {
                    var own = (r.Data.Colors?[i] ?? Series.ColorBands.ColorOf(r.Spec.ColorBands, r.Data.Y[i])) is { } color ? styles.Add($"bar-{color}", new Style { Fill = color }) : style;
                    nodes.Add(new RectNode(new RectF((float)x0, (float)top, (float)Math.Max(0.5, x1 - x0), (float)h), own)
                    {
                        CornerRadius = stacked ? 0 : (float)Math.Min(3, h / 4),
                    });
                }
                points.Add(((float)((x0 + x1) / 2), (float)(top + h / 2), (float)Math.Max(6, x1 - x0), i, row, end - start));
                if (errors is not null && RawErrorAt(r, i) is var (low, high) && double.IsFinite(low))
                {
                    var shift = end - r.Data.Y[i];
                    HorizontalErrorBar(errors, valueScale.Map(low + shift), valueScale.Map(high + shift), top + h / 2, h);
                    anyError = true;
                }

                if (r.Spec.DataLabels == true && !compact)
                {
                    var text = ValueFormatter.Format(r.Data.Y[i], culture, format);
                    var textWidth = measure.Measure(text, font - 1);
                    var positive = end >= start;
                    var outside = positive ? x1 + 4 + textWidth <= plot.Right + 8 : x0 - 4 - textWidth >= plot.X - 4;
                    if (stacked)
                    {
                        // Inside the segment when it fits; otherwise no label (it would collide with the next).
                        if (x1 - x0 >= textWidth + 6 && h >= font)
                        {
                            labelNodes.Add(new TextNode(text, new PointF((float)((x0 + x1) / 2), (float)(top + h / 2)), new TextStyle("data-label-inverse"), Anchor.Middle)
                            {
                                Class = "rio-data-label",
                            });
                        }
                    }
                    else if (outside)
                    {
                        labelNodes.Add(new TextNode(text, new PointF((float)(positive ? x1 + 4 : x0 - 4), (float)(top + h / 2)), new TextStyle("data-label"),
                            positive ? Anchor.Start : Anchor.End) { Class = "rio-data-label" });
                    }
                    else if (x1 - x0 >= textWidth + 6)
                    {
                        labelNodes.Add(new TextNode(text, new PointF((float)(positive ? x1 - 4 : x0 + 4), (float)(top + h / 2)), new TextStyle("data-label-inverse"),
                            positive ? Anchor.End : Anchor.Start) { Class = "rio-data-label" });
                    }
                }
            }

            if (hasConnectors)
            {
                nodes.Add(new PathNode(connectors!.Build(), styles.Add("waterfall-connector", new Style { Stroke = ThemeTokens.Axis, StrokeWidth = 1f, Fill = "none" }))
                {
                    Class = "rio-connectors",
                });
            }

            if (anyError)
            {
                nodes.Add(new PathNode(errors!.Build(), styles.Add("error-bar", new Style { Stroke = ThemeTokens.Text, StrokeWidth = 1.25f, Fill = "none" }))
                {
                    Class = "rio-error-bar",
                });
            }
            else if (HasErrorData(r) && r.Percent)
            {
                warnings.Add($"series '{r.Spec.Id}': error bars are not drawn on series stacked to 100 %.");
            }

            seriesNodes.Add(new GroupNode(nodes)
            {
                Key = "series:" + r.Spec.Id,
                Class = r.Spec.Type switch
                {
                    SeriesType.Dumbbell => "rio-series rio-series-dumbbell",
                    SeriesType.BoxPlot => "rio-series rio-series-boxplot",
                    SeriesType.Waterfall => "rio-series rio-series-waterfall",
                    _ => "rio-series rio-series-bar",
                },
                AriaLabel = texts.FillCount(nameof(ChartTexts.SeriesPoints), points.Count, culture, r.Spec.DisplayName, points.Count),
            });

            points.Sort((a, b) => a.Row.CompareTo(b.Row)); // top to bottom, for the keyboard
            var cellHeight = IsDumbbell(r) ? Series.DumbbellSeriesType.Radius * 2 + 6 : IsBox(r) ? BoxHeight(thickness) : Math.Max(1, thickness - gap);
            if (options.SelectedPoints is { } selected && selected.TryGetValue(r.Spec.Id, out var picked) && picked.Count > 0)
            {
                // Selected bars, boxes, waterfall steps and dumbbells: an outline around the shape the pointer hovers.
                var outline = styles.Add("selected-bar", new Style { Stroke = ThemeTokens.Text, StrokeWidth = 1.5f });
                foreach (var p in points.Where(p => picked.Contains(p.Index)))
                {
                    selectedMarks.Add(new RectNode(new RectF((float)(p.X - p.Width / 2 - 1.5), (float)(p.Y - cellHeight / 2 - 1.5), p.Width + 3, (float)(cellHeight + 3)), outline)
                    {
                        CornerRadius = 3,
                        Class = "rio-selected-bar",
                    });
                }
            }

            var hits = new HitTestIndex.SeriesHits(r.Spec.Id, [.. points.Select(p => p.X)], [.. points.Select(p => p.Y)], [.. points.Select(p => p.Index)],
                [.. points.Select(p => r.Data.ToSourceIndex(p.Index))]);
            hitTest.Add(hits);
            bool IsTotal(int i) => r.Data.Totals is { } flags && flags[i];
            tooltipSeries.Add(new TooltipSeries(r.Spec.Id, r.Spec.DisplayName, r.Color, SeriesType.Bar, 0, 0, 0, hits.X, hits.Y, hits.Index,
                [.. points.Select(p => rows[p.Row])],
                [.. points.Select(p => r.Spec.Type switch
                {
                    SeriesType.Dumbbell => Series.DumbbellSeriesType.Text(r.Data.Low?[p.Index] ?? double.NaN, r.Data.Y[p.Index], culture, format),
                    // A change with its sign; a total bar shows the total.
                    SeriesType.Waterfall => IsTotal(p.Index)
                        ? ValueFormatter.Format(r.Upper[p.Index], culture, format)
                        : (r.Data.Y[p.Index] > 0 ? "+" : "") + ValueFormatter.Format(r.Data.Y[p.Index], culture, format),
                    _ => ValueFormatter.Format(r.Data.Y[p.Index], culture, format) + (HasErrors(r) ? ErrorText(r, p.Index, culture, format) : "")
                        + (r.Percent && IsBar(r) ? $" ({p.Share.ToString("P0", culture)})" : ""),
                })])
            {
                CellHeight = (float)cellHeight,
                CellWidths = [.. points.Select(p => p.Width)],
                PartLabels = IsBox(r) && r.Data.Box is not null ? BoxPartLabels(texts) : IsWaterfall(r) ? [texts.Total] : null,
                PartValues = IsBox(r) && r.Data.Box is { } box
                    ? [.. points.Select(p => BoxPartValues(box, p.Index, r.Data.Y[p.Index], culture, format))]
                    : IsWaterfall(r)
                    ? [.. points.Select(p => new[] { IsTotal(p.Index) ? "" : ValueFormatter.Format(r.Upper[p.Index], culture, format) })]
                    : null,
                PointColors = IsWaterfall(r)
                    ? [.. points.Select(p => Series.WaterfallSeriesType.ColorOf(r.Data, p.Index, r.Color))]
                    : r.Data.Colors is null && r.Spec.ColorBands is null
                    ? null
                    : [.. points.Select(p => r.Data.Colors?[p.Index] ?? Series.ColorBands.ColorOf(r.Spec.ColorBands, r.Data.Y[p.Index]) ?? r.Color)],
            });
        }

        // ---------- Value lines and bands ----------
        var below = new List<Node>();
        var above = new List<Node>();
        if (hasData)
        {
            foreach (var (a, first, second) in lines)
            {
                var label = compact ? null : a.Label;
                if (a.Type == AnnotationType.Band)
                {
                    var (bx0, bx1) = (valueScale.Map(Math.Min(first, second)), valueScale.Map(Math.Max(first, second)));
                    below.Add(new RectNode(new RectF((float)bx0, plot.Y, (float)Math.Max(0.5, bx1 - bx0), plot.Height), new StyleRef("annotation-band"))
                    {
                        Class = "rio-annotation rio-annotation-band",
                    });
                    if (label is not null)
                    {
                        above.Add(new TextNode(label, new PointF((float)bx0, (float)(plot.Y - lineLabels / 2)), new TextStyle("annotation-label"), Anchor.Start) { Class = "rio-annotation-label" });
                    }
                }
                else
                {
                    var x = valueScale.Map(first);
                    above.Add(new PathNode(new PathBuilder().MoveTo(x, plot.Y).LineTo(x, plot.Bottom).Build(), new StyleRef("annotation-line"))
                    {
                        Class = "rio-annotation rio-annotation-line",
                    });
                    if (label is not null)
                    {
                        above.Add(new TextNode(label, new PointF((float)x, (float)(plot.Y - lineLabels / 2)), new TextStyle("annotation-label"), Anchor.Middle) { Class = "rio-annotation-label" });
                    }
                }
            }
        }

        var overlay = new List<Node>();
        if (!hasData && !compact)
        {
            overlay.Add(new TextNode(EmptyMessage(bars, texts), new PointF(plot.CenterX, plot.CenterY), new TextStyle("empty"), Anchor.Middle) { Class = "rio-empty" });
        }

        var plotNodes = new List<Node>(below);
        plotNodes.AddRange(seriesNodes);
        plotNodes.AddRange(above);
        if (selectedMarks.Count > 0)
        {
            plotNodes.Add(new GroupNode(selectedMarks) { Key = "selection:points", Class = "rio-selected-points" });
        }
        var seriesLayer = new GroupNode(
        [
            new GroupNode(plotNodes) { Class = "rio-plot" },
            new GroupNode(labelNodes) { Class = "rio-labels" },
        ])
        {
            Class = "rio-series-layer",
        };

        var scene = new Scene.Scene(new GroupNode(statics) { Class = "rio-static" }, seriesLayer, new GroupNode(overlay) { Class = "rio-overlay" })
        {
            Width = (float)width,
            Height = (float)height,
            Styles = styles,
            Description = compact
                ? ChartDescriber.DescribeCompact(spec, bars.Select(r => (r.Spec, r.Data, r.Visible)).ToList(), xKind, culture, texts)
                : ChartDescriber.Describe(spec, bars.Select(r => (r.Spec, r.Data, r.Visible)).ToList(), xKind, culture, texts),
        };

        return new ChartLayoutResult
        {
            Scene = scene,
            Plot = plot,
            HitTest = hitTest,
            Tooltip = new TooltipModel(plot, spec.Tooltip.Enabled && hasData, false, 0, tooltipSeries),
            Legend = legendEntries,
            Warnings = warnings,
            XKind = xKind,
            YScale = valueScale,
            YScales = new Dictionary<string, ContinuousScale> { ["y"] = valueScale },
            HasData = hasData,
        };
    }

    /// <summary>The label, shortened with "…" to fit the width.</summary>
    private static string Shorten(string label, double maxWidth, ITextMeasurer measure, double font)
    {
        if (measure.Measure(label, font) <= maxWidth || label.Length <= 2)
        {
            return label;
        }

        var length = label.Length - 1;
        while (length > 1 && measure.Measure(label[..length].TrimEnd() + "…", font) > maxWidth)
        {
            length--;
        }

        return label[..length].TrimEnd() + "…";
    }
}
