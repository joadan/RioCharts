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
    /// Horizontal bars (<see cref="ChartSpec.Horizontal"/>): categories down the left side, first on
    /// top, values along the bottom (<see cref="ChartSpec.Y"/> formats them). Several bar series are
    /// grouped side by side in each row, or stacked with <see cref="SeriesSpec.Stack"/>. Value lines and
    /// bands from the annotations are drawn across the rows.
    /// </summary>
    private static ChartLayoutResult ComputeHorizontal(ChartSpec spec, List<Resolved> resolved, LayoutOptions options, List<string> warnings, StyleTable styles,
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

        foreach (var r in resolved.Where(r => r.Spec.Type != SeriesType.Bar))
        {
            warnings.Add($"series '{r.Spec.Id}': {r.Spec.Type} series cannot be drawn in a horizontal bar chart; the series is skipped.");
        }

        var bars = resolved.Where(r => r.Spec.Type == SeriesType.Bar).ToList();
        var xKind = ResolveXKind(spec.X.Scale, bars);

        // ---------- Rows: categories, or the distinct x values as labels ----------
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
        var slotKeys = drawn.Select(r => r.Spec.Stack is { Length: > 0 } s ? "stack|" + s : "series|" + r.Spec.Id).Distinct().ToList();
        var segments = new Dictionary<Resolved, (double Start, double End, int Index)[]>();
        var stacks = new Dictionary<(string Key, int Row, bool Positive), double>();
        double vMin = 0, vMax = 0;
        foreach (var r in drawn)
        {
            var key = r.Spec.Stack is { Length: > 0 } s ? "stack|" + s : "series|" + r.Spec.Id;
            var stacked = r.Spec.Stack is { Length: > 0 };
            var list = new List<(double, double, int)>();
            for (var i = 0; i < r.Data.Count; i++)
            {
                var v = r.Data.Y[i];
                var row = rowOf(r.Data, i);
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
                }

                list.Add((start, start + v, i));
                vMin = Math.Min(vMin, Math.Min(start, start + v));
                vMax = Math.Max(vMax, Math.Max(start, start + v));
            }

            segments[r] = [.. list];
        }

        var lines = new List<(AnnotationSpec Spec, double A, double B)>();
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

        var (niceMin, niceMax, step) = NiceTicks.NiceDomain(vMin, vMax, Math.Clamp((int)(plot.Width / 90), 2, 10));
        var valueScale = new LinearScale(spec.Y.Min ?? niceMin, spec.Y.Max ?? niceMax, plot.X, plot.Right);
        var ticks = NiceTicks.Ticks(valueScale.DomainMin, valueScale.DomainMax, step);
        var tickFormat = NumberFormatter.ForTicks(ticks, step, culture, spec.Y.Format);
        var band = new BandScale(rows, plot.Y, plot.Bottom, 0.25, 0.1);
        var slotCount = Math.Max(1, slotKeys.Count);
        var thickness = band.Bandwidth / slotCount;
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
        var labelNodes = new List<Node>();
        var hitTest = new HitTestIndex();
        var tooltipSeries = new List<TooltipSeries>();
        foreach (var r in drawn)
        {
            var key = r.Spec.Stack is { Length: > 0 } s ? "stack|" + s : "series|" + r.Spec.Id;
            var slot = slotKeys.IndexOf(key);
            var stacked = r.Spec.Stack is { Length: > 0 };
            var style = styles.Add($"series-{r.ColorIndex}-bar", new Style { Fill = r.Color });
            var format = r.Spec.Format ?? spec.Y.Format;
            var nodes = new List<Node>();
            var points = new List<(float X, float Y, float Width, int Index, int Row)>();
            foreach (var (start, end, i) in segments[r])
            {
                var row = rowOf(r.Data, i);
                var top = band.Start(row) + slot * thickness + gap / 2.0;
                var h = Math.Max(1, thickness - gap);
                var (x0, x1) = (valueScale.Map(Math.Min(start, end)), valueScale.Map(Math.Max(start, end)));
                nodes.Add(new RectNode(new RectF((float)x0, (float)top, (float)Math.Max(0.5, x1 - x0), (float)h), style)
                {
                    CornerRadius = stacked ? 0 : (float)Math.Min(3, h / 4),
                });
                points.Add(((float)((x0 + x1) / 2), (float)(top + h / 2), (float)Math.Max(6, x1 - x0), i, row));

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

            seriesNodes.Add(new GroupNode(nodes)
            {
                Key = "series:" + r.Spec.Id,
                Class = "rio-series rio-series-bar",
                AriaLabel = texts.FillCount(nameof(ChartTexts.SeriesPoints), points.Count, culture, r.Spec.DisplayName, points.Count),
            });

            points.Sort((a, b) => a.Row.CompareTo(b.Row)); // top to bottom, for the keyboard
            var hits = new HitTestIndex.SeriesHits(r.Spec.Id, [.. points.Select(p => p.X)], [.. points.Select(p => p.Y)], [.. points.Select(p => p.Index)],
                [.. points.Select(p => r.Data.ToSourceIndex(p.Index))]);
            hitTest.Add(hits);
            tooltipSeries.Add(new TooltipSeries(r.Spec.Id, r.Spec.DisplayName, r.Color, SeriesType.Bar, 0, 0, 0, hits.X, hits.Y, hits.Index,
                [.. points.Select(p => rows[p.Row])], [.. points.Select(p => ValueFormatter.Format(r.Data.Y[p.Index], culture, format))])
            {
                CellHeight = (float)Math.Max(1, thickness - gap),
                CellWidths = [.. points.Select(p => p.Width)],
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
            YScales = new Dictionary<string, LinearScale> { ["y"] = valueScale },
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
