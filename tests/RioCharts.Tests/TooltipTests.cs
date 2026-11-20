using System.Globalization;
using RioCharts.Blazor;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;

namespace RioCharts.Tests;

public class TooltipTests
{
    private sealed record Reading(DateTime Time, double Temp, double Setpoint);

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
        public SeriesSpec Spec { get; } = new() { Id = id, Name = id };
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
        var setpoint = SeriesData.FromItems(Readings, r => r.Time, r => r.Setpoint);
        var data = new Dictionary<string, SeriesData> { ["temp"] = temp, ["setpoint"] = setpoint };
        var sources = new Dictionary<string, ISeriesSource>
        {
            ["temp"] = new FakeSeries("temp", temp, Readings, tempValue),
            ["setpoint"] = new FakeSeries("setpoint", setpoint, Readings, null),
        };
        var spec = new ChartSpec
        {
            Series = [new SeriesSpec { Id = "temp", Name = "Temperature" }, new SeriesSpec { Id = "setpoint", Name = "Setpoint" }],
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
        var (layout, data, sources) = Build(p => string.Create(CultureInfo.InvariantCulture, $"{p.Y:0.0} °C (setpoint {p.Item!.Setpoint} °C)"));

        var model = TooltipBuilder.Customize<Reading>(layout.Tooltip, data.GetValueOrDefault, sources.GetValueOrDefault, layout.Time, null);

        var temp = model.Series.Single(s => s.Id == "temp");
        Assert.Equal(["20.5 °C (setpoint 21 °C)", "21.3 °C (setpoint 21 °C)", "22.0 °C (setpoint 22 °C)"], temp.Values);
        Assert.Same(layout.Tooltip.Series.Single(s => s.Id == "setpoint"), model.Series.Single(s => s.Id == "setpoint"));
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
            ["temp", "setpoint"], [1, 1]);

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
        Assert.Equal("setpoint", context.Points[1].SeriesId);
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
        var shares = SeriesData.FromCategories(["Heating", "Cooling", "Fans"], [50, 30, 20]);
        var data = new Dictionary<string, SeriesData> { ["energy"] = shares };
        var sources = new Dictionary<string, ISeriesSource>
        {
            ["energy"] = new FakeSeries("energy", shares, [], p => $"{p.X}: {p.Y} kWh"),
        };
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "energy", Type = SeriesType.Pie }] };
        var layout = ChartLayout.Compute(spec, data, new LayoutOptions { Culture = CultureInfo.InvariantCulture });

        var model = TooltipBuilder.Customize<Reading>(layout.Tooltip, data.GetValueOrDefault, sources.GetValueOrDefault, layout.Time, null);

        Assert.Contains(model.Slices, s => s.Value == "Cooling: 30 kWh");
        var context = TooltipBuilder.Context<Reading>(model, data.GetValueOrDefault, sources.GetValueOrDefault, layout.Time, ["energy"], [2]);
        Assert.Equal("Fans", context!.Points[0].X);
        Assert.Equal("Fans: 20 kWh", context.Points[0].ValueText);
    }
}
