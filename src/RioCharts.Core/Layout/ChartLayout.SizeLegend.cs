using System.Globalization;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scales;
using RioCharts.Core.Scene;
using RioCharts.Core.Series;
using RioCharts.Core.Text;

namespace RioCharts.Core.Layout;

internal static partial class ChartLayout
{
    /// <summary>Space between the circles and their values, and the length of the leader lines.</summary>
    private const double SizeLegendGap = 8;

    /// <summary>
    /// A bubble chart's size legend: nested circles at their real size for up to three round sizes,
    /// sharing their bottom, each with its value at its top, under the size's name.
    /// </summary>
    /// <param name="Title">What the size is (the first bubble series' size name).</param>
    /// <param name="Circles">Largest first: each size's radius in pixels and its formatted value.</param>
    private sealed record SizeLegendModel(string Title, IReadOnlyList<(double Radius, string Label)> Circles, double Width, double Height, double TitleHeight, double LineHeight);

    /// <summary>
    /// The size legend for the bubbles among <paramref name="drawable"/>, or null when there are none
    /// or it is turned off. By default it shows when the chart has room for a legend and is at least
    /// 360 px wide.
    /// </summary>
    private static SizeLegendModel? MeasureSizeLegend(ChartDefinition spec, List<Resolved> drawable, BubbleScale? bubbles, bool compact, bool room,
        double width, CultureInfo culture, ChartTexts texts, ITextMeasurer measure, double font, double lineHeight)
    {
        if (compact || bubbles is not { MaxSize: > 0 } scale || !(spec.Legend.Sizes ?? (room && width >= 360))
            || drawable.FirstOrDefault(r => r.Spec.Type == SeriesType.Scatter && r.Data.Sizes is not null) is not { } first)
        {
            return null;
        }

        // Round sizes: the largest 1, 2 or 5 × 10ⁿ at or below the largest size, a quarter and a sixteenth of it
        // (half and a quarter of its width). Too small to see, or labels on top of each other: left out.
        var largest = NiceAtOrBelow(scale.MaxSize);
        var circles = new List<(double Radius, string Label)>();
        var lastTop = double.NegativeInfinity;
        foreach (var value in new[] { largest, NiceAtOrBelow(largest / 4), NiceAtOrBelow(largest / 16) }.Distinct())
        {
            var radius = scale.Radius(first.Spec, value);
            if (!double.IsFinite(radius) || radius < 3 || (circles.Count > 0 && 2 * (circles[0].Radius - radius) - lastTop < font + 1))
            {
                continue;
            }

            circles.Add((radius, ValueFormatter.Format(value, culture, first.Spec.SizeFormat)));
            lastTop = 2 * (circles[0].Radius - radius); // its top, down from the largest circle's top
        }

        if (circles.Count == 0)
        {
            return null;
        }

        var title = string.IsNullOrWhiteSpace(first.Spec.SizeName) ? texts.Size : first.Spec.SizeName;
        var diameter = 2 * circles[0].Radius;
        var circlesWidth = diameter + SizeLegendGap + circles.Max(c => measure.Measure(c.Label, font));
        var titleHeight = lineHeight + font / 2 + 2; // the title's line, and room for the largest circle's value above its top
        return new SizeLegendModel(title, circles, Math.Max(circlesWidth, measure.Measure(title, font)), titleHeight + diameter + 2, titleHeight, lineHeight);
    }

    /// <summary>The largest 1, 2 or 5 × 10ⁿ at or below <paramref name="value"/> (positive).</summary>
    private static double NiceAtOrBelow(double value)
    {
        var power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var m = value / power;
        return NiceTicks.Clean((m >= 5 ? 5 : m >= 2 ? 2 : 1) * power);
    }

    /// <summary>Draws the size legend with its top left at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    private static GroupNode BuildSizeLegend(SizeLegendModel model, double x, double y, StyleTable styles)
    {
        var circleStyle = styles.Add("size-legend-circle", new Style { Fill = "none", Stroke = ThemeTokens.TextMuted, StrokeWidth = 1, Opacity = 0.8f });
        var leaderStyle = styles.Add("size-legend-leader", new Style { Fill = "none", Stroke = ThemeTokens.TextMuted, StrokeWidth = 1, Opacity = 0.5f, DashArray = "2 2" });
        var big = model.Circles[0].Radius;
        var (cx, bottom) = (x + big, y + model.TitleHeight + 2 * big);
        var labelX = x + 2 * big + SizeLegendGap;
        var nodes = new List<Node>
        {
            new TextNode(model.Title, new PointF((float)x, (float)(y + model.LineHeight / 2)), new TextStyle("legend-label"), Anchor.Start),
        };
        foreach (var (r, label) in model.Circles)
        {
            var top = bottom - 2 * r;
            nodes.Add(new PathNode(new PathBuilder().MoveTo(cx, top).Arc(cx, bottom - r, r, 0, Math.PI * 2).Close().Build(), circleStyle));
            nodes.Add(new PathNode(new PathBuilder().MoveTo(cx, top).LineTo(labelX - 3, top).Build(), leaderStyle));
            nodes.Add(new TextNode(label, new PointF((float)labelX, (float)top), new TextStyle("axis-label"), Anchor.Start));
        }

        return new GroupNode(nodes)
        {
            Class = "rio-size-legend",
            AriaLabel = $"{model.Title}: {string.Join(", ", model.Circles.Select(c => c.Label).Reverse())}",
        };
    }
}
