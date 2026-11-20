using Microsoft.AspNetCore.Components;
using RioCharts.Core;

namespace RioCharts.Blazor;

/// <summary>
/// Base for annotation components. Like series, they render nothing themselves: they register
/// an <see cref="AnnotationSpec"/> with the chart.
/// </summary>
public abstract class AnnotationBase : ComponentBase, IAnnotationSource, IDisposable
{
    [CascadingParameter] internal IChartContext? Chart { get; set; }

    /// <summary>Optional id, reported by <c>Chart.OnAnnotationClick</c>.</summary>
    [Parameter] public string? Id { get; set; }

    /// <summary>X for a vertical line or band; Y (default) for a horizontal one.</summary>
    [Parameter] public AnnotationAxis Axis { get; set; } = AnnotationAxis.Y;

    /// <summary>Vertical axis id for Y annotations. Default: the primary axis.</summary>
    [Parameter] public string? YAxisId { get; set; }

    [Parameter] public string? Label { get; set; }

    /// <summary>Where the label sits. Default: End for lines, Start for bands.</summary>
    [Parameter] public LabelPosition? LabelPosition { get; set; }

    /// <summary>CSS color. Default: the muted annotation color (<c>--rio-annotation</c>).</summary>
    [Parameter] public string? Color { get; set; }

    /// <summary>Grow the axis range to include the annotation. Default: lines yes, bands no.</summary>
    [Parameter] public bool? ExpandsDomain { get; set; }

    /// <summary>Draw below or above the series. Default: bands below, lines above.</summary>
    [Parameter] public AnnotationLayer? Layer { get; set; }

    public AnnotationSpec Spec { get; private set; } = new();

    protected abstract AnnotationSpec CreateSpec();

    protected AnnotationSpec Common(AnnotationType type) => new()
    {
        Id = Id,
        Type = type,
        Axis = Axis,
        YAxisId = YAxisId,
        Label = Label,
        LabelPosition = LabelPosition,
        Color = Color,
        ExpandsDomain = ExpandsDomain,
        Layer = Layer,
    };

    private bool _registered;

    protected override void OnParametersSet()
    {
        if (Chart is null)
        {
            throw new InvalidOperationException($"{GetType().Name} must be placed inside a <Chart>.");
        }

        var spec = CreateSpec();
        if (!_registered || spec != Spec)
        {
            Spec = spec;
            _registered = true;
            Chart.AnnotationChanged(this);
        }
    }

    protected override bool ShouldRender() => false;

    public void Dispose()
    {
        Chart?.Unregister(this);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// A horizontal (Axis="Y", default) or vertical (Axis="X") line, e.g. a setpoint or "today".
/// <code>&lt;ReferenceLine Value="21" Label="Setpoint" /&gt;</code>
/// </summary>
public sealed class ReferenceLine : AnnotationBase
{
    /// <summary>Position in data units: a number, DateTime, DateTimeOffset, DateOnly or category name.</summary>
    [Parameter, EditorRequired] public object? Value { get; set; }

    protected override AnnotationSpec CreateSpec() => Common(AnnotationType.Line) with { Value = Value };
}

/// <summary>
/// A range on the y axis (an acceptable zone) or the x axis (a period such as a fault).
/// <code>&lt;ReferenceBand Axis="AnnotationAxis.X" From="faultStart" To="faultEnd" Label="Fault" /&gt;</code>
/// </summary>
public sealed class ReferenceBand : AnnotationBase
{
    [Parameter, EditorRequired] public object? From { get; set; }

    [Parameter, EditorRequired] public object? To { get; set; }

    protected override AnnotationSpec CreateSpec() => Common(AnnotationType.Band) with { From = From, To = To };
}
