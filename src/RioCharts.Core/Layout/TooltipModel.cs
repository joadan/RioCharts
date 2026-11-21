using RioCharts.Core.Scene;

namespace RioCharts.Core.Layout;

/// <summary>
/// What the interactive layer needs for tooltips, crosshair and point clicks, computed once
/// per layout: pixel positions of the drawn points plus their formatted x and y values, or the
/// geometry of pie slices and radial bars. Renderers send it to the client once; pointer moves
/// are then handled locally.
/// </summary>
/// <param name="BandWidth">Width of the hover highlight for bars and category axes; 0 draws a crosshair line.</param>
public sealed record TooltipModel(RectF Plot, bool Enabled, bool Shared, float BandWidth, IReadOnlyList<TooltipSeries> Series)
{
    /// <summary>Pie slices and radial bars (polar charts); empty for cartesian charts.</summary>
    public IReadOnlyList<TooltipSlice> Slices { get; init; } = [];
}

/// <param name="Color">CSS color or theme token.</param>
/// <param name="Snap">How far (px) from the hovered x a point may be and still be listed in a shared tooltip.</param>
/// <param name="BarWidth">Bar width (0 for lines and areas).</param>
/// <param name="BarOffset">Offset of the bar center from <see cref="X"/>.</param>
/// <param name="X">Pixel x per point, ascending.</param>
/// <param name="Y">Pixel y per point (the top of the stack segment for stacked series).</param>
/// <param name="Index">Index into the series' data, per point.</param>
public sealed record TooltipSeries(string Id, string Name, string Color, SeriesType Type, float Snap, float BarWidth, float BarOffset,
    float[] X, float[] Y, int[] Index, string[] XLabels, string[] Values)
{
    /// <summary>
    /// A step line (step-after): its value at any x is that of the last point at or before x, so a
    /// shared tooltip shows the value in effect at the hovered x, not the nearest point's.
    /// </summary>
    public bool Step { get; init; }

    /// <summary>
    /// Box plots: the names of the rows shown under each point's value (max, quartiles, median,
    /// min, count, outliers); null for other series.
    /// </summary>
    public string[]? PartLabels { get; init; }

    /// <summary>Box plots: per point, the values for <see cref="PartLabels"/> (empty: leave the row out).</summary>
    public string[][]? PartValues { get; init; }

    /// <summary>Heatmap: the cell height (a cell is <see cref="BarWidth"/> wide, centered on its point).</summary>
    public float CellHeight { get; init; }

    /// <summary>Horizontal bars: each bar's length (with <see cref="CellHeight"/> its hover area); null when all cells are <see cref="BarWidth"/> wide.</summary>
    public float[]? CellWidths { get; init; }
}

/// <summary>
/// A pie slice or radial bar. Angles are radians clockwise from 12 o'clock; the pointer is over
/// it when its distance from the center is between <see cref="R0"/> and <see cref="R1"/> and its
/// angle is between <see cref="A0"/> and <see cref="HitEnd"/>.
/// </summary>
/// <param name="Index">Index into the series' data; -1 for a grouped "Other" slice.</param>
/// <param name="Header">Tooltip header (the series name for nested rings), or empty.</param>
/// <param name="Label">The slice's name (category).</param>
/// <param name="Value">Formatted value, e.g. "42 (35%)".</param>
/// <param name="A1">End of the drawn arc (highlighted on hover).</param>
/// <param name="HitEnd">End of the hover area (the whole track for radial bars).</param>
public sealed record TooltipSlice(string SeriesId, int Index, string Header, string Label, string Value, string Color,
    float Cx, float Cy, float R0, float R1, float A0, float A1, float HitEnd)
{
    /// <summary>For the grouped "Other" slice: the categories it holds, in data order; empty otherwise.</summary>
    public IReadOnlyList<TooltipPart> Parts { get; init; } = [];
}

/// <summary>One category inside a grouped "Other" slice.</summary>
/// <param name="Label">The category.</param>
/// <param name="Value">Formatted value and share, e.g. "9 k€ (1%)".</param>
public sealed record TooltipPart(string Label, string Value);
