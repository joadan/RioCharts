using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RioCharts.Core;

/// <summary>
/// The chart model. Plain, serializable data: Razor components and (later) JSON specs both
/// compile to this. Data is not part of the spec; it is passed separately by series id.
/// </summary>
public sealed record ChartSpec
{
    /// <summary>Optional chart title.</summary>
    [Description("Optional chart title.")]
    public string? Title { get; init; }

    /// <summary>Compact chart (sparkline): no axes, grid, legend or title; the series fill the box and the y range fits the data tightly (bars keep zero). For small charts in tables and tiles.</summary>
    [Description("Compact chart (sparkline): no axes, grid, legend or title; the series fill the box and the y range fits the data tightly (bars keep zero). For small charts in tables and tiles.")]
    public bool Compact { get; init; }

    /// <summary>Default true: as the chart gets small, leave out axis titles, then the legend, title and axis labels; very small charts become compact. False keeps everything at any size.</summary>
    [Description("Default true: as the chart gets small, leave out axis titles, then the legend, title and axis labels; very small charts become compact. False keeps everything at any size.")]
    public bool AutoCompact { get; init; } = true;

    /// <summary>Bar charts: bars run left to right with the categories down the side (first on top). For rankings and long category names. The y axis settings (title, format, min, max) apply to the values.</summary>
    [Description("Bar charts: bars run left to right with the categories down the side (first on top). For rankings and long category names. The y axis settings (title, format, min, max) apply to the values.")]
    public bool Horizontal { get; init; }

    /// <summary>Horizontal axis.</summary>
    [Description("Horizontal axis.")]
    public AxisSpec X { get; init; } = new();

    /// <summary>Primary vertical axis (id "y" unless set). Drawn on the left and owns the grid lines.</summary>
    [Description("Primary vertical axis (id \"y\" unless set). Drawn on the left and owns the grid lines.")]
    public AxisSpec Y { get; init; } = new();

    /// <summary>Additional vertical axes, e.g. a second unit. Drawn on the right unless Position is Left. Series bind to them with yAxisId.</summary>
    [Description("Additional vertical axes, e.g. a second unit. Drawn on the right unless Position is Left. Series bind to them with yAxisId.")]
    public IReadOnlyList<AxisSpec> YAxes { get; init; } = [];

    /// <summary>Legend settings. By default it is shown only when there is more than one series.</summary>
    [Description("Legend settings. By default it is shown only when there is more than one series.")]
    public LegendSpec Legend { get; init; } = new();

    /// <summary>Tooltip settings. On by default, with one tooltip per x position across series.</summary>
    [Description("Tooltip settings. On by default, with one tooltip per x position across series.")]
    public TooltipSpec Tooltip { get; init; } = new();

    /// <summary>Series to draw, in order. Colors are assigned from the palette in this order.</summary>
    [Description("Series to draw, in order. Colors are assigned from the palette in this order.")]
    public IReadOnlyList<SeriesSpec> Series { get; init; } = [];

    /// <summary>Reference lines and bands, e.g. a target or an incident period.</summary>
    [Description("Reference lines and bands, e.g. a target or an incident period.")]
    public IReadOnlyList<AnnotationSpec> Annotations { get; init; } = [];

    /// <summary>Display time zone: "UTC", "local" (the viewer's zone) or an IANA id such as "Europe/Stockholm". Null shows times as given. Times are then plotted on instants and labelled in this zone.</summary>
    [Description("Display time zone: \"UTC\", \"local\" (the viewer's zone) or an IANA id such as \"Europe/Stockholm\". Null shows times as given. Times are then plotted on instants and labelled in this zone.")]
    public string? TimeZone { get; init; }

    /// <summary>With a display zone: how to read times without a zone (DateTime with Kind Unspecified). Utc or DisplayZone.</summary>
    [Description("With a display zone: how to read times without a zone (DateTime with Kind Unspecified). Utc or DisplayZone.")]
    public RioCharts.Core.Data.UnspecifiedTimes? UnspecifiedTimes { get; init; }

    /// <summary>Dataset (by id from the data source) that series read their x and y fields from, unless a series names its own.</summary>
    [Description("Dataset (by id from the data source) that series read their x and y fields from, unless a series names its own.")]
    public string? Dataset { get; init; }

    /// <summary>Inline rows for small data, e.g. [{"month":"Jan","revenue":42}]. At most 1000 rows; prefer a dataset id for real data.</summary>
    [Description("Inline rows for small data, e.g. [{\"month\":\"Jan\",\"revenue\":42}]. At most 1000 rows; prefer a dataset id for real data.")]
    public IReadOnlyList<IReadOnlyDictionary<string, object?>>? Data { get; init; }
}

/// <summary>Settings for one axis: title, range, scale, label format and grid lines.</summary>
public sealed record AxisSpec
{
    /// <summary>Axis id that series and annotations refer to (vertical axes only). The primary vertical axis defaults to "y".</summary>
    [Description("Axis id that series and annotations refer to (vertical axes only). The primary vertical axis defaults to \"y\".")]
    public string? Id { get; init; }

    /// <summary>Axis title.</summary>
    [Description("Axis title.")]
    public string? Title { get; init; }

    /// <summary>Fixed minimum. When null it comes from the data.</summary>
    [Description("Fixed minimum. When null it comes from the data.")]
    public double? Min { get; init; }

    /// <summary>Fixed maximum. When null it comes from the data.</summary>
    [Description("Fixed maximum. When null it comes from the data.")]
    public double? Max { get; init; }

    /// <summary>Scale type. Auto picks from the data: times give a time axis, numbers a linear axis, strings a band axis.</summary>
    [Description("Scale type. Auto picks from the data: times give a time axis, numbers a linear axis, strings a band axis.")]
    public ScaleType Scale { get; init; } = ScaleType.Auto;

    /// <summary>.NET format string for labels, e.g. "P0", "C0" or "yyyy-MM-dd". Null for automatic formatting.</summary>
    [Description(".NET format string for labels, e.g. \"P0\", \"C0\" or \"yyyy-MM-dd\". Null for automatic formatting.")]
    public string? Format { get; init; }

    /// <summary>Draw grid lines. Null uses the default: on for the primary vertical axis, off otherwise.</summary>
    [Description("Draw grid lines. Null uses the default: on for the primary vertical axis, off otherwise.")]
    public bool? GridLines { get; init; }

    /// <summary>Side of a vertical axis. Auto: the primary axis on the left, additional axes on the right.</summary>
    [Description("Side of a vertical axis. Auto: the primary axis on the left, additional axes on the right.")]
    public AxisPosition Position { get; init; } = AxisPosition.Auto;
}

/// <summary>Legend settings: when to show it and where.</summary>
public sealed record LegendSpec
{
    /// <summary>Auto shows the legend only when there is more than one series.</summary>
    [Description("Auto shows the legend only when there is more than one series.")]
    public LegendMode Mode { get; init; } = LegendMode.Auto;

    /// <summary>Where the legend sits. Default Top.</summary>
    public LegendPosition Position { get; init; } = LegendPosition.Top;
}

/// <summary>Tooltip settings.</summary>
public sealed record TooltipSpec
{
    /// <summary>Show a tooltip and crosshair when hovering the plot.</summary>
    [Description("Show a tooltip and crosshair when hovering the plot.")]
    public bool Enabled { get; init; } = true;

    /// <summary>True: one tooltip per x position listing every series. False: only the nearest point.</summary>
    [Description("True: one tooltip per x position listing every series. False: only the nearest point.")]
    public bool Shared { get; init; } = true;
}

/// <summary>
/// One series: its type, where its data comes from (fields and transforms in JSON specs) and how it looks.
/// </summary>
public sealed record SeriesSpec
{
    /// <summary>Unique id of the series. Optional in JSON specs (generated from the y field).</summary>
    [Description("Unique id of the series. Optional in JSON specs (generated from the y field).")]
    public string Id { get; init; } = "";

    /// <summary>Chart type: line, area, bar, pie (donut with innerRadius), radialBar, or auto to pick from the data.</summary>
    [Description("Chart type: line, area, bar, pie (donut with innerRadius), radialBar, or auto to pick from the data.")]
    public SeriesType Type { get; init; } = SeriesType.Line;

    /// <summary>Dataset id to read fields from. Defaults to the chart's dataset (or its inline data).</summary>
    [Description("Dataset id to read fields from. Defaults to the chart's dataset (or its inline data).")]
    public string? Dataset { get; init; }

    /// <summary>Field for the x axis: a time, number or text (category; the slice name for pies).</summary>
    [Description("Field for the x axis: a time, number or text (category; the slice name for pies).")]
    public string? X { get; init; }

    /// <summary>Numeric field for the y value. Optional with aggregate count.</summary>
    [Description("Numeric field for the y value. Optional with aggregate count.")]
    public string? Y { get; init; }

    /// <summary>Keep only rows that match all of these conditions.</summary>
    [Description("Keep only rows that match all of these conditions.")]
    public IReadOnlyList<FilterSpec>? Filter { get; init; }

    /// <summary>Time x: group rows into buckets (hour, day, week, month, quarter, year) and aggregate y per bucket.</summary>
    [Description("Time x: group rows into buckets (hour, day, week, month, quarter, year) and aggregate y per bucket.")]
    public TimeBucket? TimeBucket { get; init; }

    /// <summary>Combine rows with the same x (or time bucket): sum, average, min, max, count or last.</summary>
    [Description("Combine rows with the same x (or time bucket): sum, average, min, max, count or last.")]
    public Aggregate? Aggregate { get; init; }

    /// <summary>Field to split by: one series per distinct value (e.g. one line per sales channel).</summary>
    [Description("Field to split by: one series per distinct value (e.g. one line per sales channel).")]
    public string? GroupBy { get; init; }

    /// <summary>Order points by y (for categories, e.g. a ranking): ascending or descending.</summary>
    [Description("Order points by y (for categories, e.g. a ranking): ascending or descending.")]
    public SortOrder? Sort { get; init; }

    /// <summary>Keep only the first N points after sorting (e.g. top 10).</summary>
    [Description("Keep only the first N points after sorting (e.g. top 10).")]
    public int? Limit { get; init; }

    /// <summary>Name shown in the legend and tooltips. Defaults to the id.</summary>
    [Description("Name shown in the legend and tooltips. Defaults to the id.")]
    public string? Name { get; init; }

    /// <summary>CSS color. Null uses the next palette color.</summary>
    [Description("CSS color. Null uses the next palette color.")]
    public string? Color { get; init; }

    /// <summary>Line shape for line and area series.</summary>
    [Description("Line shape for line and area series.")]
    public CurveType Curve { get; init; } = CurveType.Linear;

    /// <summary>Line width in pixels. Default 2.</summary>
    [Description("Line width in pixels. Default 2.")]
    public double? StrokeWidth { get; init; }

    /// <summary>Draw order override. By default areas are drawn first, then bars, then lines.</summary>
    [Description("Draw order override. By default areas are drawn first, then bars, then lines.")]
    public int? ZIndex { get; init; }

    /// <summary>Id of the vertical axis this series uses. Null uses the primary axis.</summary>
    [Description("Id of the vertical axis this series uses. Null uses the primary axis.")]
    public string? YAxisId { get; init; }

    /// <summary>Stack group name. Series of the same type with the same stack name (and axis) are stacked on top of each other.</summary>
    [Description("Stack group name. Series of the same type with the same stack name (and axis) are stacked on top of each other.")]
    public string? Stack { get; init; }

    /// <summary>Show values on the chart. Null uses the default: on for pie, donut and radial bar, off otherwise.</summary>
    [Description("Show values on the chart. Null uses the default: on for pie, donut and radial bar, off otherwise.")]
    public bool? DataLabels { get; init; }

    /// <summary>Range area: the field with the low value of each point (y is the high value).</summary>
    [Description("Range area: the field with the low value of each point (y is the high value).")]
    public string? Low { get; init; }

    /// <summary>Scatter: marker diameter in pixels. Default 5.</summary>
    [Description("Scatter: marker diameter in pixels. Default 5.")]
    public double? MarkerSize { get; init; }

    /// <summary>Scatter: draw a dashed least-squares trend line through the points.</summary>
    [Description("Scatter: draw a dashed least-squares trend line through the points.")]
    public bool TrendLine { get; init; }

    /// <summary>Line and area series: mark points with a dot. One or more of last, first, min, max, comma-separated (e.g. "last, max").</summary>
    [Description("Line and area series: mark points with a dot. One or more of last, first, min, max, comma-separated (e.g. \"last, max\").")]
    public PointHighlight Highlight { get; init; }

    /// <summary>Pie, donut and radial bar: inner radius as a fraction of the outer radius (0 = pie, 0.6 = donut).</summary>
    [Description("Pie, donut and radial bar: inner radius as a fraction of the outer radius (0 = pie, 0.6 = donut).")]
    public double? InnerRadius { get; init; }

    /// <summary>Pie, donut and radial bar: start angle in degrees, clockwise from 12 o'clock. Default 0.</summary>
    [Description("Pie, donut and radial bar: start angle in degrees, clockwise from 12 o'clock. Default 0.")]
    public double? StartAngle { get; init; }

    /// <summary>Pie, donut and radial bar: end angle in degrees. Default: start + 360 for pies, 270 for radial bars.</summary>
    [Description("Pie, donut and radial bar: end angle in degrees. Default: start + 360 for pies, 270 for radial bars.")]
    public double? EndAngle { get; init; }

    /// <summary>Pie and donut: slices smaller than this share of the total (e.g. 0.03) are grouped into one "Other" slice.</summary>
    [Description("Pie and donut: slices smaller than this share of the total (e.g. 0.03) are grouped into one \"Other\" slice.")]
    public double? GroupSmallSlices { get; init; }

    /// <summary>Radial bar: the value of a full track (e.g. 100 for percentages). Default: a nice value above the largest.</summary>
    [Description("Radial bar: the value of a full track (e.g. 100 for percentages). Default: a nice value above the largest.")]
    public double? Max { get; init; }

    /// <summary>.NET format string for values in tooltips and data labels, e.g. "0.0" or "P0". Null: the axis format, else automatic.</summary>
    [Description(".NET format string for values in tooltips and data labels, e.g. \"0.0\" or \"P0\". Null: the axis format, else automatic.")]
    public string? Format { get; init; }

    /// <summary>The name shown in the legend and tooltips: <see cref="Name"/>, or the id when no name is set.</summary>
    [JsonIgnore]
    public string DisplayName => Name ?? Id;
}

/// <summary>
/// A reference line or band. Values are in data units of the axis: numbers, times
/// (DateTime, DateTimeOffset, DateOnly or an ISO 8601 string) or category names.
/// </summary>
public sealed record AnnotationSpec
{
    /// <summary>Optional id, reported in click events.</summary>
    [Description("Optional id, reported in click events.")]
    public string? Id { get; init; }

    /// <summary>Kind of annotation: a line, a band, a point or a callout. Default Line.</summary>
    public AnnotationType Type { get; init; } = AnnotationType.Line;

    /// <summary>Axis the value(s) refer to: Y for a horizontal line or band, X for a vertical one.</summary>
    [Description("Axis the value(s) refer to: Y for a horizontal line or band, X for a vertical one.")]
    public AnnotationAxis Axis { get; init; } = AnnotationAxis.Y;

    /// <summary>Vertical axis id for Y annotations. Null uses the primary axis.</summary>
    [Description("Vertical axis id for Y annotations. Null uses the primary axis.")]
    public string? YAxisId { get; init; }

    /// <summary>Position of a line: a number, a time (ISO 8601) or a category.</summary>
    [Description("Position of a line: a number, a time (ISO 8601) or a category.")]
    public object? Value { get; init; }

    /// <summary>Start of a band.</summary>
    [Description("Start of a band.")]
    public object? From { get; init; }

    /// <summary>End of a band.</summary>
    [Description("End of a band.")]
    public object? To { get; init; }

    /// <summary>Point and callout: the x value (a number, a time or a category).</summary>
    [Description("Point and callout: the x value (a number, a time or a category).")]
    public object? X { get; init; }

    /// <summary>Point and callout: the y value on the annotation's vertical axis.</summary>
    [Description("Point and callout: the y value on the annotation's vertical axis.")]
    public object? Y { get; init; }

    /// <summary>Callout: horizontal offset of the text box from the point, in pixels. Default 24.</summary>
    [Description("Callout: horizontal offset of the text box from the point, in pixels. Default 24.")]
    public double? OffsetX { get; init; }

    /// <summary>Callout: vertical offset of the text box from the point, in pixels (negative is up). Default -32.</summary>
    [Description("Callout: vertical offset of the text box from the point, in pixels (negative is up). Default -32.")]
    public double? OffsetY { get; init; }

    /// <summary>Line: the user can drag it to a new value (e.g. to edit a threshold); the chart reports the new value.</summary>
    [Description("Line: the user can drag it to a new value (e.g. to edit a threshold); the chart reports the new value.")]
    public bool Draggable { get; init; }

    /// <summary>Optional label.</summary>
    [Description("Optional label.")]
    public string? Label { get; init; }

    /// <summary>Where the label sits along the line or band. Default: End for lines, Start for bands.</summary>
    [Description("Where the label sits along the line or band. Default: End for lines, Start for bands.")]
    public LabelPosition? LabelPosition { get; init; }

    /// <summary>CSS color. Null uses the muted annotation color.</summary>
    [Description("CSS color. Null uses the muted annotation color.")]
    public string? Color { get; init; }

    /// <summary>Whether the axis range grows to include the annotation. Default: true for lines, false for bands.</summary>
    [Description("Whether the axis range grows to include the annotation. Default: true for lines, false for bands.")]
    public bool? ExpandsDomain { get; init; }

    /// <summary>Draw below or above the series. Default: bands below, lines above.</summary>
    [Description("Draw below or above the series. Default: bands below, lines above.")]
    public AnnotationLayer? Layer { get; init; }
}

/// <summary>How a series is drawn.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SeriesType>))]
public enum SeriesType
{
    /// <summary>A line through the points, for trends over time or another continuous x.</summary>
    Line,

    /// <summary>A line with the area below it filled.</summary>
    Area,

    /// <summary>A bar per point, for comparing categories (or time buckets).</summary>
    Bar,

    /// <summary>Slices of a circle, for shares of a whole; a donut with <see cref="SeriesSpec.InnerRadius"/>.</summary>
    Pie,

    /// <summary>A circular bar per category, each on its own track.</summary>
    RadialBar,

    /// <summary>A marker per point, for how two measures relate (e.g. delivery distance against delivery time).</summary>
    Scatter,

    /// <summary>A grid of colored cells: every heatmap series is a row, x the columns, y the color.</summary>
    Heatmap,

    /// <summary>A band between a low and a high value per x (e.g. min to max around an average).</summary>
    RangeArea,
    /// <summary>JSON specs: picked from the data (time or numbers: line; categories: bar; shares of a whole: donut).</summary>
    Auto,
}

/// <summary>Line shape. Step holds each value until the next point.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CurveType>))]
public enum CurveType
{
    /// <summary>Straight segments between points.</summary>
    Linear,

    /// <summary>A smooth curve that does not overshoot the data.</summary>
    Monotone,

    /// <summary>Holds each value until the next point.</summary>
    Step,
}

/// <summary>Points of a line or area series to mark with a dot (combine with |).</summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<PointHighlight>))]
public enum PointHighlight
{
    /// <summary>No highlighted points.</summary>
    None = 0,

    /// <summary>The last value (the latest, for time series).</summary>
    Last = 1,

    /// <summary>The first value.</summary>
    First = 2,

    /// <summary>The lowest value.</summary>
    Min = 4,

    /// <summary>The highest value.</summary>
    Max = 8,
}

/// <summary>The scale of an axis.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScaleType>))]
public enum ScaleType
{
    /// <summary>Picked from the data: times give a time axis, numbers a linear axis, strings a band axis.</summary>
    Auto,

    /// <summary>A continuous numeric axis.</summary>
    Linear,

    /// <summary>A continuous time axis with calendar ticks.</summary>
    Time,

    /// <summary>Evenly spaced categories.</summary>
    Band,
}

/// <summary>Side of a vertical axis.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AxisPosition>))]
public enum AxisPosition
{
    /// <summary>The primary axis on the left, additional axes on the right.</summary>
    Auto,

    /// <summary>On the left of the plot.</summary>
    Left,

    /// <summary>On the right of the plot.</summary>
    Right,
}

/// <summary>When the legend is shown.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LegendMode>))]
public enum LegendMode
{
    /// <summary>Only when there is more than one series.</summary>
    Auto,

    /// <summary>Always.</summary>
    Show,

    /// <summary>Never.</summary>
    Hide,
}

/// <summary>Where the legend sits.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LegendPosition>))]
public enum LegendPosition
{
    /// <summary>Above the plot.</summary>
    Top,

    /// <summary>Below the plot.</summary>
    Bottom,

    /// <summary>To the right of the plot (moves to the top when the chart is too narrow).</summary>
    Right,
}

/// <summary>Kind of annotation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnnotationType>))]
public enum AnnotationType
{
    /// <summary>A reference line at <see cref="AnnotationSpec.Value"/> (e.g. a sales target).</summary>
    Line,

    /// <summary>A shaded band from <see cref="AnnotationSpec.From"/> to <see cref="AnnotationSpec.To"/> (e.g. a campaign period).</summary>
    Band,

    /// <summary>A marker at <see cref="AnnotationSpec.X"/>, <see cref="AnnotationSpec.Y"/>.</summary>
    Point,

    /// <summary>A point marker with its label in a text box offset from the point.</summary>
    Callout,
}

/// <summary>The axis a line or band annotation refers to.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnnotationAxis>))]
public enum AnnotationAxis
{
    /// <summary>The horizontal axis: a vertical line or band.</summary>
    X,

    /// <summary>A vertical axis: a horizontal line or band.</summary>
    Y,
}

/// <summary>Where an annotation label sits along its line or band.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LabelPosition>))]
public enum LabelPosition
{
    /// <summary>At the start: the left of a horizontal line, the bottom of a vertical line.</summary>
    Start,

    /// <summary>In the middle.</summary>
    Middle,

    /// <summary>At the end: the right of a horizontal line, the top of a vertical line.</summary>
    End,
}

/// <summary>Whether an annotation is drawn below or above the series.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnnotationLayer>))]
public enum AnnotationLayer
{
    /// <summary>Behind the series.</summary>
    Below,

    /// <summary>On top of the series.</summary>
    Above,
}

/// <summary>A condition on a field, e.g. { "field": "channel", "op": "eq", "value": "Web" }.</summary>
public sealed record FilterSpec
{
    /// <summary>Field name.</summary>
    [Description("Field name.")]
    public string Field { get; init; } = "";

    /// <summary>Comparison: eq, ne, gt, gte, lt, lte or contains (text).</summary>
    [Description("Comparison: eq, ne, gt, gte, lt, lte or contains (text).")]
    public FilterOp Op { get; init; } = FilterOp.Eq;

    /// <summary>Value to compare with: a number, a time (ISO 8601) or text.</summary>
    [Description("Value to compare with: a number, a time (ISO 8601) or text.")]
    public object? Value { get; init; }
}

/// <summary>Comparison of a <see cref="FilterSpec"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FilterOp>))]
public enum FilterOp
{
    /// <summary>Equal to the value.</summary>
    Eq,

    /// <summary>Not equal to the value.</summary>
    Ne,

    /// <summary>Greater than the value.</summary>
    Gt,

    /// <summary>Greater than or equal to the value.</summary>
    Gte,

    /// <summary>Less than the value.</summary>
    Lt,

    /// <summary>Less than or equal to the value.</summary>
    Lte,

    /// <summary>Text that contains the value, ignoring case.</summary>
    Contains,
}

/// <summary>Time interval that rows are grouped into before aggregating.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimeBucket>))]
public enum TimeBucket
{
    /// <summary>One bucket per minute.</summary>
    Minute,

    /// <summary>One bucket per hour.</summary>
    Hour,

    /// <summary>One bucket per day.</summary>
    Day,

    /// <summary>One bucket per week, starting on Monday.</summary>
    Week,

    /// <summary>One bucket per calendar month.</summary>
    Month,

    /// <summary>One bucket per calendar quarter.</summary>
    Quarter,

    /// <summary>One bucket per calendar year.</summary>
    Year,
}

/// <summary>How rows with the same x (or time bucket) are combined into one value.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Aggregate>))]
public enum Aggregate
{
    /// <summary>The sum of the values.</summary>
    Sum,

    /// <summary>The mean of the values.</summary>
    Average,

    /// <summary>The lowest value.</summary>
    Min,

    /// <summary>The highest value.</summary>
    Max,

    /// <summary>The number of rows (no y field needed).</summary>
    Count,

    /// <summary>The last value in row order, skipping missing values.</summary>
    Last,
}

/// <summary>Order of points sorted by value.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SortOrder>))]
public enum SortOrder
{
    /// <summary>Smallest first.</summary>
    Ascending,

    /// <summary>Largest first.</summary>
    Descending,
}
