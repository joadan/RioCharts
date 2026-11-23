using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;

namespace RioCharts.Core.Layout;

public static partial class ChartLayout
{
    /// <summary>
    /// Bullets: one row per data point of every bullet series, top to bottom, labeled on the left.
    /// Each series has its own scale (min to max, with tick labels under its last row), so measures
    /// in different units can share a chart. A row is a track over the scale, the color bands'
    /// ranges tinted on it, a bar from the base (0, or the scale's start) to the value in the series
    /// color or the band the value is in, and a target mark across it.
    /// </summary>
    private static ChartLayoutResult ComputeBullets(ChartDefinition spec, List<Resolved> resolved, LayoutOptions options, List<string> warnings, StyleTable styles)
    {
        var culture = options.Culture;
        var texts = options.Texts ?? ChartTexts.For(culture);
        var measure = options.Measurer;
        var compact = spec.Compact || AutoCompacts(spec, options);
        var width = Math.Max(compact ? 8 : 50, options.Width);
        var height = Math.Max(compact ? 8 : 50, options.Height);
        var font = options.FontSize;
        var small = Math.Max(9, font - 1);
        var lineHeight = font * 1.3;
        var chrome = FitChrome(spec.AutoCompact && !compact, width, height, font);

        foreach (var r in resolved.Where(r => r.Spec.Type != SeriesType.Bullet))
        {
            warnings.Add($"series '{r.Spec.Id}': {r.Spec.Type} series cannot be mixed with bullet series; the series is skipped.");
        }

        if (spec.Annotations.Count > 0)
        {
            warnings.Add("bullet charts show no annotations (use a series' target or color bands); the annotations are skipped.");
        }

        // ---------- Rows and a scale per series ----------
        var groups = new List<(Resolved Series, List<int> Points, double Min, double Max)>();
        foreach (var r in resolved.Where(r => r.Spec.Type == SeriesType.Bullet && r.Visible))
        {
            var points = Enumerable.Range(0, r.Data.Count).Where(i => double.IsFinite(r.Data.Y[i])).OrderBy(r.Data.ToSourceIndex).ToList();
            if (points.Count == 0)
            {
                continue;
            }

            var s = r.Spec;
            double TargetOf(int i) => r.Data.Targets?[i] is var t && double.IsFinite(t ?? double.NaN) ? t!.Value : s.Target ?? double.NaN;
            var values = points.Select(i => r.Data.Y[i]).Concat(points.Select(TargetOf).Where(double.IsFinite)).ToList();
            var lowest = values.Min();
            var highest = values.Max();
            var min = s.Min ?? (lowest < 0 ? NiceTicks.NiceDomain(lowest, Math.Max(highest, 0), 5).Min : 0);
            var max = s.Max ?? NiceTicks.NiceDomain(min, Math.Max(highest, min + 1e-9), 5).Max;
            if (!(max > min))
            {
                warnings.Add($"series '{s.Id}': max ({max.ToString(CultureInfo.InvariantCulture)}) must be above min ({min.ToString(CultureInfo.InvariantCulture)}); using min + 1.");
                max = min + 1;
            }

            groups.Add((r, points, min, max));
        }

        var rowCount = groups.Sum(g => g.Points.Count);
        var hasData = rowCount > 0;
        if (rowCount > 40)
        {
            warnings.Add($"{rowCount} bullets are too many to show; the first 40 are drawn.");
            var kept = 0;
            groups = groups.Select(g =>
            {
                var take = Math.Min(g.Points.Count, 40 - kept);
                kept += take;
                return (g.Series, g.Points.Take(take).ToList(), g.Min, g.Max);
            }).Where(g => g.Item2.Count > 0).ToList();
            rowCount = groups.Sum(g => g.Points.Count);
        }

        string LabelOf(Resolved r, int i)
        {
            var own = r.Data.Kind == XKind.Category
                ? ((int)r.Data.X[i] is var c && c >= 0 && c < r.Data.Categories.Count ? r.Data.Categories[c] : "")
                : ValueFormatter.ForX(r.Data.Kind, r.Data.X, culture)(r.Data.X[i]);
            return own.Length > 0 ? own : r.Spec.Name ?? "";
        }

        // ---------- Title ----------
        var statics = new List<Node>();
        var y0 = PadTop;
        if (!compact && chrome.Title && !string.IsNullOrWhiteSpace(spec.Title))
        {
            var titleHeight = options.TitleFontSize * 1.3;
            statics.Add(new TextNode(spec.Title!, new PointF((float)PadLeft, (float)(y0 + titleHeight / 2)), new TextStyle("title"), Anchor.Start) { Class = "rio-title" });
            y0 += titleHeight + 6;
        }

        // ---------- Plot: labels on the left, a scale under each series ----------
        var labels = groups.SelectMany(g => g.Points.Select(i => LabelOf(g.Series, i))).ToList();
        var showLabels = !compact && chrome.YLabels && labels.Any(l => l.Length > 0);
        var shown = labels.Select(l => Shorten(l, width * 0.35, measure, font)).ToList();
        var labelWidth = showLabels ? shown.Max(l => measure.Measure(l, font)) : 0;
        var showTicks = !compact && chrome.XLabels;
        var axisHeight = showTicks ? small * 1.3 + 4 : 0;
        var plotLeft = compact ? 1 : PadLeft + (showLabels ? labelWidth + LabelGap : 0);
        var plotRight = compact ? width - 1 : width - PadRight - 8; // room for the last tick label's half
        var plotTop = compact ? 1 : y0;
        var plotBottom = compact ? height - 1 : height - PadBottom;
        var plot = new RectF((float)plotLeft, (float)plotTop, (float)Math.Max(2, plotRight - plotLeft), (float)Math.Max(2, plotBottom - plotTop));

        var rowHeight = rowCount == 0 ? 0 : Math.Clamp((plot.Height - groups.Count * axisHeight) / rowCount, compact ? 2 : 8, compact ? 1e4 : 48);
        var rangeHeight = Math.Max(2, Math.Min(rowHeight * (compact ? 0.9 : 0.7), 30));
        var barHeight = Math.Max(2, rangeHeight / 3);
        var block = rowCount * rowHeight + groups.Count * axisHeight;
        var top = plot.Y + Math.Max(0, (plot.Height - block) / 2);
        var tickCount = Math.Clamp((int)(plot.Width / 70), 2, 8);

        styles.Add("bullet-range", new Style { Fill = ThemeTokens.Grid });
        styles.Add("bullet-target", new Style { Stroke = ThemeTokens.Text, StrokeWidth = 2.5f, Fill = "none" });
        styles.Add("bullet-tick", new TextStyleDef { FontSize = (float)small, Fill = ThemeTokens.TextMuted });

        var seriesNodes = new List<Node>();
        var labelNodes = new List<Node>();
        var rowLabelNodes = new List<Node>();
        var axisNodes = new List<Node>();
        var hitTest = new HitTestIndex();
        var tooltipSeries = new List<TooltipSeries>();
        var described = new List<string>();
        ContinuousScale? firstScale = null;
        var row = 0;
        var y = top;
        foreach (var (r, points, min, max) in groups)
        {
            var s = r.Spec;
            var scale = new ContinuousScale(min, max, plot.X, plot.Right);
            firstScale ??= scale;
            var bands = s.ColorBands is { Count: > 0 } colorBands ? Series.ColorBands.Ranges(colorBands, "") : null;
            var baseValue = min > 0 ? min : max < 0 ? max : 0;

            // Rows are labeled, not told apart by color: every series takes the first palette color unless it sets its own.
            var seriesColor = s.Color is null ? ThemeTokens.Series(0) : r.Color;
            string Format(double v) => ValueFormatter.Format(v, culture, s.Format, compactFrom: 1e5);
            double TargetOf(int i) => r.Data.Targets?[i] is var t && double.IsFinite(t ?? double.NaN) ? t!.Value : s.Target ?? double.NaN;

            var nodes = new List<Node>();
            var hits = new List<(float X, float Y, int Index)>();
            var tipLabels = new List<string>();
            var tipValues = new List<string>();
            var colors = new List<string>();
            foreach (var i in points)
            {
                var cy = y + rowHeight / 2;
                var v = r.Data.Y[i];
                var rangeTop = cy - rangeHeight / 2;
                var rowNodes = new List<Node>
                {
                    new RectNode(new RectF(plot.X, (float)rangeTop, plot.Width, (float)rangeHeight), new StyleRef("bullet-range")) { Class = "rio-bullet-range" },
                };
                if (bands is not null)
                {
                    foreach (var (from, to, bandColor) in bands)
                    {
                        var (lo, hi) = (Math.Max(from, min), Math.Min(to, max));
                        if (bandColor.Length == 0 || !(hi > lo))
                        {
                            continue;
                        }

                        var bandStyle = styles.Add("bullet-band-" + bandColor, new Style { Fill = bandColor, FillOpacity = 0.3f });
                        rowNodes.Add(new RectNode(new RectF((float)scale.Map(lo), (float)rangeTop, (float)(scale.Map(hi) - scale.Map(lo)), (float)rangeHeight), bandStyle)
                        {
                            Class = "rio-bullet-band",
                        });
                    }
                }

                var color = r.Data.Colors?[i] ?? Series.ColorBands.ColorOf(s.ColorBands, v) ?? seriesColor;
                var x0 = scale.Map(baseValue);
                var x1 = Math.Clamp(scale.Map(v), plot.X, plot.Right);
                if (Math.Abs(x1 - x0) >= 0.5)
                {
                    var barStyle = styles.Add("bullet-bar-" + color, new Style { Fill = color });
                    rowNodes.Add(new RectNode(new RectF((float)Math.Min(x0, x1), (float)(cy - barHeight / 2), (float)Math.Abs(x1 - x0), (float)barHeight), barStyle)
                    {
                        Class = "rio-bullet-bar",
                    });
                }

                var target = TargetOf(i);
                if (double.IsFinite(target) && target >= min && target <= max)
                {
                    var tx = scale.Map(target);
                    rowNodes.Add(new PathNode(new PathBuilder().MoveTo(tx, cy - rangeHeight * 0.4).LineTo(tx, cy + rangeHeight * 0.4).Build(), new StyleRef("bullet-target"))
                    {
                        Class = "rio-bullet-target",
                    });
                }

                nodes.Add(new GroupNode(rowNodes) { Key = $"bullet:{s.Id}:{i}" });

                var label = LabelOf(r, i);
                if (showLabels && label.Length > 0)
                {
                    rowLabelNodes.Add(new TextNode(shown[row], new PointF((float)(plot.X - LabelGap), (float)cy), new TextStyle("axis-label"), Anchor.End) { Class = "rio-row-label" });
                }

                var valueText = Format(v);
                if (s.DataLabels == true && !compact)
                {
                    var w = measure.Measure(valueText, small);
                    var after = Math.Max(x0, x1) + 4;
                    if (after + w <= plot.Right - 2)
                    {
                        labelNodes.Add(new TextNode(valueText, new PointF((float)after, (float)cy), new TextStyle("data-label"), Anchor.Start) { Class = "rio-data-label" });
                    }
                }

                var targetText = double.IsFinite(target) ? Format(target) : null;
                hits.Add(((float)plot.CenterX, (float)cy, i));
                tipLabels.Add(label.Length > 0 ? label : s.DisplayName);
                tipValues.Add(targetText is null ? valueText : $"{valueText} ({texts.Target} {targetText})");
                colors.Add(color);
                described.Add($"{(label.Length > 0 ? label + " " : "")}{valueText} ({Format(min)}–{Format(max)}{(targetText is null ? "" : $", {texts.Target} {targetText}")})");

                y += rowHeight;
                row++;
            }

            // The scale's ticks under the series' last row.
            if (showTicks)
            {
                var step = NiceTicks.NiceDomain(min, max, tickCount).Step;
                var ticks = new List<double>();
                for (var t = Math.Ceiling(min / step - 1e-9) * step; t <= max + step * 1e-9 && ticks.Count < 50; t += step)
                {
                    ticks.Add(Math.Abs(t) < step * 1e-9 ? 0 : t);
                }

                var widest = ticks.Max(t => measure.Measure(Format(t), small));
                var every = Math.Max(1, (int)Math.Ceiling((widest + 8) / Math.Max(1, plot.Width / Math.Max(1, ticks.Count - 1))));
                var tickY = y + axisHeight / 2;
                for (var k = 0; k < ticks.Count; k += every)
                {
                    axisNodes.Add(new TextNode(Format(ticks[k]), new PointF((float)scale.Map(ticks[k]), (float)tickY), new TextStyle("bullet-tick"), Anchor.Middle)
                    {
                        Class = "rio-tick-label",
                    });
                }

                y += axisHeight;
            }

            seriesNodes.Add(new GroupNode(nodes)
            {
                Key = "series:" + s.Id,
                Class = "rio-series rio-series-bullet",
                AriaLabel = s.DisplayName + ": " + string.Join(", ", points.Select(i => $"{LabelOf(r, i)} {Format(r.Data.Y[i])}".Trim())),
            });

            var hitList = new HitTestIndex.SeriesHits(s.Id, [.. hits.Select(h => h.X)], [.. hits.Select(h => h.Y)], [.. hits.Select(h => h.Index)],
                [.. hits.Select(h => r.Data.ToSourceIndex(h.Index))]);
            hitTest.Add(hitList);
            tooltipSeries.Add(new TooltipSeries(s.Id, s.DisplayName, seriesColor, SeriesType.Bullet, 0, 0, 0, hitList.X, hitList.Y, hitList.Index, [.. tipLabels], [.. tipValues])
            {
                CellHeight = (float)rangeHeight,
                CellWidths = [.. hits.Select(_ => plot.Width)],
                PointColors = colors.Any(c => c != seriesColor) ? [.. colors] : null,
            });
        }

        if (rowLabelNodes.Count > 0)
        {
            statics.Add(new GroupNode(rowLabelNodes) { Class = "rio-axis-y", AriaLabel = texts.YAxis });
        }

        if (axisNodes.Count > 0)
        {
            statics.Add(new GroupNode(axisNodes) { Class = "rio-axis-x", AriaLabel = texts.XAxis });
        }

        var overlay = new List<Node>();
        if (!hasData && !compact)
        {
            overlay.Add(new TextNode(EmptyMessage(resolved.Where(r => r.Spec.Type == SeriesType.Bullet).ToList(), texts), new PointF(plot.CenterX, plot.CenterY),
                new TextStyle("empty"), Anchor.Middle) { Class = "rio-empty" });
        }

        var seriesLayer = new GroupNode(
        [
            new GroupNode(seriesNodes) { Class = "rio-plot" },
            new GroupNode(labelNodes) { Class = "rio-labels" },
        ])
        {
            Class = "rio-series-layer",
        };

        var items = string.Join(", ", described.Take(8)) + (described.Count > 8 ? " " + texts.Fill(nameof(ChartTexts.More), culture, described.Count - 8) : "");
        var description = (string.IsNullOrWhiteSpace(spec.Title) ? "" : spec.Title + ". ")
            + (described.Count == 0
                ? texts.Fill(nameof(ChartTexts.PolarNoData), culture, texts.BulletChart)
                : texts.FillCount(nameof(ChartTexts.PolarItems), described.Count, culture, texts.BulletChart, described.Count, items) + ".");
        var scene = new Scene.Scene(new GroupNode(statics) { Class = "rio-static" }, seriesLayer, new GroupNode(overlay) { Class = "rio-overlay" })
        {
            Width = (float)width,
            Height = (float)height,
            Styles = styles,
            Description = description,
        };

        var xScale = firstScale ?? new ContinuousScale(0, 1, plot.X, plot.Right);
        var yScale = new ContinuousScale(Math.Max(1, rowCount), 0, plot.Bottom, plot.Y);
        return new ChartLayoutResult
        {
            Scene = scene,
            Plot = plot,
            HitTest = hitTest,
            Tooltip = new TooltipModel(plot, spec.Tooltip.Enabled && hasData, false, 0, tooltipSeries),
            Legend = [],
            Warnings = warnings,
            XKind = XKind.Category,
            XScale = xScale,
            YScale = yScale,
            YScales = new Dictionary<string, ContinuousScale> { ["y"] = yScale },
            HasData = hasData,
        };
    }
}
