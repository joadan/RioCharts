using RioCharts.AI;
using RioCharts.Core.Definitions;

namespace RioCharts.Tests;

/// <summary>get_razor (a definition as Blazor code) and get_usage (the guide for coding agents).</summary>
public class AgentCodeToolTests
{
    private static readonly ChartTool Tool = new(new InMemoryDataSource(
    [
        ChartDataset.FromRows("web_orders",
        [
            new Dictionary<string, object?> { ["day"] = new DateTime(2026, 3, 2), ["region"] = "North", ["amount"] = 120.0 },
            new Dictionary<string, object?> { ["day"] = new DateTime(2026, 3, 3), ["region"] = "South", ["amount"] = null },
        ]),
    ]));

    [Fact]
    public async Task A_spec_on_a_dataset_becomes_a_record_and_markup()
    {
        var result = await Tool.GetRazorAsync("""{"dataset":"web_orders","series":[{"type":"bar","x":"region","y":"amount","aggregate":"sum"}]}""");

        Assert.True(result.Success);
        Assert.Equal("public sealed record WebOrder(DateTime Day, string Region, double? Amount);", result.ItemType);
        Assert.Null(result.Items);
        Assert.Contains("<RioChart TItem=\"WebOrder\" Items=\"webOrders\"", result.Razor, StringComparison.Ordinal);
        Assert.Contains("XValue=\"r => r.Region\"", result.Razor, StringComparison.Ordinal);
        var text = result.ToMarkdown();
        Assert.Contains("dotnet add package RioCharts.Blazor --prerelease", text, StringComparison.Ordinal);
        Assert.Contains("```razor", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inline_data_becomes_a_list_and_names_can_be_given()
    {
        var result = await Tool.GetRazorAsync(
            """{"data":[{"product":"Shoes","sold":45},{"product":"Bags","sold":12.5}],"series":[{"type":"pie","x":"product","y":"sold"}]}""",
            itemType: "Sale", itemsName: "sales");

        Assert.True(result.Success);
        Assert.Equal("public sealed record Sale(string Product, double Sold);", result.ItemType);
        Assert.Equal("List<Sale> sales =\n[\n    new(\"Shoes\", 45),\n    new(\"Bags\", 12.5),\n];", result.Items);
        Assert.Contains("<PieSeries", result.Razor, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Errors_come_back_to_fix()
    {
        var result = await Tool.GetRazorAsync("""{"dataset":"web_orders","series":[{"type":"bar","x":"region","y":"price"}]}""");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.StartsWith("The definition has errors", result.ToMarkdown(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ShoppingCarts", "ShoppingCart")]
    [InlineData("categories", "Category")]
    [InlineData("glass", "Glass")]
    [InlineData("bus", "Bus")]
    public void Type_names_from_dataset_names(string dataset, string type) => Assert.Equal(type, ChartRazor.ItemTypeName(dataset));

    [Fact]
    public void The_guide_is_embedded_and_split_into_topics()
    {
        Assert.StartsWith("# RioCharts", RioChartsUsage.Full, StringComparison.Ordinal);

        var overview = RioChartsUsage.Get(null);
        Assert.Contains("## Setup", overview, StringComparison.Ordinal);
        Assert.Contains("## Common mistakes", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("#### `<LineSeries>`", overview, StringComparison.Ordinal);
        Assert.Contains("`<LineSeries>`", overview, StringComparison.Ordinal); // listed as a topic

        var line = RioChartsUsage.Get("LineSeries");
        Assert.StartsWith("#### `<LineSeries>`", line, StringComparison.Ordinal);
        Assert.Contains("`Curve`", line, StringComparison.Ordinal);
        Assert.DoesNotContain("<AreaSeries>", line, StringComparison.Ordinal);
        Assert.Equal(line, RioChartsUsage.Get("<LineSeries>"));
        Assert.Contains("`Title`", RioChartsUsage.Get("axisoptions"), StringComparison.Ordinal);
        Assert.StartsWith("## Common mistakes", RioChartsUsage.Get("Common mistakes"), StringComparison.Ordinal);
        Assert.StartsWith("No section", RioChartsUsage.Get("Sankey"), StringComparison.Ordinal);
        Assert.StartsWith("#### `<GaugeSeries>`", RioChartsUsage.Get("GaugeSeries"), StringComparison.Ordinal);
    }
}
