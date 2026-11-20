using RioCharts.Core.Scene;

namespace RioCharts.Blazor.Rendering;

/// <summary>
/// Turns the series groups of a scene into draw operations for the canvas layer: SVG path data
/// (which canvas reads with <c>Path2D</c>), rectangles and paint. Colors stay CSS (tokens become
/// <c>var(...)</c>); the JS side resolves them against the chart's styles.
/// </summary>
public static class CanvasSeriesWriter
{
    public static object Build(Scene scene)
    {
        var plot = FindPlot(scene.Series);
        var ops = new List<object>();
        RectF? clip = plot?.Clip;
        if (plot is not null)
        {
            foreach (var group in plot.Children.OfType<GroupNode>().Where(IsSeries))
            {
                Add(group, scene.Styles, ops);
            }
        }

        return new
        {
            clip = clip is { } c ? new[] { c.X, c.Y, c.Width, c.Height } : null,
            ops,
        };
    }

    /// <summary>Series groups (keyed "series:…") are drawn on the canvas instead of in SVG.</summary>
    public static bool IsSeries(GroupNode group) => group.Key?.StartsWith("series:", StringComparison.Ordinal) == true;

    private static GroupNode? FindPlot(Node node) => node switch
    {
        GroupNode { Class: "rio-plot" } g => g,
        GroupNode g => g.Children.Select(FindPlot).FirstOrDefault(p => p is not null),
        _ => null,
    };

    private static void Add(Node node, StyleTable styles, List<object> ops)
    {
        switch (node)
        {
            case GroupNode g:
                foreach (var child in g.Children)
                {
                    Add(child, styles, ops);
                }

                break;
            case PathNode p:
                ops.Add(Paint(new { t = "p", d = SvgWriter.PathD(p.Path) }, styles.Get(p.Style)));
                break;
            case RectNode r:
                ops.Add(Paint(new { t = "r", x = r.Bounds.X, y = r.Bounds.Y, w = r.Bounds.Width, h = r.Bounds.Height, rx = r.CornerRadius }, styles.Get(r.Style)));
                break;
        }
    }

    private static object Paint(object shape, Style s) => new
    {
        shape,
        fill = s.Fill is null ? null : SvgWriter.Color(s.Fill),
        fo = s.FillOpacity,
        stroke = s.Stroke is null ? null : SvgWriter.Color(s.Stroke),
        sw = s.StrokeWidth ?? 1,
        dash = s.DashArray,
        op = s.Opacity,
    };
}
