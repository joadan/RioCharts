using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;

namespace RioCharts.Core.Layout;

public static partial class ChartLayout
{
    /// <summary>The most stages a funnel draws.</summary>
    private const int MaxFunnelStages = 40;

    /// <summary>
    /// A funnel: one row per stage, top to bottom in input order, labeled on the left. Each stage is
    /// a bar centered in the plot, as wide as its share of the largest stage, with a light band to
    /// the next bar showing what is lost between them. Labels give each value and its share of the
    /// first stage; the tooltip adds the share of the stage before. One funnel series per chart.
    /// </summary>
    private static ChartLayoutResult ComputeFunnel(ChartDefinition spec, List<Resolved> resolved, LayoutOptions options, List<string> warnings, StyleTable styles)
    {
        var culture = options.Culture;
        var texts = options.Texts ?? ChartTexts.For(culture);
        var measure = options.Measurer;
        var compact = spec.Compact || AutoCompacts(spec, options);
        var width = Math.Max(compact ? 8 : 50, options.Width);
        var height = Math.Max(compact ? 8 : 50, options.Height);
        var font = options.FontSize;
        var small = Math.Max(9, font - 1);
        var chrome = FitChrome(spec.AutoCompact && !compact, width, height, font);

        foreach (var r in resolved.Where(r => r.Spec.Type != SeriesType.Funnel))
        {
            warnings.Add($"series '{r.Spec.Id}': {r.Spec.Type} series cannot be mixed with a funnel; the series is skipped.");
        }

        if (spec.Annotations.Count > 0)
        {
            warnings.Add("funnels show no annotations; the annotations are skipped.");
        }

        var funnels = resolved.Where(r => r.Spec.Type == SeriesType.Funnel && r.Visible).ToList();
        foreach (var extra in funnels.Skip(1))
        {
            warnings.Add($"series '{extra.Spec.Id}': a chart shows one funnel; the series is skipped (use a chart per funnel).");
        }

        // ---------- Stages, in input order ----------
        var r0 = funnels.FirstOrDefault();
        var stages = r0 is null ? [] : Enumerable.Range(0, r0.Data.Count).Where(i => double.IsFinite(r0.Data.Y[i])).OrderBy(r0.Data.ToSourceIndex).ToList();
        if (stages.Count > MaxFunnelStages)
        {
            warnings.Add($"{stages.Count} funnel stages are too many to show; the first {MaxFunnelStages} are drawn.");
            stages = stages.Take(MaxFunnelStages).ToList();
        }

        if (r0 is not null && stages.Any(i => r0.Data.Y[i] < 0))
        {
            warnings.Add($"series '{r0.Spec.Id}': a funnel stage cannot be below zero; negative values are drawn as zero.");
        }

        var hasData = stages.Count > 0;
        string LabelOf(int i)
        {
            var data = r0!.Data;
            return data.Kind == XKind.Category
                ? ((int)data.X[i] is var c && c >= 0 && c < data.Categories.Count ? data.Categories[c] : "")
                : ValueFormatter.ForX(data.Kind, data.X, culture)(data.X[i]);
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

        // ---------- Plot: stage names on the left ----------
        var labels = stages.Select(LabelOf).ToList();
        var showLabels = !compact && chrome.YLabels && labels.Any(l => l.Length > 0);
        var shown = labels.Select(l => Shorten(l, width * 0.3, measure, font)).ToList();
        var labelWidth = showLabels ? shown.Max(l => measure.Measure(l, font)) : 0;
        var plotLeft = compact ? 1 : PadLeft + (showLabels ? labelWidth + LabelGap : 0);
        var plotRight = compact ? width - 1 : width - PadRight;
        var plotTop = compact ? 1 : y0;
        var plotBottom = compact ? height - 1 : height - PadBottom;
        var plot = new RectF((float)plotLeft, (float)plotTop, (float)Math.Max(2, plotRight - plotLeft), (float)Math.Max(2, plotBottom - plotTop));

        var rowHeight = stages.Count == 0 ? 0 : Math.Clamp(plot.Height / stages.Count, compact ? 2 : 10, compact ? 1e4 : 64);
        var barHeight = Math.Max(2, rowHeight * (compact ? 0.8 : 0.72));
        var top = plot.Y + Math.Max(0, (plot.Height - stages.Count * rowHeight) / 2);
        var largest = stages.Count == 0 ? 0 : stages.Max(i => Math.Max(0, r0!.Data.Y[i]));
        var widthScale = new ContinuousScale(0, largest > 0 ? largest : 1, 0, plot.Width);

        var seriesNodes = new List<Node>();
        var labelNodes = new List<Node>();
        var rowLabelNodes = new List<Node>();
        var selectedMarks = new List<Node>();
        var hitTest = new HitTestIndex();
        var tooltipSeries = new List<TooltipSeries>();
        var described = new List<string>();
        if (r0 is not null && hasData)
        {
            var s = r0.Spec;
            var data = r0.Data;
            // Stages are labeled, not told apart by color: the first palette color unless the series sets its own.
            var color = s.Color is null ? ThemeTokens.Series(0) : r0.Color;
            var neckStyle = styles.Add("funnel-neck-" + color, new Style { Fill = color, FillOpacity = 0.15f });
            var selected = options.SelectedPoints is { } picks && picks.TryGetValue(s.Id, out var picked) && picked.Count > 0 ? picked : null;
            string Format(double v) => ValueFormatter.Format(v, culture, s.Format, compactFrom: 1e6);
            var first = Math.Max(0, data.Y[stages[0]]);
            string? Share(double v, double of) => of > 0 ? Percent(Math.Max(0, v) / of, culture) : null;

            var nodes = new List<Node>();
            var hits = new List<(float X, float Y, int Index)>();
            var widths = new List<float>();
            var tipLabels = new List<string>();
            var tipValues = new List<string>();
            var parts = new List<string[]>();
            var colors = new List<string>();
            (double Left, double Right, double Bottom)? previous = null;
            var before = double.NaN;
            for (var k = 0; k < stages.Count; k++)
            {
                var i = stages[k];
                var v = data.Y[i];
                var cy = top + rowHeight * (k + 0.5);
                var w = widthScale.Map(Math.Max(0, v));
                if (v > 0)
                {
                    w = Math.Max(w, 2); // a small stage stays visible
                }

                var left = plot.CenterX - w / 2;
                var barTop = cy - barHeight / 2;

                // The band from the bar above narrows down to this one.
                if (previous is { } p && !compact)
                {
                    var neck = new PathBuilder().MoveTo(p.Left, p.Bottom).LineTo(p.Right, p.Bottom).LineTo(left + w, barTop).LineTo(left, barTop).Close().Build();
                    nodes.Add(new PathNode(neck, neckStyle) { Class = "rio-funnel-neck" });
                }

                var pointColor = data.Colors?[i] ?? color;
                if (w > 0)
                {
                    var barStyle = styles.Add("funnel-bar-" + pointColor, new Style { Fill = pointColor });
                    nodes.Add(new RectNode(new RectF((float)left, (float)barTop, (float)w, (float)barHeight), barStyle) { CornerRadius = 2, Class = "rio-funnel-bar" });
                }

                if (selected?.Contains(i) == true)
                {
                    var outline = styles.Add("selected-bar", new Style { Stroke = ThemeTokens.Text, StrokeWidth = 1.5f });
                    selectedMarks.Add(new RectNode(new RectF((float)(left - 1.5), (float)(barTop - 1.5), (float)(w + 3), (float)(barHeight + 3)), outline)
                    {
                        CornerRadius = 3,
                        Class = "rio-selected-bar",
                    });
                }

                previous = (left, left + w, barTop + barHeight);
                var label = labels[k];
                if (showLabels && label.Length > 0)
                {
                    rowLabelNodes.Add(new TextNode(shown[k], new PointF((float)(plot.X - LabelGap), (float)cy), new TextStyle("axis-label"), Anchor.End) { Class = "rio-row-label" });
                }

                var valueText = Format(v);
                var ofFirst = k == 0 ? null : Share(v, first);
                var ofPrevious = k == 0 ? null : Share(v, before);
                if (s.DataLabels != false && !compact)
                {
                    // Inside the bar when it fits, else beside it, else left out.
                    var text = ofFirst is null ? valueText : $"{valueText} ({ofFirst})";
                    var textWidth = measure.Measure(text, small);
                    if (textWidth + 8 <= w && barHeight >= small + 2)
                    {
                        labelNodes.Add(new TextNode(text, new PointF((float)plot.CenterX, (float)cy), new TextStyle("data-label-inverse"), Anchor.Middle) { Class = "rio-data-label" });
                    }
                    else if (left + w + 4 + textWidth <= plot.Right)
                    {
                        labelNodes.Add(new TextNode(text, new PointF((float)(left + w + 4), (float)cy), new TextStyle("data-label"), Anchor.Start) { Class = "rio-data-label" });
                    }
                }

                hits.Add(((float)plot.CenterX, (float)cy, i));
                widths.Add((float)Math.Max(w, 24)); // a thin stage is still easy to hover
                tipLabels.Add(label.Length > 0 ? label : s.DisplayName);
                tipValues.Add(valueText);
                parts.Add([ofFirst ?? "", ofPrevious ?? ""]);
                colors.Add(pointColor);
                described.Add($"{(label.Length > 0 ? label + " " : "")}{valueText}{(ofFirst is null ? "" : $" ({ofFirst})")}");
                before = Math.Max(0, v);
            }

            seriesNodes.Add(new GroupNode(nodes)
            {
                Key = "series:" + s.Id,
                Class = "rio-series rio-series-funnel",
                AriaLabel = s.DisplayName + ": " + string.Join(", ", described),
            });

            var hitList = new HitTestIndex.SeriesHits(s.Id, [.. hits.Select(h => h.X)], [.. hits.Select(h => h.Y)], [.. hits.Select(h => h.Index)],
                [.. hits.Select(h => data.ToSourceIndex(h.Index))]);
            hitTest.Add(hitList);
            tooltipSeries.Add(new TooltipSeries(s.Id, s.DisplayName, color, SeriesType.Funnel, 0, 0, 0, hitList.X, hitList.Y, hitList.Index, [.. tipLabels], [.. tipValues])
            {
                CellHeight = (float)barHeight,
                CellWidths = [.. widths],
                PartLabels = [texts.FunnelOfFirst, texts.FunnelOfPrevious],
                PartValues = [.. parts],
                PointColors = colors.Any(c => c != color) ? [.. colors] : null,
            });
        }

        if (rowLabelNodes.Count > 0)
        {
            statics.Add(new GroupNode(rowLabelNodes) { Class = "rio-axis-y", AriaLabel = texts.YAxis });
        }

        var overlay = new List<Node>();
        if (!hasData && !compact)
        {
            overlay.Add(new TextNode(EmptyMessage(resolved.Where(r => r.Spec.Type == SeriesType.Funnel).ToList(), texts), new PointF(plot.CenterX, plot.CenterY),
                new TextStyle("empty"), Anchor.Middle) { Class = "rio-empty" });
        }

        var plotNodes = new List<Node>(seriesNodes);
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

        var items = string.Join(", ", described.Take(8)) + (described.Count > 8 ? " " + texts.Fill(nameof(ChartTexts.More), culture, described.Count - 8) : "");
        var description = (string.IsNullOrWhiteSpace(spec.Title) ? "" : spec.Title + ". ")
            + (described.Count == 0
                ? texts.Fill(nameof(ChartTexts.PolarNoData), culture, texts.FunnelChart)
                : texts.FillCount(nameof(ChartTexts.PolarItems), described.Count, culture, texts.FunnelChart, described.Count, items) + ".");
        var scene = new Scene.Scene(new GroupNode(statics) { Class = "rio-static" }, seriesLayer, new GroupNode(overlay) { Class = "rio-overlay" })
        {
            Width = (float)width,
            Height = (float)height,
            Styles = styles,
            Description = description,
        };

        var xScale = new ContinuousScale(0, largest > 0 ? largest : 1, plot.X, plot.Right);
        var yScale = new ContinuousScale(Math.Max(1, stages.Count), 0, plot.Bottom, plot.Y);
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
