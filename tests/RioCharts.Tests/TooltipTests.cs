using System.Globalization;
using RioCharts.Blazor;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;

namespace RioCharts.Tests;

public class TooltipTests
{
    private sealed record Reading(DateTime Time, double Temp, double Forecast);

    private static readonly Reading[] Readings =
    [
        new(new DateTime(2026, 1, 5, 8, 0, 0), 20.5, 21),
        new(new DateTime(2026, 1, 5, 9, 0, 0), 21.25, 21),
        new(new DateTime(2026, 1, 5, 10, 0, 0), 22, 22),
    ];

    /// <summary>A series as the chart sees it, over the readings, with an optional TooltipValue.</summary>
    private sealed class FakeSeries(string id, SeriesData data, IReadOnlyList<Reading> items, Func<TooltipPoint<Reading>, string?>? tooltipValue)
        : ISeriesSource
    {
        public SeriesOptions Spec { get; } = new() { Id = id, Name = id };
        public SeriesData Data { get; } = data;
        public object? ItemAt(int inputIndex) => inputIndex >= 0 && inputIndex < items.Count ? items[inputIndex] : null;
        public IReadOnlyList<int> InputIndexes(int sourceIndex) => [sourceIndex];
        public bool HasTooltipValue => tooltipValue is not null;
        public string? FormatTooltipValue(TooltipPointInfo point) => tooltipValue?.Invoke(TooltipBuilder.Point<Reading>(point, this));
    }

    private static (ChartLayoutResult Layout, Dictionary<string, SeriesData> Data, Dictionary<string, ISeriesSource> Sources) Build(
        Func<TooltipPoint<Reading>, string?>? tempValue)
    {
        var temp = SeriesData.FromItems(Readings, r => r.Time, r => r.Temp);
        var forecast = SeriesData.FromItems(Readings, r => r.Time, r => r.Forecast);
        var data = new Dictionary<string, SeriesData> { ["temp"] = temp, ["forecast"] = forecast };
        var sources = new Dictionary<string, ISeriesSource>
        {
            ["temp"] = new FakeSeries("temp", temp, Readings, tempValue),
            ["forecast"] = new FakeSeries("forecast", forecast, Readings, null),
        };
        var spec = new ChartDefinition
        {
            Series = [new SeriesOptions { Id = "temp", Name = "Temperature" }, new SeriesOptions { Id = "forecast", Name = "Forecast" }],
        };
        var layout = ChartLayout.Compute(spec, data, new LayoutOptions { Culture = CultureInfo.InvariantCulture });
        return (layout, data, sources);
    }

    [Fact]
    public void Without_formatters_the_model_is_unchanged()
    {
        var (layout, data, sources) = Build(null);

        var model = TooltipBuilder.Customize<Reading>(layout.Tooltip, data.GetValueOrDefault, sources.GetValueOrDefault, layout.Time, null);

        Assert.Same(layout.Tooltip, model);
    }

    [Fact]
    public void TooltipValue_replaces_the_values_of_its_series_only()
    {
        var (layout, data, sources) = Build(p => string.Create(CultureInfo.InvariantCulture, $"{p.Y:0.0} °C (forecast {p.Item!.Forecast} °C)"));

        var model = TooltipBuilder.Customize<Reading>(layout.Tooltip, data.GetValueOrDefault, sources.GetValueOrDefault, layout.Time, null);

        var temp = model.Series.Single(s => s.Id == "temp");
        Assert.Equal(["20.5 °C (forecast 21 °C)", "21.3 °C (forecast 21 °C)", "22.0 °C (forecast 22 °C)"], temp.Values);
        Assert.Same(layout.Tooltip.Series.Single(s => s.Id == "forecast"), model.Series.Single(s => s.Id == "forecast"));
    }

    [Fact]
    public void TooltipHeader_replaces_the_x_labels_and_sees_the_x_value()
    {
        var (layout, data, sources) = Build(null);

        var model = TooltipBuilder.Customize<Reading>(layout.Tooltip, data.GetValueOrDefault, sources.GetValueOrDefault, layout.Time,
            p => $"Hour {((DateTime)p.X!).Hour}");

        Assert.All(model.Series, s => Assert.Equal(["Hour 8", "Hour 9", "Hour 10"], s.XLabels));
    }

    [Fact]
    public void Template_context_has_the_hovered_points_with_items_and_custom_text()
    {
        var (layout, data, sources) = Build(p => string.Create(CultureInfo.InvariantCulture, $"{p.Y} °C"));
        var model = TooltipBuilder.Customize<Reading>(layout.Tooltip, data.GetValueOrDefault, sources.GetValueOrDefault, layout.Time,
            p => "Morning");

        var context = TooltipBuilder.Context<Reading>(model, data.GetValueOrDefault, sources.GetValueOrDefault, layout.Time,
            ["temp", "forecast"], [1, 1]);

        Assert.NotNull(context);
        Assert.Equal("Morning", context.Header);
        Assert.Equal(Readings[1].Time, context.X);
        Assert.Equal(2, context.Points.Count);
        var temp = context.Points[0];
        Assert.Equal("Temperature", temp.SeriesName);
        Assert.Same(Readings[1], temp.Item);
        Assert.Equal(1, temp.Index);
        Assert.Equal(21.25, temp.Y);
        Assert.Equal("21.25 °C", temp.ValueText);
        Assert.Equal("forecast", context.Points[1].SeriesId);
    }

    [Fact]
    public void Template_context_is_null_for_unknown_points()
    {
        var (layout, data, sources) = Build(null);

        Assert.Null(TooltipBuilder.Context<Reading>(layout.Tooltip, data.GetValueOrDefault, sources.GetValueOrDefault, layout.Time, ["temp"], [99]));
        Assert.Null(TooltipBuilder.Context<Reading>(layout.Tooltip, data.GetValueOrDefault, sources.GetValueOrDefault, layout.Time, ["nope"], [0]));
    }

    [Fact]
    public void TooltipValue_applies_to_pie_slices()
    {
        var shares = SeriesData.FromCategories(["Industry", "Homes", "Transport"], [50, 30, 20]);
        var data = new Dictionary<string, SeriesData> { ["energy"] = shares };
        var sources = new Dictionary<string, ISeriesSource>
        {
            ["energy"] = new FakeSeries("energy", shares, [], p => $"{p.X}: {p.Y} kWh"),
        };
        var spec = new ChartDefinition { Series = [new SeriesOptions { Id = "energy", Type = SeriesType.Pie }] };
        var layout = ChartLayout.Compute(spec, data, new LayoutOptions { Culture = CultureInfo.InvariantCulture });

        var model = TooltipBuilder.Customize<Reading>(layout.Tooltip, data.GetValueOrDefault, sources.GetValueOrDefault, layout.Time, null);

        Assert.Contains(model.Slices, s => s.Value == "Homes: 30 kWh");
        var context = TooltipBuilder.Context<Reading>(model, data.GetValueOrDefault, sources.GetValueOrDefault, layout.Time, ["energy"], [2]);
        Assert.Equal("Transport", context!.Points[0].X);
        Assert.Equal("Transport: 20 kWh", context.Points[0].ValueText);
    }

    [Fact]
    public void Step_lines_are_marked_so_the_shared_tooltip_uses_the_value_in_effect()
    {
        var data = new Dictionary<string, SeriesData>
        {
            ["outdoor"] = SeriesData.FromItems(Readings, r => r.Time, r => r.Temp),
            ["forecast"] = SeriesData.FromItems(Readings, r => r.Time, r => r.Forecast),
            ["bars"] = SeriesData.FromItems(Readings, r => r.Time, r => r.Temp),
        };
        var spec = new ChartDefinition
        {
            Series =
            [
                new SeriesOptions { Id = "outdoor" },
                new SeriesOptions { Id = "forecast", Curve = CurveType.Step },
                new SeriesOptions { Id = "bars", Type = SeriesType.Bar, Curve = CurveType.Step },
            ],
        };

        var layout = ChartLayout.Compute(spec, data, new LayoutOptions { Width = 600, Height = 300 });

        Assert.False(layout.Tooltip.Series.Single(s => s.Id == "outdoor").Step);
        Assert.True(layout.Tooltip.Series.Single(s => s.Id == "forecast").Step);
        Assert.False(layout.Tooltip.Series.Single(s => s.Id == "bars").Step); // bars have no curve
    }
}
