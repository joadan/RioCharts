using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;

namespace RioCharts.Blazor;

/// <summary>
/// Settings shared by charts instead of repeated on each one, e.g.
/// <c>new RioChartDefaults { Culture = new("sv-SE"), TimeZone = "Europe/Stockholm", Height = 260 }</c>. Register them for
/// the whole app in Program.cs with <c>builder.Services.AddRioCharts(defaults)</c> (reaches every render mode), and/or
/// cascade them to part of the app with <c>&lt;CascadingValue Value="defaults" IsFixed="true"&gt;</c> (a cascade does
/// not cross into components with another render mode). Each setting comes from the chart's own parameter, else the
/// cascaded defaults, else the registered ones, else the built-in default; null leaves it to the next. A chart's
/// <c>YAxis</c> replaces the default axis as a whole: write <c>defaults.YAxis with { Title = "kWh" }</c> to change one setting.
/// A non-fixed cascade can be replaced at runtime (e.g. on a language switch) and the charts follow.
/// </summary>
public sealed record RioChartDefaults
{
    /// <summary>Height in pixels. Null: 300.</summary>
    public double? Height { get; init; }

    /// <summary>Culture for labels. Null: the current culture.</summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>Display time zone: "UTC", "local" or an IANA id such as "Europe/Stockholm". Null: times are shown as given.</summary>
    public string? TimeZone { get; init; }

    /// <summary>With a TimeZone: how DateTime values with Kind Unspecified are read.</summary>
    public UnspecifiedTimes? UnspecifiedTimes { get; init; }

    /// <summary>The horizontal axis, e.g. its format.</summary>
    public AxisOptions? XAxis { get; init; }

    /// <summary>The primary vertical axis, e.g. its format or grid lines.</summary>
    public AxisOptions? YAxis { get; init; }

    /// <summary>Legend settings, e.g. <c>new LegendOptions { Position = LegendPosition.Bottom }</c>.</summary>
    public LegendOptions? Legend { get; init; }

    /// <summary>Tooltip settings.</summary>
    public TooltipOptions? Tooltip { get; init; }

    /// <summary>False turns off the fades on every chart. Null: on.</summary>
    public bool? Animate { get; init; }

    /// <summary>Keyboard navigation of points. Null: on for charts that are not compact.</summary>
    public bool? KeyboardNavigation { get; init; }

    /// <summary>Extra CSS class on every chart's container.</summary>
    public string? Class { get; init; }
}
