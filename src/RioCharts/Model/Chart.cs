namespace RioCharts;

/// <summary>
/// The chart model: everything the layout engine needs to produce a chart.
/// </summary>
public class Chart
{
    public string? Title { get; set; }
    public double Width { get; set; } = 800;
    public double Height { get; set; } = 600;
    public ChartMargin Margin { get; set; } = new(60, 40, 60, 80);
    public string BackgroundColor { get; set; } = "white";
    public Axis? XAxis { get; set; }
    public List<Axis> YAxes { get; set; } = new();
    public LegendSettings Legend { get; set; } = new();
    public List<ISeries> Series { get; set; } = new();
    public TooltipSettings Tooltip { get; set; } = new();
    public bool EnableInteractions { get; set; } = true;
    public bool EnableAnimations { get; set; } = true;

    /// <summary>Colors used for series (and pie slices) that do not set their own color.</summary>
    public IReadOnlyList<string> Palette { get; set; } = ChartPalette.Default;

    /// <summary>
    /// XY series with more points than this are downsampled with LTTB before rendering.
    /// 0 or less disables downsampling.
    /// </summary>
    public int DownsampleThreshold { get; set; } = 1000;
}

/// <summary>Space around the plot area for axes, labels and the title.</summary>
public sealed record ChartMargin(double Top, double Right, double Bottom, double Left);
