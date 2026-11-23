namespace RioCharts.Core.Scene;

/// <summary>A drawing primitive. Core emits these; renderers turn them into SVG, canvas calls, etc.</summary>
public abstract record Node
{
    /// <summary>Optional semantic class (e.g. "rio-grid"), useful to renderers and for styling.</summary>
    public string? Class { get; init; }

    /// <summary>Stable identity for diffing renderers (e.g. "series:revenue").</summary>
    public string? Key { get; init; }
}

/// <summary>A path (lines, curves, arcs) drawn with a paint style.</summary>
/// <param name="Path">The geometry, in pixels.</param>
/// <param name="Style">Fill and stroke, looked up in <see cref="Scene.Styles"/>.</param>
public sealed record PathNode(PathData Path, StyleRef Style) : Node;

/// <summary>An axis-aligned rectangle drawn with a paint style.</summary>
/// <param name="Bounds">Position and size in pixels.</param>
/// <param name="Style">Fill and stroke, looked up in <see cref="Scene.Styles"/>.</param>
public sealed record RectNode(RectF Bounds, StyleRef Style) : Node
{
    /// <summary>Corner radius in pixels; 0 (the default) for square corners.</summary>
    public float CornerRadius { get; init; }
}

/// <summary>Horizontal text alignment relative to the anchor point.</summary>
public enum Anchor
{
    /// <summary>Text starts at the point.</summary>
    Start,

    /// <summary>Text is centered on the point.</summary>
    Middle,

    /// <summary>Text ends at the point.</summary>
    End,
}

/// <summary>Vertical text alignment relative to the anchor point.</summary>
public enum Baseline
{
    /// <summary>Top of the text at the point.</summary>
    Top,

    /// <summary>Text vertically centered on the point.</summary>
    Middle,

    /// <summary>Bottom of the text at the point.</summary>
    Bottom,
}

/// <summary>A single line of text.</summary>
/// <param name="Text">The text to draw.</param>
/// <param name="At">Anchor point in pixels.</param>
/// <param name="Style">Font and color, looked up in <see cref="Scene.Styles"/>.</param>
/// <param name="Anchor">Horizontal alignment relative to <paramref name="At"/>.</param>
public sealed record TextNode(string Text, PointF At, TextStyle Style, Anchor Anchor) : Node
{
    /// <summary>Vertical alignment relative to <see cref="At"/>. Default <see cref="Baseline.Middle"/>.</summary>
    public Baseline Baseline { get; init; } = Baseline.Middle;

    /// <summary>Rotation in degrees around <see cref="TextNode.At"/>.</summary>
    public float Rotation { get; init; }
}

/// <summary>A group of nodes with optional clipping and transform.</summary>
/// <param name="Children">The nodes in the group, drawn in order.</param>
/// <param name="Clip">Clip rectangle in pixels; null for no clipping.</param>
/// <param name="Transform">Transform applied to the children; null for none.</param>
public sealed record GroupNode(IReadOnlyList<Node> Children, RectF? Clip = null, Matrix? Transform = null) : Node
{
    /// <summary>A group with no children.</summary>
    public static GroupNode Empty { get; } = new([]);

    /// <summary>Set when the group is interactive (legend items, later points and annotations).</summary>
    public NodeInteraction? Interaction { get; init; }

    /// <summary>Accessible name for the group.</summary>
    public string? AriaLabel { get; init; }

    /// <summary>Hover text (e.g. an annotation's label and value); SVG renders it as &lt;title&gt;.</summary>
    public string? Title { get; init; }
}

/// <summary>What happens when an interactive group is activated.</summary>
/// <param name="Kind">e.g. "legend".</param>
/// <param name="Id">e.g. the series id.</param>
public sealed record NodeInteraction(string Kind, string Id)
{
    /// <summary>Toggle state for toggle buttons (legend items): true when the series is shown.</summary>
    public bool? Pressed { get; init; }

    /// <summary>
    /// False for buttons that take clicks but no keyboard focus (e.g. the countries without data on a
    /// map, so the keyboard does not stop at every one of them). Default true.
    /// </summary>
    public bool Focusable { get; init; } = true;
}

/// <summary>
/// The render-agnostic result of a layout pass, in three layers: <see cref="Static"/> (axes,
/// grid, legend), <see cref="Series"/> (data) and <see cref="Overlay"/> (crosshair, tooltip,
/// hover; on canvas only this layer redraws when the pointer moves).
/// </summary>
public sealed record Scene(Node Static, Node Series, Node Overlay)
{
    /// <summary>Width of the chart in pixels.</summary>
    public float Width { get; init; }

    /// <summary>Height of the chart in pixels.</summary>
    public float Height { get; init; }

    /// <summary>The paint and text styles that the nodes reference by name.</summary>
    public StyleTable Styles { get; init; } = new();

    /// <summary>Text alternative describing the chart (for screen readers and agents).</summary>
    public string Description { get; init; } = "";
}
