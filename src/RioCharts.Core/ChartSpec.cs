using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RioCharts.Core;

/// <summary>
/// The chart model. Plain, serializable data: Razor components and (later) JSON specs both
/// compile to this. Data is not part of the spec; it is passed separately by series id.
/// </summary>
public sealed record ChartSpec
{
    [Description("Optional chart title.")]
    public string? Title { get; init; }

    [Description("Horizontal axis.")]
    public AxisSpec X { get; init; } = new();

    [Description("Primary vertical axis (id \"y\" unless set). Drawn on the left and owns the grid lines.")]
    public AxisSpec Y { get; init; } = new();

    [Description("Additional vertical axes, e.g. a second unit. Drawn on the right unless Position is Left. Series bind to them with yAxisId.")]
    public IReadOnlyList<AxisSpec> YAxes { get; init; } = [];

    [Description("Legend settings. By default it is shown only when there is more than one series.")]
    public LegendSpec Legend { get; init; } = new();

    [Description("Tooltip settings. On by default, with one tooltip per x position across series.")]
    public TooltipSpec Tooltip { get; init; } = new();

    [Description("Series to draw, in order. Colors are assigned from the palette in this order.")]
    public IReadOnlyList<SeriesSpec> Series { get; init; } = [];

    [Description("Reference lines and bands, e.g. a setpoint or a fault period.")]
    public IReadOnlyList<AnnotationSpec> Annotations { get; init; } = [];
}

public sealed record AxisSpec
{
    [Description("Axis id that series and annotations refer to (vertical axes only). The primary vertical axis defaults to \"y\".")]
    public string? Id { get; init; }

    [Description("Axis title.")]
    public string? Title { get; init; }

    [Description("Fixed minimum. When null it comes from the data.")]
    public double? Min { get; init; }

    [Description("Fixed maximum. When null it comes from the data.")]
    public double? Max { get; init; }

    [Description("Scale type. Auto picks from the data: times give a time axis, numbers a linear axis, strings a band axis.")]
    public ScaleType Scale { get; init; } = ScaleType.Auto;

    [Description(".NET format string for labels, e.g. \"P0\", \"C0\" or \"yyyy-MM-dd\". Null for automatic formatting.")]
    public string? Format { get; init; }

    [Description("Draw grid lines. Null uses the default: on for the primary vertical axis, off otherwise.")]
    public bool? GridLines { get; init; }

    [Description("Side of a vertical axis. Auto: the primary axis on the left, additional axes on the right.")]
    public AxisPosition Position { get; init; } = AxisPosition.Auto;
}

public sealed record LegendSpec
{
    [Description("Auto shows the legend only when there is more than one series.")]
    public LegendMode Mode { get; init; } = LegendMode.Auto;

    public LegendPosition Position { get; init; } = LegendPosition.Top;
}

public sealed record TooltipSpec
{
    [Description("Show a tooltip and crosshair when hovering the plot.")]
    public bool Enabled { get; init; } = true;

    [Description("True: one tooltip per x position listing every series. False: only the nearest point.")]
    public bool Shared { get; init; } = true;
}

public sealed record SeriesSpec
{
    [Description("Unique id of the series; data is supplied by this id.")]
    public required string Id { get; init; }

    public SeriesType Type { get; init; } = SeriesType.Line;

    [Description("Name shown in the legend and tooltips. Defaults to the id.")]
    public string? Name { get; init; }

    [Description("CSS color. Null uses the next palette color.")]
    public string? Color { get; init; }

    [Description("Line shape for line and area series.")]
    public CurveType Curve { get; init; } = CurveType.Linear;

    [Description("Line width in pixels. Default 2.")]
    public double? StrokeWidth { get; init; }

    [Description("Draw order override. By default areas are drawn first, then bars, then lines.")]
    public int? ZIndex { get; init; }

    [Description("Id of the vertical axis this series uses. Null uses the primary axis.")]
    public string? YAxisId { get; init; }

    [Description("Stack group name. Series of the same type with the same stack name (and axis) are stacked on top of each other.")]
    public string? Stack { get; init; }

    [Description("Show values on the chart. Null uses the default: on for pie, donut and radial bar, off otherwise.")]
    public bool? DataLabels { get; init; }

    [Description("Pie, donut and radial bar: inner radius as a fraction of the outer radius (0 = pie, 0.6 = donut).")]
    public double? InnerRadius { get; init; }

    [Description("Pie, donut and radial bar: start angle in degrees, clockwise from 12 o'clock. Default 0.")]
    public double? StartAngle { get; init; }

    [Description("Pie, donut and radial bar: end angle in degrees. Default: start + 360 for pies, 270 for radial bars.")]
    public double? EndAngle { get; init; }

    [Description("Pie and donut: slices smaller than this share of the total (e.g. 0.03) are grouped into one \"Other\" slice.")]
    public double? GroupSmallSlices { get; init; }

    [Description("Radial bar: the value of a full track (e.g. 100 for percentages). Default: a nice value above the largest.")]
    public double? Max { get; init; }

    [Description(".NET format string for values in tooltips and data labels, e.g. \"0.0\" or \"P0\". Null: the axis format, else automatic.")]
    public string? Format { get; init; }

    [JsonIgnore]
    public string DisplayName => Name ?? Id;
}

/// <summary>
/// A reference line or band. Values are in data units of the axis: numbers, times
/// (DateTime, DateTimeOffset, DateOnly or an ISO 8601 string) or category names.
/// </summary>
public sealed record AnnotationSpec
{
    [Description("Optional id, reported in click events.")]
    public string? Id { get; init; }

    public AnnotationType Type { get; init; } = AnnotationType.Line;

    [Description("Axis the value(s) refer to: Y for a horizontal line or band, X for a vertical one.")]
    public AnnotationAxis Axis { get; init; } = AnnotationAxis.Y;

    [Description("Vertical axis id for Y annotations. Null uses the primary axis.")]
    public string? YAxisId { get; init; }

    [Description("Position of a line: a number, a time (ISO 8601) or a category.")]
    public object? Value { get; init; }

    [Description("Start of a band.")]
    public object? From { get; init; }

    [Description("End of a band.")]
    public object? To { get; init; }

    [Description("Optional label.")]
    public string? Label { get; init; }

    [Description("Where the label sits along the line or band. Default: End for lines, Start for bands.")]
    public LabelPosition? LabelPosition { get; init; }

    [Description("CSS color. Null uses the muted annotation color.")]
    public string? Color { get; init; }

    [Description("Whether the axis range grows to include the annotation. Default: true for lines, false for bands.")]
    public bool? ExpandsDomain { get; init; }

    [Description("Draw below or above the series. Default: bands below, lines above.")]
    public AnnotationLayer? Layer { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<SeriesType>))]
public enum SeriesType { Line, Area, Bar, Pie, RadialBar }

/// <summary>Line shape. Step holds each value until the next point.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CurveType>))]
public enum CurveType { Linear, Monotone, Step }

[JsonConverter(typeof(JsonStringEnumConverter<ScaleType>))]
public enum ScaleType { Auto, Linear, Time, Band }

[JsonConverter(typeof(JsonStringEnumConverter<AxisPosition>))]
public enum AxisPosition { Auto, Left, Right }

[JsonConverter(typeof(JsonStringEnumConverter<LegendMode>))]
public enum LegendMode { Auto, Show, Hide }

[JsonConverter(typeof(JsonStringEnumConverter<LegendPosition>))]
public enum LegendPosition { Top, Bottom, Right }

[JsonConverter(typeof(JsonStringEnumConverter<AnnotationType>))]
public enum AnnotationType { Line, Band }

[JsonConverter(typeof(JsonStringEnumConverter<AnnotationAxis>))]
public enum AnnotationAxis { X, Y }

[JsonConverter(typeof(JsonStringEnumConverter<LabelPosition>))]
public enum LabelPosition { Start, Middle, End }

[JsonConverter(typeof(JsonStringEnumConverter<AnnotationLayer>))]
public enum AnnotationLayer { Below, Above }
