using RioCharts.Core;
using RioCharts.Core.Data;

namespace RioCharts.Blazor;

/// <summary>A series as the chart sees it: its spec and its columnar data.</summary>
internal interface ISeriesSource
{
    SeriesSpec Spec { get; }
    SeriesData Data { get; }

    /// <summary>The input item at an index into the items list, or null for columnar data.</summary>
    object? ItemAt(int inputIndex);

    /// <summary>
    /// The items-list indexes behind a point's source index: one, or the group's when the series
    /// aggregates; empty when unknown.
    /// </summary>
    IReadOnlyList<int> InputIndexes(int sourceIndex);

    /// <summary>True when the series has a <c>TooltipValue</c> formatter.</summary>
    bool HasTooltipValue { get; }

    /// <summary>The series' <c>TooltipValue</c> text for a point, or null for the default.</summary>
    string? FormatTooltipValue(TooltipPointInfo point);
}

/// <summary>An annotation component as the chart sees it.</summary>
internal interface IAnnotationSource
{
    AnnotationSpec Spec { get; }

    /// <summary>The user dropped a draggable annotation at a new value.</summary>
    Task DraggedAsync(object value);
}

/// <summary>Cascaded from <see cref="RioChart{TItem}"/> to its series and annotation components.</summary>
internal interface IChartContext
{
    /// <summary>The chart's Items, used by series that do not set their own.</summary>
    object? DefaultItems { get; }

    /// <summary>Adds a series (once) and returns its id, generating one when none is given.</summary>
    string Register(ISeriesSource series, string? requestedId);

    void Unregister(ISeriesSource series);

    /// <summary>The series' spec or data changed; the chart lays out again.</summary>
    void SeriesChanged(ISeriesSource series);

    /// <summary>A live buffer changed (any thread); the chart redraws at its refresh rate.</summary>
    void DataArrived();

    /// <summary>Adds an annotation (once), or lays out again after its spec changed.</summary>
    void AnnotationChanged(IAnnotationSource annotation);

    void Unregister(IAnnotationSource annotation);
}

/// <summary>Raised when a legend item is clicked (the series has already been toggled).</summary>
public sealed record LegendClickEventArgs(string SeriesId, string Name, bool Visible);

/// <summary>Raised when a point or bar is clicked.</summary>
/// <param name="Item">The input item (the first of the group when the series aggregates), or default for columnar data.</param>
/// <param name="Index">Index into the series' input (items list or columns); the first item's when the series aggregates.</param>
/// <param name="X">The x value: a number, DateTime or category name.</param>
/// <param name="Y">The y value (the series' own value, also when stacked).</param>
public sealed record PointClickEventArgs<TItem>(string SeriesId, string SeriesName, TItem? Item, int Index, object? X, double Y)
{
    /// <summary>
    /// All input items behind the point: the group when the series aggregates (<c>Aggregate</c> or
    /// <c>YAggregate</c>), else just <see cref="Item"/>; empty for columnar data.
    /// </summary>
    public IReadOnlyList<TItem> Items { get; init; } = [];
}

/// <summary>
/// A range on the x axis (the visible window when zoomed). Values are <see cref="DateTime"/> on
/// time axes and <see cref="double"/> on numeric axes; DateTimeOffset, DateOnly and numbers are
/// accepted as input.
/// </summary>
/// <param name="Min">Start of the range.</param>
/// <param name="Max">End of the range.</param>
public sealed record ChartRange(object Min, object Max)
{
    /// <summary>A range on a time axis.</summary>
    public static ChartRange Of(DateTime min, DateTime max) => new(min, max);

    /// <summary>A range on a numeric axis.</summary>
    public static ChartRange Of(double min, double max) => new(min, max);
}

/// <summary>
/// Raised before a zoom or pan is applied. Change <see cref="Range"/> to clamp it, or set
/// <see cref="Cancel"/> to keep the current view. <see cref="Range"/> is null for a reset.
/// </summary>
public sealed class ZoomChangingEventArgs
{
    /// <summary>The range about to be shown; null when the zoom is reset.</summary>
    public ChartRange? Range { get; set; }
    /// <summary>Set to true to keep the current view.</summary>
    public bool Cancel { get; set; }
}

/// <summary>Raised when the user has dragged a reference line to a new value.</summary>
/// <param name="AnnotationId">The annotation's <c>Id</c>.</param>
/// <param name="Label">The annotation's label.</param>
/// <param name="Value">The new value: DateTime on time axes, else double.</param>
public sealed record AnnotationChangedEventArgs(string AnnotationId, string? Label, object Value);

/// <summary>Raised when a reference line or band is clicked.</summary>
/// <param name="AnnotationId">The annotation's <c>Id</c>.</param>
/// <param name="Label">The annotation's label.</param>
public sealed record AnnotationClickEventArgs(string AnnotationId, string? Label);

/// <summary>Where the chart draws its series.</summary>
public enum SeriesLayer
{
    /// <summary>SVG elements (default): crisp, styleable, one path per series.</summary>
    Svg,

    /// <summary>
    /// A canvas over the SVG: no DOM per series, for many or dense series. Axes, labels and
    /// annotations stay SVG.
    /// </summary>
    Canvas,
}

