namespace RioCharts.Core.Scene;

/// <summary>A drawing primitive. Core emits these; renderers turn them into SVG, canvas calls, etc.</summary>
public abstract record Node
{
    /// <summary>Optional semantic class (e.g. "rio-grid"), useful to renderers and for styling.</summary>
    public string? Class { get; init; }

    /// <summary>Stable identity for diffing renderers (e.g. "series:revenue").</summary>
    public string? Key { get; init; }
}

public sealed record PathNode(PathData Path, StyleRef Style) : Node;

public sealed record RectNode(RectF Bounds, StyleRef Style) : Node
{
    public float CornerRadius { get; init; }
}

public enum Anchor { Start, Middle, End }

public enum Baseline { Top, Middle, Bottom }

public sealed record TextNode(string Text, PointF At, TextStyle Style, Anchor Anchor) : Node
{
    public Baseline Baseline { get; init; } = Baseline.Middle;

    /// <summary>Rotation in degrees around <see cref="TextNode.At"/>.</summary>
    public float Rotation { get; init; }
}

public sealed record GroupNode(IReadOnlyList<Node> Children, RectF? Clip = null, Matrix? Transform = null) : Node
{
    public static GroupNode Empty { get; } = new([]);

    /// <summary>Set when the group is interactive (legend items, later points and annotations).</summary>
    public NodeInteraction? Interaction { get; init; }

    /// <summary>Accessible name for the group.</summary>
    public string? AriaLabel { get; init; }
}

/// <summary>What happens when an interactive group is activated.</summary>
/// <param name="Kind">e.g. "legend".</param>
/// <param name="Id">e.g. the series id.</param>
public sealed record NodeInteraction(string Kind, string Id)
{
    /// <summary>Toggle state for toggle buttons (legend items): true when the series is shown.</summary>
    public bool? Pressed { get; init; }
}

/// <summary>
/// The render-agnostic result of a layout pass, in three layers: <see cref="Static"/> (axes,
/// grid, legend), <see cref="Series"/> (data) and <see cref="Overlay"/> (crosshair, tooltip,
/// hover; on canvas only this layer redraws when the pointer moves).
/// </summary>
public sealed record Scene(Node Static, Node Series, Node Overlay)
{
    public float Width { get; init; }
    public float Height { get; init; }
    public StyleTable Styles { get; init; } = new();

    /// <summary>Text alternative describing the chart (for screen readers and agents).</summary>
    public string Description { get; init; } = "";
}
