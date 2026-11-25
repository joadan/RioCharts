using System.Globalization;
using RioCharts.Core.Scene;
using RioCharts.Core.Text;

namespace RioCharts.Core.Layout;

internal static partial class ChartLayout
{
    private const double SwatchWidth = 12;
    private const double SwatchGap = 6;
    private const double ItemGap = 16;

    private static List<(LegendEntry Entry, double Width)> MeasureLegend(List<LegendEntry> entries, ITextMeasurer measure, double font) =>
        entries.Select(e => (e, SwatchWidth + SwatchGap + measure.Measure(e.Label, font))).ToList();

    private static List<List<(LegendEntry Entry, double Width)>> WrapRows(List<(LegendEntry Entry, double Width)> items, double maxWidth)
    {
        var rows = new List<List<(LegendEntry, double)>>();
        var row = new List<(LegendEntry, double)>();
        double used = 0;
        foreach (var item in items)
        {
            var add = row.Count == 0 ? item.Width : item.Width + ItemGap;
            if (row.Count > 0 && used + add > maxWidth)
            {
                rows.Add(row);
                row = [];
                used = 0;
                add = item.Width;
            }

            row.Add(item);
            used += add;
        }

        if (row.Count > 0)
        {
            rows.Add(row);
        }

        return rows;
    }

    private static GroupNode BuildLegend(List<(LegendEntry Entry, double Width)> items, List<List<(LegendEntry Entry, double Width)>> rows,
        LegendPosition position, double rightX, RectF plot, double top, double bottomTop, double lineHeight, StyleTable styles, ChartTexts texts)
    {
        var nodes = new List<Node>();
        var rowHeight = lineHeight + 4;
        if (position == LegendPosition.Right)
        {
            var y = (double)plot.Y;
            foreach (var (entry, w) in items)
            {
                nodes.Add(LegendItem(entry, rightX, y, w, rowHeight, styles, texts));
                y += rowHeight;
            }
        }
        else
        {
            var y = position == LegendPosition.Top ? top : bottomTop;
            foreach (var row in rows)
            {
                double x = PadLeft;
                foreach (var (entry, w) in row)
                {
                    nodes.Add(LegendItem(entry, x, y, w, rowHeight, styles, texts));
                    x += w + ItemGap;
                }

                y += rowHeight;
            }
        }

        return new GroupNode(nodes) { Class = "rio-legend", AriaLabel = texts.Legend };
    }

    /// <summary>Candlesticks: a falling and a rising candle body, as drawn.</summary>
    private static GroupNode CandleSwatch(LegendEntry entry, double x, double cy, StyleTable styles)
    {
        var w = (SwatchWidth - 3) / 2;
        var (up, down) = entry.DirectionColors ? (ThemeTokens.Positive, ThemeTokens.Negative) : (entry.Color, entry.Color);
        return new GroupNode(
        [
            new RectNode(new RectF((float)x, (float)(cy - 5), (float)w, 10), styles.Add($"candle-down-{down}", Series.CandlestickSeriesType.DownStyle(down))),
            new RectNode(new RectF((float)(x + w + 3), (float)(cy - 4), (float)w, 8), styles.Add($"candle-up-{up}", Series.CandlestickSeriesType.UpStyle(up))),
        ])
        {
            Class = "rio-swatch rio-swatch-candle",
        };
    }

    /// <summary>Waterfalls: a rising and a falling bar, in the positive and negative colors, as drawn.</summary>
    private static GroupNode WaterfallSwatch(double x, double cy, StyleTable styles)
    {
        var w = (SwatchWidth - 3) / 2;
        return new GroupNode(
        [
            new RectNode(new RectF((float)x, (float)(cy - 5), (float)w, 7), styles.Add($"bar-{ThemeTokens.Positive}", new Style { Fill = ThemeTokens.Positive })) { CornerRadius = 1 },
            new RectNode(new RectF((float)(x + w + 3), (float)(cy - 2), (float)w, 7), styles.Add($"bar-{ThemeTokens.Negative}", new Style { Fill = ThemeTokens.Negative })) { CornerRadius = 1 },
        ])
        {
            Class = "rio-swatch rio-swatch-waterfall",
        };
    }

    /// <summary>A small dumbbell: a hollow dot, a line and a filled dot, so the legend says which end is which.</summary>
    private static GroupNode DumbbellSwatch(LegendEntry entry, double x, double cy, StyleTable styles)
    {
        var from = new PathBuilder();
        Series.DumbbellSeriesType.Dot(from, x + 3.5, cy, 2.75);
        var to = new PathBuilder();
        Series.DumbbellSeriesType.Dot(to, x + SwatchWidth - 3.5, cy, 3.5);
        return new GroupNode(
        [
            new PathNode(new PathBuilder().MoveTo(x + 6, cy).LineTo(x + SwatchWidth - 6, cy).Build(),
                styles.Add("swatch-line-" + entry.SeriesId, new Style { Stroke = entry.Color, StrokeWidth = 2, Opacity = 0.45f })),
            new PathNode(from.Build(), styles.Add("swatch-from-" + entry.SeriesId, new Style { Fill = ThemeTokens.Surface, Stroke = entry.Color, StrokeWidth = 1.5f })),
            new PathNode(to.Build(), styles.Add("swatch-dot-" + entry.SeriesId, new Style { Fill = entry.Color })),
        ])
        {
            Class = "rio-swatch rio-swatch-dumbbell",
        };
    }

    private static GroupNode LegendItem(LegendEntry entry, double x, double y, double width, double height, StyleTable styles, ChartTexts texts)
    {
        var cy = y + height / 2;
        var swatchStyle = styles.Add("swatch-" + entry.SeriesId, entry.Type switch
        {
            SeriesType.RangeArea => new Style { Fill = entry.Color, FillOpacity = 0.35f }, // like the band
            SeriesType.Radar => new Style { Fill = entry.Color, FillOpacity = 0.25f, Stroke = entry.Color, StrokeWidth = 1.5f }, // like the shape
            _ => new Style { Fill = entry.Color, Stroke = entry.Color, StrokeWidth = 2 },
        });
        Node swatch = entry.Type == SeriesType.Line
            ? new PathNode(new PathBuilder().MoveTo(x, cy).LineTo(x + SwatchWidth, cy).Build(), swatchStyle) { Class = "rio-swatch rio-swatch-line" }
            : entry.Type is SeriesType.Scatter or SeriesType.MapBubble
            ? new PathNode(new PathBuilder().MoveTo(x + SwatchWidth / 2, cy - 4.5).Arc(x + SwatchWidth / 2, cy, 4.5, 0, Math.PI * 2).Close().Build(),
                styles.Add("swatch-dot-" + entry.SeriesId, new Style { Fill = entry.Color })) { Class = "rio-swatch rio-swatch-dot" }
            : entry.Type == SeriesType.Candlestick
            ? CandleSwatch(entry, x, cy, styles)
            : entry.Type == SeriesType.Dumbbell
            ? DumbbellSwatch(entry, x, cy, styles)
            : entry.Type == SeriesType.Waterfall
            ? WaterfallSwatch(x, cy, styles)
            : new RectNode(new RectF((float)x, (float)(cy - 5), (float)SwatchWidth, 10), swatchStyle) { CornerRadius = 2, Class = "rio-swatch" };

        var name = !entry.Visible ? texts.Fill(nameof(ChartTexts.LegendHidden), CultureInfo.InvariantCulture, entry.Label)
            : entry.IsEmpty ? texts.Fill(nameof(ChartTexts.LegendNoData), CultureInfo.InvariantCulture, entry.Label) : entry.Label;
        return new GroupNode(
        [
            new RectNode(new RectF((float)(x - 3), (float)y, (float)(width + 6), (float)height), new StyleRef("legend-hit")) { CornerRadius = 3, Class = "rio-legend-hit" },
            swatch,
            new TextNode(entry.Label, new PointF((float)(x + SwatchWidth + SwatchGap), (float)cy), new TextStyle("legend-label"), Anchor.Start),
        ])
        {
            Key = "legend:" + entry.SeriesId,
            Class = "rio-legend-item" + (entry.Visible ? "" : " rio-legend-off") + (entry.IsEmpty ? " rio-legend-empty" : ""),
            Interaction = new NodeInteraction("legend", entry.SeriesId) { Pressed = entry.Visible },
            AriaLabel = name,
            Title = entry.Tooltip,
        };
    }
}
