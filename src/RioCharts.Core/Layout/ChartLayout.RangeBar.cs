using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;

namespace RioCharts.Core.Layout;

public static partial class ChartLayout
{
    /// <summary>
    /// Range bars (timelines): rows down the left side in order of first appearance, a time (or
    /// number) axis along the bottom, and a bar from each point's start (x) to its end (y) in its
    /// row. Series share the rows and are drawn on top of each other, so a state timeline is one
    /// series per state. Vertical grid lines at the ticks; x lines and bands from the annotations
    /// (e.g. "now" or a maintenance window) are drawn across the rows.
    /// </summary>
    private static ChartLayoutResult ComputeRangeBars(ChartDefinition spec, List<Resolved> resolved, LayoutOptions options, List<string> warnings, StyleTable styles,
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

        foreach (var r in resolved.Where(r => r.Spec.Type != SeriesType.RangeBar))
        {
            warnings.Add($"series '{r.Spec.Id}': {r.Spec.Type} series cannot be mixed with range bar series; the series is skipped.");
        }

        var series = resolved.Where(r => r.Spec.Type == SeriesType.RangeBar).ToList();
        var xKind = spec.X.Scale switch
        {
            ScaleType.Time => XKind.Time,
            ScaleType.Linear => XKind.Numeric,
            _ => series.FirstOrDefault(r => r.Data.Count > 0)?.Data.Kind ?? XKind.Time,
        };
        if (xKind == XKind.Category)
        {
            warnings.Add("range bars need a time or number axis; the bars are skipped.");
        }

        foreach (var r in series.Where(r => r.Data.Count > 0 && r.Data.Kind != xKind && xKind != XKind.Category))
        {
            warnings.Add($"series '{r.Spec.Id}': starts and ends are {r.Data.Kind} but the axis is {xKind}; the series is skipped.");
        }

        bool Usable(Resolved r) => r.Data.Count > 0 && r.Data.Kind == xKind && xKind != XKind.Category;
        string RowOf(Resolved r, int i) => r.Data.Rows is { } rows && !string.IsNullOrEmpty(rows[i]) ? rows[i] : r.Spec.DisplayName;

        // ---------- Rows: in order of first appearance in the input (also hidden series, so rows stay put) ----------
        var rowNames = new List<string>();
        var rowIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in series.Where(Usable))
        {
            foreach (var i in Enumerable.Range(0, r.Data.Count).OrderBy(r.Data.ToSourceIndex))
            {
                var name = RowOf(r, i);
                if (rowIndex.TryAdd(name, rowNames.Count))
                {
                    rowNames.Add(name);
                }
            }
        }

        // ---------- Bars and the time range ----------
        var drawn = series.Where(r => r.Visible && Usable(r)).ToList();
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        var bars = new Dictionary<Resolved, List<int>>();
        var reversed = 0;
        foreach (var r in drawn)
        {
            var list = new List<int>();
            for (var i = 0; i < r.Data.Count; i++)
            {
                var (start, end) = (r.Data.X[i], r.Data.Y[i]);
                if (!double.IsFinite(start) || !double.IsFinite(end))
                {
                    continue;
                }

                if (end < start)
                {
                    reversed++;
                    continue;
                }

                list.Add(i);
                min = Math.Min(min, start);
                max = Math.Max(max, end);
            }

            bars[r] = list;
        }

        if (reversed > 0)
        {
            warnings.Add($"{reversed} range bar(s) end before they start; they are not drawn.");
        }

        var hasData = bars.Values.Any(b => b.Count > 0);

        // ---------- Annotations: x lines and bands ----------
        var lines = new List<(Annotation Spec, double A, double B)>();
        double ToValue(object? v) => time is not null && xKind == XKind.Time ? time.ToInstant(v) : AxisValue.ToDouble(v, xKind, []);
        foreach (var (a, i) in spec.Annotations.Select((a, i) => (a, i)))
        {
            if (a.Axis != AnnotationAxis.X || a.Type is not (AnnotationType.Line or AnnotationType.Band))
            {
                warnings.Add($"annotations[{i}]: range bar charts show x lines and bands only (e.g. \"now\" or a maintenance window); the annotation is skipped.");
                continue;
            }

            var first = ToValue(a.Type == AnnotationType.Band ? a.From : a.Value);
            var second = a.Type == AnnotationType.Band ? ToValue(a.To) : double.NaN;
            if (!double.IsFinite(first) || (a.Type == AnnotationType.Band && !double.IsFinite(second)))
            {
                warnings.Add($"annotations[{i}]: needs a {(xKind == XKind.Time ? "time" : "number")}; the annotation is skipped.");
                continue;
            }

            lines.Add((a, first, second));
            if (hasData && a.Type == AnnotationType.Line && (a.ExpandsDomain ?? false))
            {
                min = Math.Min(min, first);
                max = Math.Max(max, first);
            }
        }

        if (!double.IsFinite(min))
        {
            (min, max) = xKind == XKind.Time ? (0d, 86_400_000d) : (0d, 1d);
        }

        if (max <= min)
        {
            var pad = xKind == XKind.Time ? 1_800_000 : Math.Max(0.5, Math.Abs(min) * 0.05);
            (min, max) = (min - pad, max + pad);
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

        var times = drawn.SelectMany(r => r.Data.X.Concat(r.Data.Y)).Where(double.IsFinite).Take(5000).ToList();
        var formatTime = TimeFormat(xKind, times, culture, spec.X.Format, time, options.ViewerTimeZone);
        string FormatAt(Resolved r, double v) => xKind == XKind.Time ? formatTime(v) : ValueFormatter.Format(v, culture, r.Spec.Format ?? spec.X.Format);

        // "09:30 – 10:00" when every bar is on one day, "2 Mar 22:00 – 06:00" for an end on its start's day; dates otherwise.
        double Wall(double v) => time?.ToDisplay(v) ?? v;
        var walls = times.Select(v => TimeValue.ToDateTime(Wall(v))).ToList();
        var oneDay = walls.Count > 0 && walls.All(t => t.Date == walls[0].Date || (t.TimeOfDay == TimeSpan.Zero && t.Date == walls[0].Date.AddDays(1)));
        var clockPattern = walls.Any(t => t.Second != 0 || t.Millisecond != 0) ? "T" : "t";
        var viewer = options.ViewerTimeZone is { } viewerZone ? TimeConversion.FindZone(viewerZone, null) : null;
        var showZone = time is not null && (viewer is null || !SameZone(viewer, time.Zone));
        string Span(Resolved r, double start, double end)
        {
            if (xKind != XKind.Time || !string.IsNullOrEmpty(spec.X.Format) || walls.All(t => t.TimeOfDay == TimeSpan.Zero))
            {
                return $"{FormatAt(r, start)} – {FormatAt(r, end)}";
            }

            var (from, to) = (TimeValue.ToDateTime(Wall(start)), TimeValue.ToDateTime(Wall(end)));
            var sameDay = to.Date == from.Date || (to.TimeOfDay == TimeSpan.Zero && to.Date == from.Date.AddDays(1));
            var first = oneDay ? from.ToString(clockPattern, culture) : TimeValue.ToDateTime(Wall(start)).ToString(clockPattern == "T" ? "G" : "g", culture);
            var second = oneDay || sameDay ? to.ToString(clockPattern, culture) : to.ToString(clockPattern == "T" ? "G" : "g", culture);
            return $"{first} – {second}" + (showZone ? " " + time!.OffsetLabel(start) : "");
        }
        var units = texts.DurationUnits.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string Length(Resolved r, double start, double end) => xKind == XKind.Time
            ? FormatDuration(end - start, units, culture)
            : ValueFormatter.Format(end - start, culture, r.Spec.Format ?? spec.X.Format);

        var legendEntries = series
            .Select(r => new LegendEntry(r.Spec.Id, r.Spec.DisplayName, r.Color, r.Spec.Type, r.Visible, r.Empty,
                DescribeRangeBars(r, Usable(r) ? Enumerable.Range(0, r.Data.Count).Where(i => r.Data.Y[i] >= r.Data.X[i]).ToList() : [], RowOf, FormatAt, texts, culture)))
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
        var rowLabels = rowNames.Select(l => Shorten(l, width * 0.35, measure, font)).ToList();
        var labelWidth = showRowLabels && rowLabels.Count > 0 ? rowLabels.Max(l => measure.Measure(l, font)) : 0;
        var xLabelHeight = compact || !chrome.XLabels ? 0 : lineHeight + 6;
        var xTitleHeight = compact || !chrome.XTitle || string.IsNullOrWhiteSpace(spec.X.Title) ? 0 : lineHeight + 4;
        var plotLeft = compact ? 1 : PadLeft + (showRowLabels ? labelWidth + LabelGap : 0);
        var plotRight = compact ? width - 1 : width - PadRight - 8; // room for the last tick label's half
        var lineLabels = !compact && lines.Any(l => !string.IsNullOrWhiteSpace(l.Spec.Label)) ? lineHeight + 2 : 0;
        var plotTop = compact ? 1 : y0 + lineLabels;
        var plotBottom = compact ? height - 1 : height - PadBottom - xTitleHeight - xLabelHeight;
        var plot = new RectF((float)plotLeft, (float)plotTop, (float)Math.Max(2, plotRight - plotLeft), (float)Math.Max(2, plotBottom - plotTop));

        if (xKind == XKind.Numeric)
        {
            (min, max, _) = NiceTicks.NiceDomain(min, max, Math.Clamp((int)(plot.Width / 90), 2, 10));
        }

        (double Min, double Max) full = (spec.X.Min ?? min, spec.X.Max ?? max);
        (double Min, double Max)? window = options.XRange is { } range && range.Max > range.Min && double.IsFinite(range.Min) && double.IsFinite(range.Max)
            ? range
            : null;
        var xScale = new ContinuousScale(window?.Min ?? full.Min, window?.Max ?? full.Max, plot.X, plot.Right);
        var band = new BandScale(rowNames, plot.Y, plot.Bottom, rowNames.Count > 1 ? 0.3 : 0.5, 0.15);
        var barHeight = Math.Max(1, Math.Min(band.Bandwidth, 40));

        // ---------- Grid, x labels, row labels ----------
        var xTicks = new List<Node>();
        var gridX = hasData && !compact ? AddContinuousXLabels(xScale, xKind == XKind.Category ? XKind.Numeric : xKind, spec.X, culture, measure, font, plot.Bottom, width, xTicks, time) : [];
        if (hasData && (spec.X.GridLines ?? !compact))
        {
            var grid = gridX.Where(px => px > plot.X + 0.5 && px <= plot.Right + 0.5)
                .Select(px => (Node)new PathNode(new PathBuilder().MoveTo(px, plot.Y).LineTo(px, plot.Bottom).Build(), new StyleRef("grid")))
                .ToList();
            statics.Add(new GroupNode(grid) { Class = "rio-grid" });
        }

        if (hasData && !compact)
        {
            statics.Add(new PathNode(new PathBuilder().MoveTo(plot.X, plot.Bottom).LineTo(plot.Right, plot.Bottom).Build(), new StyleRef("axis")) { Class = "rio-baseline" });
        }

        if (hasData && xLabelHeight > 0)
        {
            statics.Add(new GroupNode(xTicks) { Class = "rio-axis-x", AriaLabel = texts.XAxis + (spec.X.Title is { } xt ? ": " + xt : "") });
        }

        if (xTitleHeight > 0)
        {
            statics.Add(new TextNode(spec.X.Title!, new PointF(plot.CenterX, (float)(plot.Bottom + xLabelHeight + xTitleHeight / 2)), new TextStyle("axis-title"),
                Anchor.Middle) { Class = "rio-axis-title" });
        }

        if (showRowLabels && hasData)
        {
            var labels = rowLabels.Select((l, i) => (Node)new TextNode(l, new PointF((float)(plot.X - LabelGap), (float)band.Center(i)),
                new TextStyle("axis-label"), Anchor.End) { Class = "rio-row-label" }).ToList();
            statics.Add(new GroupNode(labels) { Class = "rio-axis-y", AriaLabel = texts.YAxis + (spec.Y.Title is { } yt ? ": " + yt : "") });
        }

        if (showLegend)
        {
            statics.Add(BuildLegend(legendItems, legendRows, LegendPosition.Top, 0, plot, y0 - legendRows.Count * (lineHeight + 4) - 8, 0, lineHeight, styles,
                texts.Legend));
        }

        // ---------- Bars ----------
        var seriesNodes = new List<Node>();
        var labelNodes = new List<Node>();
        var hitTest = new HitTestIndex();
        var tooltipSeries = new List<TooltipSeries>();
        foreach (var r in drawn)
        {
            var style = styles.Add($"series-{r.ColorIndex}-bar", new Style { Fill = r.Color });
            var nodes = new List<Node>();
            var points = new List<(float X, float Y, float Width, int Index, int Row)>();
            foreach (var i in bars[r])
            {
                var (start, end) = (r.Data.X[i], r.Data.Y[i]);
                if (end < xScale.DomainMin || start > xScale.DomainMax)
                {
                    continue; // outside a fixed x range
                }

                var row = rowIndex[RowOf(r, i)];
                var cy = band.Center(row);
                var x0 = Math.Max(plot.X, xScale.Map(start));
                var x1 = Math.Min(plot.Right, xScale.Map(end));
                var w = Math.Max(1, x1 - x0);
                var top = cy - barHeight / 2;
                var own = r.Data.Colors?[i] is { } color ? styles.Add($"bar-{color}", new Style { Fill = color }) : style;
                nodes.Add(new RectNode(new RectF((float)x0, (float)top, (float)w, (float)barHeight), own)
                {
                    CornerRadius = (float)Math.Min(2, Math.Min(barHeight / 4, w / 2)),
                });
                points.Add(((float)(x0 + w / 2), (float)cy, (float)Math.Max(6, w), i, row));

                if (r.Spec.DataLabels == true && !compact)
                {
                    var text = Length(r, start, end);
                    if (measure.Measure(text, font - 1) + 8 <= w && barHeight >= font)
                    {
                        labelNodes.Add(new TextNode(text, new PointF((float)(x0 + 4), (float)cy), new TextStyle("data-label-inverse"), Anchor.Start)
                        {
                            Class = "rio-data-label",
                        });
                    }
                }
            }

            seriesNodes.Add(new GroupNode(nodes)
            {
                Key = "series:" + r.Spec.Id,
                Class = "rio-series rio-series-range-bar",
                AriaLabel = texts.FillCount(nameof(ChartTexts.SeriesPoints), points.Count, culture, r.Spec.DisplayName, points.Count),
            });

            // Top to bottom, then left to right, for the keyboard.
            points.Sort((a, b) => a.Row != b.Row ? a.Row.CompareTo(b.Row) : a.X.CompareTo(b.X));
            var hits = new HitTestIndex.SeriesHits(r.Spec.Id, [.. points.Select(p => p.X)], [.. points.Select(p => p.Y)], [.. points.Select(p => p.Index)],
                [.. points.Select(p => r.Data.ToSourceIndex(p.Index))]);
            hitTest.Add(hits);
            tooltipSeries.Add(new TooltipSeries(r.Spec.Id, r.Spec.DisplayName, r.Color, SeriesType.RangeBar, 0, 0, 0, hits.X, hits.Y, hits.Index,
                [.. points.Select(p => rowNames[p.Row])],
                [.. points.Select(p => $"{Span(r, r.Data.X[p.Index], r.Data.Y[p.Index])} ({Length(r, r.Data.X[p.Index], r.Data.Y[p.Index])})")])
            {
                CellHeight = (float)barHeight,
                CellWidths = [.. points.Select(p => p.Width)],
                PointColors = r.Data.Colors is null ? null : [.. points.Select(p => r.Data.Colors[p.Index] ?? r.Color)],
            });
        }

        // ---------- Lines and bands ----------
        var below = new List<Node>();
        var above = new List<Node>();
        if (hasData)
        {
            foreach (var (a, first, second) in lines)
            {
                var label = compact ? null : a.Label;
                if (a.Type == AnnotationType.Band)
                {
                    var bx0 = Math.Clamp(xScale.Map(Math.Min(first, second)), plot.X, plot.Right);
                    var bx1 = Math.Clamp(xScale.Map(Math.Max(first, second)), plot.X, plot.Right);
                    if (bx1 - bx0 < 0.5)
                    {
                        continue;
                    }

                    below.Add(new RectNode(new RectF((float)bx0, plot.Y, (float)(bx1 - bx0), plot.Height), new StyleRef("annotation-band"))
                    {
                        Class = "rio-annotation rio-annotation-band",
                    });
                    if (label is not null)
                    {
                        above.Add(new TextNode(label, new PointF((float)bx0, (float)(plot.Y - lineLabels / 2)), new TextStyle("annotation-label"), Anchor.Start)
                        {
                            Class = "rio-annotation-label",
                        });
                    }
                }
                else
                {
                    var x = xScale.Map(first);
                    if (x < plot.X - 0.5 || x > plot.Right + 0.5)
                    {
                        continue;
                    }

                    above.Add(new PathNode(new PathBuilder().MoveTo(x, plot.Y).LineTo(x, plot.Bottom).Build(), new StyleRef("annotation-line"))
                    {
                        Class = "rio-annotation rio-annotation-line",
                    });
                    if (label is not null)
                    {
                        above.Add(new TextNode(label, new PointF((float)x, (float)(plot.Y - lineLabels / 2)), new TextStyle("annotation-label"), Anchor.Middle)
                        {
                            Class = "rio-annotation-label",
                        });
                    }
                }
            }
        }

        var overlay = new List<Node>();
        if (!hasData && !compact)
        {
            overlay.Add(new TextNode(EmptyMessage(series, texts), new PointF(plot.CenterX, plot.CenterY), new TextStyle("empty"), Anchor.Middle) { Class = "rio-empty" });
        }

        var plotNodes = new List<Node>(below);
        plotNodes.AddRange(seriesNodes);
        plotNodes.AddRange(above);
        var seriesLayer = new GroupNode(
        [
            new GroupNode(plotNodes) { Class = "rio-plot" },
            new GroupNode(labelNodes) { Class = "rio-labels" },
        ])
        {
            Class = "rio-series-layer",
        };

        var summary = string.IsNullOrWhiteSpace(spec.Title)
            ? texts.FillCount(nameof(ChartTexts.Summary), series.Count, culture, series.Count)
            : texts.FillCount(nameof(ChartTexts.SummaryTitled), series.Count, culture, series.Count, spec.Title);
        var described = legendEntries.Where(e => series.First(r => r.Spec.Id == e.SeriesId).Visible).Select(e => e.Tooltip).OfType<string>().ToList();
        var scene = new Scene.Scene(new GroupNode(statics) { Class = "rio-static" }, seriesLayer, new GroupNode(overlay) { Class = "rio-overlay" })
        {
            Width = (float)width,
            Height = (float)height,
            Styles = styles,
            Description = hasData ? $"{summary} {string.Join(" ", described.Select(d => d.EndsWith('.') ? d : d + "."))}" : $"{summary} {texts.NoData}.",
        };

        var yScale = new ContinuousScale(rowNames.Count, 0, plot.Bottom, plot.Y);
        return new ChartLayoutResult
        {
            Scene = scene,
            Plot = plot,
            HitTest = hitTest,
            Tooltip = new TooltipModel(plot, spec.Tooltip.Enabled && hasData, false, 0, tooltipSeries),
            Legend = legendEntries,
            Warnings = warnings,
            XKind = xKind,
            XScale = xScale,
            XExtent = xKind == XKind.Category ? null : full,
            Zoomed = window is not null,
            Time = time,
            YScale = yScale,
            YScales = new Dictionary<string, ContinuousScale> { ["y"] = yScale },
            HasData = hasData,
        };
    }

    /// <summary>"Running: 12 bars in 3 rows, from Mon 06:00 to Fri 18:00", or "Running: no data".</summary>
    private static string DescribeRangeBars(Resolved r, List<int> bars, Func<Resolved, int, string> rowOf, Func<Resolved, double, string> format, ChartTexts texts,
        CultureInfo culture)
    {
        var kept = bars.Where(i => double.IsFinite(r.Data.X[i]) && double.IsFinite(r.Data.Y[i])).ToList();
        if (kept.Count == 0)
        {
            return $"{r.Spec.DisplayName}: {texts.NoData}";
        }

        var rows = kept.Select(i => rowOf(r, i)).Distinct(StringComparer.Ordinal).Count();
        return texts.FillCount(nameof(ChartTexts.RangeBarSummary), kept.Count, culture, r.Spec.DisplayName, kept.Count,
            format(r, kept.Min(i => r.Data.X[i])), format(r, kept.Max(i => r.Data.Y[i])), rows);
    }

    /// <summary>"45 s", "12 min", "2 h 30 min", "3 d 4 h" with the units of the chart's language (days, hours, minutes, seconds).</summary>
    internal static string FormatDuration(double milliseconds, IReadOnlyList<string> units, CultureInfo culture)
    {
        string Unit(int i) => i < units.Count && !string.IsNullOrWhiteSpace(units[i]) ? units[i] : new[] { "d", "h", "min", "s" }[i];
        string N(long v) => v.ToString(culture);
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
        {
            return "";
        }

        if (milliseconds < 59_500)
        {
            var seconds = milliseconds / 1000;
            return seconds < 9.95 && seconds != Math.Round(seconds)
                ? $"{seconds.ToString("0.#", culture)} {Unit(3)}"
                : $"{N((long)Math.Round(seconds))} {Unit(3)}";
        }

        var minutes = (long)Math.Round(milliseconds / 60_000);
        if (minutes < 60)
        {
            return $"{N(minutes)} {Unit(2)}";
        }

        if (minutes < 24 * 60)
        {
            var (h, m) = (minutes / 60, minutes % 60);
            return m == 0 ? $"{N(h)} {Unit(1)}" : $"{N(h)} {Unit(1)} {N(m)} {Unit(2)}";
        }

        var hours = (long)Math.Round(milliseconds / 3_600_000);
        var (d, rest) = (hours / 24, hours % 24);
        return rest == 0 ? $"{N(d)} {Unit(0)}" : $"{N(d)} {Unit(0)} {N(rest)} {Unit(1)}";
    }
}
