using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;
using RioCharts.Core.Text;

namespace RioCharts.Core.Layout;

internal static partial class ChartLayout
{
    /// <summary>
    /// The radar pass: one spoke per category (clockwise from 12 o'clock), a linear value scale from
    /// zero shared by all spokes, and a closed shape per series. The legend lists series, as in
    /// cartesian charts; the tooltip is shared per spoke.
    /// </summary>
    private static ChartLayoutResult ComputeRadar(ChartDefinition spec, List<Resolved> resolved, LayoutOptions options, List<string> warnings, StyleTable styles)
    {
        var culture = options.Culture;
        var texts = options.Texts ?? ChartTexts.For(culture);
        var measure = options.Measurer;
        var font = options.FontSize;
        var small = Math.Max(9, font - 1);
        var lineHeight = font * 1.3;
        var chrome = FitChrome(spec, options, out var compact, out var width, out var height);

        foreach (var r in resolved.Where(r => r.Spec.Type != SeriesType.Radar))
        {
            warnings.Add($"series '{r.Spec.Id}': {r.Spec.Type} series cannot be mixed with radar series; the series is skipped.");
        }

        var radar = resolved.Where(r => r.Spec.Type == SeriesType.Radar).ToList();

        // ---------- Spokes: the categories of all series, in order of first appearance ----------
        var labelsPerSeries = radar.ToDictionary(r => r, r => SliceLabels(r.Data, culture));
        var spokes = new List<string>();
        var spokeIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in radar)
        {
            var labels = labelsPerSeries[r];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < labels.Length; i++)
            {
                if (!seen.Add(labels[i]))
                {
                    warnings.Add($"series '{r.Spec.Id}': category '{labels[i]}' appears more than once; only the first value is drawn. Combine them with an aggregate.");
                    continue;
                }

                if (spokeIndex.TryAdd(labels[i], spokes.Count))
                {
                    spokes.Add(labels[i]);
                }
            }
        }

        var n = spokes.Count;

        // The value of each series on each spoke (NaN: missing), and the data index it came from.
        var values = new Dictionary<Resolved, (double[] Value, int[] Index)>();
        foreach (var r in radar)
        {
            var v = Enumerable.Repeat(double.NaN, n).ToArray();
            var index = Enumerable.Repeat(-1, n).ToArray();
            var labels = labelsPerSeries[r];
            for (var i = 0; i < labels.Length; i++)
            {
                var k = spokeIndex[labels[i]];
                if (index[k] < 0)
                {
                    (v[k], index[k]) = (r.Data.Y[i], i);
                }
            }

            values[r] = (v, index);
        }

        ValueFormat FormatOf(Resolved r) => ValueFormat.Of(r.Spec.Format, spec.Y);

        // ---------- Legend: the series ----------
        var legendEntries = radar
            .Select(r => new LegendEntry(r.Spec.Id, r.Spec.DisplayName, r.Color, SeriesType.Radar, r.Visible, r.Empty,
                r.Empty ? $"{r.Spec.DisplayName}: {texts.NoData}"
                    : string.Join("\n", Enumerable.Range(0, n).Where(k => double.IsFinite(values[r].Value[k]))
                        .Select(k => $"{spokes[k]}\t{ValueFormatter.Format(values[r].Value[k], culture, FormatOf(r))}"))))
            .ToList();
        var showLegend = !compact && spec.Legend.Mode switch
        {
            LegendMode.Show => legendEntries.Count > 0,
            LegendMode.Hide => false,
            _ => legendEntries.Count > 1 && chrome.Legend,
        };
        var legendPosition = FitLegendPosition(spec.Legend.Position, chrome);

        // ---------- Title and legend ----------
        var statics = new List<Node>();
        var y0 = PadTop;
        if (chrome.Title && !string.IsNullOrWhiteSpace(spec.Title))
        {
            var titleHeight = options.TitleFontSize * 1.3;
            statics.Add(new TextNode(spec.Title!, new PointF((float)PadLeft, (float)(y0 + titleHeight / 2)), new TextStyle("title"), Anchor.Start)
            {
                Class = "rio-title",
            });
            y0 += titleHeight + 6;
        }

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
                else
                {
                    (legendTop, legendBottom) = legendPosition == LegendPosition.Top ? (legendHeight + 8, 0.0) : (0.0, legendHeight + 8);
                }
            }
        }

        var plotTop = y0 + legendTop + 4;
        var plotBottom = height - PadBottom - legendBottom - 4;
        var plotRight = width - PadRight - legendRight;
        var plot = compact ? CompactPlot(width, height) : new RectF((float)PadLeft, (float)plotTop, (float)Math.Max(10, plotRight - PadLeft), (float)Math.Max(10, plotBottom - plotTop));
        if (showLegend)
        {
            statics.Add(BuildLegend(legendItems, legendRows, legendPosition, width - PadRight - legendRight + 16, plot, y0,
                plotBottom + 8, lineHeight, styles, texts.Legend));
        }

        var drawable = radar.Where(r => r.Visible && !r.Empty).ToList();
        var hasData = drawable.Count > 0 && n > 0;

        // ---------- Value scale: linear from zero (or below, for negative values) ----------
        var finite = drawable.SelectMany(r => values[r].Value).Where(double.IsFinite).ToList();
        var lo = spec.Y.Min ?? Math.Min(0, finite.Count > 0 ? finite.Min() : 0);
        var hi = spec.Y.Max ?? Math.Max(0, finite.Count > 0 ? finite.Max() : 1);
        if (!(hi > lo))
        {
            hi = lo + 1;
        }

        // ---------- Geometry: the radius leaves room for the spoke labels around it ----------
        const double LabelGap = 8;
        var cx = (double)plot.CenterX;
        var cy = (double)plot.CenterY;
        var textHeight = small * 1.25;
        var rMax = compact ? Math.Max(2, Math.Min(plot.Width, plot.Height) / 2.0 - 1) : Math.Max(10, Math.Min(plot.Width, plot.Height) / 2.0 - 4);
        var rMin = Math.Max(10, Math.Min(rMax, Math.Min(plot.Width, plot.Height) * 0.3));
        double Angle(int k) => n == 0 ? 0 : Math.PI * 2 * k / n;
        bool Side(int k) => Math.Abs(Math.Sin(Angle(k))) > 0.15;
        var labelCap = Math.Max(40, plot.Width * 0.22);
        var spokeText = spokes.Select(l => Shorten(l, labelCap, measure, small)).ToArray();
        var radius = rMax;
        if (hasData && !compact) // compact: no spoke labels to leave room for
        {
            for (var k = 0; k < n; k++)
            {
                var (s, c) = (Math.Abs(Math.Sin(Angle(k))), Math.Abs(Math.Cos(Angle(k))));
                var w = measure.Measure(spokeText[k], small);
                if (Side(k))
                {
                    radius = Math.Min(radius, (plot.Width / 2.0 - LabelGap - 2 - w) / s);
                }

                if (c > 0.01)
                {
                    radius = Math.Min(radius, (plot.Height / 2.0 - LabelGap - 2 - textHeight * (Side(k) ? 0.5 : 1)) / c);
                }
            }

            radius = Math.Clamp(radius, rMin, rMax);
        }

        var spoke = Enumerable.Range(0, n).Select(k => Polar(cx, cy, radius, Angle(k))).ToArray();
        var target = Math.Clamp((int)(radius / 22), 3, 6);
        var (niceMin, niceMax, step) = NiceTicks.NiceDomain(lo, hi, target);
        var (domainMin, domainMax) = (spec.Y.Min ?? niceMin, spec.Y.Max ?? niceMax);
        var scale = new ContinuousScale(domainMin, domainMax, cy, cy - radius);
        double R(double v) => Math.Clamp((v - domainMin) / (domainMax - domainMin), 0, 1) * radius;

        // ---------- Grid, spokes and labels ----------
        var grid = new List<Node>();
        var axisNodes = new List<Node>();
        var spokeNodes = new List<Node>();
        if (hasData)
        {
            var ticks = NiceTicks.Ticks(domainMin, domainMax, step).Where(t => t > domainMin + step * 1e-6).ToList();
            if (ticks.Count == 0 || Math.Abs(ticks[^1] - domainMax) > step * 1e-6)
            {
                ticks.Add(domainMax);
            }

            foreach (var t in ticks)
            {
                var rt = R(t);
                var path = new PathBuilder();
                if (n >= 3)
                {
                    for (var k = 0; k < n; k++)
                    {
                        var (px, py) = Polar(cx, cy, rt, Angle(k));
                        _ = k == 0 ? path.MoveTo(px, py) : path.LineTo(px, py);
                    }

                    path.Close();
                }
                else
                {
                    path.MoveTo(cx, cy - rt).Arc(cx, cy, rt, 0, Math.PI * 2).Close(); // too few spokes for a polygon: rings
                }

                grid.Add(new PathNode(path.Build(), styles.Add("radar-grid", new Style { Stroke = ThemeTokens.Grid, StrokeWidth = 1, Fill = "none" }))
                {
                    Class = "rio-radar-ring",
                });
            }

            var spokePath = new PathBuilder();
            foreach (var (px, py) in spoke)
            {
                spokePath.MoveTo(cx, cy).LineTo(px, py);
            }

            grid.Add(new PathNode(spokePath.Build(), new StyleRef("grid")) { Class = "rio-radar-spokes" });

            // Tick values along the first spoke (12 o'clock), just right of it.
            var tickFormat = NumberFormatter.ForTicks(ticks, step, culture, ValueFormat.Of(spec.Y));
            // Drawn above the shapes on a light backing in the surface color, so a shape crossing the
            // first spoke does not cover them.
            styles.Add("radar-tick", new TextStyleDef { FontSize = (float)small, Fill = ThemeTokens.TextMuted });
            var backing = styles.Add("radar-tick-bg", new Style { Fill = ThemeTokens.Surface, FillOpacity = 0.8f });
            foreach (var t in compact ? [] : ticks)
            {
                var label = tickFormat(t);
                var ty = cy - R(t) + small * 0.6;
                axisNodes.Add(new RectNode(new RectF((float)(cx + 2), (float)(ty - small * 0.65), (float)(measure.Measure(label, small) + 4), (float)(small * 1.3)), backing)
                {
                    CornerRadius = 2,
                });
                axisNodes.Add(new TextNode(label, new PointF((float)(cx + 4), (float)ty), new TextStyle("radar-tick"), Anchor.Start));
            }

            // Spoke labels just outside the ends, anchored by side. A label that would overlap one
            // already placed is left out (the tooltip still names the spoke).
            styles.Add("radar-label", new TextStyleDef { FontSize = (float)small, Fill = ThemeTokens.Text });
            var placed = new List<RectF>();
            var chord = n > 1 ? 2 * (radius + LabelGap) * Math.Sin(Math.PI / n) - 6 : plot.Width;
            for (var k = 0; k < (compact ? 0 : n); k++)
            {
                var a = Angle(k);
                var (sin, cos) = (Math.Sin(a), Math.Cos(a));
                var (lx, ly) = Polar(cx, cy, radius + LabelGap, a);
                var anchor = sin > 0.15 ? Anchor.Start : sin < -0.15 ? Anchor.End : Anchor.Middle;
                var room = anchor == Anchor.Start ? plot.Right - lx : anchor == Anchor.End ? lx - plot.X : Math.Min(chord, plot.Width);
                var text = Shorten(spokes[k], Math.Min(labelCap, room), measure, small);
                var w = measure.Measure(text, small);
                ly -= cos * textHeight * (anchor == Anchor.Middle ? 0.5 : 0.25); // away from the center at the top and bottom
                var left = anchor == Anchor.Start ? lx : anchor == Anchor.End ? lx - w : lx - w / 2;
                var box = new RectF((float)left, (float)(ly - textHeight / 2), (float)w, (float)textHeight);
                if (text.Length == 0 || placed.Any(p => p.X < box.Right && box.X < p.Right && p.Y < box.Bottom && box.Y < p.Bottom))
                {
                    continue;
                }

                placed.Add(box);
                spokeNodes.Add(new TextNode(text, new PointF((float)lx, (float)ly), new TextStyle("radar-label"), anchor));
            }
        }

        statics.Add(new GroupNode(grid) { Class = "rio-grid" });
        if (hasData)
        {

            statics.Add(new GroupNode(spokeNodes) { Class = "rio-axis-x", AriaLabel = texts.XAxis + ": " + string.Join(", ", spokes) });
        }

        // ---------- Series ----------
        var seriesNodes = new List<Node>();
        var tooltipSeries = new List<TooltipSeries>();
        var selectedMarks = new List<Node>();
        foreach (var r in hasData ? drawable : [])
        {
            var (v, index) = values[r];
            var strokeWidth = (float)(r.Spec.StrokeWidth ?? 2);
            var points = new (double X, double Y)[n];
            for (var k = 0; k < n; k++)
            {
                points[k] = double.IsFinite(v[k]) ? Polar(cx, cy, R(v[k]), Angle(k)) : (double.NaN, double.NaN);
            }

            var nodes = new List<Node>();
            var complete = n >= 3 && v.All(double.IsFinite);
            if (complete)
            {
                var path = new PathBuilder().MoveTo(points[0].X, points[0].Y);
                for (var k = 1; k < n; k++)
                {
                    path.LineTo(points[k].X, points[k].Y);
                }

                nodes.Add(new PathNode(path.Close().Build(), styles.Add($"series-{r.ColorIndex}-radar",
                    new Style { Fill = r.Color, FillOpacity = 0.14f, Stroke = r.Color, StrokeWidth = strokeWidth }))
                {
                    Class = "rio-radar-shape",
                });
            }
            else
            {
                // A missing value breaks the shape into open lines (never pulled to the center). The
                // runs wrap around the circle; a value with no neighbour is a dot.
                var line = new PathBuilder();
                var dots = new PathBuilder();
                var (anyLine, anyDot) = (false, false);
                var gap = Array.FindIndex(v, x => !double.IsFinite(x));
                var start = n >= 3 && gap >= 0 ? gap + 1 : 0;
                var run = new List<(double X, double Y)>();
                void Flush()
                {
                    if (run.Count == 1)
                    {
                        var dot = Math.Max(2.5, strokeWidth * 1.25);
                        dots.MoveTo(run[0].X, run[0].Y - dot).Arc(run[0].X, run[0].Y, dot, 0, Math.PI * 2).Close();
                        anyDot = true;
                    }
                    else if (run.Count > 1)
                    {
                        line.MoveTo(run[0].X, run[0].Y);
                        foreach (var p in run.Skip(1))
                        {
                            line.LineTo(p.X, p.Y);
                        }

                        anyLine = true;
                    }

                    run.Clear();
                }

                for (var j = 0; j < n; j++)
                {
                    var k = (start + j) % n;
                    if (double.IsFinite(v[k]))
                    {
                        run.Add(points[k]);
                    }
                    else
                    {
                        Flush();
                    }
                }

                Flush();
                if (anyLine)
                {
                    nodes.Add(new PathNode(line.Build(), styles.Add($"series-{r.ColorIndex}-line", new Style { Stroke = r.Color, StrokeWidth = strokeWidth, Fill = "none" }))
                    {
                        Class = "rio-line",
                    });
                }

                if (anyDot)
                {
                    nodes.Add(new PathNode(dots.Build(), styles.Add($"series-{r.ColorIndex}-point", new Style { Fill = r.Color })) { Class = "rio-point" });
                }
            }

            seriesNodes.Add(new GroupNode(nodes)
            {
                Key = "series:" + r.Spec.Id,
                Class = "rio-series rio-series-radar",
                AriaLabel = r.Spec.DisplayName + ": " + string.Join(", ", Enumerable.Range(0, n).Where(k => double.IsFinite(v[k]))
                    .Select(k => $"{spokes[k]} {ValueFormatter.Format(v[k], culture, FormatOf(r))}")),
            });

            // The tooltip: each drawn value, in spoke order.
            var drawn = Enumerable.Range(0, n).Where(k => double.IsFinite(v[k])).ToList();
            tooltipSeries.Add(new TooltipSeries(r.Spec.Id, r.Spec.DisplayName, r.Color, SeriesType.Radar, 0, 0, 0,
                [.. drawn.Select(k => (float)points[k].X)], [.. drawn.Select(k => (float)points[k].Y)], [.. drawn.Select(k => index[k])],
                [.. drawn.Select(k => spokes[k])], [.. drawn.Select(k => ValueFormatter.Format(v[k], culture, FormatOf(r)))])
            {
                Spokes = [.. drawn],
            });

            if (options.SelectedPoints is { } selected && selected.TryGetValue(r.Spec.Id, out var picked) && picked.Count > 0)
            {
                var style = styles.Add($"series-{r.ColorIndex}-selected", new Style { Fill = r.Color, Stroke = ThemeTokens.Surface, StrokeWidth = 2 });
                foreach (var k in drawn.Where(k => picked.Contains(index[k])))
                {
                    var (x, y) = points[k];
                    selectedMarks.Add(new PathNode(new PathBuilder().MoveTo(x, y - 5).Arc(x, y, 5, 0, Math.PI * 2).Close().Build(), style) { Class = "rio-selected-point" });
                }
            }
        }

        if (selectedMarks.Count > 0)
        {
            seriesNodes.Add(new GroupNode(selectedMarks) { Key = "selection:points", Class = "rio-selected-points" });
        }

        var overlay = new List<Node>();
        if (!hasData && !compact)
        {
            overlay.Add(new TextNode(EmptyMessage(resolved, texts), new PointF(plot.CenterX, plot.CenterY), new TextStyle("empty"), Anchor.Middle) { Class = "rio-empty" });
        }

        // ---------- Description: each series' values per spoke ----------
        var items = string.Join("; ", drawable.Take(4).Select(r => (drawable.Count > 1 ? r.Spec.DisplayName + ": " : "")
            + string.Join(", ", Enumerable.Range(0, n).Where(k => double.IsFinite(values[r].Value[k])).Take(8)
                .Select(k => $"{spokes[k]} {ValueFormatter.Format(values[r].Value[k], culture, FormatOf(r))}"))));
        var description = (string.IsNullOrWhiteSpace(spec.Title) ? "" : spec.Title + ". ")
            + (hasData
                ? texts.FillCount(nameof(ChartTexts.PolarItems), n, culture, texts.RadarChart, n, items) + "."
                : texts.Fill(nameof(ChartTexts.PolarNoData), culture, texts.RadarChart));

        var scene = new Scene.Scene(
            new GroupNode(statics) { Class = "rio-static" },
            new GroupNode(
            [
                new GroupNode(seriesNodes) { Class = "rio-plot" },
                new GroupNode(hasData
                    ? [new GroupNode(axisNodes) { Class = "rio-axis-y", AriaLabel = AxisLabel(texts, culture, texts.YAxis, spec.Y.Title, axisNodes.OfType<TextNode>().Select(t => t.Text).ToList()) }]
                    : []) { Class = "rio-labels" },
            ]) { Class = "rio-series-layer rio-radar" },
            new GroupNode(overlay) { Class = "rio-overlay" })
        {
            Width = (float)width,
            Height = (float)height,
            Styles = styles,
            Description = description,
        };

        return new ChartLayoutResult
        {
            Scene = scene,
            Plot = plot,
            HitTest = new HitTestIndex(),
            Tooltip = new TooltipModel(plot, spec.Tooltip.Enabled && hasData, true, 0, tooltipSeries)
            {
                Radar = hasData ? new RadarGeometry((float)cx, (float)cy, (float)radius, n) : null,
            },
            Legend = legendEntries,
            Warnings = warnings,
            XKind = XKind.Category,
            YScale = scale,
            YScales = new Dictionary<string, ContinuousScale>(StringComparer.Ordinal) { [spec.Y.Id ?? "y"] = scale },
            HasData = hasData,
        };
    }
}
