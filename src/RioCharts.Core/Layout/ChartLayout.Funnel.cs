using System.Globalization;
using RioCharts.Core.Data;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;

namespace RioCharts.Core.Layout;

internal static partial class ChartLayout
{
    /// <summary>The most stages a funnel draws.</summary>
    private const int MaxFunnelStages = 40;

    /// <summary>
    /// A funnel: one segment per stage, top to bottom in input order, taller and wider for a larger
    /// stage and narrowing to the next stage's width, in a palette color of its own. The value and
    /// its share of the stage before are written inside (or just after a thin segment); the stage's
    /// name sits in a column on the left, joined to its segment by a thin line. One funnel per chart.
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

        // ---------- Plot: stage names in a column on the left, the funnel beside them ----------
        var plotLeft = compact ? 1 : PadLeft;
        var plotRight = compact ? width - 1 : width - PadRight;
        var plotTop = compact ? 1 : y0;
        var plotBottom = compact ? height - 1 : height - PadBottom;
        var plot = new RectF((float)plotLeft, (float)plotTop, (float)Math.Max(2, plotRight - plotLeft), (float)Math.Max(2, plotBottom - plotTop));

        var labels = stages.Select(LabelOf).ToList();
        // Each stage needs room for its name; when they do not all fit (many stages, a low chart), the stages share
        // the height without names, which the tooltips and the description still give.
        var labelFloor = Math.Max(10, font * 1.3 + 4);
        var fits = stages.Count * labelFloor <= plot.Height;
        var showLabels = !compact && fits && chrome.YLabels && labels.Any(l => l.Length > 0);
        const double Leader = 16;
        var shown = labels.Select(l => Shorten(l, plot.Width * 0.3, measure, font)).ToList();
        var nameWidth = showLabels ? shown.Max(l => measure.Measure(l, font)) : 0;
        var funnelLeft = plot.X + (showLabels ? nameWidth + Leader : 0);
        var funnelWidth = Math.Max(2, plot.Right - funnelLeft);

        // Heights follow the values: each stage gets a minimum (room for its name), and the rest
        // of the funnel's height is shared in proportion to the stages' values.
        var n = stages.Count;
        var floor = n == 0 ? 0 : compact || !fits ? Math.Min(2, plot.Height / n) : labelFloor;
        var total = n == 0 ? 0 : Math.Max(n * floor, Math.Min(plot.Height, n * (compact ? 1e4 : 72)));
        var gap = n == 0 ? 0 : compact ? Math.Min(1, total / n * 0.1) : Math.Clamp(total / n * 0.08, 2, 5);
        var sum = stages.Sum(i => Math.Max(0, r0!.Data.Y[i]));
        var heights = stages.Select(i => floor + (total - n * floor) * (sum > 0 ? Math.Max(0, r0!.Data.Y[i]) / sum : 1.0 / n)).ToArray();
        var top = plot.Y + Math.Max(0, (plot.Height - total) / 2);
        var largest = stages.Count == 0 ? 0 : stages.Max(i => Math.Max(0, r0!.Data.Y[i]));
        var widthScale = new ContinuousScale(0, largest > 0 ? largest : 1, 0, funnelWidth);
        styles.Add("funnel-leader", new Style { Stroke = ThemeTokens.Axis, StrokeWidth = 1 });


        var seriesNodes = new List<Node>();
        var labelNodes = new List<Node>();
        var calloutNodes = new List<Node>();
        var selectedMarks = new List<Node>();
        var hitTest = new HitTestIndex();
        var tooltipSeries = new List<TooltipSeries>();
        var described = new List<string>();
        if (r0 is not null && hasData)
        {
            var s = r0.Spec;
            var data = r0.Data;
            var cx = funnelLeft + funnelWidth / 2;
            var color = s.Color is null ? ThemeTokens.Series(0) : r0.Color;
            var selected = options.SelectedPoints is { } picks && picks.TryGetValue(s.Id, out var picked) && picked.Count > 0 ? picked : null;
            string Format(double v) => ValueFormatter.Format(v, culture, s.Format, compactFrom: 1e6);
            var first = Math.Max(0, data.Y[stages[0]]);
            string? Share(double v, double of) => of > 0 ? FunnelPercent(Math.Max(0, v) / of, culture) : null;
            double WidthOf(int k) => data.Y[stages[k]] is var v && v > 0 ? Math.Max(widthScale.Map(v), 2) : 0;

            var nodes = new List<Node>();
            var hits = new List<(float X, float Y, int Index)>();
            var widths = new List<float>();
            var tipLabels = new List<string>();
            var tipValues = new List<string>();
            var parts = new List<string[]>();
            var colors = new List<string>();
            var before = double.NaN;
            var y = top;
            var cellHeights = new List<float>();
            for (var k = 0; k < stages.Count; k++)
            {
                var i = stages[k];
                var v = data.Y[i];
                var y1 = y;
                var y2 = y1 + Math.Max(1, heights[k] - gap);
                y += heights[k];
                var cy = (y1 + y2) / 2;

                // Narrows to the next stage, never widens: a larger later stage is a wider segment of its own.
                var wTop = WidthOf(k);
                var wBottom = k + 1 < stages.Count ? Math.Min(wTop, WidthOf(k + 1)) : wTop;

                // Stages are told apart by color, as in the usual funnel; a series color makes it one color.
                var stageColor = data.Colors?[i] ?? (s.Color is null ? ThemeTokens.Series(k) : color);
                PathData Segment() => new PathBuilder().MoveTo(cx - wTop / 2, y1).LineTo(cx + wTop / 2, y1).LineTo(cx + wBottom / 2, y2).LineTo(cx - wBottom / 2, y2).Close().Build();
                if (wTop > 0)
                {
                    var fill = styles.Add("funnel-stage-" + stageColor, new Style { Fill = stageColor });
                    nodes.Add(new PathNode(Segment(), fill) { Class = "rio-funnel-stage" });
                    if (selected?.Contains(i) == true)
                    {
                        var outline = styles.Add("selected-funnel", new Style { Stroke = ThemeTokens.Text, StrokeWidth = 1.5f, Fill = "none" });
                        selectedMarks.Add(new PathNode(Segment(), outline) { Class = "rio-selected-bar" });
                    }
                }

                var valueText = Format(v);
                var ofFirst = k == 0 ? null : Share(v, first);
                var ofPrevious = k == 0 ? FunnelPercent(1, culture) : Share(v, before);
                var text = ofPrevious is null ? valueText : $"{valueText} ({ofPrevious})";


                // The value inside the segment when it fits at the text's lower edge, else just after it.
                var edge = (wTop + wBottom) / 4; // the segment's side at mid-height
                if (s.DataLabels != false && !compact)
                {
                    var textWidth = measure.Measure(text, small);
                    var t = Math.Clamp(0.5 + small * 0.65 / Math.Max(1, y2 - y1), 0, 1);
                    var room = wTop + (wBottom - wTop) * t;
                    if (textWidth + 10 <= room && y2 - y1 >= small + 2)
                    {
                        // Dark text on the palette's light colors, white on the others.
                        var ink = ThemeTokens.IsLightSeries(stageColor) ? "data-label-dark" : "data-label-inverse";
                        labelNodes.Add(new TextNode(text, new PointF((float)cx, (float)cy), new TextStyle(ink), Anchor.Middle) { Class = "rio-data-label" });
                    }
                    else if (cx + edge + 6 + textWidth <= plot.Right)
                    {
                        labelNodes.Add(new TextNode(text, new PointF((float)(cx + edge + 6), (float)cy), new TextStyle("data-label"), Anchor.Start) { Class = "rio-data-label" });
                    }
                }

                // The stage's name in the column on the left, with a line to its segment.
                var label = labels[k];
                if (showLabels && label.Length > 0)
                {
                    var nameRight = plot.X + nameWidth;
                    calloutNodes.Add(new TextNode(shown[k], new PointF((float)nameRight, (float)cy), new TextStyle("axis-label"), Anchor.End) { Class = "rio-row-label" });
                    var lineEnd = cx - edge - 3;
                    if (lineEnd - (nameRight + 4) >= 4)
                    {
                        calloutNodes.Add(new PathNode(new PathBuilder().MoveTo(nameRight + 4, cy).LineTo(lineEnd, cy).Build(), new StyleRef("funnel-leader"))
                        {
                            Class = "rio-funnel-leader",
                        });
                    }
                }


                hits.Add(((float)cx, (float)cy, i));
                widths.Add((float)Math.Max(Math.Max(wTop, wBottom), 24)); // a thin stage is still easy to hover
                cellHeights.Add((float)(y2 - y1));
                tipLabels.Add(label.Length > 0 ? label : s.DisplayName);
                tipValues.Add(valueText);
                parts.Add([ofFirst ?? "", k == 0 ? "" : ofPrevious ?? ""]);
                colors.Add(stageColor);
                described.Add($"{(label.Length > 0 ? label + " " : "")}{valueText}{(k == 0 || ofPrevious is null ? "" : $" ({ofPrevious})")}");
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
                CellHeight = cellHeights.Count > 0 ? cellHeights.Max() : 0,
                CellHeights = [.. cellHeights],
                CellWidths = [.. widths],
                PartLabels = [texts.FunnelOfFirst, texts.FunnelOfPrevious],
                PartValues = [.. parts],
                PointColors = colors.Any(c => c != color) ? [.. colors] : null,
            });
        }

        if (calloutNodes.Count > 0)
        {
            statics.Add(new GroupNode(calloutNodes) { Class = "rio-axis-y", AriaLabel = texts.YAxis });
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
            ZoomsX = false,
            YScale = yScale,
            YScales = new Dictionary<string, ContinuousScale> { ["y"] = yScale },
            HasData = hasData,
        };
    }

    /// <summary>A funnel share with one decimal ("66.7%", "100%"); "&lt;0.1%" for a tiny share above zero.</summary>
    private static string FunnelPercent(double share, CultureInfo culture) =>
        share > 0 && share < 0.0005 ? "<" + 0.1.ToString("0.0", culture) + "%" : (share * 100).ToString("0.#", culture) + "%";
}
