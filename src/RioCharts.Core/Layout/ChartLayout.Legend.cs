using RioCharts.Core.Scene;
using RioCharts.Core.Text;

namespace RioCharts.Core.Layout;

public static partial class ChartLayout
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
        LegendPosition position, double rightX, RectF plot, double top, double bottomTop, double lineHeight, StyleTable styles, string label)
    {
        var nodes = new List<Node>();
        var rowHeight = lineHeight + 4;
        if (position == LegendPosition.Right)
        {
            var y = (double)plot.Y;
            foreach (var (entry, w) in items)
            {
                nodes.Add(LegendItem(entry, rightX, y, w, rowHeight, styles));
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
                    nodes.Add(LegendItem(entry, x, y, w, rowHeight, styles));
                    x += w + ItemGap;
                }

                y += rowHeight;
            }
        }

        return new GroupNode(nodes) { Class = "rio-legend", AriaLabel = label };
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

    private static GroupNode LegendItem(LegendEntry entry, double x, double y, double width, double height, StyleTable styles)
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
            : entry.Type == SeriesType.Scatter
            ? new PathNode(new PathBuilder().MoveTo(x + SwatchWidth / 2, cy - 4.5).Arc(x + SwatchWidth / 2, cy, 4.5, 0, Math.PI * 2).Close().Build(),
                styles.Add("swatch-dot-" + entry.SeriesId, new Style { Fill = entry.Color })) { Class = "rio-swatch rio-swatch-dot" }
            : entry.Type == SeriesType.Candlestick
            ? CandleSwatch(entry, x, cy, styles)
            : new RectNode(new RectF((float)x, (float)(cy - 5), (float)SwatchWidth, 10), swatchStyle) { CornerRadius = 2, Class = "rio-swatch" };

        var state = !entry.Visible ? ", hidden" : entry.IsEmpty ? ", no data" : "";
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
            AriaLabel = entry.Label + state,
            Title = entry.Tooltip,
        };
    }
}
