using RioCharts.Core;
using RioCharts.Core.Data;

namespace RioCharts.Blazor;

/// <summary>A series as the chart sees it: its spec and its columnar data.</summary>
internal interface ISeriesSource
{
    SeriesSpec Spec { get; }
    SeriesData Data { get; }
}

/// <summary>Cascaded from <see cref="Chart{TItem}"/> to its series components.</summary>
internal interface IChartContext
{
    /// <summary>The chart's Items, used by series that do not set their own.</summary>
    object? DefaultItems { get; }

    /// <summary>Adds a series (once) and returns its id, generating one when none is given.</summary>
    string Register(ISeriesSource series, string? requestedId);

    void Unregister(ISeriesSource series);

    /// <summary>The series' spec or data changed; the chart lays out again.</summary>
    void SeriesChanged(ISeriesSource series);
}

/// <summary>Raised when a legend item is clicked (the series has already been toggled).</summary>
public sealed record LegendClickEventArgs(string SeriesId, string Name, bool Visible);
