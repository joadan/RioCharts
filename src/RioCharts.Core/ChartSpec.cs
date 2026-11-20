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

    [Description("Vertical axis.")]
    public AxisSpec Y { get; init; } = new();

    [Description("Legend settings. By default it is shown only when there is more than one series.")]
    public LegendSpec Legend { get; init; } = new();

    [Description("Series to draw, in order. Colors are assigned from the palette in this order.")]
    public IReadOnlyList<SeriesSpec> Series { get; init; } = [];
}

public sealed record AxisSpec
{
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

    [Description("Draw grid lines. Null uses the default: on for the vertical axis, off for the horizontal axis.")]
    public bool? GridLines { get; init; }
}

public sealed record LegendSpec
{
    [Description("Auto shows the legend only when there is more than one series.")]
    public LegendMode Mode { get; init; } = LegendMode.Auto;

    public LegendPosition Position { get; init; } = LegendPosition.Top;
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

    [JsonIgnore]
    public string DisplayName => Name ?? Id;
}

[JsonConverter(typeof(JsonStringEnumConverter<SeriesType>))]
public enum SeriesType { Line, Area, Bar }

/// <summary>Line shape. Step holds each value until the next point.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CurveType>))]
public enum CurveType { Linear, Monotone, Step }

[JsonConverter(typeof(JsonStringEnumConverter<ScaleType>))]
public enum ScaleType { Auto, Linear, Time, Band }

[JsonConverter(typeof(JsonStringEnumConverter<LegendMode>))]
public enum LegendMode { Auto, Show, Hide }

[JsonConverter(typeof(JsonStringEnumConverter<LegendPosition>))]
public enum LegendPosition { Top, Bottom, Right }
