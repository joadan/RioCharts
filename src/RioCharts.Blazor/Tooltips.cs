using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;

namespace RioCharts.Blazor;

/// <summary>
/// One point in a tooltip: what <c>TooltipValue</c>, <c>TooltipHeader</c> and the rows of a
/// <c>TooltipTemplate</c> get.
/// </summary>
/// <param name="SeriesId">Id of the series.</param>
/// <param name="SeriesName">Name of the series (as in the legend).</param>
/// <param name="Color">The series color as a CSS color, e.g. for a dot in a template.</param>
/// <param name="Index">Index into the series' input (items list or columns); the first item's when the series aggregates; -1 for a grouped "Other" slice.</param>
/// <param name="X">The x value: a number, DateTime (DateTimeOffset with a display time zone) or category name.</param>
/// <param name="Y">The y value (the series' own value, also when stacked); NaN for a gap or an "Other" slice.</param>
/// <param name="XText">The x value as the tooltip header shows it by default (the category for pie slices).</param>
/// <param name="ValueText">The value as the tooltip shows it: formatted with the series' <c>Format</c>, or the series' <c>TooltipValue</c>.</param>
public sealed record TooltipPoint<TItem>(string SeriesId, string SeriesName, string Color, int Index, object? X, double Y, string XText, string ValueText)
{
    /// <summary>The input item (the first of the group when the series aggregates), or default for columnar data.</summary>
    public TItem? Item { get; init; }

    /// <summary>All input items behind the point: the group when the series aggregates; empty for columnar data.</summary>
    public IReadOnlyList<TItem> Items { get; init; } = [];
}

/// <summary>What a <c>TooltipTemplate</c> renders: the hovered x and one point per listed series.</summary>
/// <param name="Header">The default header text (the formatted x, or <c>TooltipHeader</c>); the series name for nested pies.</param>
/// <param name="X">The hovered x value (the first point's).</param>
/// <param name="Points">The listed points in tooltip order: one per series for a shared tooltip, else one.</param>
public sealed record TooltipContext<TItem>(string Header, object? X, IReadOnlyList<TooltipPoint<TItem>> Points);

/// <summary>A tooltip point before it is tied to an item type.</summary>
internal readonly record struct TooltipPointInfo(string SeriesId, string SeriesName, string Color, int Index, int SourceIndex, object? X, double Y,
    string XText, string ValueText);

/// <summary>
/// Applies the tooltip formatter delegates to the layout's <see cref="TooltipModel"/> (once per
/// layout) and builds template contexts for hovered points.
/// </summary>
internal static class TooltipBuilder
{
    /// <summary>
    /// The model with <c>TooltipValue</c> (per series) and <paramref name="header"/> applied to the
    /// listed points. Returns <paramref name="model"/> itself when there is nothing to apply.
    /// </summary>
    public static TooltipModel Customize<TItem>(TooltipModel model, Func<string, SeriesData?> dataOf, Func<string, ISeriesSource?> sourceOf,
        TimeConversion? time, Func<TooltipPoint<TItem>, string?>? header)
    {
        var any = header is not null;
        var series = new List<TooltipSeries>(model.Series.Count);
        foreach (var s in model.Series)
        {
            var source = sourceOf(s.Id);
            var custom = source?.HasTooltipValue == true;
            var data = dataOf(s.Id);
            if (data is null || (!custom && header is null))
            {
                series.Add(s);
                continue;
            }

            any = true;
            var xLabels = (string[])s.XLabels.Clone();
            var values = (string[])s.Values.Clone();
            for (var k = 0; k < s.Index.Length; k++)
            {
                var info = Info(s.Id, s.Name, s.Color, data, s.Index[k], time, s.XLabels[k], s.Values[k]);
                if (custom && source!.FormatTooltipValue(info) is { } value)
                {
                    values[k] = value;
                    info = info with { ValueText = value };
                }

                if (header is not null && header(Point<TItem>(info, source)) is { } head)
                {
                    xLabels[k] = head;
                }
            }

            series.Add(s with { XLabels = xLabels, Values = values });
        }

        var slices = new List<TooltipSlice>(model.Slices.Count);
        foreach (var slice in model.Slices)
        {
            var source = sourceOf(slice.SeriesId);
            var custom = source?.HasTooltipValue == true;
            if (!custom && header is null)
            {
                slices.Add(slice);
                continue;
            }

            any = true;
            var info = SliceInfo(slice, dataOf(slice.SeriesId), sourceOf(slice.SeriesId)?.Spec.DisplayName);
            var result = slice;
            if (custom && source!.FormatTooltipValue(info) is { } value)
            {
                result = result with { Value = value };
                info = info with { ValueText = value };
            }

            if (header is not null && header(Point<TItem>(info, source)) is { } head)
            {
                result = result with { Header = head };
            }

            slices.Add(result);
        }

        return any ? model with { Series = series, Slices = slices } : model;
    }

    /// <summary>
    /// The template context for hovered points (parallel arrays of series ids and data indexes),
    /// or null when none of them is in the model.
    /// </summary>
    public static TooltipContext<TItem>? Context<TItem>(TooltipModel model, Func<string, SeriesData?> dataOf, Func<string, ISeriesSource?> sourceOf,
        TimeConversion? time, IReadOnlyList<string> seriesIds, IReadOnlyList<int> indexes)
    {
        var points = new List<TooltipPoint<TItem>>();
        string? header = null;
        object? x = null;
        for (var p = 0; p < Math.Min(seriesIds.Count, indexes.Count); p++)
        {
            var id = seriesIds[p];
            var index = indexes[p];
            var series = model.Series.FirstOrDefault(s => s.Id == id);
            if (series is not null)
            {
                var k = Array.IndexOf(series.Index, index);
                if (k < 0 || dataOf(id) is not { } data)
                {
                    continue;
                }

                var info = Info(id, series.Name, series.Color, data, index, time, series.XLabels[k], series.Values[k]);
                header ??= series.XLabels[k];
                x ??= info.X;
                points.Add(Point<TItem>(info, sourceOf(id)));
                continue;
            }

            var slice = model.Slices.FirstOrDefault(s => s.SeriesId == id && s.Index == index);
            if (slice is not null)
            {
                var info = SliceInfo(slice, dataOf(id), sourceOf(id)?.Spec.DisplayName);
                header ??= slice.Header;
                x ??= info.X;
                points.Add(Point<TItem>(info, sourceOf(id)));
            }
        }

        return points.Count > 0 ? new TooltipContext<TItem>(header ?? "", x, points) : null;
    }

    /// <summary>The x value of a point as events and tooltips show it.</summary>
    public static object? XValueOf(SeriesData data, int index, TimeConversion? time)
    {
        var x = data.X[index];
        return data.Kind switch
        {
            // Cast first: a plain ?: would turn the DateTime into a DateTimeOffset with the server's offset.
            XKind.Time => time is not null ? time.ToDateTimeOffset(x) : (object)TimeValue.ToDateTime(x),
            XKind.Category => (int)x >= 0 && (int)x < data.Categories.Count ? data.Categories[(int)x] : null,
            _ => x,
        };
    }

    public static TooltipPoint<TItem> Point<TItem>(TooltipPointInfo info, ISeriesSource? source)
    {
        var inputs = source is null || info.SourceIndex < 0 ? [] : source.InputIndexes(info.SourceIndex);
        var items = source is null ? [] : inputs.Select(source.ItemAt).OfType<TItem>().ToList();
        var input = inputs.Count > 0 ? inputs[0] : info.SourceIndex;
        return new TooltipPoint<TItem>(info.SeriesId, info.SeriesName, Rendering.SvgWriter.Color(info.Color), input, info.X, info.Y, info.XText, info.ValueText)
        {
            Item = items.FirstOrDefault(),
            Items = items,
        };
    }

    private static TooltipPointInfo Info(string id, string name, string color, SeriesData data, int index, TimeConversion? time, string xText,
        string valueText)
    {
        var valid = index >= 0 && index < data.Count;
        return new TooltipPointInfo(id, name, color, index, valid ? data.ToSourceIndex(index) : -1, valid ? XValueOf(data, index, time) : null,
            valid ? data.Y[index] : double.NaN, xText, valueText);
    }

    private static TooltipPointInfo SliceInfo(TooltipSlice slice, SeriesData? data, string? seriesName)
    {
        var valid = data is not null && slice.Index >= 0 && slice.Index < data.Count;
        return new TooltipPointInfo(slice.SeriesId, seriesName ?? slice.Header, slice.Color, slice.Index, valid ? data!.ToSourceIndex(slice.Index) : -1,
            slice.Label, valid ? data!.Y[slice.Index] : double.NaN, slice.Label, slice.Value);
    }
}

/// <summary>Renders the chart's <c>TooltipTemplate</c> inside the tooltip card; the JS module places the card.</summary>
internal sealed class TooltipHost<TItem> : ComponentBase
{
    private TooltipContext<TItem>? _context;

    [Parameter] public RenderFragment<TooltipContext<TItem>>? Template { get; set; }

    public Task ShowAsync(TooltipContext<TItem> context) => InvokeAsync(() =>
    {
        _context = context;
        StateHasChanged();
    });

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Template is not null && _context is not null)
        {
            builder.AddContent(0, Template(_context));
        }
    }
}
