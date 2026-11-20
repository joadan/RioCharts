using RioCharts.Core;
using RioCharts.Core.Data;

namespace RioCharts.Blazor;

/// <summary>A series as the chart sees it: its spec and its columnar data.</summary>
internal interface ISeriesSource
{
    SeriesSpec Spec { get; }
    SeriesData Data { get; }

    /// <summary>The input item behind a point, or null for columnar data.</summary>
    object? ItemAt(int sourceIndex);
}

/// <summary>An annotation component as the chart sees it.</summary>
internal interface IAnnotationSource
{
    AnnotationSpec Spec { get; }
}

/// <summary>Cascaded from <see cref="Chart{TItem}"/> to its series and annotation components.</summary>
internal interface IChartContext
{
    /// <summary>The chart's Items, used by series that do not set their own.</summary>
    object? DefaultItems { get; }

    /// <summary>Adds a series (once) and returns its id, generating one when none is given.</summary>
    string Register(ISeriesSource series, string? requestedId);

    void Unregister(ISeriesSource series);

    /// <summary>The series' spec or data changed; the chart lays out again.</summary>
    void SeriesChanged(ISeriesSource series);

    /// <summary>Adds an annotation (once), or lays out again after its spec changed.</summary>
    void AnnotationChanged(IAnnotationSource annotation);

    void Unregister(IAnnotationSource annotation);
}

/// <summary>Raised when a legend item is clicked (the series has already been toggled).</summary>
public sealed record LegendClickEventArgs(string SeriesId, string Name, bool Visible);

/// <summary>Raised when a point or bar is clicked.</summary>
/// <param name="Item">The input item, or default for columnar data.</param>
/// <param name="Index">Index into the series' input (items list or columns).</param>
/// <param name="X">The x value: a number, DateTime or category name.</param>
/// <param name="Y">The y value (the series' own value, also when stacked).</param>
public sealed record PointClickEventArgs<TItem>(string SeriesId, string SeriesName, TItem? Item, int Index, object? X, double Y);

/// <summary>Raised when a reference line or band is clicked.</summary>
public sealed record AnnotationClickEventArgs(string AnnotationId, string? Label);
