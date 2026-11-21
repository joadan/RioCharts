using System.Text.Json.Serialization.Metadata;
using RioCharts.Core;
using RioCharts.Core.Specs;

namespace RioCharts.Tests;

/// <summary>Spec JSON with source-generated metadata (trimming and native AOT safe).</summary>
public class SpecJsonTests
{
    [Fact]
    public void Missing_properties_keep_their_defaults()
    {
        var spec = ChartSpecJson.Parse("""
            { "series": [ { "x": "t", "y": "v" } ], "annotations": [ { "value": 21 } ], "tooltip": { "shared": false }, "yAxes": [ { "id": "kw" } ] }
            """).Spec!;

        Assert.Equal(SeriesType.Line, spec.Series[0].Type);
        Assert.Equal(CurveType.Linear, spec.Series[0].Curve);
        Assert.Equal(AnnotationAxis.Y, spec.Annotations[0].Axis); // default(AnnotationAxis) would be X
        Assert.True(spec.Tooltip.Enabled);                         // default(bool) would hide the tooltip
        Assert.False(spec.Tooltip.Shared);
        Assert.NotNull(spec.X);
        Assert.NotNull(spec.Y);
        Assert.Equal(LegendPosition.Top, spec.Legend.Position);
        Assert.Equal(ScaleType.Auto, spec.YAxes[0].Scale);
    }

    [Fact]
    public void Every_object_type_in_a_spec_has_a_factory_for_its_defaults()
    {
        var found = new HashSet<Type>();
        void Walk(Type type)
        {
            if (!ChartSpecJson.Options.TryGetTypeInfo(type, out var info))
            {
                return;
            }

            if (info.Kind == JsonTypeInfoKind.Object && found.Add(type))
            {
                foreach (var property in info.Properties)
                {
                    Walk(property.PropertyType);
                }
            }
            else if (info.Kind == JsonTypeInfoKind.Enumerable && info.ElementType is { } element)
            {
                Walk(element);
            }
        }

        Walk(typeof(ChartSpec));

        Assert.Equal(found.OrderBy(t => t.Name), ChartSpecJson.TypesWithDefaults.OrderBy(t => t.Name));
    }

    [Fact]
    public void Specs_built_in_code_with_object_values_serialize()
    {
        var spec = new ChartSpec
        {
            Data = [new Dictionary<string, object?> { ["t"] = new DateTime(2026, 11, 16), ["v"] = 20.5, ["unit"] = "AHU 1", ["on"] = true, ["n"] = 3 }],
            Series = [new SeriesSpec { Id = "s", X = "t", Y = "v" }],
            Annotations = [new AnnotationSpec { Type = AnnotationType.Point, X = new DateTime(2026, 11, 16), Y = 20.5m }],
        };

        var json = ChartSpecJson.Serialize(spec);

        Assert.Contains("\"2026-11-16T00:00:00\"", json, StringComparison.Ordinal);
        Assert.Contains("\"unit\": \"AHU 1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"point\"", json, StringComparison.Ordinal);
        Assert.Equal(20.5, ChartSpecJson.Parse(json).Spec!.Data![0]["v"] switch { System.Text.Json.JsonElement e => e.GetDouble(), var o => Convert.ToDouble(o) });
    }
}
