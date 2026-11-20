namespace RioCharts;

public enum LegendPosition { Top, Bottom, Left, Right, TopLeft, TopRight, BottomLeft, BottomRight }

public class LegendSettings
{
    public bool Visible { get; set; } = true;
    public LegendPosition Position { get; set; } = LegendPosition.TopRight;
    public float FontSize { get; set; } = 12;
    public string FontColor { get; set; } = "black";
    public float MarkerSize { get; set; } = 10;
    public string BackgroundColor { get; set; } = "white";
    public string BorderColor { get; set; } = "#cccccc";
    public double BorderWidth { get; set; } = 1;
    public double Padding { get; set; } = 8;
    public double Opacity { get; set; } = 0.9;
}
