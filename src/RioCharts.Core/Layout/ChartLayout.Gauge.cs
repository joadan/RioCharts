using System.Globalization;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;
using RioCharts.Core.Text;

namespace RioCharts.Core.Layout;

public static partial class ChartLayout
{
    private sealed record Gauge(Resolved Series, int Index, string Label, double Value, double Min, double Max);

    /// <summary>
    /// Gauges: one per data point of every gauge series, in a grid that gives them the largest
    /// radius. Each is a track from min to max (on the series' angles), filled up to its value in
    /// the series color or the color band the value is in; color bands also show as a thin scale
    /// ring outside the track. The value sits in the middle, min and max at the ends of the track,
    /// the label under the value (or under the gauge, on a half circle).
    /// </summary>
    private static bool LayoutGauges(List<Resolved> series, Dictionary<Resolved, string[]> labels, RectF plot, bool compact, CultureInfo culture, ChartTexts texts,
        ITextMeasurer measure, double font, StyleTable styles, List<string> warnings, List<Node> seriesNodes, List<Node> labelNodes,
        List<TooltipSlice> tooltip, List<string> described)
    {
        var gauges = new List<Gauge>();
        foreach (var r in series)
        {
            var values = Enumerable.Range(0, r.Data.Count).Where(i => double.IsFinite(r.Data.Y[i])).ToList();
            if (values.Count == 0)
            {
                continue;
            }

            var spec = r.Spec;
            var lowest = values.Min(i => r.Data.Y[i]);
            var highest = values.Max(i => r.Data.Y[i]);
            var min = spec.Min ?? (lowest < 0 ? NiceTicks.NiceDomain(lowest, Math.Max(highest, 0), 5).Min : 0);
            var max = spec.Max ?? NiceTicks.NiceDomain(min, Math.Max(Math.Max(highest, spec.Target ?? highest), min + 1e-9), 5).Max;
            if (!(max > min))
            {
                warnings.Add($"series '{spec.Id}': max ({max.ToString(CultureInfo.InvariantCulture)}) must be above min ({min.ToString(CultureInfo.InvariantCulture)}); using min + 1.");
                max = min + 1;
            }

            foreach (var i in values)
            {
                // A gauge's label: its category (or x), else the series name.
                var label = labels[r][i] is { Length: > 0 } own ? own : spec.Name ?? "";
                gauges.Add(new Gauge(r, i, label, r.Data.Y[i], min, max));
            }
        }

        if (gauges.Count == 0)
        {
            return false;
        }

        if (gauges.Count > 24)
        {
            warnings.Add($"{gauges.Count} gauges are too many to show; the first 24 are drawn.");
            gauges = gauges.Take(24).ToList();
        }

        var first = gauges[0].Series.Spec;
        var startDegrees = first.StartAngle ?? -120;
        var start = Radians(startDegrees);
        var sweep = Math.Clamp(Radians((first.EndAngle ?? 120) - startDegrees), -Math.PI * 2, Math.PI * 2);
        if (Math.Abs(sweep) < 0.1)
        {
            warnings.Add("the gauge's start and end angles are too close; using -120 to 120.");
            (start, sweep) = (Radians(-120), Radians(240));
        }

        var full = Math.Abs(sweep) >= Math.PI * 2 - 1e-6;
        var box = ArcBounds(start, sweep);
        var small = Math.Max(9, font - 1);
        var lineHeight = small * 1.3;

        // The ends' labels (min, max) sit under the ends when those point down or sideways; the
        // gauge's label sits under the value when the track leaves room below the center, else
        // under the gauge.
        var (_, endY0) = Polar(0, 0, 1, start);
        var (_, endY1) = Polar(0, 0, 1, start + sweep);
        var endLabels = !compact && !full && Math.Min(endY0, endY1) > -0.3;
        var anyLabel = gauges.Any(g => g.Label.Length > 0);
        var labelInside = box.MaxY >= 0.45;
        var below = (endLabels ? lineHeight + 2 : 0) + (!compact && anyLabel && !labelInside ? lineHeight + 4 : 0);
        var hasTarget = gauges.Any(g => g.Series.Spec.Target is not null);
        var pad = compact ? 1.0 : hasTarget ? 6.0 : 3.0; // a target marker reaches past the track

        // The ends' labels reach past the track: leave half the widest on each side.
        var endWidth = endLabels
            ? gauges.Max(g => Math.Max(measure.Measure(ValueFormatter.Format(g.Min, culture, g.Series.Spec.Format, compactFrom: 1e5), small),
                measure.Measure(ValueFormatter.Format(g.Max, culture, g.Series.Spec.Format, compactFrom: 1e5), small)))
            : 0;

        // The grid with the largest radius.
        var (bestCols, radius) = (1, 0.0);
        for (var cols = 1; cols <= gauges.Count; cols++)
        {
            var rows = (int)Math.Ceiling(gauges.Count / (double)cols);
            var cellW = plot.Width / cols;
            var cellH = plot.Height / rows;
            var r = Math.Min((cellW - 2 * pad - (compact ? 0 : 8) - endWidth) / (box.MaxX - box.MinX), (cellH - below - 2 * pad) / (box.MaxY - box.MinY));
            if (r > radius + 0.5)
            {
                (bestCols, radius) = (cols, r);
            }
        }

        radius = Math.Max(compact ? 2 : 10, radius);
        var columns = bestCols;
        var rowCount = (int)Math.Ceiling(gauges.Count / (double)columns);
        var cellWidth = plot.Width / columns;
        var cellHeight = plot.Height / rowCount;

        styles.Add("gauge-track", new Style { Fill = ThemeTokens.Grid });
        styles.Add("gauge-target", new Style { Stroke = ThemeTokens.Text, StrokeWidth = 2.5f, Fill = "none" });
        styles.Add("gauge-end", new TextStyleDef { FontSize = (float)small, Fill = ThemeTokens.TextMuted });
        styles.Add("gauge-label", new TextStyleDef { FontSize = (float)small, Fill = ThemeTokens.TextMuted });

        var nodesPerSeries = new Dictionary<Resolved, List<Node>>();
        for (var k = 0; k < gauges.Count; k++)
        {
            var g = gauges[k];
            var spec = g.Series.Spec;
            var (row, col) = (k / columns, k % columns);

            // The last row's gauges are centered under the others.
            var inRow = row == rowCount - 1 ? gauges.Count - row * columns : columns;
            var cellX = plot.X + (plot.Width - inRow * cellWidth) / 2 + col * cellWidth;
            var cellY = plot.Y + row * cellHeight;
            var block = (box.MaxY - box.MinY) * radius + below;
            var cx = cellX + cellWidth / 2 - (box.MinX + box.MaxX) / 2 * radius;
            var cy = cellY + (cellHeight - block) / 2 - box.MinY * radius;

            var inner = radius * Math.Clamp(spec.InnerRadius ?? 0.8, 0, 0.95);
            var bands = spec.ColorBands is { Count: > 0 } colorBands ? Series.ColorBands.Ranges(colorBands, "") : null;
            var thickness = radius - inner;
            var bandWidth = bands is null || compact ? 0 : Math.Clamp(thickness * 0.25, 2, 5);
            var trackOuter = radius - (bandWidth > 0 ? bandWidth + 2 : 0);
            var trackInner = Math.Min(inner, trackOuter - 2);
            double AngleOf(double v) => start + sweep * Math.Clamp((v - g.Min) / (g.Max - g.Min), 0, 1);

            var nodes = new List<Node>();
            Node Ring(double r0, double r1, double a0, double a1, StyleRef style, string cls)
            {
                var path = new PathBuilder().Arc(cx, cy, r1, a0, a1);
                if (r0 > 0)
                {
                    path.Arc(cx, cy, r0, a1, a0);
                }
                else
                {
                    path.LineTo(cx, cy);
                }

                return new PathNode(path.Close().Build(), style) { Class = cls };
            }

            nodes.Add(Ring(trackInner, trackOuter, start, start + sweep, new StyleRef("gauge-track"), "rio-track"));

            // The scale ring: each band's range of the scale in its color.
            if (bands is not null && bandWidth > 0)
            {
                foreach (var (from, to, bandColor) in bands)
                {
                    var (lo, hi) = (Math.Max(from, g.Min), Math.Min(to, g.Max));
                    if (bandColor.Length == 0 || !(hi > lo))
                    {
                        continue;
                    }

                    var style = styles.Add("gauge-band-" + bandColor, new Style { Fill = bandColor });
                    nodes.Add(Ring(radius - bandWidth, radius, AngleOf(lo), AngleOf(hi), style, "rio-gauge-band"));
                }
            }

            var color = g.Series.Data.Colors?[g.Index] ?? Series.ColorBands.ColorOf(spec.ColorBands, g.Value) ?? g.Series.Color;
            var end = AngleOf(g.Value);
            if (Math.Abs(end - start) > 1e-6)
            {
                var style = styles.Add("gauge-value-" + color, new Style { Fill = color });
                nodes.Add(Ring(trackInner, trackOuter, start, end, style, "rio-gauge-value"));
            }

            string Format(double v) => ValueFormatter.Format(v, culture, spec.Format, compactFrom: 1e5);
            var valueText = Format(g.Value);
            string? targetText = null;
            if (spec.Target is { } target && double.IsFinite(target))
            {
                targetText = Format(target);
                var a = AngleOf(target);
                var (x0, y0) = Polar(cx, cy, trackInner - 4, a);
                var (x1, y1) = Polar(cx, cy, radius + 4, a);
                nodes.Add(new PathNode(new PathBuilder().MoveTo(x0, y0).LineTo(x1, y1).Build(), new StyleRef("gauge-target")) { Class = "rio-gauge-target" });
            }

            (nodesPerSeries.TryGetValue(g.Series, out var list) ? list : nodesPerSeries[g.Series] = []).Add(new GroupNode(nodes) { Key = $"gauge:{spec.Id}:{g.Index}" });

            // The value in the middle, as large as fits inside the track.
            var room = trackInner * (labelInside || full ? 1.5 : 1.7);
            var valueSize = Math.Clamp(trackInner * 0.5, small, font * 3.2);
            while (valueSize > small && measure.Measure(valueText, valueSize) > room)
            {
                valueSize *= 0.9;
            }

            var showValue = (spec.DataLabels ?? true) && measure.Measure(valueText, valueSize) <= room && (!compact || valueSize >= 9);
            var valueY = labelInside || full ? cy - (anyLabel && !compact ? valueSize * 0.15 : 0) : cy - valueSize * 0.4;
            if (showValue)
            {
                var style = styles.Add("gauge-value-text-" + SvgSafe(valueSize), new TextStyleDef { FontSize = (float)valueSize, FontWeight = 600, Fill = ThemeTokens.Text });
                labelNodes.Add(new TextNode(valueText, new PointF((float)cx, (float)valueY), style, Anchor.Middle) { Class = "rio-gauge-value-text" });
            }

            if (!compact && g.Label.Length > 0)
            {
                var labelY = labelInside || full
                    ? valueY + valueSize * 0.55 + small * 0.8
                    : cy + (endLabels ? lineHeight + 2 : 0) + lineHeight / 2 + 4;
                var text = Shorten(g.Label, labelInside || full ? trackInner * 1.6 : cellWidth - 8, measure, small);
                labelNodes.Add(new TextNode(text, new PointF((float)cx, (float)labelY), new TextStyle("gauge-label"), Anchor.Middle) { Class = "rio-gauge-label" });
            }

            if (endLabels)
            {
                var mid = (trackInner + radius) / 2;
                foreach (var (v, a) in new[] { (g.Min, start), (g.Max, start + sweep) })
                {
                    var (x, y) = Polar(cx, cy, mid, a);
                    labelNodes.Add(new TextNode(Format(v), new PointF((float)x, (float)(y + (radius - trackInner) / 2 * Math.Abs(Math.Sin(a)) + lineHeight / 2 + 2)),
                        new TextStyle("gauge-end"), Anchor.Middle) { Class = "rio-gauge-end" });
                }
            }

            tooltip.Add(new TooltipSlice(spec.Id, g.Index, "", g.Label.Length > 0 ? g.Label : spec.DisplayName, valueText, color, (float)cx, (float)cy, 0f, (float)radius,
                (float)Math.Min(start, start + sweep), (float)Math.Max(start, start + sweep), (float)Math.Max(start, start + sweep))
            {
                Parts = targetText is null ? [] : [new TooltipPart(texts.Target, targetText)],
            });
            described.Add($"{(g.Label.Length > 0 ? g.Label + " " : "")}{valueText} ({Format(g.Min)}–{Format(g.Max)}{(targetText is null ? "" : $", {texts.Target} {targetText}")})");
        }

        foreach (var (r, nodes) in nodesPerSeries)
        {
            seriesNodes.Add(new GroupNode(nodes)
            {
                Key = "series:" + r.Spec.Id,
                Class = "rio-series rio-series-gauge",
                AriaLabel = r.Spec.DisplayName + ": " + string.Join(", ", gauges.Where(g => g.Series == r)
                    .Select(g => $"{(g.Label.Length > 0 ? g.Label + " " : "")}{ValueFormatter.Format(g.Value, culture, r.Spec.Format)}")),
            });
        }

        return true;
    }
}
