namespace RioCharts;

public enum MouseButton { None = -1, Left = 0, Middle = 1, Right = 2 }

/// <summary>Data for series hover, click and selection events.</summary>
public class ChartEventArgs
{
    public ISeries? Series { get; init; }

    /// <summary>Index into the series' Points list, or -1 when not on a point.</summary>
    public int PointIndex { get; init; } = -1;

    /// <summary>Data X value (slice index for value series).</summary>
    public double X { get; init; }

    /// <summary>Data Y value (slice value for value series).</summary>
    public double Y { get; init; }

    public MouseButton Button { get; init; } = MouseButton.None;
}

/// <summary>Raised when zoom or pan changes an axis range.</summary>
public sealed record AxisRangeChangedArgs(Axis Axis, double OldMinimum, double OldMaximum, double NewMinimum, double NewMaximum);
