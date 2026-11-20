namespace RioCharts;

/// <summary>Axis configuration. The computed range and ticks live in <see cref="Layout.AxisLayout"/>.</summary>
public class Axis
{
    public string? Title { get; set; }

    /// <summary>Fixed minimum. When null the minimum comes from the data.</summary>
    public double? Minimum { get; set; }

    /// <summary>Fixed maximum. When null the maximum comes from the data.</summary>
    public double? Maximum { get; set; }

    /// <summary>When true, data-derived ends are rounded out to "nice" tick values.</summary>
    public bool AutoRange { get; set; } = true;

    public int TargetTickCount { get; set; } = 6;
    public bool ShowGridLines { get; set; } = true;
    public string GridLineColor { get; set; } = "#e0e0e0";
    public double GridLineWidth { get; set; } = 1;
    public AxisLabelFormat LabelFormat { get; set; } = AxisLabelFormat.Auto;

    /// <summary>.NET format string used when <see cref="LabelFormat"/> is Custom, e.g. "C2" or "P0".</summary>
    public string? CustomFormatString { get; set; }

    /// <summary>Label rotation in degrees, e.g. 45 for long date labels.</summary>
    public double LabelRotation { get; set; }

    public string LabelColor { get; set; } = "black";
    public float LabelFontSize { get; set; } = 11;
}

/// <summary>
/// How axis labels are formatted. Date, Time and DateTime treat values as
/// OLE Automation dates (<see cref="DateTime.ToOADate"/>).
/// </summary>
public enum AxisLabelFormat { Auto, Number, Currency, Percentage, Date, Time, DateTime, Custom }

/// <summary>A visible range on an axis, used for zoom/pan binding.</summary>
public sealed record AxisRange(double Minimum, double Maximum);
