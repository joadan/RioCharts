using RioCharts.Core.Scene;

namespace RioCharts.Core.Layout;

/// <summary>
/// What the interactive layer needs for tooltips, crosshair and point clicks, computed once
/// per layout: pixel positions of the drawn points plus their formatted x and y values.
/// Renderers send it to the client once; pointer moves are then handled locally.
/// </summary>
/// <param name="BandWidth">Width of the hover highlight for bars and category axes; 0 draws a crosshair line.</param>
public sealed record TooltipModel(RectF Plot, bool Enabled, bool Shared, float BandWidth, IReadOnlyList<TooltipSeries> Series);

/// <param name="Color">CSS color or theme token.</param>
/// <param name="Snap">How far (px) from the hovered x a point may be and still be listed in a shared tooltip.</param>
/// <param name="BarWidth">Bar width (0 for lines and areas).</param>
/// <param name="BarOffset">Offset of the bar center from <see cref="X"/>.</param>
/// <param name="X">Pixel x per point, ascending.</param>
/// <param name="Y">Pixel y per point (the top of the stack segment for stacked series).</param>
/// <param name="Index">Index into the series' data, per point.</param>
public sealed record TooltipSeries(string Id, string Name, string Color, SeriesType Type, float Snap, float BarWidth, float BarOffset,
    float[] X, float[] Y, int[] Index, string[] XLabels, string[] Values);
