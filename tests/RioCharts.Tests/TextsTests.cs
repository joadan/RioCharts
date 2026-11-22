using System.Globalization;
using System.Text.RegularExpressions;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;
using RioCharts.Core.Scene;

namespace RioCharts.Tests;

public partial class TextsTests
{
    private static IEnumerable<Node> All(Node node) => node is GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    private static readonly DateTime Start = new(2026, 11, 16);

    private static SeriesData Temps(params double[] values) =>
        SeriesData.From(values.Select((_, i) => Start.AddHours(i)).ToArray(), values);

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    [Theory]
    [InlineData("sv-SE", "Diagram")]
    [InlineData("sv-FI", "Diagram")]
    [InlineData("nb-NO", "Diagram")]
    [InlineData("fi-FI", "Kaavio")]
    [InlineData("de-CH", "Diagramm")]
    [InlineData("fr-CA", "Graphique")]
    [InlineData("es-MX", "Gráfico")]
    [InlineData("en-GB", "Chart")]
    [InlineData("ja-JP", "Chart")] // not built in: English
    [InlineData("", "Chart")]      // invariant
    public void For_picks_the_language_from_the_culture_and_its_parents(string culture, string expected)
    {
        Assert.Equal(expected, ChartTexts.For(CultureInfo.GetCultureInfo(culture)).Chart);
    }

    [Fact]
    public void Register_adds_a_language_for_every_chart()
    {
        var polish = new ChartTexts { Chart = "Wykres", NoData = "Brak danych" };
        ChartTexts.Register("pl", polish);

        Assert.Same(polish, ChartTexts.For(CultureInfo.GetCultureInfo("pl-PL")));
        Assert.Equal("Reset zoom", polish.ResetZoom); // not set: English

        var layout = ChartLayout.Compute(new ChartSpec { Series = [new SeriesSpec { Id = "t" }] }, new Dictionary<string, SeriesData> { ["t"] = SeriesData.Empty },
            new LayoutOptions { Culture = CultureInfo.GetCultureInfo("pl-PL") });
        Assert.Contains(All(layout.Scene.Overlay), n => n is TextNode { Text: "Brak danych" });
    }

    [Fact]
    public void With_changes_single_texts_and_leaves_the_original_alone()
    {
        var mine = ChartTexts.Swedish with { ResetZoom = "Visa allt" };

        Assert.Equal("Visa allt", mine.ResetZoom);
        Assert.Equal("Inga data", mine.NoData);
        Assert.Equal("Återställ zoom", ChartTexts.Swedish.ResetZoom);
        Assert.Equal(ChartTexts.Swedish with { ResetZoom = "Visa allt" }, mine); // value equality: no relayout per render
        Assert.NotEqual(ChartTexts.Swedish, mine);
    }

    [Fact]
    public void From_reads_texts_by_key_and_keeps_the_fallback_for_missing_ones()
    {
        var resources = new Dictionary<string, string> { ["NoData"] = "Pas de mesures", ["ResetZoom"] = "" };

        var texts = ChartTexts.From(key => resources.GetValueOrDefault(key), ChartTexts.French);

        Assert.Equal("Pas de mesures", texts.NoData);
        Assert.Equal("Réinitialiser le zoom", texts.ResetZoom); // empty: the fallback
        Assert.Equal("Légende", texts["Legend"]);
        Assert.Contains("ResetZoom", ChartTexts.Keys);
    }

    [Fact]
    public void Counts_pick_the_one_or_other_form()
    {
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a", Name = "Stockholm" }] };
        var sv = CultureInfo.GetCultureInfo("sv-SE");

        var one = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["a"] = Temps(20) }, new LayoutOptions { Culture = sv });
        var two = ChartLayout.Compute(spec with { Series = [.. spec.Series, new SeriesSpec { Id = "b", Name = "Göteborg" }] },
            new Dictionary<string, SeriesData> { ["a"] = Temps(20, 21), ["b"] = Temps(22, 22) }, new LayoutOptions { Culture = sv });

        Assert.StartsWith("Diagram med 1 serie. Stockholm: 1 värde,", one.Scene.Description);
        Assert.StartsWith("Diagram med 2 serier. Stockholm: 2 värden,", two.Scene.Description);
    }

    [Fact]
    public void Compact_summary_in_Swedish()
    {
        var spec = new ChartSpec { Compact = true, Series = [new SeriesSpec { Id = "t", Name = "Uppsala", Format = "0.0 °C" }] };

        var layout = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["t"] = Temps(18.2, 19.5, 20.1, 20.8) },
            new LayoutOptions { Width = 160, Height = 32, Culture = CultureInfo.GetCultureInfo("sv-SE") });

        Assert.Equal("Uppsala: 18,2 °C till 20,8 °C, senast 20,8 °C, stigande.", layout.Scene.Description);
    }

    [Fact]
    public void A_broken_template_falls_back_to_English_instead_of_throwing()
    {
        var texts = ChartTexts.English with { LastValue = "senast {3}", ValueRange = "{0} till {1" };
        var spec = new ChartSpec { Compact = true, Series = [new SeriesSpec { Id = "t", Name = "Uppsala" }] };

        var layout = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["t"] = Temps(18, 21) },
            new LayoutOptions { Culture = CultureInfo.InvariantCulture, Texts = texts });

        Assert.Equal("Uppsala: 18 to 21, last 21, rising.", layout.Scene.Description);
        Assert.Equal("broken {", ChartTexts.Format("broken {", null, null, null));
    }

    [Fact]
    public void Pie_texts_follow_the_texts()
    {
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "p", Type = SeriesType.Pie, InnerRadius = 0.6, GroupSmallSlices = 0.05 }] };
        var data = SeriesData.FromCategories(["Industri", "Bostäder", "Transporter", "Övrigt 1", "Övrigt 2"], [50, 30, 18, 1, 1]);

        var layout = ChartLayout.Compute(spec, new Dictionary<string, SeriesData> { ["p"] = data },
            new LayoutOptions { Width = 600, Height = 400, Culture = CultureInfo.GetCultureInfo("de-DE") });

        Assert.Contains(All(layout.Scene.Series), n => n is TextNode { Text: "Gesamt" });
        Assert.Contains(layout.Tooltip.Slices, s => s.Label == "Sonstige");
        Assert.StartsWith("Ringdiagramm mit 4 Einträgen: ", layout.Scene.Description);
    }

    public static TheoryData<string> Languages() => [.. ChartTexts.BuiltIn.Keys];

    [Theory]
    [MemberData(nameof(Languages))]
    public void Built_in_languages_use_the_same_placeholders_as_English(string language)
    {
        var texts = ChartTexts.BuiltIn[language];
        foreach (var key in ChartTexts.Keys)
        {
            var english = Placeholder().Matches(ChartTexts.English[key]).Select(m => m.Value).Distinct().Order();
            var translated = Placeholder().Matches(texts[key]).Select(m => m.Value).Distinct().Order();
            Assert.True(english.SequenceEqual(translated), $"{language} {key}: '{texts[key]}'");
            Assert.True(texts[key].Count(c => c == '|') <= 1, $"{language} {key}: at most one|other");
        }
    }
}
