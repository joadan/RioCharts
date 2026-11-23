using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;

namespace RioCharts.Blazor;

// Selection (points or an x range) and the tooltip shown from code.
public partial class RioChart<TItem>
{
    private List<TItem> _selected = [];
    private ChartRange? _selectedRange;
    private IReadOnlyCollection<TItem>? _lastSelectedItemsParameter;
    private ChartRange? _lastSelectedRangeParameter;
    private object? _tooltipX;

    // SelectedRange changed from the parent without new SelectedItems: the items follow the range
    // after the next layout (they may need the data), e.g. clearing the range clears its items.
    private bool _rangeItemsPending;

    /// <summary>
    /// What the user can select: points (click) or an x range (drag). Default none. Selections
    /// from code (<see cref="SelectedItems"/>, <see cref="SelectedRange"/>, <see cref="SelectItemsAsync"/>)
    /// show either way. Points need items (not columnar data). Drawn on vertical cartesian charts.
    /// </summary>
    [Parameter] public SelectionMode Selection { get; set; }

    /// <summary>The selected items. Two-way bindable with <c>@bind-SelectedItems</c>.</summary>
    [Parameter] public IReadOnlyCollection<TItem>? SelectedItems { get; set; }

    /// <summary>Called with the selected items when the selection changes.</summary>
    [Parameter] public EventCallback<IReadOnlyCollection<TItem>> SelectedItemsChanged { get; set; }

    /// <summary>The selected x range (null: none). Two-way bindable with <c>@bind-SelectedRange</c>.</summary>
    [Parameter] public ChartRange? SelectedRange { get; set; }

    /// <summary>Called with the selected range when the selection changes (null: none).</summary>
    [Parameter] public EventCallback<ChartRange?> SelectedRangeChanged { get; set; }

    /// <summary>Raised when the selection changes, by the user or from code.</summary>
    [Parameter] public EventCallback<SelectionChangedEventArgs<TItem>> OnSelectionChanged { get; set; }

    /// <summary>The selected items: the selected points', or those of the points in the selected range.</summary>
    public IReadOnlyList<TItem> GetSelectedItems() => [.. _selected];

    /// <summary>The selected x range, or null.</summary>
    public ChartRange? GetSelectedRange() => _selectedRange;

    /// <summary>Selects the points of these items (replacing the selection).</summary>
    public Task SelectItemsAsync(IEnumerable<TItem> items) => SetSelectionAsync([.. (items ?? []).Distinct()], null);

    /// <summary>Selects an x range (replacing the selection); null clears it.</summary>
    public Task SelectRangeAsync(ChartRange? range) =>
        ToRange(range) is { } r ? SetSelectionAsync(ItemsInRange(r), FromRange(r)) : ClearSelectionAsync();

    /// <summary>Clears the selection.</summary>
    public Task ClearSelectionAsync() => SetSelectionAsync([], null);

    /// <summary>
    /// The items of the visible series' points inside an x range, in series order, each once.
    /// Empty for columnar data and category axes.
    /// </summary>
    public IReadOnlyList<TItem> GetItemsInRange(ChartRange range) => ToRange(range) is { } r ? ItemsInRange(r) : [];

    /// <summary>
    /// Shows the crosshair and tooltip at an x value (DateTime, number or category name), as if the
    /// pointer were there; linked charts (same <see cref="Group"/>) show their crosshair too. It
    /// stays until <see cref="HideTooltipAsync"/>, also across new data, and comes back after the
    /// pointer leaves the chart. Cartesian charts; needs JS (interactive rendering).
    /// </summary>
    public Task ShowTooltipAsync(object x)
    {
        _tooltipX = x;
        return SendTooltipAsync();
    }

    /// <summary>Hides the tooltip shown with <see cref="ShowTooltipAsync"/>.</summary>
    public async Task HideTooltipAsync()
    {
        _tooltipX = null;
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("hideTooltip", _container);
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    private async Task SendTooltipAsync()
    {
        if (_module is null || _tooltipX is null || _layout is not { HasData: true } layout || Pixel(layout, _tooltipX) is not { } px)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("showTooltip", _container, px);
        }
        catch (JSDisconnectedException)
        {
        }
    }

    /// <summary>The x pixel of an axis value, or null when it is not on the axis.</summary>
    private static double? Pixel(ChartLayoutResult layout, object x)
    {
        if (layout.Tooltip.Radar is not null)
        {
            // Radar: the spoke's index (the JS module shows that spoke's shared tooltip).
            var spoke = Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture);
            foreach (var s in layout.Tooltip.Series)
            {
                var i = Array.IndexOf(s.XLabels, spoke);
                if (i >= 0 && s.Spokes is { } spokes)
                {
                    return spokes[i];
                }
            }

            return null;
        }

        if (layout.XKind == XKind.Category)
        {
            var name = Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture);
            var categories = layout.XBand?.Categories ?? [];
            for (var i = 0; i < categories.Count; i++)
            {
                if (string.Equals(categories[i], name, StringComparison.Ordinal))
                {
                    return layout.XBand!.Center(i);
                }
            }

            return null;
        }

        if (layout.XScale is not { } scale)
        {
            return null;
        }

        var value = layout.XKind == XKind.Time && layout.Time is { } time ? time.ToInstant(x) : AxisValue.ToDouble(x, layout.XKind);
        return double.IsFinite(value) ? scale.Map(value) : null;
    }

    // ---------- Parameters, layout and JS ----------

    /// <summary>Takes SelectedItems/SelectedRange from the parent when they are new references.</summary>
    private void SelectionParametersSet()
    {
        var itemsChanged = !ReferenceEquals(SelectedItems, _lastSelectedItemsParameter);
        if (itemsChanged)
        {
            _lastSelectedItemsParameter = SelectedItems;
            _selected = [.. (SelectedItems ?? []).Distinct()];
            _dirty = true;
        }

        if (!ReferenceEquals(SelectedRange, _lastSelectedRangeParameter))
        {
            _lastSelectedRangeParameter = SelectedRange;
            _selectedRange = SelectedRange;
            _rangeItemsPending = !itemsChanged;
            _dirty = true;
        }
    }

    /// <summary>After a layout: give the items of a range set by the parent (none when it was cleared).</summary>
    private Task SyncRangeItemsAsync()
    {
        if (!_rangeItemsPending || _layout is null)
        {
            return Task.CompletedTask;
        }

        _rangeItemsPending = false;
        var items = ToRange(_selectedRange) is { } r ? ItemsInRange(r) : [];
        return items.SequenceEqual(_selected) ? Task.CompletedTask : SetItemsAsync(items);
    }

    /// <summary>The selection for the layout: the range, or else the selected points per series.</summary>
    private void SelectionForLayout(out (double Min, double Max)? range, out IReadOnlyDictionary<string, IReadOnlyCollection<int>>? points)
    {
        range = _selectedRange is null ? null : ToRange(_selectedRange);
        points = null;
        if (range is not null || _rangeItemsPending || _selected.Count == 0 || Definition is not null)
        {
            return;
        }

        var wanted = new HashSet<TItem>(_selected);
        var result = new Dictionary<string, IReadOnlyCollection<int>>(StringComparer.Ordinal);
        foreach (var series in _series)
        {
            if (!_layoutData.TryGetValue(series.Spec.Id, out var data))
            {
                continue;
            }

            List<int>? indexes = null;
            for (var i = 0; i < data.Count; i++)
            {
                foreach (var input in series.InputIndexes(data.ToSourceIndex(i)))
                {
                    if (series.ItemAt(input) is TItem item && wanted.Contains(item))
                    {
                        (indexes ??= []).Add(i);
                        break;
                    }
                }
            }

            if (indexes is not null)
            {
                result[series.Spec.Id] = indexes;
            }
        }

        points = result;
    }

    private IReadOnlyList<TItem> ItemsInRange((double Min, double Max) range)
    {
        var items = new List<TItem>();
        var seen = new HashSet<TItem>();
        if (Definition is not null)
        {
            return items;
        }

        foreach (var series in _series)
        {
            if (_hidden.Contains(series.Spec.Id) || !_layoutData.TryGetValue(series.Spec.Id, out var data) || data.Kind == XKind.Category)
            {
                continue;
            }

            // Sorted x: start at the first point inside the range.
            var x = data.X;
            var i = Array.BinarySearch(x, range.Min);
            for (i = i < 0 ? ~i : i; i > 0 && x[i - 1] >= range.Min; i--)
            {
            }

            for (; i < x.Length && x[i] <= range.Max; i++)
            {
                if (double.IsNaN(data.Y[i]))
                {
                    continue; // a gap
                }

                foreach (var input in series.InputIndexes(data.ToSourceIndex(i)))
                {
                    if (series.ItemAt(input) is TItem item && seen.Add(item))
                    {
                        items.Add(item);
                    }
                }
            }
        }

        return items;
    }

    private async Task SetSelectionAsync(IReadOnlyList<TItem> items, ChartRange? range)
    {
        if (Equals(range, _selectedRange) && items.SequenceEqual(_selected))
        {
            return;
        }

        _rangeItemsPending = false;
        _selected = [.. items];
        _selectedRange = range;
        _dirty = true;
        await NotifyItemsAsync();

        if (SelectedRangeChanged.HasDelegate)
        {
            _lastSelectedRangeParameter = range;
            await SelectedRangeChanged.InvokeAsync(range);
        }

        await OnSelectionChanged.InvokeAsync(new SelectionChangedEventArgs<TItem>([.. _selected], range));
        StateHasChanged();
    }

    /// <summary>The range came from the parent; only the items change (and are reported).</summary>
    private async Task SetItemsAsync(IReadOnlyList<TItem> items)
    {
        _selected = [.. items];
        _dirty = true;
        await NotifyItemsAsync();
        await OnSelectionChanged.InvokeAsync(new SelectionChangedEventArgs<TItem>([.. _selected], _selectedRange));
        StateHasChanged();
    }

    private async Task NotifyItemsAsync()
    {
        if (SelectedItemsChanged.HasDelegate)
        {
            // The parent passes this list back as SelectedItems; it matches the current state.
            IReadOnlyCollection<TItem> bound = _selected.ToArray();
            _lastSelectedItemsParameter = bound;
            await SelectedItemsChanged.InvokeAsync(bound);
        }
    }

    /// <summary>A click on a point with Selection="Points": select it, or add/remove it (additive: Ctrl/Cmd/Shift).</summary>
    private Task SelectPointAsync(IReadOnlyList<TItem> items, bool additive)
    {
        if (Selection != SelectionMode.Points || items.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (!additive)
        {
            // Clicking the only selected point again clears it.
            return SetSelectionAsync(items.SequenceEqual(_selected) ? [] : items, null);
        }

        var next = _selectedRange is null ? new List<TItem>(_selected) : [];
        if (items.All(next.Contains))
        {
            next.RemoveAll(items.Contains);
        }
        else
        {
            next.AddRange(items.Where(i => !next.Contains(i)));
        }

        return SetSelectionAsync(next, null);
    }

    /// <summary>Called by JS at the end of a range drag (data units).</summary>
    internal Task OnRangeSelected(double min, double max)
    {
        (min, max) = (FromScaleUnits(min), FromScaleUnits(max));
        if (Selection != SelectionMode.Range || !double.IsFinite(min) || !double.IsFinite(max) || max <= min)
        {
            return Task.CompletedTask;
        }

        return SetSelectionAsync(ItemsInRange((min, max)), FromRange((min, max)));
    }

    /// <summary>Called by JS on a click on an empty part of the plot (or a range click without a drag).</summary>
    internal Task OnSelectionCleared() => Selection == SelectionMode.None ? Task.CompletedTask : ClearSelectionAsync();
}
