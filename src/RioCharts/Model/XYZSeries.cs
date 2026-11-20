namespace RioCharts;

/// <summary>Bubble and heatmap series. Z maps to bubble size or cell intensity.</summary>
public class XYZSeries : ISeries
{
    public string Name { get; set; } = "";
    public bool Visible { get; set; } = true;
    public int RenderOrder { get; set; }
    public double Opacity { get; set; } = 1.0;
    public List<XYZPoint> Points { get; set; } = new();
    public int YAxisIndex { get; set; }

    public XYZSeriesType Type { get; set; } = XYZSeriesType.Bubble;

    /// <summary>Smallest bubble radius in pixels.</summary>
    public double MinBubbleSize { get; set; } = 5;

    /// <summary>Largest bubble radius in pixels.</summary>
    public double MaxBubbleSize { get; set; } = 50;

    /// <summary>Bubble or cell color. When null, a color is taken from the chart palette.</summary>
    public string? FillColor { get; set; }

    public string? StrokeColor { get; set; }
    public double StrokeWidth { get; set; } = 1;
    public bool IsSelected { get; set; }
}

public enum XYZSeriesType { Bubble, Heatmap }

/// <summary>A point with a size/intensity value. For date axes, X is an OLE Automation date.</summary>
public readonly record struct XYZPoint(double X, double Y, double Z);
