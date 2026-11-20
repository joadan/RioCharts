namespace RioCharts;

/// <summary>Pie, donut and funnel series.</summary>
public class ValueSeries : ISeries
{
    public string Name { get; set; } = "";
    public bool Visible { get; set; } = true;
    public int RenderOrder { get; set; }
    public double Opacity { get; set; } = 1.0;
    public List<ValuePoint> Points { get; set; } = new();
    public bool IsSelected { get; set; }

    public ValueSeriesType Type { get; set; } = ValueSeriesType.Pie;

    /// <summary>Inner radius as a fraction of the outer radius, used by donut charts.</summary>
    public double InnerRadiusRatio { get; set; } = 0.55;

    /// <summary>Show percentage labels on slices (pie/donut) or labels on segments (funnel).</summary>
    public bool ShowLabels { get; set; } = true;
}

public enum ValueSeriesType { Pie, Donut, Funnel }

public class ValuePoint
{
    public ValuePoint()
    {
    }

    public ValuePoint(string label, double value, string? color = null)
    {
        Label = label;
        Value = value;
        Color = color;
    }

    public string Label { get; set; } = "";
    public double Value { get; set; }

    /// <summary>Optional per-slice color overriding the palette.</summary>
    public string? Color { get; set; }

    /// <summary>Offset the slice from the center for emphasis (pie/donut).</summary>
    public bool IsExploded { get; set; }

    /// <summary>Show or hide this slice. Settable so the legend can toggle it.</summary>
    public bool Visible { get; set; } = true;
}
