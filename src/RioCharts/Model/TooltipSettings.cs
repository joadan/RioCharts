namespace RioCharts;

public class TooltipSettings
{
    public bool Enabled { get; set; } = true;
    public string BackgroundColor { get; set; } = "rgba(50, 50, 50, 0.9)";
    public string TextColor { get; set; } = "white";
    public float FontSize { get; set; } = 12;
    public double Padding { get; set; } = 8;
    public double BorderRadius { get; set; } = 4;

    /// <summary>
    /// Composite format for tooltip text. Arguments: {0} series name, {1} Y (or slice value),
    /// {2} X (or slice label), {3} Z (bubble/heatmap) or slice percentage (0–1).
    /// Example: "{0}: {1:N2}".
    /// </summary>
    public string? CustomFormat { get; set; }
}
