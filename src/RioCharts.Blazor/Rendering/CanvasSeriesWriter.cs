using RioCharts.Core.Scene;

namespace RioCharts.Blazor.Rendering;

/// <summary>
/// Turns the series groups of a scene into draw operations for the canvas layer: SVG path data
/// (which canvas reads with <c>Path2D</c>), rectangles and paint. Colors stay CSS (tokens become
/// <c>var(...)</c>); the JS side resolves them against the chart's styles.
/// </summary>
internal static class CanvasSeriesWriter
{
    internal static CanvasModel Build(Scene scene)
    {
        var plot = FindPlot(scene.Series);
        var ops = new List<CanvasOp>();
        RectF? clip = plot?.Clip;
        if (plot is not null)
        {
            foreach (var group in plot.Children.OfType<GroupNode>().Where(IsSeries))
            {
                Add(group, scene.Styles, ops, null);
            }
        }

        return new CanvasModel(clip is { } c ? [c.X, c.Y, c.Width, c.Height] : null, ops);
    }

    /// <summary>Series groups (keyed "series:…") are drawn on the canvas instead of in SVG.</summary>
    public static bool IsSeries(GroupNode group) => group.Key?.StartsWith("series:", StringComparison.Ordinal) == true;

    private static GroupNode? FindPlot(Node node) => node switch
    {
        GroupNode { Class: "rio-plot" } g => g,
        GroupNode g => g.Children.Select(FindPlot).FirstOrDefault(p => p is not null),
        _ => null,
    };

    // clip: the nearest enclosing group's clip (e.g. a line's color band), drawn with each shape.
    private static void Add(Node node, StyleTable styles, List<CanvasOp> ops, float[]? clip)
    {
        switch (node)
        {
            case GroupNode g:
                var inner = g.Clip is { } c ? new[] { c.X, c.Y, c.Width, c.Height } : clip;
                foreach (var child in g.Children)
                {
                    Add(child, styles, ops, inner);
                }

                break;
            case PathNode p:
                ops.Add(Paint(new CanvasShape("p", D: SvgWriter.PathD(p.Path)), styles.Get(p.Style)) with { Cl = clip });
                break;
            case RectNode r:
                ops.Add(Paint(new CanvasShape("r", X: r.Bounds.X, Y: r.Bounds.Y, W: r.Bounds.Width, H: r.Bounds.Height, Rx: r.CornerRadius), styles.Get(r.Style)) with { Cl = clip });
                break;
        }
    }

    private static CanvasOp Paint(CanvasShape shape, Style s) => new(
        shape,
        s.Fill is null ? null : SvgWriter.Color(s.Fill),
        s.FillOpacity,
        s.Stroke is null ? null : SvgWriter.Color(s.Stroke),
        s.StrokeWidth ?? 1,
        s.DashArray,
        s.Opacity);
}
