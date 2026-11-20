namespace RioCharts.Layout;

/// <summary>A position in SVG pixel coordinates.</summary>
public readonly record struct ScreenPoint(double X, double Y);

/// <summary>A rectangle in SVG pixel coordinates.</summary>
public readonly record struct PlotArea(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public double CenterX => Left + Width / 2;
    public double CenterY => Top + Height / 2;

    public bool Contains(double x, double y, double tolerance = 0.5) =>
        x >= Left - tolerance && x <= Right + tolerance && y >= Top - tolerance && y <= Bottom + tolerance;
}

public enum AxisSide { Bottom, Left, Right }

/// <summary>Computed result for the whole chart.</summary>
public sealed class ChartLayout
{
    public required Chart Chart { get; init; }
    public required PlotArea Plot { get; init; }

    /// <summary>True when the chart has at least one visible XY or XYZ series, or no series at all.</summary>
    public bool HasCartesian { get; init; }

    /// <summary>True when any visible series has at least one point to draw.</summary>
    public bool HasData { get; init; }

    public AxisLayout? XAxis { get; init; }
    public IReadOnlyList<AxisLayout> YAxes { get; init; } = [];

    /// <summary>Series layouts in render order (lowest RenderOrder first).</summary>
    public IReadOnlyList<SeriesLayout> Series { get; init; } = [];

    public LegendLayout? Legend { get; init; }

    /// <summary>Problems found and corrected during layout (invalid ranges, bad axis indexes).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>An axis with its computed range, ticks and pixel mapping.</summary>
public sealed class AxisLayout
{
    public required Axis Axis { get; init; }
    public required AxisSide Side { get; init; }
    public required double Min { get; init; }
    public required double Max { get; init; }
    public required double TickStep { get; init; }
    public required IReadOnlyList<double> Ticks { get; init; }

    /// <summary>Pixel for <see cref="Min"/> (left for X, bottom for Y).</summary>
    public required double PixelStart { get; init; }

    /// <summary>Pixel for <see cref="Max"/> (right for X, top for Y).</summary>
    public required double PixelEnd { get; init; }

    /// <summary>Position of the axis line: y for a bottom axis, x for a left/right axis.</summary>
    public required double Offset { get; init; }

    /// <summary>Index into the chart's Y axes, or -1 for the X axis.</summary>
    public int Index { get; init; } = -1;

    public double ToPixel(double value) => PixelStart + (value - Min) / (Max - Min) * (PixelEnd - PixelStart);

    public double FromPixel(double pixel) => Min + (pixel - PixelStart) / (PixelEnd - PixelStart) * (Max - Min);

    public string FormatLabel(double value) => AxisLabelFormatter.Format(Axis, value, TickStep);
}

/// <summary>Computed result for one series.</summary>
public sealed class SeriesLayout
{
    public required ISeries Series { get; init; }

    /// <summary>Position of the series in Chart.Series (stable id for keys and colors).</summary>
    public required int SeriesIndex { get; init; }

    /// <summary>Resolved main color for the series.</summary>
    public required string Color { get; init; }

    /// <summary>XY/XYZ: pixel positions for rendering (after downsampling and filtering).</summary>
    public IReadOnlyList<ScreenPoint> ScreenPoints { get; init; } = [];

    /// <summary>For each screen point, the index of the source point in the series' Points list.</summary>
    public IReadOnlyList<int> PointIndices { get; init; } = [];

    /// <summary>XYZ bubble radii in pixels, parallel to <see cref="ScreenPoints"/>.</summary>
    public IReadOnlyList<double>? BubbleSizes { get; init; }

    /// <summary>XYZ heatmap intensities (0–1), parallel to <see cref="ScreenPoints"/>.</summary>
    public IReadOnlyList<double>? Intensities { get; init; }

    /// <summary>Heatmap cell size in pixels.</summary>
    public double CellWidth { get; init; }

    public double CellHeight { get; init; }

    /// <summary>Area charts: pixel y of the fill baseline.</summary>
    public double? BaselineY { get; init; }

    public AxisLayout? LayoutXAxis { get; init; }
    public AxisLayout? LayoutYAxis { get; init; }

    /// <summary>Value series: slices (pie/donut) or segments (funnel).</summary>
    public IReadOnlyList<SliceLayout>? Slices { get; init; }

    /// <summary>True when the series has no points to draw (shown grayed out in the legend).</summary>
    public bool IsEmpty { get; init; }

    /// <summary>True when the series was downsampled.</summary>
    public bool IsDownsampled { get; init; }
}

/// <summary>A pie/donut slice or funnel segment.</summary>
public sealed class SliceLayout
{
    public required int PointIndex { get; init; }
    public required ValuePoint Point { get; init; }
    public required string Color { get; init; }

    /// <summary>Share of the series total, 0–1.</summary>
    public required double Fraction { get; init; }

    /// <summary>Start angle in radians, clockwise from 12 o'clock.</summary>
    public double StartAngle { get; init; }

    public double EndAngle { get; init; }
    public ScreenPoint Center { get; init; }
    public double OuterRadius { get; init; }
    public double InnerRadius { get; init; }

    /// <summary>Funnel segment corners (clockwise from top-left).</summary>
    public IReadOnlyList<ScreenPoint>? Polygon { get; init; }

    /// <summary>Where to anchor the slice label and tooltip.</summary>
    public ScreenPoint LabelPosition { get; init; }
}

public sealed class LegendLayout
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required IReadOnlyList<LegendItemLayout> Items { get; init; }
}

public sealed class LegendItemLayout
{
    public required ISeries Series { get; init; }

    /// <summary>Set for value series slices; null for whole-series items.</summary>
    public ValuePoint? Point { get; init; }

    public int PointIndex { get; init; } = -1;
    public required string Label { get; init; }
    public required string Color { get; init; }

    /// <summary>False when the series or slice is hidden.</summary>
    public required bool Enabled { get; init; }

    /// <summary>True when the series has no data.</summary>
    public required bool IsEmpty { get; init; }

    public MarkerStyle Marker { get; init; }

    /// <summary>True when the item represents a line (drawn as a short line, not a box).</summary>
    public bool IsLine { get; init; }

    /// <summary>Top-left of the item row.</summary>
    public required double X { get; init; }

    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
}
