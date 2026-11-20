namespace RioCharts;

/// <summary>Line, scatter and area series.</summary>
public class XYSeries : ISeries
{
    public string Name { get; set; } = "";
    public bool Visible { get; set; } = true;
    public int RenderOrder { get; set; }
    public double Opacity { get; set; } = 1.0;
    public List<XYPoint> Points { get; set; } = new();

    /// <summary>Line color. When null, a color is taken from the chart palette.</summary>
    public string? StrokeColor { get; set; }

    public double StrokeWidth { get; set; } = 2;

    /// <summary>Draw the connecting line. Set to false for a scatter plot.</summary>
    public bool ShowLine { get; set; } = true;

    /// <summary>Fill under the line (area chart) when set. "auto" uses the line color.</summary>
    public string? FillColor { get; set; }

    /// <summary>Opacity of the area fill.</summary>
    public double FillOpacity { get; set; } = 0.3;

    public MarkerStyle Marker { get; set; } = MarkerStyle.None;
    public double MarkerSize { get; set; } = 6;

    /// <summary>Marker color; defaults to the line color when null.</summary>
    public string? MarkerColor { get; set; }

    /// <summary>Index into <see cref="Chart.YAxes"/>.</summary>
    public int YAxisIndex { get; set; }

    public bool IsSelected { get; set; }
}

public enum MarkerStyle { None, Circle, Square, Triangle, Diamond, Cross, Plus }

/// <summary>A point in an XY series. For date axes, X is an OLE Automation date.</summary>
public readonly record struct XYPoint(double X, double Y)
{
    /// <summary>Creates a point with a date X value (stored as an OLE Automation date).</summary>
    public XYPoint(DateTime x, double y) : this(x.ToOADate(), y)
    {
    }
}
