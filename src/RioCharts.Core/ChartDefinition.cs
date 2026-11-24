using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RioCharts.Core;

/// <summary>
/// The chart model. Plain, serializable data: Razor components and (later) JSON definitions both
/// compile to this. Data is not part of the definition; it is passed separately by series id.
/// </summary>
public sealed record ChartDefinition
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

    /// <summary>Bar, box plot, waterfall and dumbbell charts: they run left to right with the categories down the side (first on top). For rankings and long category names. The y axis settings (title, format, min, max) apply to the values.</summary>
    [Description("Bar, box plot, waterfall and dumbbell charts: they run left to right with the categories down the side (first on top). For rankings and long category names. The y axis settings (title, format, min, max) apply to the values.")]
    public bool Horizontal { get; init; }

    /// <summary>Maps: how the round world is drawn flat. EqualEarth (default) keeps every country's true relative size; Mercator is the projection of web maps, familiar shapes but areas that grow toward the poles.</summary>
    [Description("Maps: how the round world is drawn flat. equalEarth (default) keeps every country's true relative size; mercator is the projection of web maps, familiar shapes but areas that grow toward the poles.")]
    public MapProjection Projection { get; init; }

    /// <summary>Horizontal axis.</summary>
    [Description("Horizontal axis.")]
    public AxisOptions X { get; init; } = new();

    /// <summary>Primary vertical axis (id "y" unless set). Drawn on the left and owns the grid lines.</summary>
    [Description("Primary vertical axis (id \"y\" unless set). Drawn on the left and owns the grid lines.")]
    public AxisOptions Y { get; init; } = new();

    /// <summary>Additional vertical axes, e.g. a second unit. Drawn on the right unless Position is Left. Series bind to them with yAxisId.</summary>
    [Description("Additional vertical axes, e.g. a second unit. Drawn on the right unless Position is Left. Series bind to them with yAxisId.")]
    public IReadOnlyList<AxisOptions> YAxes { get; init; } = [];

    /// <summary>Legend settings. By default it is shown only when there is more than one series.</summary>
    [Description("Legend settings. By default it is shown only when there is more than one series.")]
    public LegendOptions Legend { get; init; } = new();

    /// <summary>Tooltip settings. On by default, with one tooltip per x position across series.</summary>
    [Description("Tooltip settings. On by default, with one tooltip per x position across series.")]
    public TooltipOptions Tooltip { get; init; } = new();

    /// <summary>Series to draw, in order. Colors are assigned from the palette in this order.</summary>
    [Description("Series to draw, in order. Colors are assigned from the palette in this order.")]
    public IReadOnlyList<SeriesOptions> Series { get; init; } = [];

    /// <summary>Reference lines and bands, e.g. a target or an incident period.</summary>
    [Description("Reference lines and bands, e.g. a target or an incident period.")]
    public IReadOnlyList<Annotation> Annotations { get; init; } = [];

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
public sealed record AxisOptions
{
    /// <summary>Axis id that series and annotations refer to (vertical axes only). The primary vertical axis defaults to "y".</summary>
    [Description("Axis id that series and annotations refer to (vertical axes only). The primary vertical axis defaults to \"y\".")]
    public string? Id { get; init; }

    /// <summary>Axis title.</summary>
    [Description("Axis title.")]
    public string? Title { get; init; }

    /// <summary>
    /// Vertical axes: CSS color of the tick labels and title, e.g. to show which axis belongs to which line. Null: the
    /// muted axis color (or the series color with <see cref="SeriesColor"/>).
    /// </summary>
    [Description("Vertical axes: CSS color of the tick labels and title. Null: the muted axis color.")]
    public string? Color { get; init; }

    /// <summary>
    /// Vertical axes: color the tick labels and title like the first visible series on the axis, so with one axis per
    /// series each axis matches its line. <see cref="Color"/> wins when both are set.
    /// </summary>
    [Description("Vertical axes: color the tick labels and title like the first visible series on the axis (one axis per series). color wins when both are set.")]
    public bool SeriesColor { get; init; }

    /// <summary>
    /// X axis: .NET format string for the x value in tooltip headers only, e.g. "HH:mm" or "d MMM HH:mm". Unlike
    /// <see cref="Format"/> it leaves the axis labels alone, and on a time axis it also leaves out the time zone offset
    /// that tooltips otherwise add when the chart's zone differs from the viewer's. Null: automatic.
    /// </summary>
    [Description("X axis: .NET format string for the x value in tooltip headers, e.g. \"d MMM HH:mm\". The axis labels keep their format; no time zone offset is added. Null: automatic.")]
    public string? TooltipFormat { get; init; }

    /// <summary>Fixed minimum. When null it comes from the data.</summary>
    [Description("Fixed minimum. When null it comes from the data.")]
    public double? Min { get; init; }

    /// <summary>Fixed maximum. When null it comes from the data.</summary>
    [Description("Fixed maximum. When null it comes from the data.")]
    public double? Max { get; init; }

    /// <summary>Scale type. Auto picks from the data: times give a time axis, numbers a linear axis, strings a band axis.</summary>
    [Description("Scale type. Auto picks from the data: times give a time axis, numbers a linear axis, strings a band axis.")]
    public ScaleType Scale { get; init; } = ScaleType.Auto;

    /// <summary>
    /// .NET format string for labels, e.g. "P0", "C0" or "yyyy-MM-dd". Null for automatic formatting. On a value axis, series
    /// without a format of their own also use it for their tooltips and data labels (a series' Format wins). Ticks are
    /// never closer than the format can tell apart: with "#,0" they are whole numbers.
    /// </summary>
    [Description(".NET format string for labels, e.g. \"P0\", \"C0\" or \"yyyy-MM-dd\". Null for automatic formatting. On a value axis, series without their own format use it for tooltips and data labels too.")]
    public string? Format { get; init; }

    /// <summary>
    /// A function that writes the labels of a numeric axis, e.g. bytes as "1.5 MB": used instead of <see cref="Format"/>.
    /// Like Format, it also writes the tooltips and data labels of series on the axis that have no format of their own,
    /// and the screen reader text. Ticks stay on round steps (1, 2 or 5 times a power of ten), never closer than the
    /// function can tell apart from zero. Not used on time or category axes, and not part of JSON definitions.
    /// </summary>
    [JsonIgnore]
    public Func<double, string>? LabelFormatter { get; init; }

    /// <summary>
    /// Numeric axes: the base of the tick steps' unit. With 1024, for bytes, a step is 1, 2 or 5 × a power of ten of the
    /// largest power of 1024 below it (e.g. 20 × 1024² bytes), so with a 1024-based <see cref="LabelFormatter"/> the ticks
    /// read 20 MB, 40 MB, 60 MB instead of 47.7 MB, 95.4 MB. Null (or 1 and below): plain powers of ten. Not used on log axes.
    /// </summary>
    [Description("Numeric axes: the base of the tick steps' unit, e.g. 1024 for bytes (steps of 1, 2 or 5 × 10^k × 1024^n, so labels in KB/MB read round). Null: powers of ten.")]
    public double? TickBase { get; init; }

    /// <summary>Draw grid lines. Null uses the default: on for the primary vertical axis, off otherwise.</summary>
    [Description("Draw grid lines. Null uses the default: on for the primary vertical axis, off otherwise.")]
    public bool? GridLines { get; init; }

    /// <summary>
    /// Angle of category labels on the x axis, in degrees from 0 to 90; they slope down to the left. Null (the default):
    /// horizontal when every label fits, otherwise 45 degrees. 0 keeps them horizontal and shows every n-th label when
    /// they do not all fit. Rotated labels still thin out when even they overlap.
    /// </summary>
    [Description("Angle of category labels on the x axis, 0 to 90 degrees (sloping down to the left). Null: horizontal when they fit, else 45. 0: horizontal, every n-th label when they do not fit.")]
    public double? LabelRotation { get; init; }

    /// <summary>Side of a vertical axis. Auto: the primary axis on the left, additional axes on the right.</summary>
    [Description("Side of a vertical axis. Auto: the primary axis on the left, additional axes on the right.")]
    public AxisPosition Position { get; init; } = AxisPosition.Auto;

    /// <summary>
    /// How stacked series on a vertical axis add up. Percent: each stack fills the axis (0–100 %), so the series show
    /// their share per x value; line, area and bar series without a stack name are stacked together.
    /// </summary>
    [Description("Vertical axes: normal, or percent to stack the line, area and bar series on the axis to 100 % (each series' share per x value). Series without a stack name are stacked together.")]
    public StackMode Stacking { get; init; } = StackMode.Normal;
}

/// <summary>Legend settings: when to show it and where.</summary>
public sealed record LegendOptions
{
    /// <summary>Auto shows the legend only when there is more than one series.</summary>
    [Description("Auto shows the legend only when there is more than one series.")]
    public LegendMode Mode { get; init; } = LegendMode.Auto;

    /// <summary>Where the legend sits. Default Top.</summary>
    public LegendPosition Position { get; init; } = LegendPosition.Top;

    /// <summary>Bubble charts: a size legend right of the plot, with circles for a few round sizes. Null: shown when the chart is at least 360 px wide and has room for a legend.</summary>
    [Description("Bubble charts: a size legend right of the plot, with circles for a few round sizes. Null: shown when the chart is at least 360 px wide and has room for a legend.")]
    public bool? Sizes { get; init; }
}

/// <summary>Tooltip settings.</summary>
public sealed record TooltipOptions
{
    /// <summary>Show a tooltip and crosshair when hovering the plot.</summary>
    [Description("Show a tooltip and crosshair when hovering the plot.")]
    public bool Enabled { get; init; } = true;

    /// <summary>True: one tooltip per x position listing every series. False: only the nearest point.</summary>
    [Description("True: one tooltip per x position listing every series. False: only the nearest point.")]
    public bool Shared { get; init; } = true;
}

/// <summary>
/// One series: its type, where its data comes from (fields and transforms in JSON definitions) and how it looks.
/// </summary>
public sealed record SeriesOptions
{
    /// <summary>
    /// The id of a pie's or donut's "Other" slice (<see cref="GroupSmallSlices"/>) in its legend and in the
    /// chart's hidden series: hide it with this id.
    /// </summary>
    public const string OtherSliceKey = "rio:other";

    /// <summary>Unique id of the series. Optional in JSON definitions (generated from the y field).</summary>
    [Description("Unique id of the series. Optional in JSON definitions (generated from the y field).")]
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
    public IReadOnlyList<Filter>? Filter { get; init; }

    /// <summary>Time x: group rows into buckets (minute, hour, day, week, month, quarter, year, or auto to pick one from the time span; in the chart's timeZone when set) and aggregate y per bucket.</summary>
    [Description("Time x: group rows into buckets (minute, hour, day, week, month, quarter, year, or auto to pick one from the time span; in the chart's timeZone when set) and aggregate y per bucket.")]
    public TimeBucket? TimeBucket { get; init; }

    /// <summary>Combine rows with the same x (or time bucket): sum, average, min, max, count or last.</summary>
    [Description("Combine rows with the same x (or time bucket): sum, average, min, max, count or last.")]
    public Aggregate? Aggregate { get; init; }

    /// <summary>Field to split by: one series per distinct value (e.g. one line per sales channel).</summary>
    [Description("Field to split by: one series per distinct value (e.g. one line per sales channel).")]
    public string? GroupBy { get; init; }

    /// <summary>
    /// Order points by y (for categories, e.g. a ranking): ascending or descending. On a category x axis the categories
    /// follow this series' values after aggregation (the first series with a sort decides).
    /// </summary>
    [Description("Order points by y (for categories, e.g. a ranking): ascending or descending. The category axis follows this series' values after aggregation.")]
    public ChartSort? Sort { get; init; }

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

    /// <summary>Range area: the field with the low value of each point (y is the high value). Box plot with precomputed boxes: the lower whisker end.</summary>
    [Description("Range area: the field with the low value of each point (y is the high value). Box plot with precomputed boxes: the lower whisker end.")]
    public string? Low { get; init; }

    /// <summary>Candlestick with precomputed candles: the field with each period's first value (y is the close, low and high the extremes). Leave it out to make candles from the raw y values per timeBucket.</summary>
    [Description("Candlestick with precomputed candles: the field with each period's first value (y is the close, low and high the extremes). Leave it out to make candles from the raw y values per timeBucket.")]
    public string? Open { get; init; }

    /// <summary>Box plot with precomputed boxes: the field with the lower quartile (y is the median). Leave out q1, q3, low and high to compute the boxes from the raw y values per x.</summary>
    [Description("Box plot with precomputed boxes: the field with the lower quartile (y is the median). Leave out q1, q3, low and high to compute the boxes from the raw y values per x.")]
    public string? Q1 { get; init; }

    /// <summary>Box plot with precomputed boxes: the field with the upper quartile.</summary>
    [Description("Box plot with precomputed boxes: the field with the upper quartile.")]
    public string? Q3 { get; init; }

    /// <summary>Box plot with precomputed boxes: the field with the upper whisker end.</summary>
    [Description("Box plot with precomputed boxes: the field with the upper whisker end.")]
    public string? High { get; init; }

    /// <summary>Range bar: the field with the row of each bar (e.g. the machine or task); text. Null: one row per series.</summary>
    [Description("Range bar: the field with the row of each bar (e.g. the machine or task); text. Null: one row per series.")]
    public string? Row { get; init; }

    /// <summary>Range bar: the field with each bar's start, a time (or a number).</summary>
    [Description("Range bar: the field with each bar's start, a time (or a number).")]
    public string? Start { get; init; }

    /// <summary>Range bar: the field with each bar's end, of the same kind as start.</summary>
    [Description("Range bar: the field with each bar's end, of the same kind as start.")]
    public string? End { get; init; }

    /// <summary>Waterfall: the field that marks total rows (true, yes or 1). A total bar shows the running total from zero; its own y is ignored. Other rows are changes.</summary>
    [Description("Waterfall: the field that marks total rows (true, yes or 1). A total bar shows the running total from zero; its own y is ignored. Other rows are changes.")]
    public string? Total { get; init; }

    /// <summary>Histogram: the width of each bin, in x units (e.g. 5 for bins of 5 ms). Null: a nice width picked from the data.</summary>
    [Description("Histogram: the width of each bin, in x units (e.g. 5 for bins of 5 ms). Null: a nice width picked from the data.")]
    public double? BinWidth { get; init; }

    /// <summary>Histogram: about how many bins, when binWidth is not set; the width is rounded to a nice number. Null: picked from the data.</summary>
    [Description("Histogram: about how many bins, when binWidth is not set; the width is rounded to a nice number. Null: picked from the data.")]
    public int? BinCount { get; init; }

    /// <summary>
    /// Scatter: marker diameter in pixels. Default 5. Bubbles: the diameter of the largest bubble; default from the chart size.
    /// Line and area: the diameter of the dots from <see cref="Highlight"/>; default from the line width (for a 2 px line,
    /// 5 px, and 7 px for dots on every point).
    /// </summary>
    [Description("Scatter: marker diameter in pixels. Default 5. Bubbles: the diameter of the largest bubble; default from the chart size. Line and area: the diameter of the highlight dots; default from the line width.")]
    public double? MarkerSize { get; init; }

    /// <summary>
    /// Bars: the widest a bar may be, in pixels. Null: as wide as the band allows, but a group of bars never takes more
    /// than a fifth of the plot (as with four categories), so a lone bar does not fill the chart.
    /// </summary>
    [Description("Bars: the widest a bar may be, in pixels. Null: as wide as the band allows, but never more than a fifth of the plot.")]
    public double? MaxBarWidth { get; init; }

    /// <summary>Map bubbles: on the world map, one bubble per country with the sum of its places; zoomed into a country, each place. Default true.</summary>
    [Description("Map bubbles: on the world map, one bubble per country with the sum of its places; zoomed into a country, each place. Default true.")]
    public bool? GroupByCountry { get; init; }

    /// <summary>Map bubbles: the field with each place's latitude in degrees north.</summary>
    [Description("Map bubbles (type \"mapBubble\"): the field with each place's latitude in degrees north.")]
    public string? Latitude { get; init; }

    /// <summary>Map bubbles: the field with each place's longitude in degrees east.</summary>
    [Description("Map bubbles (type \"mapBubble\"): the field with each place's longitude in degrees east.")]
    public string? Longitude { get; init; }

    /// <summary>Scatter: the field with each point's size, which makes a bubble chart. A bubble's area is proportional to its size; sizes of zero or less, or missing, are not drawn. All bubble series in a chart share one size scale. Aggregated points: the sum.</summary>
    [Description("Scatter: the field with each point's size, which makes a bubble chart (type \"bubble\" is scatter with a size). A bubble's area is proportional to its size; sizes of zero or less, or missing, are not drawn. All bubble series in a chart share one size scale. Aggregated points: the sum.")]
    public string? Size { get; init; }

    /// <summary>Bubbles: what the size is, shown with it in the tooltip (e.g. "Population"). Null: the size field's name.</summary>
    [Description("Bubbles: what the size is, shown with it in the tooltip (e.g. \"Population\"). Null: the size field's name.")]
    public string? SizeName { get; init; }

    /// <summary>Bubbles: .NET format string for the size in tooltips, e.g. "0 M". Null: automatic.</summary>
    [Description("Bubbles: .NET format string for the size in tooltips, e.g. \"0 M\". Null: automatic.")]
    public string? SizeFormat { get; init; }

    /// <summary>Scatter: draw a dashed least-squares trend line through the points.</summary>
    [Description("Scatter: draw a dashed least-squares trend line through the points.")]
    public bool TrendLine { get; init; }

    /// <summary>
    /// Candlestick: color candles by direction, rising in the positive and falling in the negative theme color (green
    /// and red), with the change in percent in the tooltip, as for prices. Off: candles take the series color, and the
    /// fill alone shows the direction (light when rising, solid when falling).
    /// </summary>
    [Description("Candlestick: color candles by direction (rising green, falling red) and show the change in percent, as for prices. Off (default): the series color, with a light fill for rising and a solid fill for falling candles.")]
    public bool DirectionColors { get; init; }

    /// <summary>Line and area series: mark points with a dot. One or more of last, first, min, max, comma-separated (e.g. "last, max"), or all.</summary>
    [Description("Line and area series: mark points with a dot. One or more of last, first, min, max, comma-separated (e.g. \"last, max\"), or all (a dot on every point, for sparse data).")]
    public PointHighlight Highlight { get; init; }

    /// <summary>Pie, donut, radial bar and gauge: inner radius as a fraction of the outer radius (0 = pie, 0.6 = donut). Gauge default 0.8.</summary>
    [Description("Pie, donut, radial bar and gauge: inner radius as a fraction of the outer radius (0 = pie, 0.6 = donut). Gauge default 0.8.")]
    public double? InnerRadius { get; init; }

    /// <summary>Pie, donut, radial bar and gauge: start angle in degrees, clockwise from 12 o'clock. Default 0 (gauge: -120).</summary>
    [Description("Pie, donut, radial bar and gauge: start angle in degrees, clockwise from 12 o'clock. Default 0 (gauge: -120).")]
    public double? StartAngle { get; init; }

    /// <summary>Pie, donut, radial bar and gauge: end angle in degrees. Default: start + 360 for pies, 270 for radial bars, 120 for gauges (-90 and 90 make a half circle).</summary>
    [Description("Pie, donut, radial bar and gauge: end angle in degrees. Default: start + 360 for pies, 270 for radial bars, 120 for gauges (-90 and 90 make a half circle).")]
    public double? EndAngle { get; init; }

    /// <summary>Pie and donut: slices smaller than this share of the total (e.g. 0.03) are grouped into one "Other" slice. Treemap: items smaller than this share of their group, per group.</summary>
    [Description("Pie and donut: slices smaller than this share of the total (e.g. 0.03) are grouped into one \"Other\" slice. Treemap: items smaller than this share of their group, per group.")]
    public double? GroupSmallSlices { get; init; }

    /// <summary>Treemap: the fields that name the groups, from the outermost to the item, e.g. ["region", "country", "city"]; used instead of x. Without it, x is the item's path, e.g. "Europe/Sweden/Stockholm".</summary>
    [Description("Treemap: the fields that name the groups, from the outermost to the item, e.g. [\"region\", \"country\", \"city\"]; used instead of x. Without it, x is the item's path, e.g. \"Europe/Sweden/Stockholm\".")]
    public IReadOnlyList<string>? Path { get; init; }

    /// <summary>Treemap: how many levels to draw at once below the group shown, e.g. 2; deeper groups show as one cell with their total and open on a click. Null: all levels.</summary>
    [Description("Treemap: how many levels to draw at once below the group shown, e.g. 2; deeper groups show as one cell with their total and open on a click. Null: all levels.")]
    public int? VisibleLevels { get; init; }

    /// <summary>Colors by value: a line or area changes color where it crosses into a band (e.g. red above 120), a bar, scatter point or heatmap cell takes the color of the band its value is in (a heatmap's legend then lists the bands). Later bands win where they overlap; a point's own color (colorField) wins over bands.</summary>
    [Description("Colors by value: a line or area changes color where it crosses into a band (e.g. red above 120), a bar, scatter point or heatmap cell takes the color of the band its value is in (a heatmap's legend then lists the bands). Later bands win where they overlap; a point's own color (colorField) wins over bands.")]
    public IReadOnlyList<ColorBand>? ColorBands { get; init; }

    /// <summary>The field with each row's own color (a CSS color such as "#d6485a", or a theme token such as "--rio-negative"); empty keeps the series color. Bars, scatter markers, slices and treemap items take it; lines and areas mark the point. Aggregated points take their first row's color.</summary>
    [Description("The field with each row's own color (a CSS color such as \"#d6485a\", or a theme token such as \"--rio-negative\"); empty keeps the series color. Bars, scatter markers, slices and treemap items take it; lines and areas mark the point. Aggregated points take their first row's color.")]
    public string? ColorField { get; init; }

    /// <summary>Treemap: what separates the groups in an x path. Default "/".</summary>
    [Description("Treemap: what separates the groups in an x path. Default \"/\".")]
    public string? PathSeparator { get; init; }

    /// <summary>Radial bar, gauge and bullet: the value of a full track (e.g. 100 for percentages). Default: a nice value above the largest.</summary>
    [Description("Radial bar, gauge and bullet: the value of a full track (e.g. 100 for percentages). Default: a nice value above the largest.")]
    public double? Max { get; init; }

    /// <summary>Gauge and bullet: the value at the start of the scale. Default 0 (or a nice value below the lowest, when a value is negative).</summary>
    [Description("Gauge and bullet: the value at the start of the scale. Default 0 (or a nice value below the lowest, when a value is negative).")]
    public double? Min { get; init; }

    /// <summary>Heatmap and map: colors diverge from this value, e.g. 0 for changes or 20 for °C. Values above it take the series color, values below it belowColor, stronger the further they are from it. Null: one color, stronger for higher values.</summary>
    [Description("Heatmap and map: colors diverge from this value, e.g. 0 for changes or 20 for °C. Values above it take the series color, values below it belowColor, stronger the further they are from it. Null: one color, stronger for higher values.")]
    public double? Midpoint { get; init; }

    /// <summary>Heatmap and map with a midpoint: the color of values below it. Default the negative theme color (--rio-negative).</summary>
    [Description("Heatmap and map with a midpoint: the color of values below it. Default the negative theme color (--rio-negative).")]
    public string? BelowColor { get; init; }

    /// <summary>Gauge and bullet: a target value, marked across the scale and given in the tooltip. A bullet's targetField gives one per row instead.</summary>
    [Description("Gauge and bullet: a target value, marked across the scale and given in the tooltip. A bullet's targetField gives one per row instead.")]
    public double? Target { get; init; }

    /// <summary>Dumbbell: the field with each row's first value (before, plan), drawn as a hollow dot; y is the second (after, actual).</summary>
    [Description("Dumbbell: the field with each row's first value (before, plan), drawn as a hollow dot; y is the second (after, actual).")]
    public string? From { get; init; }

    /// <summary>Error bars on bar, line and scatter series: the same plus-or-minus amount for every point, e.g. 2.5.</summary>
    [Description("Error bars on bar, line and scatter series: the same plus-or-minus amount for every point, e.g. 2.5.")]
    public double? Error { get; init; }

    /// <summary>Error bars: the field with each row's plus-or-minus amount (e.g. a standard deviation). Aggregated rows take the average.</summary>
    [Description("Error bars: the field with each row's plus-or-minus amount (e.g. a standard deviation). Aggregated rows take the average.")]
    public string? ErrorField { get; init; }

    /// <summary>Error bars: the field with the low end of each row's error bar (a value, e.g. a confidence interval's lower bound); use with errorHigh. Aggregated rows take the lowest.</summary>
    [Description("Error bars: the field with the low end of each row's error bar (a value, e.g. a confidence interval's lower bound); use with errorHigh. Aggregated rows take the lowest.")]
    public string? ErrorLow { get; init; }

    /// <summary>Error bars: the field with the high end of each row's error bar; use with errorLow. Aggregated rows take the highest.</summary>
    [Description("Error bars: the field with the high end of each row's error bar; use with errorLow. Aggregated rows take the highest.")]
    public string? ErrorHigh { get; init; }

    /// <summary>Bullet: the field with each row's target value (numbers), aggregated like y.</summary>
    [Description("Bullet: the field with each row's target value (numbers), aggregated like y.")]
    public string? TargetField { get; init; }

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
public sealed record Annotation
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

    /// <summary>Slices of a circle, for shares of a whole; a donut with <see cref="SeriesOptions.InnerRadius"/>.</summary>
    Pie,

    /// <summary>A circular bar per category, each on its own track.</summary>
    RadialBar,

    /// <summary>A marker per point, for how two measures relate (e.g. delivery distance against delivery time).</summary>
    Scatter,

    /// <summary>A grid of colored cells: every heatmap series is a row, x the columns, y the color.</summary>
    Heatmap,

    /// <summary>A band between a low and a high value per x (e.g. min to max around an average).</summary>
    RangeArea,

    /// <summary>The spread of values per x: a box from the lower to the upper quartile, the median, whiskers and outliers.</summary>
    BoxPlot,

    /// <summary>How a value moved within each period: open, high, low and close.</summary>
    Candlestick,

    /// <summary>A closed shape per series across category spokes, for comparing several measures (e.g. products' test scores).</summary>
    Radar,

    /// <summary>Rectangles sized by value, nested by group (a path such as "Europe/Sweden/Stockholm"), for how a total splits into many parts.</summary>
    Treemap,

    /// <summary>How a start value becomes an end value: a floating bar per change (rising or falling from the running total), and total bars from zero.</summary>
    Waterfall,

    /// <summary>The distribution of values: x values counted in equal-width bins, drawn as bars without gaps.</summary>
    Histogram,

    /// <summary>Bars from a start to an end on a time (or number) axis, in rows: schedules, or states over time such as running, idle and stopped.</summary>
    RangeBar,

    /// <summary>A single value on a scale from min to max, drawn as an arc with the value in the middle; colorBands color the scale's ranges and a target is marked. One gauge per data point.</summary>
    Gauge,

    /// <summary>Bullets: a bar for each value on a horizontal scale, in a row of its own, over colored ranges (colorBands) with a target mark: a KPI against its target in little space. Each series has its own scale.</summary>
    Bullet,

    /// <summary>Dumbbells: two values per category joined by a line, from a hollow dot (from: before, plan) to a filled dot (y: after, actual).</summary>
    Dumbbell,

    /// <summary>A world map with each country colored by its value (x: the country's ISO code or English name); a click zooms into a country.</summary>
    Map,

    /// <summary>Bubbles on a world map at each point's latitude and longitude, sized by value (e.g. users per city).</summary>
    MapBubble,

    /// <summary>A funnel: one segment per stage (x: the stage, y: how many reached it), top to bottom in input order, each as wide as its share of the largest and narrowing to the next, with its value and its share of the stage before.</summary>
    Funnel,

    /// <summary>A calendar heatmap: a square per day (x: a date or time), weeks as columns and weekdays as rows, colored by the day's value. Rows on the same day add up (or use aggregate); without y each day counts its rows. One block per year for more than a year of data.</summary>
    Calendar,

    /// <summary>JSON definitions: picked from the data (time or numbers: line; categories: bar; shares of a whole: donut).</summary>
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

    /// <summary>Every point: shows where the values are when there are few of them.</summary>
    All = 16,
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

    /// <summary>
    /// A logarithmic numeric axis: equal ratios get equal distances, for data that spans several powers of ten. Ticks
    /// are powers of ten (with 2 and 5 between them for short ranges). Values of zero or below have no place on it:
    /// they are left out (gaps in lines) with a warning. On the x axis for numbers under lines, areas and scatter
    /// points, not bars.
    /// </summary>
    Log,
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

/// <summary>How a map draws the round world flat.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MapProjection>))]
public enum MapProjection
{
    /// <summary>Equal Earth: equal-area, so every country keeps its true relative size; the outline of the world is rounded.</summary>
    EqualEarth,

    /// <summary>Mercator, as on web maps: familiar shapes on a rectangle, but areas grow toward the poles; cut at ±85° latitude.</summary>
    Mercator,
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
    /// <summary>A reference line at <see cref="Annotation.Value"/> (e.g. a sales target).</summary>
    Line,

    /// <summary>A shaded band from <see cref="Annotation.From"/> to <see cref="Annotation.To"/> (e.g. a campaign period).</summary>
    Band,

    /// <summary>A marker at <see cref="Annotation.X"/>, <see cref="Annotation.Y"/>.</summary>
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

/// <summary>
/// A color for values in a range, e.g. { "above": 120, "color": "--rio-negative" }: a line or
/// area takes it where it runs in the range, a bar or scatter point when its value is in it.
/// </summary>
public sealed record ColorBand
{
    /// <summary>Values above this take the color. Null: no lower bound.</summary>
    [Description("Values above this take the color. Null: no lower bound.")]
    public double? Above { get; init; }

    /// <summary>Values below this take the color. Null: no upper bound.</summary>
    [Description("Values below this take the color. Null: no upper bound.")]
    public double? Below { get; init; }

    /// <summary>A CSS color or a theme token such as "--rio-negative".</summary>
    [Description("A CSS color or a theme token such as \"--rio-negative\".")]
    public string Color { get; init; } = "";

    /// <summary>A name for the band, e.g. "High": a heatmap's legend shows it instead of the band's range. Null: the range ("&gt; 80").</summary>
    [Description("A name for the band, e.g. \"High\": a heatmap's legend shows it instead of the range. Null: the range (\"> 80\").")]
    public string? Label { get; init; }
}

/// <summary>A condition on a field, e.g. { "field": "channel", "op": "eq", "value": "Web" }.</summary>
public sealed record Filter
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

/// <summary>Comparison of a <see cref="Filter"/>.</summary>
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

/// <summary>How stacked series add up on their axis.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StackMode>))]
public enum StackMode
{
    /// <summary>Values stack on each other: the top of a stack is the total.</summary>
    Normal,

    /// <summary>Each stack fills the axis (100 %): the series show their share of the total per x value.</summary>
    Percent,
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

    /// <summary>
    /// Picks one of the others from the data's time span: the one that gives closest to 50 buckets
    /// (e.g. weeks for a year of data, hours for a day). Chosen once per data change.
    /// </summary>
    Auto,
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
[JsonConverter(typeof(JsonStringEnumConverter<ChartSort>))]
public enum ChartSort
{
    /// <summary>Smallest first.</summary>
    Ascending,

    /// <summary>Largest first.</summary>
    Descending,
}
