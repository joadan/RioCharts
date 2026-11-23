using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;
using RioCharts.Core.Series;
using RioCharts.Core.Text;

namespace RioCharts.Core.Layout;

public static partial class ChartLayout
{

    private sealed record Slice(string Label, int Index, double Value, string Color);

    /// <summary>
    /// The polar pass: pie and donut rings (one ring per series, slices per category) or radial
    /// bars (one track per category). The legend lists categories; hiding one removes its slice.
    /// </summary>
    private static ChartLayoutResult ComputePolar(ChartDefinition spec, List<Resolved> resolved, LayoutOptions options, List<string> warnings, StyleTable styles)
    {
        var culture = options.Culture;
        var texts = options.Texts ?? ChartTexts.For(culture);
        var measure = options.Measurer;
        var font = options.FontSize;
        var lineHeight = font * 1.3;
        var chrome = FitChrome(spec, options, out var compact, out var width, out var height);

        foreach (var r in resolved.Where(r => r.Type.CoordinateSystem != CoordinateSystem.Polar))
        {
            warnings.Add($"series '{r.Spec.Id}': {r.Spec.Type} series cannot be mixed with pie, radial bar or gauge series; the series is skipped.");
        }

        var polar = resolved.Where(r => r.Type.CoordinateSystem == CoordinateSystem.Polar).ToList();
        var type = polar[0].Spec.Type;
        foreach (var r in polar.Where(r => r.Spec.Type != type))
        {
            warnings.Add($"series '{r.Spec.Id}': {r.Spec.Type} cannot be mixed with {type}; the series is skipped.");
        }

        polar = polar.Where(r => r.Spec.Type == type).ToList();
        if (type == SeriesType.RadialBar && polar.Count > 1)
        {
            warnings.Add("only one radial bar series is drawn; the others are skipped.");
            polar = [polar[0]];
        }

        // ---------- Categories (the legend items) ----------
        var labelsPerSeries = polar.ToDictionary(r => r, r => SliceLabels(r.Data, culture));
        var categories = new List<string>();
        var categoryIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var label in labelsPerSeries.Values.SelectMany(l => l))
        {
            if (categoryIndex.TryAdd(label, categories.Count))
            {
                categories.Add(label);
            }
        }

        // Where each category sits in each series (the first occurrence), for the legend tooltips.
        var indexPerSeries = labelsPerSeries.ToDictionary(p => p.Key, p =>
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < p.Value.Length; i++)
            {
                index.TryAdd(p.Value[i], i);
            }

            return index;
        });

        // A category's own color (SeriesData.Colors, the first one given in any ring), else its palette color.
        var ownColors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in polar.Where(r => r.Data.Colors is not null))
        {
            var labels = labelsPerSeries[r];
            for (var i = 0; i < labels.Length; i++)
            {
                if (r.Data.Colors![i] is { } own)
                {
                    ownColors.TryAdd(labels[i], own);
                }
            }
        }

        string ColorOf(string category) => ownColors.TryGetValue(category, out var own) ? own : ThemeTokens.Series(categoryIndex.TryGetValue(category, out var i) ? i : -1);
        bool Hidden(string category) => options.HiddenSeries?.Contains(category) == true;

        // Pies: categories grouped into "Other" in every ring they appear in get one "Other" legend item.
        var pieRings = type == SeriesType.Pie
            ? polar.Select(r => (Ring: r, Pie: PieSlices(r, labelsPerSeries[r], Hidden, ColorOf, texts, out _))).ToList()
            : [];
        var shown = pieRings.SelectMany(p => p.Pie.Slices).Where(s => s.Index >= 0).Select(s => s.Label).ToHashSet(StringComparer.Ordinal);
        var grouped = pieRings.SelectMany(p => p.Pie.Grouped).Select(s => s.Label).Where(l => !shown.Contains(l)).ToHashSet(StringComparer.Ordinal);
        var hasOther = pieRings.Any(p => p.Pie.Grouped.Count > 0);

        var totals = pieRings.ToDictionary(p => p.Ring, p => p.Pie.Total);
        string SliceTip(string category)
        {
            var parts = new List<string>();
            foreach (var r in polar)
            {
                if (!indexPerSeries[r].TryGetValue(category, out var i) || !double.IsFinite(r.Data.Y[i]))
                {
                    continue;
                }

                var v = r.Data.Y[i];
                var value = ValueFormatter.Format(v, culture, r.Spec.Format ?? "");
                var total = totals.GetValueOrDefault(r);
                var text = total > 0 && v > 0 ? $"{value} ({Percent(v / total, culture)})" : value;
                parts.Add(polar.Count > 1 ? $"{r.Spec.DisplayName} {text}" : text);
            }

            return $"{category}: {(parts.Count > 0 ? string.Join(", ", parts) : texts.NoData)}";
        }

        string OtherTip()
        {
            var lines = new List<string>();
            foreach (var (r, pie) in pieRings.Where(p => p.Pie.Grouped.Count > 0))
            {
                var sum = pie.Grouped.Sum(s => s.Value);
                var name = polar.Count > 1 ? $"{texts.Other} ({r.Spec.DisplayName})" : texts.Other;
                // The total, then each part as "• name": a tab between name and value lets the JS module
                // lay them out as rows; as plain text (a native title, a screen reader) it reads as a list.
                lines.Add($"{name}\t{ValueFormatter.Format(sum, culture, r.Spec.Format ?? "")} ({Percent(sum / pie.Total, culture)})");
                lines.AddRange(pie.Grouped.Select(s => $"{OtherPartPrefix}{s.Label}\t{ValueFormatter.Format(s.Value, culture, r.Spec.Format ?? "")} ({Percent(s.Value / pie.Total, culture)})"));
            }

            return string.Join("\n", lines);
        }

        // Gauges label themselves: no legend.
        var legendEntries = type == SeriesType.Gauge ? [] : categories.Where(c => !grouped.Contains(c))
            .Select(c => new LegendEntry(c, c, ColorOf(c), type, !Hidden(c), false, SliceTip(c)))
            .ToList();
        if (hasOther || Hidden(OtherSliceKey))
        {
            legendEntries.Add(new LegendEntry(OtherSliceKey, texts.Other, OtherColor, type, !Hidden(OtherSliceKey), false, hasOther ? OtherTip() : null));
        }
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

        styles.Add("slice-label", new TextStyleDef { FontSize = (float)Math.Max(9, font - 1), Fill = ThemeTokens.Text });
        styles.Add("leader", new Style { Stroke = ThemeTokens.Axis, StrokeWidth = 1, Fill = "none" });
        styles.Add("center-value", new TextStyleDef { FontSize = (float)(font * 1.75), FontWeight = 600, Fill = ThemeTokens.Text });
        styles.Add("center-caption", new TextStyleDef { FontSize = (float)Math.Max(9, font - 1), Fill = ThemeTokens.TextMuted });
        styles.Add("track", new Style { Stroke = ThemeTokens.Grid, Fill = "none" });

        var seriesNodes = new List<Node>();
        var labelNodes = new List<Node>();
        var tooltipSlices = new List<TooltipSlice>();
        var described = new List<(string Label, double Value)>();
        var gaugeItems = new List<string>();
        var hasData = type == SeriesType.Pie
            ? LayoutPies(polar, labelsPerSeries, plot, compact, ColorOf, Hidden, culture, texts, measure, font, styles, warnings, seriesNodes, labelNodes, tooltipSlices, described)
            : type == SeriesType.Gauge
            ? LayoutGauges(polar, labelsPerSeries, plot, compact, culture, texts, measure, font, styles, warnings, seriesNodes, labelNodes, tooltipSlices, gaugeItems)
            : LayoutRadialBars(polar[0], labelsPerSeries[polar[0]], plot, compact, ColorOf, Hidden, culture, measure, font, styles, warnings, seriesNodes, labelNodes,
                tooltipSlices, described);
        if (type != SeriesType.Gauge && options.SelectedPoints is { Count: > 0 } selected && SliceSelection(tooltipSlices, selected, styles) is { Count: > 0 } marks)
        {
            seriesNodes.Add(new GroupNode(marks) { Key = "selection:points", Class = "rio-selected-points" });
        }

        var overlay = new List<Node>();
        if (!hasData && !compact)
        {
            var allHidden = categories.Count > 0 && categories.All(Hidden);
            overlay.Add(new TextNode(allHidden ? texts.AllSlicesHidden : texts.NoData, new PointF(plot.CenterX, plot.CenterY),
                new TextStyle("empty"), Anchor.Middle) { Class = "rio-empty" });
        }

        var kind = type switch
        {
            SeriesType.Pie => polar.Any(r => (r.Spec.InnerRadius ?? 0) > 0) ? texts.DonutChart : texts.PieChart,
            SeriesType.Gauge => texts.GaugeChart,
            _ => texts.RadialBarChart,
        };
        var itemTexts = type == SeriesType.Gauge ? gaugeItems : described.Select(d => $"{d.Label} {ValueFormatter.Format(d.Value, culture)}").ToList();
        var items = string.Join(", ", itemTexts.Take(8))
            + (itemTexts.Count > 8 ? " " + texts.Fill(nameof(ChartTexts.More), culture, itemTexts.Count - 8) : "");
        var description = (string.IsNullOrWhiteSpace(spec.Title) ? "" : spec.Title + ". ")
            + (itemTexts.Count == 0
                ? texts.Fill(nameof(ChartTexts.PolarNoData), culture, kind)
                : texts.FillCount(nameof(ChartTexts.PolarItems), itemTexts.Count, culture, kind, itemTexts.Count, items) + ".");

        var scene = new Scene.Scene(
            new GroupNode(statics) { Class = "rio-static" },
            new GroupNode(
            [
                new GroupNode(seriesNodes) { Class = "rio-plot" },
                new GroupNode(labelNodes) { Class = "rio-labels" },
            ])
            { Class = "rio-series-layer rio-polar" },
            new GroupNode(overlay) { Class = "rio-overlay" })
        {
            Width = (float)width,
            Height = (float)height,
            Styles = styles,
            Description = description,
        };

        var unit = new ContinuousScale(0, 1, plot.Bottom, plot.Y);
        return new ChartLayoutResult
        {
            Scene = scene,
            Plot = plot,
            HitTest = new HitTestIndex(),
            Tooltip = new TooltipModel(plot, spec.Tooltip.Enabled && hasData, false, 0, []) { Slices = tooltipSlices },
            Legend = legendEntries,
            Warnings = warnings,
            XKind = XKind.Category,
            YScale = unit,
            YScales = new Dictionary<string, ContinuousScale>(StringComparer.Ordinal) { [spec.Y.Id ?? "y"] = unit },
            HasData = hasData,
        };
    }

    /// <summary>The name of each data point: its category, or its formatted x.</summary>
    private static string[] SliceLabels(SeriesData data, CultureInfo culture)
    {
        var format = ValueFormatter.ForX(data.Kind, data.X, culture);
        var labels = new string[data.Count];
        for (var i = 0; i < data.Count; i++)
        {
            labels[i] = data.Kind == XKind.Category
                ? ((int)data.X[i] is var c && c >= 0 && c < data.Categories.Count ? data.Categories[c] : "")
                : format(data.X[i]);
        }

        return labels;
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    private readonly record struct UnitBox(double MinX, double MaxX, double MinY, double MaxY);

    /// <summary>
    /// Bounds of a unit-radius sector (center included) in screen orientation (y down): the arc's
    /// end points plus every 90° extreme it passes.
    /// </summary>
    private static UnitBox ArcBounds(double start, double sweep)
    {
        double minX = 0, maxX = 0, minY = 0, maxY = 0;
        void Add(double a)
        {
            var (x, y) = (Math.Sin(a), -Math.Cos(a));
            (minX, maxX, minY, maxY) = (Math.Min(minX, x), Math.Max(maxX, x), Math.Min(minY, y), Math.Max(maxY, y));
        }

        Add(start);
        Add(start + sweep);
        var (lo, hi) = sweep >= 0 ? (start, start + sweep) : (start + sweep, start);
        for (var k = (int)Math.Ceiling(lo / (Math.PI / 2) - 1e-9); k * Math.PI / 2 <= hi + 1e-9; k++)
        {
            Add(k * Math.PI / 2);
        }

        return new UnitBox(minX, maxX, minY, maxY);
    }

    private static (double X, double Y) Polar(double cx, double cy, double r, double angle) => (cx + Math.Sin(angle) * r, cy - Math.Cos(angle) * r);

    private static string Percent(double share, CultureInfo culture) =>
        share > 0 && share < 0.005 ? "<1%" : (share * 100).ToString("0", culture) + "%";

    // ---------- Pie and donut ----------

    /// <summary>The legend key of the "Other" slice (hide it with this id in HiddenSeries).</summary>
    public const string OtherSliceKey = "rio:other";

    /// <summary>Starts each part's line in the "Other" legend tooltip (the JS module shows those lines as parts).</summary>
    internal const string OtherPartPrefix = "• ";

    private static readonly string OtherColor = ThemeTokens.Series(ThemeTokens.SeriesColorCount - 1);

    /// <summary>
    /// A ring's slices: positive values of visible categories, slices under GroupSmallSlices grouped
    /// into one "Other" slice (index -1) unless it is hidden. Total: the visible slices' sum.
    /// </summary>
    private static (List<Slice> Slices, double Total, List<Slice> Grouped) PieSlices(Resolved r, string[] labels, Func<string, bool> hidden,
        Func<string, string> colorOf, ChartTexts texts, out bool skippedNegative)
    {
        var slices = new List<Slice>();
        skippedNegative = false;
        for (var i = 0; i < r.Data.Count; i++)
        {
            var v = r.Data.Y[i];
            var label = labels[i];
            if (!double.IsFinite(v) || hidden(label))
            {
                continue;
            }

            if (v <= 0)
            {
                skippedNegative |= v < 0;
                continue;
            }

            slices.Add(new Slice(label, i, v, colorOf(label)));
        }

        var total = slices.Sum(s => s.Value);
        List<Slice> grouped = [];
        if (r.Spec.GroupSmallSlices is > 0 and var threshold && total > 0)
        {
            var tiny = slices.Where(s => s.Value / total < threshold).ToList();
            if (tiny.Count > 1)
            {
                grouped = tiny;
                slices = slices.Except(tiny).ToList();
                if (hidden(OtherSliceKey))
                {
                    total -= tiny.Sum(s => s.Value);
                }
                else
                {
                    slices.Add(new Slice(texts.Other, -1, tiny.Sum(s => s.Value), OtherColor));
                }
            }
        }

        return (slices, total, grouped);
    }

    /// <summary>
    /// Selected slices and radial bars: an outline along the shape's edge in the text color, as for selected bars. The
    /// grouped "Other" slice is no single item and is never outlined.
    /// </summary>
    private static List<Node> SliceSelection(List<TooltipSlice> slices, IReadOnlyDictionary<string, IReadOnlyCollection<int>> selected, StyleTable styles)
    {
        var marks = new List<Node>();
        var style = styles.Add("selected-slice", new Style { Stroke = ThemeTokens.Text, StrokeWidth = 1.5f, Fill = "none" });
        foreach (var s in slices)
        {
            if (s.Index < 0 || !selected.TryGetValue(s.SeriesId, out var picked) || !picked.Contains(s.Index) || s.A1 - s.A0 <= 0)
            {
                continue;
            }

            var path = new PathBuilder().Arc(s.Cx, s.Cy, s.R1, s.A0, s.A1);
            if (s.R0 > 0)
            {
                path.Arc(s.Cx, s.Cy, s.R0, s.A1, s.A0);
            }
            else if (s.A1 - s.A0 < Math.PI * 2 - 1e-6)
            {
                path.LineTo(s.Cx, s.Cy);
            }

            marks.Add(new PathNode(path.Close().Build(), style) { Class = "rio-selected-slice" });
        }

        return marks;
    }

    private static bool LayoutPies(List<Resolved> rings, Dictionary<Resolved, string[]> labels, RectF plot, bool compact, Func<string, string> colorOf,
        Func<string, bool> hidden, CultureInfo culture, ChartTexts texts, ITextMeasurer measure, double font, StyleTable styles, List<string> warnings,
        List<Node> seriesNodes, List<Node> labelNodes, List<TooltipSlice> tooltip, List<(string, double)> described)
    {
        // Slices per ring: positive values of visible categories, small ones grouped when asked.
        var slicesPerRing = new List<(Resolved Ring, List<Slice> Slices, double Total, List<Slice> Grouped)>();
        foreach (var r in rings)
        {
            var (slices, total, grouped) = PieSlices(r, labels[r], hidden, colorOf, texts, out var skipped);
            if (skipped)
            {
                warnings.Add($"series '{r.Spec.Id}': negative values cannot be shown in a pie and are left out.");
            }

            if (slices.Count > 0)
            {
                slicesPerRing.Add((r, slices, total, grouped));
            }
        }

        if (slicesPerRing.Count == 0)
        {
            return false;
        }

        var first = slicesPerRing[0].Ring.Spec;
        var start = Radians(first.StartAngle ?? 0);
        var end = Radians(first.EndAngle ?? (first.StartAngle ?? 0) + 360);
        var sweep = Math.Clamp(end - start, -Math.PI * 2, Math.PI * 2);
        var n = slicesPerRing.Count;
        var innerFraction = Math.Clamp(first.InnerRadius ?? (n > 1 ? 0.4 : 0), 0, 0.95);
        var small = Math.Max(9, font - 1);
        var textHeight = small * 1.25;

        // Fit the drawn sweep, not the whole circle: a half donut uses the full plot height.
        var box = ArcBounds(start, sweep);
        if (innerFraction > 0 && n == 1)
        {
            box = box with { MaxY = Math.Max(box.MaxY, 0.25) }; // room for the center total under a half donut
        }

        double Fit(double w, double h) => Math.Min(w / (box.MaxX - box.MinX), h / (box.MaxY - box.MinY));

        // The radius leaves room for outside labels only when some slice needs one.
        var outer = slicesPerRing[0];
        var showLabels = !compact && (outer.Ring.Spec.DataLabels ?? true); // compact: no labels, the pie fills the box
        var full = compact ? Math.Max(2, Fit(plot.Width, plot.Height)) : Math.Max(10.0, Fit(plot.Width - 8, plot.Height - 8));
        string LabelText(Resolved ring, Slice s, double total) =>
            ring.Spec.Format is { } f ? ValueFormatter.Format(s.Value, culture, f) : Percent(s.Value / total, culture);

        double RadiusFor(bool outside)
        {
            if (!outside)
            {
                return full;
            }

            var widest = outer.Slices.Max(s => measure.Measure(LabelText(outer.Ring, s, outer.Total), small));
            return Fit(plot.Width - 2 * (widest + 30), plot.Height - 2 * textHeight);
        }

        // Labels are horizontal, so test the label's box (not its width along the arc): on the sides of a
        // thin donut the text runs across the ring and must not spill over its edges.
        bool FitsInside(double r0, double r1, double a0, double a1, double w)
        {
            var rm = r0 > 0 ? (r0 + r1) / 2 : r1 * 0.62;
            if (Math.Abs(a1 - a0) * rm < w + 8 || r1 - r0 < textHeight + 4 || (Math.Abs(a1 - a0) < 0.25 && r0 <= 0))
            {
                return false;
            }

            var mid = (a0 + a1) / 2;
            var (lx, ly) = Polar(0, 0, rm, mid);
            var (lo, hi) = (Math.Min(a0, a1), Math.Max(a0, a1));
            const double pad = 2;
            var halfW = w / 2 + pad;
            var halfH = small * 0.4 + pad; // digits and % sit within about 0.8 em
            foreach (var (x, y) in new[] { (lx - halfW, ly - halfH), (lx + halfW, ly - halfH), (lx - halfW, ly + halfH), (lx + halfW, ly + halfH) })
            {
                var d = Math.Sqrt(x * x + y * y);
                if (d > r1 || (r0 > 0 && d < r0))
                {
                    return false;
                }

                if (hi - lo < Math.PI * 2 - 1e-6 && d > 0)
                {
                    var angle = Math.Atan2(x, -y);
                    angle += Math.Round((mid - angle) / (Math.PI * 2)) * Math.PI * 2;
                    if (angle < lo || angle > hi)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        var radius = full;
        var outsideAllowed = false;
        if (showLabels)
        {
            var needsOutside = false;
            double acc = 0;
            foreach (var s in outer.Slices)
            {
                var a0 = start + sweep * acc / outer.Total;
                acc += s.Value;
                var a1 = start + sweep * acc / outer.Total;
                var r0 = full * (innerFraction + (1 - innerFraction) * (n - 1) / n);
                needsOutside |= !FitsInside(r0, full, a0, a1, measure.Measure(LabelText(outer.Ring, s, outer.Total), small));
            }

            if (needsOutside && RadiusFor(true) >= Math.Max(30, full * 0.55))
            {
                radius = RadiusFor(true);
                outsideAllowed = true;
            }
        }

        var cx = (float)(plot.CenterX - (box.MinX + box.MaxX) / 2 * radius);
        var cy = (float)(plot.CenterY - (box.MinY + box.MaxY) / 2 * radius);
        var gap = n > 1 ? 3 : 0;
        var outsideLabels = new List<(double Angle, string Text, double Width, double Sweep, string Key)>();
        for (var k = 0; k < n; k++)
        {
            var (ring, slices, total, grouped) = slicesPerRing[k];
            var sum = slices.Sum(s => s.Value);

            // The first series is the outer ring.
            var r1 = radius * (innerFraction + (1 - innerFraction) * (n - k) / n);
            var r0 = radius * (innerFraction + (1 - innerFraction) * (n - k - 1) / n) + (k < n - 1 ? gap : 0);
            r0 = Math.Max(0, Math.Min(r0, r1 - 2));
            var nodes = new List<Node>();
            double acc = 0;
            foreach (var s in slices)
            {
                var a0 = start + sweep * acc / sum;
                acc += s.Value;
                var a1 = start + sweep * acc / sum;
                var style = styles.Add("slice-" + s.Color, new Style { Fill = s.Color, Stroke = ThemeTokens.Surface, StrokeWidth = slices.Count > 1 ? 1.5f : 0 });
                var path = new PathBuilder().Arc(cx, cy, r1, a0, a1);
                if (r0 > 0)
                {
                    path.Arc(cx, cy, r0, a1, a0);
                }
                else if (Math.Abs(a1 - a0) < Math.PI * 2 - 1e-6)
                {
                    path.LineTo(cx, cy);
                }

                // Keyed like its legend item, so hovering that item highlights the slice (in every ring).
                nodes.Add(new GroupNode([new PathNode(path.Close().Build(), style) { Class = "rio-slice" }])
                {
                    Key = "series:" + (s.Index < 0 ? OtherSliceKey : s.Label),
                });
                var share = s.Value / sum;
                var valueText = ValueFormatter.Format(s.Value, culture, ring.Spec.Format ?? "");
                tooltip.Add(new TooltipSlice(ring.Spec.Id, s.Index, n > 1 || ring.Spec.Name is not null ? ring.Spec.DisplayName : "", s.Label,
                    $"{valueText} ({Percent(share, culture)})", s.Color, cx, cy, (float)r0, (float)r1, (float)Math.Min(a0, a1), (float)Math.Max(a0, a1),
                    (float)Math.Max(a0, a1))
                {
                    // The "Other" slice lists what it holds.
                    Parts = s.Index < 0
                        ? [.. grouped.Select(g => new TooltipPart(g.Label, $"{ValueFormatter.Format(g.Value, culture, ring.Spec.Format ?? "")} ({Percent(g.Value / sum, culture)})"))]
                        : [],
                });
                if (k == 0)
                {
                    described.Add((s.Label, s.Value));
                }

                if (!showLabels)
                {
                    continue;
                }

                var text = LabelText(ring, s, sum);
                var w = measure.Measure(text, small);
                var mid = (a0 + a1) / 2;
                var labelKey = "labels:" + (s.Index < 0 ? OtherSliceKey : s.Label); // fades with its slice on legend hover
                if (FitsInside(r0, r1, a0, a1, w))
                {
                    var (lx, ly) = Polar(cx, cy, r0 > 0 ? (r0 + r1) / 2 : r1 * 0.62, mid);
                    labelNodes.Add(new GroupNode([new TextNode(text, new PointF((float)lx, (float)ly), new TextStyle("data-label-inverse"), Anchor.Middle)])
                    {
                        Key = labelKey,
                    });
                }
                else if (k == 0 && outsideAllowed)
                {
                    outsideLabels.Add((mid, text, w, Math.Abs(a1 - a0), labelKey));
                }
            }

            seriesNodes.Add(new GroupNode(nodes)
            {
                Key = "series:" + ring.Spec.Id,
                Class = "rio-series rio-series-pie",
                AriaLabel = $"{ring.Spec.DisplayName}: " + string.Join(", ", slices.Select(s => $"{s.Label} {Percent(s.Value / sum, culture)}")),
            });

            // Donut center: the total, when there is room for it.
            if (n == 1 && r0 >= 30 && !compact)
            {
                var totalText = ValueFormatter.Format(sum, culture, ring.Spec.Format, compactFrom: 1e5);
                if (measure.Measure(totalText, font * 1.75) <= r0 * 1.6)
                {
                    labelNodes.Add(new TextNode(totalText, new PointF(cx, (float)(cy - font * 0.4)), new TextStyle("center-value"), Anchor.Middle)
                    {
                        Class = "rio-center-value",
                    });
                    labelNodes.Add(new TextNode(texts.Total, new PointF(cx, (float)(cy + font * 1.1)), new TextStyle("center-caption"), Anchor.Middle)
                    {
                        Class = "rio-center-caption",
                    });
                }
            }
        }

        if (outsideLabels.Count > 0)
        {
            labelNodes.AddRange(PlaceOutsideLabels(outsideLabels, cx, cy, radius, plot, textHeight));
        }

        return true;
    }

    /// <summary>
    /// Labels outside the pie with leader lines. Each side is stacked top to bottom so labels
    /// never overlap, then shifted up if the stack runs past the plot.
    /// </summary>
    private static List<Node> PlaceOutsideLabels(List<(double Angle, string Text, double Width, double Sweep, string Key)> labels, double cx, double cy, double radius,
        RectF plot, double textHeight)
    {
        var nodes = new List<Node>();
        foreach (var right in new[] { true, false })
        {
            var side = labels
                .Where(l => Math.Sin(l.Angle) >= 0 == right)
                .Select(l => (l.Angle, l.Text, l.Width, l.Sweep, l.Key, Y: Polar(cx, cy, radius + 10, l.Angle).Y))
                .OrderBy(l => l.Y)
                .ToList();
            if (side.Count == 0)
            {
                continue;
            }

            // Only as many labels as fit down the side: the largest slices keep theirs (the rest are in
            // the tooltip). This also keeps the untangling below, which is quadratic per step, cheap.
            var room = Math.Max(1, (int)(plot.Height / textHeight));
            if (side.Count > room)
            {
                side = side.OrderByDescending(l => l.Sweep).Take(room).OrderBy(l => l.Y).ToList();
            }

            var ys = side.Select(l => Math.Max(l.Y, plot.Y + textHeight / 2)).ToArray();
            for (var i = 1; i < ys.Length; i++)
            {
                ys[i] = Math.Max(ys[i], ys[i - 1] + textHeight);
            }

            var overflow = ys[^1] - (plot.Bottom - textHeight / 2);
            if (overflow > 0)
            {
                ys[^1] -= overflow;
                for (var i = ys.Length - 2; i >= 0; i--)
                {
                    ys[i] = Math.Min(ys[i], ys[i + 1] - textHeight);
                }
            }

            // A leader runs from its slice to a bend at its label's height, then across to the text. The
            // bend sits just outside the pie (on the slice's radial line when the label is level with
            // it). Where tiny slices crowd the top or bottom, labels stack away from their slices and a
            // horizontal run could pass over a neighbour's leader: move that bend outward until it
            // clears. Only if that cannot work, swap the two labels' slots.
            var sign = right ? 1 : -1;
            var textX = cx + sign * (radius + 26);
            var endX = textX - sign * 4;
            var points = side.Select(l => Polar(cx, cy, radius + 2, l.Angle)).ToArray();
            var natural = side.Select(l => Polar(cx, cy, radius + 10, l.Angle).X).ToArray();
            var bend = new double[side.Count];
            var slot = Enumerable.Range(0, side.Count).ToArray();
            ((double X, double Y) A, (double X, double Y) B, (double X, double Y) C) Route(int i) => (points[i], (bend[i], ys[slot[i]]), (endX, ys[slot[i]]));

            void PlaceBends()
            {
                // A label stacked toward the pie's center from its slice (below a slice in the top half,
                // above one in the bottom half) would make the leader dive back along the pie: leave
                // at a shallow outward slant instead.
                for (var i = 0; i < side.Count; i++)
                {
                    var dy = ys[slot[i]] - points[i].Y;
                    var inward = points[i].Y < cy ? dy > 0 : dy < 0;
                    var slant = inward ? points[i].X + sign * 2 * Math.Abs(dy) : natural[i];
                    var limit = endX - sign * 4;
                    bend[i] = sign > 0 ? Math.Min(Math.Max(natural[i], slant), limit) : Math.Max(Math.Min(natural[i], slant), limit);
                }
                for (var pass = 0; pass <= side.Count; pass++)
                {
                    var moved = false;
                    for (var i = 0; i < side.Count; i++)
                    {
                        var y = ys[slot[i]];
                        for (var j = 0; j < side.Count; j++)
                        {
                            var (p, q) = (points[j], (X: bend[j], Y: ys[slot[j]]));
                            if (j == i || y <= Math.Min(p.Y, q.Y) || y >= Math.Max(p.Y, q.Y))
                            {
                                continue;
                            }

                            var x = p.X + (q.X - p.X) * (y - p.Y) / (q.Y - p.Y) + sign * 3;
                            var limit = endX - sign * 4;
                            var wanted = sign > 0 ? Math.Min(Math.Max(bend[i], x), limit) : Math.Max(Math.Min(bend[i], x), limit);
                            if (wanted != bend[i])
                            {
                                bend[i] = wanted;
                                moved = true;
                            }
                        }
                    }

                    if (!moved)
                    {
                        break;
                    }
                }
            }

            bool Crosses(int a, int b)
            {
                var (ra, rb) = (Route(a), Route(b));
                return SegmentsCross(ra.A, ra.B, rb.A, rb.B) || SegmentsCross(ra.A, ra.B, rb.B, rb.C)
                    || SegmentsCross(ra.B, ra.C, rb.A, rb.B) || SegmentsCross(ra.B, ra.C, rb.B, rb.C);
            }

            (int a, int b) FirstCrossing()
            {
                for (var a = 0; a < side.Count; a++)
                {
                    for (var b = a + 1; b < side.Count; b++)
                    {
                        if (Crosses(a, b))
                        {
                            return (a, b);
                        }
                    }
                }

                return (-1, -1);
            }

            PlaceBends();
            for (var step = 0; step < 40 * side.Count; step++)
            {
                var pair = FirstCrossing();
                if (pair.a < 0)
                {
                    break;
                }

                // Move a bend outward first (one, else both); swap slots only when neither can move.
                var limit = endX - sign * 4;
                bool Out(int i)
                {
                    var moved = sign > 0 ? Math.Min(bend[i] + 4, limit) : Math.Max(bend[i] - 4, limit);
                    var changed = moved != bend[i];
                    bend[i] = moved;
                    return changed;
                }

                var (oldA, oldB) = (bend[pair.a], bend[pair.b]);
                if (Out(pair.a) && !Crosses(pair.a, pair.b))
                {
                    continue;
                }

                bend[pair.a] = oldA;
                if (Out(pair.b) && !Crosses(pair.a, pair.b))
                {
                    continue;
                }

                if (!Out(pair.a) & bend[pair.b] == oldB)
                {
                    (slot[pair.a], slot[pair.b]) = (slot[pair.b], slot[pair.a]);
                    PlaceBends();
                }
            }

            for (var i = 0; i < side.Count; i++)
            {
                var text = side[i].Text;
                var y = ys[slot[i]];
                var path = new PathBuilder();
                var r = Route(i);
                path.MoveTo(r.A.X, r.A.Y);
                path.LineTo(r.B.X, r.B.Y);
                path.LineTo(r.C.X, r.C.Y);

                var leader = path.Build();
                nodes.Add(new GroupNode(
                [
                    new PathNode(leader, new StyleRef("leader")) { Class = "rio-leader" },
                    new TextNode(text, new PointF((float)textX, (float)y), new TextStyle("slice-label"), right ? Anchor.Start : Anchor.End),
                ])
                {
                    Key = side[i].Key,
                });
            }
        }

        return nodes;
    }

    private static bool SegmentsCross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        static double Side((double X, double Y) p, (double X, double Y) q, (double X, double Y) r) =>
            (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);

        return Side(a, b, c) * Side(a, b, d) < 0 && Side(c, d, a) * Side(c, d, b) < 0;
    }

    // ---------- Radial bar ----------

    private static bool LayoutRadialBars(Resolved series, string[] labels, RectF plot, bool compact, Func<string, string> colorOf, Func<string, bool> hidden,
        CultureInfo culture, ITextMeasurer measure, double font, StyleTable styles, List<string> warnings,
        List<Node> seriesNodes, List<Node> labelNodes, List<TooltipSlice> tooltip, List<(string, double)> described)
    {
        var bars = new List<Slice>();
        for (var i = 0; i < series.Data.Count; i++)
        {
            var v = series.Data.Y[i];
            if (double.IsFinite(v) && !hidden(labels[i]))
            {
                bars.Add(new Slice(labels[i], i, Math.Max(0, v), colorOf(labels[i])));
            }
        }

        if (bars.Count == 0)
        {
            return false;
        }

        if (series.Data.Y.Any(v => v < 0))
        {
            warnings.Add($"series '{series.Spec.Id}': negative values are shown as empty radial bars.");
        }

        var spec = series.Spec;
        var max = spec.Max is > 0 and var m ? m : NiceTicks.NiceDomain(0, Math.Max(bars.Max(b => b.Value), 1e-9), 5).Max;
        var start = Radians(spec.StartAngle ?? 0);
        var sweep = Math.Clamp(Radians((spec.EndAngle ?? 270) - (spec.StartAngle ?? 0)), -Math.PI * 2, Math.PI * 2);
        var cx = plot.CenterX;
        var cy = plot.CenterY;
        var radius = compact ? Math.Max(2, Math.Min(plot.Width, plot.Height) / 2.0) : Math.Max(10.0, Math.Min(plot.Width, plot.Height) / 2.0 - 4);
        var inner = radius * Math.Clamp(spec.InnerRadius ?? 0.3, 0, 0.9);
        var band = (radius - inner) / bars.Count;
        var thickness = Math.Max(2, Math.Min(band * 0.72, 28));
        var small = Math.Max(9, font - 1);
        var showLabels = !compact && (spec.DataLabels ?? true) && band >= small * 1.1;

        var nodes = new List<Node>();
        for (var k = 0; k < bars.Count; k++)
        {
            var b = bars[k];
            var rm = radius - band * k - band / 2;
            var trackStyle = styles.Add("track-" + SvgSafe(thickness), new Style { Stroke = ThemeTokens.Grid, StrokeWidth = (float)thickness, Fill = "none" });
            var bar = new List<Node> { new PathNode(new PathBuilder().Arc(cx, cy, rm, start, start + sweep).Build(), trackStyle) { Class = "rio-track" } };

            var share = Math.Clamp(b.Value / max, 0, 1);
            var a1 = start + sweep * share;
            if (share > 0)
            {
                var style = styles.Add($"radial-{b.Color}-{SvgSafe(thickness)}", new Style { Stroke = b.Color, StrokeWidth = (float)thickness, Fill = "none" });
                bar.Add(new PathNode(new PathBuilder().Arc(cx, cy, rm, start, a1).Build(), style) { Class = "rio-radial-bar" });
            }

            nodes.Add(new GroupNode(bar) { Key = "series:" + b.Label }); // keyed like its legend item

            var valueText = ValueFormatter.Format(b.Value, culture, spec.Format);
            tooltip.Add(new TooltipSlice(spec.Id, b.Index, spec.Name ?? "", b.Label, valueText, b.Color, cx, cy,
                (float)(rm - thickness / 2), (float)(rm + thickness / 2), (float)Math.Min(start, a1), (float)Math.Max(start, a1),
                (float)Math.Max(start, start + sweep)));
            described.Add((b.Label, b.Value));

            if (showLabels)
            {
                // Name and value just before the start of the track.
                var (sx, sy) = Polar(cx, cy, rm, start);
                var text = $"{b.Label} {valueText}";
                if (measure.Measure(text, small) <= sx - plot.X - 10)
                {
                    labelNodes.Add(new GroupNode([new TextNode(text, new PointF((float)(sx - 10), (float)sy), new TextStyle("slice-label"), Anchor.End)])
                    {
                        Key = "labels:" + b.Label,
                    });
                }
            }
        }

        seriesNodes.Add(new GroupNode(nodes)
        {
            Key = "series:" + spec.Id,
            Class = "rio-series rio-series-radialbar",
            AriaLabel = spec.DisplayName + ": " + string.Join(", ", bars.Select(b => $"{b.Label} {ValueFormatter.Format(b.Value, culture, spec.Format)}")),
        });
        return true;
    }

    private static string SvgSafe(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
