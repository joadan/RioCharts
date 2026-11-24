using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;
using RioCharts.Core.Series;

namespace RioCharts.Core.Layout;

public static partial class ChartLayout
{
    /// <summary>Opacity of the lowest value's cells (the highest is fully opaque).</summary>
    private const double HeatMinOpacity = 0.08;

    /// <summary>Steps in the color legend.</summary>
    private const int HeatLegendSteps = 5;

    /// <summary>
    /// Heatmap: every heatmap series is a row (top to bottom, named by the series), its x values are
    /// the columns and its y values the colors: one hue, more opaque for higher values, over the chart
    /// surface (so it works in light and dark themes and in exports). The legend is a color scale.
    /// With color bands the cells take the solid color of their band (stepped colors) and the legend
    /// lists the bands; a cell's own color (ColorValue) wins over both.
    /// </summary>
    private static ChartLayoutResult ComputeHeatmap(ChartDefinition spec, List<Resolved> resolved, LayoutOptions options, List<string> warnings, StyleTable styles,
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

        foreach (var r in resolved.Where(r => r.Spec.Type != SeriesType.Heatmap))
        {
            warnings.Add($"series '{r.Spec.Id}': {r.Spec.Type} series cannot be mixed with heatmap series; the series is skipped.");
        }

        var rows = resolved.Where(r => r.Spec.Type == SeriesType.Heatmap).ToList();
        var xKind = ResolveXKind(spec.X.Scale, rows);
        foreach (var r in rows.Where(r => r.Data.Count > 0 && r.Data.Kind != xKind))
        {
            warnings.Add($"series '{r.Spec.Id}': x values are {r.Data.Kind} but the axis is {xKind}; the row is left empty.");
        }

        bool Usable(Resolved r) => r.Data.Count > 0 && r.Data.Kind == xKind;
        var values = rows.Where(Usable).SelectMany(r => r.Data.Y).Where(double.IsFinite).ToList();
        var hasData = values.Count > 0;
        var (vMin, vMax) = hasData ? (values.Min(), values.Max()) : (0d, 1d);
        if (rows.FirstOrDefault()?.Spec.Max is { } fixedMax && fixedMax > vMin)
        {
            vMax = fixedMax; // e.g. 100 for percentages
        }

        var color = rows.FirstOrDefault()?.Spec.Color ?? ThemeTokens.Series(0);
        var format = ValueFormat.Of(rows.FirstOrDefault()?.Spec.Format, spec.Y);
        string F(double v) => ValueFormatter.Format(v, culture, format);

        // With a midpoint the colors diverge: above it the row color, below it the below color, each side
        // from faint at the midpoint to full at its furthest value.
        var mid = rows.FirstOrDefault()?.Spec.Midpoint is { } m && double.IsFinite(m) ? m : (double?)null;
        var below = rows.FirstOrDefault()?.Spec.BelowColor ?? ThemeTokens.Negative;
        double Opacity(double v) => mid is { } md
            ? HeatMinOpacity + (1 - HeatMinOpacity) * DivergingStrength(v, md, vMin, vMax)
            : vMax > vMin ? HeatMinOpacity + (1 - HeatMinOpacity) * Math.Clamp((v - vMin) / (vMax - vMin), 0, 1) : 0.6;
        bool IsBelow(double v) => mid is { } md && v < md;

        // Stepped colors: the first row's bands color every cell, solid. A cell's own color wins.
        var bands = rows.FirstOrDefault()?.Spec.ColorBands is { Count: > 0 } colorBands ? colorBands : null;
        string? SolidColor(SeriesData d, int i) =>
            d.Colors?[i] is { Length: > 0 } own ? own : bands is null ? null : Series.ColorBands.ColorOf(bands, d.Y[i]) ?? color;
        StyleRef CellStyle(double v, string? solid)
        {
            if (solid is not null)
            {
                return styles.Add($"heat-solid-{solid}", new Style { Fill = solid });
            }

            var opacity = Math.Round(Opacity(v), 2);
            var key = opacity.ToString(CultureInfo.InvariantCulture);
            return IsBelow(v)
                ? styles.Add($"heat-below-{key}", new Style { Fill = below, FillOpacity = (float)opacity })
                : styles.Add($"heat-{key}", new Style { Fill = color, FillOpacity = (float)opacity });
        }

        // ---------- Title and color legend ----------
        var statics = new List<Node>();
        var y0 = PadTop;
        if (!compact && chrome.Title && !string.IsNullOrWhiteSpace(spec.Title))
        {
            var titleHeight = options.TitleFontSize * 1.3;
            statics.Add(new TextNode(spec.Title!, new PointF((float)PadLeft, (float)(y0 + titleHeight / 2)), new TextStyle("title"), Anchor.Start) { Class = "rio-title" });
            y0 += titleHeight + 6;
        }

        var showScale = !compact && hasData && spec.Legend.Mode != LegendMode.Hide && (chrome.Legend || spec.Legend.Mode == LegendMode.Show);
        if (showScale)
        {
            statics.Add(bands is not null
                ? BandLegend(PadLeft, y0, lineHeight + 4, Series.ColorBands.Ranges(bands, color), bands, F, measure, font, styles, texts.Legend)
                : mid is { } md
                ? DivergingLegend(PadLeft, y0, lineHeight + 4, below, color, vMin < md ? F(vMin) : null, F(md), vMax > md ? F(vMax) : null, measure, font, styles,
                    texts.Legend, HeatMinOpacity, "heat")
                : HeatLegend(PadLeft, y0, lineHeight + 4, color, F(vMin), F(vMax), measure, font, styles, texts.Legend));
            y0 += lineHeight + 4 + 8;
        }

        // ---------- Rows (y labels) ----------
        var rowNames = rows.Select(r => r.Spec.DisplayName).ToList();
        var showYLabels = !compact && chrome.YLabels;
        var labelWidth = showYLabels && rowNames.Count > 0 ? rowNames.Max(n => measure.Measure(n, font)) : 0;
        var xLabelHeight = compact || !chrome.XLabels ? 0 : lineHeight + 6;
        var xTitleHeight = compact || !chrome.XTitle || string.IsNullOrWhiteSpace(spec.X.Title) ? 0 : lineHeight + 4;
        var plotLeft = compact ? 0 : PadLeft + (showYLabels ? labelWidth + LabelGap : 0);
        var plotRight = compact ? width : width - PadRight;
        var plotTop = compact ? 0 : y0;
        var plotBottom = compact ? height : height - PadBottom - xTitleHeight - xLabelHeight;
        var plot = new RectF((float)plotLeft, (float)plotTop, (float)Math.Max(2, plotRight - plotLeft), (float)Math.Max(2, plotBottom - plotTop));
        var rowHeight = plot.Height / Math.Max(1, rows.Count);

        // ---------- Columns (x) ----------
        BandScale? band = null;
        ContinuousScale? xScale = null;
        double cellWidth;
        var categoryIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        if (xKind == XKind.Category)
        {
            var categories = MergeCategories(rows.Where(Usable).ToList());
            for (var i = 0; i < categories.Count; i++)
            {
                categoryIndex[categories[i]] = i;
            }

            band = new BandScale(categories, plot.X, plot.Right, 0, 0);
            cellWidth = band.Bandwidth;
        }
        else
        {
            double min = double.PositiveInfinity, max = double.NegativeInfinity, spacing = double.PositiveInfinity;
            foreach (var r in rows.Where(Usable))
            {
                var d = r.Data;
                for (var i = 0; i < d.Count; i++)
                {
                    min = Math.Min(min, d.X[i]);
                    max = Math.Max(max, d.X[i]);
                    if (i > 0 && d.X[i] > d.X[i - 1])
                    {
                        spacing = Math.Min(spacing, d.X[i] - d.X[i - 1]);
                    }
                }
            }

            if (!double.IsFinite(min))
            {
                (min, max) = (0, 1);
            }

            if (!double.IsFinite(spacing))
            {
                spacing = max > min ? max - min : xKind == XKind.Time ? 3_600_000 : 1;
            }

            // Half a cell on each side: the x value is the cell's center.
            xScale = new ContinuousScale(spec.X.Min ?? min - spacing / 2, spec.X.Max ?? max + spacing / 2, plot.X, plot.Right);
            cellWidth = Math.Abs(xScale.Map(xScale.DomainMin + spacing) - xScale.Map(xScale.DomainMin));
        }

        double PixelX(SeriesData d, int i) => band is not null
            ? ((int)d.X[i] is var local && local >= 0 && local < d.Categories.Count && categoryIndex.TryGetValue(d.Categories[local], out var c) ? band.Center(c) : double.NaN)
            : xScale!.Map(d.X[i]);

        // ---------- Cells ----------
        var formatX = TimeFormat(xKind, rows.Where(Usable).SelectMany(r => r.Data.X).Take(5000), culture, spec.X.TooltipFormat ?? spec.X.Format, time, options.ViewerTimeZone,
            showOffset: spec.X.TooltipFormat is null);
        string XLabel(SeriesData d, int i) => xKind == XKind.Category
            ? ((int)d.X[i] is var c && c >= 0 && c < d.Categories.Count ? d.Categories[c] : "")
            : formatX(d.X[i]);

        var gap = cellWidth > 6 && rowHeight > 6 ? 1f : 0f;
        var cellNodes = new List<Node>();
        var selectedCells = new List<Node>();
        var labelNodes = new List<Node>();
        var hitTest = new HitTestIndex();
        var tooltipSeries = new List<TooltipSeries>();
        styles.Add("heat-label", new TextStyleDef { FontSize = (float)Math.Max(9, font - 1), Fill = ThemeTokens.Text });
        styles.Add("heat-label-inverse", new TextStyleDef { FontSize = (float)Math.Max(9, font - 1), FontWeight = 500, Fill = ThemeTokens.DataLabelInverse });
        styles.Add("heat-label-dark", new TextStyleDef { FontSize = (float)Math.Max(9, font - 1), FontWeight = 500, Fill = ThemeTokens.DataLabelDark });
        for (var row = 0; row < rows.Count; row++)
        {
            var r = rows[row];
            var cy = plot.Y + rowHeight * (row + 0.5);
            var points = new List<(float X, int Index)>();
            var selectedHere = options.SelectedPoints is { } selected && selected.TryGetValue(r.Spec.Id, out var picked) && picked.Count > 0 ? picked : null;
            if (Usable(r))
            {
                var d = r.Data;
                for (var i = 0; i < d.Count; i++)
                {
                    var v = d.Y[i];
                    var px = PixelX(d, i);
                    if (!double.IsFinite(v) || !double.IsFinite(px))
                    {
                        continue;
                    }

                    var solid = SolidColor(d, i);
                    var opacity = solid is null ? Opacity(v) : 1;
                    var style = CellStyle(v, solid);
                    var cell = new RectF((float)(px - cellWidth / 2 + gap / 2), (float)(cy - rowHeight / 2 + gap / 2),
                        (float)Math.Max(0.5, cellWidth - gap), (float)Math.Max(0.5, rowHeight - gap));
                    cellNodes.Add(new RectNode(cell, style) { CornerRadius = gap > 0 ? 2 : 0 });
                    if (selectedHere?.Contains(i) == true)
                    {
                        // An outline just inside the cell, so neighbouring outlines do not overlap.
                        selectedCells.Add(new RectNode(new RectF(cell.X + 0.75f, cell.Y + 0.75f, Math.Max(0.5f, cell.Width - 1.5f), Math.Max(0.5f, cell.Height - 1.5f)),
                            styles.Add("selected-bar", new Style { Stroke = ThemeTokens.Text, StrokeWidth = 1.5f })) { CornerRadius = 2, Class = "rio-selected-bar" });
                    }
                    points.Add(((float)px, i));

                    if (r.Spec.DataLabels == true && !compact)
                    {
                        var text = F(v);
                        if (measure.Measure(text, font - 1) + 4 <= cellWidth && rowHeight >= font + 2)
                        {
                            labelNodes.Add(new TextNode(text, new PointF((float)px, (float)cy), new TextStyle(solid is not null && IsLightColor(solid) ? "heat-label-dark"
                                    : opacity > 0.55 ? "heat-label-inverse" : "heat-label"),
                                Anchor.Middle) { Class = "rio-data-label" });
                        }
                    }
                }
            }

            points.Sort((a, b) => a.X.CompareTo(b.X)); // ascending x for the tooltip search
            var hits = new HitTestIndex.SeriesHits(r.Spec.Id, [.. points.Select(p => p.X)], [.. points.Select(_ => (float)cy)], [.. points.Select(p => p.Index)],
                [.. points.Select(p => r.Data.ToSourceIndex(p.Index))]);
            hitTest.Add(hits);
            tooltipSeries.Add(new TooltipSeries(r.Spec.Id, r.Spec.DisplayName, color, SeriesType.Heatmap, (float)(cellWidth / 2), (float)cellWidth, 0,
                hits.X, hits.Y, hits.Index, [.. points.Select(p => XLabel(r.Data, p.Index))], [.. points.Select(p => F(r.Data.Y[p.Index]))])
            {
                CellHeight = (float)rowHeight,
                PointColors = bands is not null || r.Data.Colors is not null
                    ? [.. points.Select(p => SolidColor(r.Data, p.Index) ?? (IsBelow(r.Data.Y[p.Index]) ? below : color))]
                    : mid is null ? null : [.. points.Select(p => IsBelow(r.Data.Y[p.Index]) ? below : color)],
            });

            if (showYLabels)
            {
                statics.Add(new TextNode(r.Spec.DisplayName, new PointF((float)(plot.X - LabelGap), (float)cy), new TextStyle("axis-label"), Anchor.End)
                {
                    Class = "rio-row-label",
                });
            }
        }

        // ---------- X labels and title ----------
        if (hasData && xLabelHeight > 0)
        {
            var xTicks = new List<Node>();
            if (band is not null)
            {
                // Heatmap columns keep horizontal labels (thinned when they do not fit): the rows above need the height.
                AddCategoryLabels(PlanCategoryLabels(band, 0, measure, font, lineHeight, height), band, plot.Bottom, xTicks);
            }
            else
            {
                AddContinuousXLabels(xScale!, xKind, spec.X, culture, measure, font, plot.Bottom, width, xTicks, time);
            }

            statics.Add(new GroupNode(xTicks) { Class = "rio-axis-x", AriaLabel = texts.XAxis + (spec.X.Title is { } xt ? ": " + xt : "") });
        }

        if (xTitleHeight > 0)
        {
            statics.Add(new TextNode(spec.X.Title!, new PointF(plot.CenterX, (float)(plot.Bottom + xLabelHeight + xTitleHeight / 2)), new TextStyle("axis-title"),
                Anchor.Middle) { Class = "rio-axis-title" });
        }

        var overlay = new List<Node>();
        if (!hasData && !compact)
        {
            overlay.Add(new TextNode(texts.NoData, new PointF(plot.CenterX, plot.CenterY), new TextStyle("empty"), Anchor.Middle) { Class = "rio-empty" });
        }

        var seriesLayer = new GroupNode(
        [
            new GroupNode(selectedCells.Count == 0
                ? [new GroupNode(cellNodes) { Key = "series:heatmap", Class = "rio-series rio-series-heatmap" }]
                : [new GroupNode(cellNodes) { Key = "series:heatmap", Class = "rio-series rio-series-heatmap" },
                    new GroupNode(selectedCells) { Key = "selection:points", Class = "rio-selected-points" }]) { Class = "rio-plot" },
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
            Description = DescribeHeatmap(spec, rows.Where(Usable).ToList(), XLabel, F, texts, culture, rows.Count),
        };

        var yScale = new ContinuousScale(rows.Count, 0, plot.Bottom, plot.Y);
        return new ChartLayoutResult
        {
            Scene = scene,
            Plot = plot,
            HitTest = hitTest,
            Tooltip = new TooltipModel(plot, spec.Tooltip.Enabled && hasData, false, 0, tooltipSeries),
            Legend = [],
            Warnings = warnings,
            XKind = xKind,
            XBand = band,
            YScale = yScale,
            YScales = new Dictionary<string, ContinuousScale> { ["y"] = yScale },
            HasData = hasData,
        };
    }

    /// <summary>The color scale: lowest value, a few steps of the hue, highest value.</summary>
    private static GroupNode HeatLegend(double x, double y, double height, string color, string low, string high, Text.ITextMeasurer measure, double font,
        StyleTable styles, string label, double minOpacity = HeatMinOpacity)
    {
        var cy = y + height / 2;
        var nodes = new List<Node>();
        nodes.Add(new TextNode(low, new PointF((float)x, (float)cy), new TextStyle("legend-label"), Anchor.Start));
        var sx = x + measure.Measure(low, font) + 8;
        const double step = 16;
        for (var i = 0; i < HeatLegendSteps; i++)
        {
            var opacity = minOpacity + (1 - minOpacity) * i / (HeatLegendSteps - 1);
            var style = styles.Add($"heat-{Math.Round(opacity, 2).ToString(CultureInfo.InvariantCulture)}", new Style { Fill = color, FillOpacity = (float)Math.Round(opacity, 2) });
            nodes.Add(new RectNode(new RectF((float)(sx + i * step), (float)(cy - 5), (float)(step - 2), 10), style) { CornerRadius = 2 });
        }

        nodes.Add(new TextNode(high, new PointF((float)(sx + HeatLegendSteps * step + 6), (float)cy), new TextStyle("legend-label"), Anchor.Start));
        return new GroupNode(nodes) { Class = "rio-legend rio-heat-legend", AriaLabel = $"{label}: {low} – {high}" };
    }

    /// <summary>
    /// The legend of stepped colors: a swatch and the label or range of each band, low to high, e.g. "Low" or "&lt; 50",
    /// "50–80", "&gt; 80".
    /// </summary>
    private static GroupNode BandLegend(double x, double y, double height, List<(double From, double To, string Color)> ranges, IReadOnlyList<ColorBand> bands,
        Func<double, string> format, Text.ITextMeasurer measure, double font, StyleTable styles, string label)
    {
        var cy = y + height / 2;
        var nodes = new List<Node>();
        var names = new List<string>();
        var at = x;
        foreach (var (from, to, color) in ranges)
        {
            var name = Series.ColorBands.LabelOf(bands, Series.ColorBands.Probe(from, to)) is { } own ? own
                : double.IsInfinity(from) && double.IsInfinity(to) ? ""
                : double.IsInfinity(from) ? "< " + format(to)
                : double.IsInfinity(to) ? "> " + format(from)
                : format(from) + "–" + format(to);
            names.Add(name);
            nodes.Add(new RectNode(new RectF((float)at, (float)(cy - 5), 14, 10), styles.Add($"heat-solid-{color}", new Style { Fill = color })) { CornerRadius = 2 });
            at += 14 + 5;
            if (name.Length > 0)
            {
                nodes.Add(new TextNode(name, new PointF((float)at, (float)cy), new TextStyle("legend-label"), Anchor.Start));
                at += measure.Measure(name, font);
            }

            at += 14;
        }

        return new GroupNode(nodes) { Class = "rio-legend rio-heat-legend", AriaLabel = $"{label}: {string.Join(", ", names.Where(n => n.Length > 0))}" };
    }

    /// <summary>
    /// True for colors where a label on top reads better dark: the light palette colors, and hex colors with a high
    /// luminance (e.g. yellow #f2c14e). Other CSS colors and tokens count as dark.
    /// </summary>
    private static bool IsLightColor(string color)
    {
        if (ThemeTokens.IsLightSeries(color))
        {
            return true;
        }

        var hex = color.StartsWith('#') ? color[1..] : null;
        if (hex is { Length: 3 })
        {
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        }

        if (hex is not { Length: 6 } || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        var (r, g, b) = ((rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff);
        return (0.299 * r + 0.587 * g + 0.114 * b) / 255 > 0.6;
    }

    /// <summary>
    /// How strong a value's color is on a diverging scale: 0 at the midpoint, 1 at the furthest value on its
    /// side (each side scaled on its own, so both reach the full color).
    /// </summary>
    private static double DivergingStrength(double v, double mid, double min, double max)
    {
        var reach = v >= mid ? max - mid : mid - min;
        return reach > 0 ? Math.Clamp(Math.Abs(v - mid) / reach, 0, 1) : 0;
    }

    /// <summary>
    /// A diverging color scale: the lowest value, steps of the below color fading toward the midpoint, the
    /// midpoint, steps of the above color growing to the highest value. A side without values is left out.
    /// </summary>
    private static GroupNode DivergingLegend(double x, double y, double height, string belowColor, string aboveColor, string? low, string mid, string? high,
        Text.ITextMeasurer measure, double font, StyleTable styles, string label, double minOpacity, string prefix)
    {
        const int steps = 4;
        const double step = 16;
        var cy = y + height / 2;
        var nodes = new List<Node>();
        var at = x;
        void Text(string text)
        {
            nodes.Add(new TextNode(text, new PointF((float)at, (float)cy), new TextStyle("legend-label"), Anchor.Start));
            at += measure.Measure(text, font) + 8;
        }

        void Box(string color, string key, double opacity)
        {
            var o = Math.Round(opacity, 2);
            var style = styles.Add($"{key}-{o.ToString(CultureInfo.InvariantCulture)}", new Style { Fill = color, FillOpacity = (float)o });
            nodes.Add(new RectNode(new RectF((float)at, (float)(cy - 5), (float)(step - 2), 10), style) { CornerRadius = 2 });
            at += step;
        }

        double Opacity(int i) => minOpacity + (1 - minOpacity) * (i + 1) / steps; // the faintest next to the midpoint
        if (low is not null)
        {
            Text(low);
            for (var i = steps - 1; i >= 0; i--)
            {
                Box(belowColor, prefix + "-below", Opacity(i));
            }

            at += 6;
        }

        Text(mid);
        if (high is not null)
        {
            for (var i = 0; i < steps; i++)
            {
                Box(aboveColor, prefix, Opacity(i));
            }

            at += 6;
            Text(high);
        }

        var range = string.Join(" – ", new[] { low, mid, high }.Where(t => t is not null));
        return new GroupNode(nodes) { Class = "rio-legend rio-heat-legend", AriaLabel = $"{label}: {range}" };
    }

    /// <summary>"Chart "Title" with 7 series. Lowest 12 at Sun 04:00, highest 312 at Fri 20:00."</summary>
    private static string DescribeHeatmap(ChartDefinition spec, List<Resolved> rows, Func<SeriesData, int, string> xLabel, Func<double, string> format,
        ChartTexts texts, CultureInfo culture, int rowCount)
    {
        var summary = string.IsNullOrWhiteSpace(spec.Title)
            ? texts.FillCount(nameof(ChartTexts.Summary), rowCount, culture, rowCount)
            : texts.FillCount(nameof(ChartTexts.SummaryTitled), rowCount, culture, rowCount, spec.Title);
        (Resolved Row, int Index)? min = null, max = null;
        foreach (var r in rows)
        {
            for (var i = 0; i < r.Data.Count; i++)
            {
                var v = r.Data.Y[i];
                if (!double.IsFinite(v))
                {
                    continue;
                }

                if (min is null || v < min.Value.Row.Data.Y[min.Value.Index])
                {
                    min = (r, i);
                }

                if (max is null || v > max.Value.Row.Data.Y[max.Value.Index])
                {
                    max = (r, i);
                }
            }
        }

        if (min is null || max is null)
        {
            return $"{summary} {texts.NoData}.";
        }

        string Where((Resolved Row, int Index) p) => $"{p.Row.Spec.DisplayName} {xLabel(p.Row.Data, p.Index)}";
        var extremes = texts.Fill(nameof(ChartTexts.LowestHighest), culture,
            format(min.Value.Row.Data.Y[min.Value.Index]), Where(min.Value), format(max.Value.Row.Data.Y[max.Value.Index]), Where(max.Value));
        return $"{summary} {char.ToUpper(extremes[0], culture)}{extremes[1..]}.";
    }
}
