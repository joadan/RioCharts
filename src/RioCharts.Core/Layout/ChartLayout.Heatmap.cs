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
    /// </summary>
    private static ChartLayoutResult ComputeHeatmap(ChartSpec spec, List<Resolved> resolved, LayoutOptions options, List<string> warnings, StyleTable styles,
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
        var format = rows.FirstOrDefault()?.Spec.Format ?? spec.Y.Format;
        string F(double v) => ValueFormatter.Format(v, culture, format);
        double Opacity(double v) => vMax > vMin ? HeatMinOpacity + (1 - HeatMinOpacity) * Math.Clamp((v - vMin) / (vMax - vMin), 0, 1) : 0.6;

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
            statics.Add(HeatLegend(PadLeft, y0, lineHeight + 4, color, F(vMin), F(vMax), measure, font, styles, texts.Legend));
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
        LinearScale? xScale = null;
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
            xScale = new LinearScale(spec.X.Min ?? min - spacing / 2, spec.X.Max ?? max + spacing / 2, plot.X, plot.Right);
            cellWidth = Math.Abs(xScale.Map(xScale.DomainMin + spacing) - xScale.Map(xScale.DomainMin));
        }

        double PixelX(SeriesData d, int i) => band is not null
            ? ((int)d.X[i] is var local && local >= 0 && local < d.Categories.Count && categoryIndex.TryGetValue(d.Categories[local], out var c) ? band.Center(c) : double.NaN)
            : xScale!.Map(d.X[i]);

        // ---------- Cells ----------
        var formatX = TimeFormat(xKind, rows.Where(Usable).SelectMany(r => r.Data.X).Take(5000), culture, spec.X.Format, time, options.ViewerTimeZone);
        string XLabel(SeriesData d, int i) => xKind == XKind.Category
            ? ((int)d.X[i] is var c && c >= 0 && c < d.Categories.Count ? d.Categories[c] : "")
            : formatX(d.X[i]);

        var gap = cellWidth > 6 && rowHeight > 6 ? 1f : 0f;
        var cellNodes = new List<Node>();
        var labelNodes = new List<Node>();
        var hitTest = new HitTestIndex();
        var tooltipSeries = new List<TooltipSeries>();
        styles.Add("heat-label", new TextStyleDef { FontSize = (float)Math.Max(9, font - 1), Fill = ThemeTokens.Text });
        styles.Add("heat-label-inverse", new TextStyleDef { FontSize = (float)Math.Max(9, font - 1), FontWeight = 500, Fill = ThemeTokens.DataLabelInverse });
        for (var row = 0; row < rows.Count; row++)
        {
            var r = rows[row];
            var cy = plot.Y + rowHeight * (row + 0.5);
            var points = new List<(float X, int Index)>();
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

                    var opacity = Opacity(v);
                    var style = styles.Add($"heat-{Math.Round(opacity, 2).ToString(CultureInfo.InvariantCulture)}",
                        new Style { Fill = color, FillOpacity = (float)Math.Round(opacity, 2) });
                    cellNodes.Add(new RectNode(new RectF((float)(px - cellWidth / 2 + gap / 2), (float)(cy - rowHeight / 2 + gap / 2),
                        (float)Math.Max(0.5, cellWidth - gap), (float)Math.Max(0.5, rowHeight - gap)), style) { CornerRadius = gap > 0 ? 2 : 0 });
                    points.Add(((float)px, i));

                    if (r.Spec.DataLabels == true && !compact)
                    {
                        var text = F(v);
                        if (measure.Measure(text, font - 1) + 4 <= cellWidth && rowHeight >= font + 2)
                        {
                            labelNodes.Add(new TextNode(text, new PointF((float)px, (float)cy), new TextStyle(opacity > 0.55 ? "heat-label-inverse" : "heat-label"),
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
                AddCategoryLabels(band, measure, font, plot.Bottom, xTicks);
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
            new GroupNode([new GroupNode(cellNodes) { Key = "series:heatmap", Class = "rio-series rio-series-heatmap" }]) { Class = "rio-plot" },
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

        var yScale = new LinearScale(rows.Count, 0, plot.Bottom, plot.Y);
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
            YScales = new Dictionary<string, LinearScale> { ["y"] = yScale },
            HasData = hasData,
        };
    }

    /// <summary>The color scale: lowest value, a few steps of the hue, highest value.</summary>
    private static GroupNode HeatLegend(double x, double y, double height, string color, string low, string high, Text.ITextMeasurer measure, double font,
        StyleTable styles, string label)
    {
        var cy = y + height / 2;
        var nodes = new List<Node>();
        nodes.Add(new TextNode(low, new PointF((float)x, (float)cy), new TextStyle("legend-label"), Anchor.Start));
        var sx = x + measure.Measure(low, font) + 8;
        const double step = 16;
        for (var i = 0; i < HeatLegendSteps; i++)
        {
            var opacity = HeatMinOpacity + (1 - HeatMinOpacity) * i / (HeatLegendSteps - 1);
            var style = styles.Add($"heat-{Math.Round(opacity, 2).ToString(CultureInfo.InvariantCulture)}", new Style { Fill = color, FillOpacity = (float)Math.Round(opacity, 2) });
            nodes.Add(new RectNode(new RectF((float)(sx + i * step), (float)(cy - 5), (float)(step - 2), 10), style) { CornerRadius = 2 });
        }

        nodes.Add(new TextNode(high, new PointF((float)(sx + HeatLegendSteps * step + 6), (float)cy), new TextStyle("legend-label"), Anchor.Start));
        return new GroupNode(nodes) { Class = "rio-legend rio-heat-legend", AriaLabel = $"{label}: {low} – {high}" };
    }

    /// <summary>"Chart "Title" with 7 series. Lowest 12 at Sun 04:00, highest 312 at Fri 20:00."</summary>
    private static string DescribeHeatmap(ChartSpec spec, List<Resolved> rows, Func<SeriesData, int, string> xLabel, Func<double, string> format,
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
