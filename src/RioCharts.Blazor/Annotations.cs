using Microsoft.AspNetCore.Components;
using RioCharts.Core;
using RioCharts.Core.Data;

namespace RioCharts.Blazor;

/// <summary>
/// Base for annotation components. Like series, they render nothing themselves: they register
/// an <see cref="AnnotationSpec"/> with the chart.
/// </summary>
public abstract class AnnotationBase : ComponentBase, IAnnotationSource, IDisposable
{
    [CascadingParameter] internal IChartContext? Chart { get; set; }

    /// <summary>Optional id, reported by <c>RioChart.OnAnnotationClick</c> and <c>OnAnnotationChanged</c>.</summary>
    [Parameter] public string? Id { get; set; }

    /// <summary>Vertical axis id for Y annotations, points and callouts. Default: the primary axis.</summary>
    [Parameter] public string? YAxisId { get; set; }

    /// <summary>Text shown next to the annotation.</summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>CSS color. Default: the muted annotation color (<c>--rio-annotation</c>).</summary>
    [Parameter] public string? Color { get; set; }

    /// <summary>Grow the axis range to include the annotation. Default: yes, except for bands.</summary>
    [Parameter] public bool? ExpandsDomain { get; set; }

    /// <summary>Draw below or above the series. Default: bands below, the rest above.</summary>
    [Parameter] public AnnotationLayer? Layer { get; set; }

    internal AnnotationSpec Spec { get; private set; } = new();

    AnnotationSpec IAnnotationSource.Spec => Spec;

    /// <summary>The annotation's spec from the current parameters.</summary>
    protected abstract AnnotationSpec CreateSpec();

    /// <summary>A spec of the given type with the parameters every annotation shares.</summary>
    protected AnnotationSpec Common(AnnotationType type) => new()
    {
        Id = Id,
        Type = type,
        YAxisId = YAxisId,
        Label = Label,
        Color = Color,
        ExpandsDomain = ExpandsDomain,
        Layer = Layer,
    };

    private bool _registered;

    /// <summary>Registers the annotation with the chart, or updates it when its spec changed.</summary>
    protected override void OnParametersSet()
    {
        if (Chart is null)
        {
            throw new InvalidOperationException($"{GetType().Name} must be placed inside a <RioChart>.");
        }

        var spec = CreateSpec();
        if (!_registered || spec != Spec)
        {
            Spec = spec;
            _registered = true;
            Chart.AnnotationChanged(this);
        }
    }

    Task IAnnotationSource.DraggedAsync(object value) => OnDraggedAsync(value);

    /// <summary>A draggable annotation was dropped at a new value (DateTime on time axes, else double).</summary>
    protected virtual Task OnDraggedAsync(object value) => Task.CompletedTask;

    /// <summary>Never renders: the chart draws annotations.</summary>
    protected override bool ShouldRender() => false;

    /// <summary>Removes the annotation from the chart.</summary>
    public void Dispose()
    {
        Chart?.Unregister(this);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// A horizontal (Axis="Y", default) or vertical (Axis="X") line, e.g. a target or "today".
/// With <see cref="Draggable"/> the user can move it; bind the value with <c>@bind-Value</c>.
/// <code>&lt;ReferenceLine @bind-Value="limit" Label="Limit" Draggable="true" /&gt;</code>
/// </summary>
/// <typeparam name="TValue">A number type, DateTime, DateTimeOffset, DateOnly or string (category).</typeparam>
public sealed class ReferenceLine<TValue> : AnnotationBase
{
    /// <summary>Position in data units.</summary>
    [Parameter, EditorRequired] public TValue? Value { get; set; }

    /// <summary>Called with the new value when the user drags the line.</summary>
    [Parameter] public EventCallback<TValue> ValueChanged { get; set; }

    /// <summary>X for a vertical line; Y (default) for a horizontal one.</summary>
    [Parameter] public AnnotationAxis Axis { get; set; } = AnnotationAxis.Y;

    /// <summary>Where the label sits along the line. Default: End.</summary>
    [Parameter] public LabelPosition? LabelPosition { get; set; }

    /// <summary>Let the user drag the line; the new value goes to <see cref="ValueChanged"/>.</summary>
    [Parameter] public bool Draggable { get; set; }

    /// <inheritdoc />
    protected override AnnotationSpec CreateSpec() => Common(AnnotationType.Line) with
    {
        Value = Value,
        Axis = Axis,
        LabelPosition = LabelPosition,
        Draggable = Draggable,
    };

    /// <inheritdoc />
    protected override Task OnDraggedAsync(object value) =>
        AxisValues.TryConvert<TValue>(value, Value, out var converted) ? ValueChanged.InvokeAsync(converted) : Task.CompletedTask;
}

/// <summary>
/// A range on the y axis (an acceptable zone) or the x axis (a period such as a fault).
/// <code>&lt;ReferenceBand Axis="AnnotationAxis.X" From="faultStart" To="faultEnd" Label="Fault" /&gt;</code>
/// </summary>
public sealed class ReferenceBand : AnnotationBase
{
    /// <summary>Start of the range: a number, DateTime, DateTimeOffset, DateOnly or category name.</summary>
    [Parameter, EditorRequired] public object? From { get; set; }

    /// <summary>End of the range, in the same units as <see cref="From"/>.</summary>
    [Parameter, EditorRequired] public object? To { get; set; }

    /// <summary>X for a time period or category range; Y (default) for a value range.</summary>
    [Parameter] public AnnotationAxis Axis { get; set; } = AnnotationAxis.Y;

    /// <summary>Where the label sits. Default: Start.</summary>
    [Parameter] public LabelPosition? LabelPosition { get; set; }

    /// <inheritdoc />
    protected override AnnotationSpec CreateSpec() => Common(AnnotationType.Band) with
    {
        From = From,
        To = To,
        Axis = Axis,
        LabelPosition = LabelPosition,
    };
}

/// <summary>
/// A marker at a data point with an optional label above it, e.g. "Alarm".
/// <code>&lt;PointAnnotation X="alarmTime" Y="26.5" Label="Alarm" /&gt;</code>
/// </summary>
public sealed class PointAnnotation : AnnotationBase
{
    /// <summary>The x value: a number, DateTime, DateTimeOffset, DateOnly or category name.</summary>
    [Parameter, EditorRequired] public object? X { get; set; }

    /// <summary>The y value on the annotation's vertical axis.</summary>
    [Parameter, EditorRequired] public double Y { get; set; }

    /// <inheritdoc />
    protected override AnnotationSpec CreateSpec() => Common(AnnotationType.Point) with { X = X, Y = Y };
}

/// <summary>
/// A text box with a connector line to a data point.
/// <code>&lt;Callout X="t" Y="18.2" Label="Filter changed" OffsetX="30" OffsetY="-40" /&gt;</code>
/// </summary>
public sealed class Callout : AnnotationBase
{
    /// <summary>The x value of the point: a number, DateTime, DateTimeOffset, DateOnly or category name.</summary>
    [Parameter, EditorRequired] public object? X { get; set; }

    /// <summary>The y value of the point on the annotation's vertical axis.</summary>
    [Parameter, EditorRequired] public double Y { get; set; }

    /// <summary>Horizontal offset of the box from the point in pixels (negative: to the left). Default 24.</summary>
    [Parameter] public double? OffsetX { get; set; }

    /// <summary>Vertical offset of the box from the point in pixels (negative: up). Default -32.</summary>
    [Parameter] public double? OffsetY { get; set; }

    /// <inheritdoc />
    protected override AnnotationSpec CreateSpec() => Common(AnnotationType.Callout) with { X = X, Y = Y, OffsetX = OffsetX, OffsetY = OffsetY };
}

/// <summary>Converts a dragged value (double or DateTime) to the type of the bound value.</summary>
internal static class AxisValues
{
    public static bool TryConvert<TValue>(object value, TValue? current, out TValue result)
    {
        result = default!;
        var target = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        try
        {
            object? converted = (value, target) switch
            {
                (DateTime t, var type) when type == typeof(DateTime) => t,
                (DateTime t, var type) when type == typeof(DateTimeOffset) =>
                    new DateTimeOffset(t, current is DateTimeOffset original ? original.Offset : TimeSpan.Zero),
                (DateTime t, var type) when type == typeof(DateOnly) => DateOnly.FromDateTime(t),
                (double d, var type) when type == typeof(int) || type == typeof(long) || type == typeof(short) =>
                    Convert.ChangeType(Math.Round(d), type, System.Globalization.CultureInfo.InvariantCulture),
                (double d, var type) when type == typeof(double) || type == typeof(float) || type == typeof(decimal) =>
                    Convert.ChangeType(d, type, System.Globalization.CultureInfo.InvariantCulture),
                (double d, var type) when type == typeof(object) => d,
                (DateTime t, var type) when type == typeof(object) => t,
                _ => null,
            };
            if (converted is null)
            {
                return false;
            }

            result = (TValue)converted;
            return true;
        }
        catch (Exception e) when (e is InvalidCastException or OverflowException or FormatException)
        {
            return false;
        }
    }

    /// <summary>A value in data units as the user sees it: DateTime on time axes, else double.</summary>
    public static object FromAxis(double value, XKind kind) => kind == XKind.Time ? TimeValue.ToDateTime(value) : value;
}
