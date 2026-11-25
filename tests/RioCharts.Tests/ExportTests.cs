using System.Globalization;
using RioCharts.Core.Data;

namespace RioCharts.Tests;

public class ExportTests
{
    [Fact]
    public void Csv_joins_series_on_x_with_empty_cells_for_gaps()
    {
        var london = SeriesData.From([1.0, 2, 3], [20.5, double.NaN, 21]);
        var toronto = SeriesData.From([2.0, 4], [19, 18.25]);

        var csv = ChartCsv.Write([("London °C", london), ("Toronto", toronto)]);

        Assert.Equal("x,London °C,Toronto\r\n1,20.5,\r\n2,,19\r\n3,21,\r\n4,,18.25\r\n", csv);
    }

    [Fact]
    public void Csv_keeps_category_order_and_quotes_cells()
    {
        var data = SeriesData.FromCategories(["Store 1", "Store, east", "Say \"hi\""], [1, 2, 3]);

        var csv = ChartCsv.Write([("Orders", data)], new CsvOptions { XHeader = "Store", NewLine = "\n" });

        Assert.Equal("Store,Orders\nStore 1,1\n\"Store, east\",2\n\"Say \"\"hi\"\"\",3\n", csv);
    }

    [Fact]
    public void Csv_repeats_rows_for_the_same_x_instead_of_losing_points()
    {
        var a = SeriesData.From([1.0, 1, 2], [10, 11, 12]);
        var b = SeriesData.From([1.0], [5]);

        var csv = ChartCsv.Write([("a", a), ("b", b)], new CsvOptions { NewLine = "\n" });

        Assert.Equal("x,a,b\n1,10,5\n1,11,\n2,12,\n", csv);
    }

    [Fact]
    public void Csv_writes_times_as_iso_8601_and_dates_alone_for_daily_data()
    {
        var hourly = SeriesData.From([new DateTime(2026, 3, 1, 0, 0, 0), new DateTime(2026, 3, 1, 1, 30, 0)], [1.0, 2]);
        var daily = SeriesData.From([new DateTime(2026, 3, 1), new DateTime(2026, 3, 2)], [1.0, 2]);
        var withOffset = SeriesData.From([new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.FromHours(1))], [1.0]);

        Assert.Contains("2026-03-01T00:00:00,1", ChartCsv.Write([("t", hourly)]));
        Assert.Contains("2026-03-01T01:30:00,2", ChartCsv.Write([("t", hourly)]));
        Assert.Contains("2026-03-02,2", ChartCsv.Write([("t", daily)]));
        Assert.Contains("2026-03-01T08:00:00+01:00,1", ChartCsv.Write([("t", withOffset)]));
    }

    [Fact]
    public void Csv_text_cells_never_start_a_spreadsheet_formula()
    {
        var data = SeriesData.FromCategories(["=HYPERLINK(\"http://example.com\")", "+1 day", "-5", "@sum", "Store 1"], [1.0, 2, 3, 4, 5]);

        var csv = ChartCsv.Write([("=cmd", data)], new CsvOptions { NewLine = "\n" });

        Assert.Equal("x,'=cmd\n\"'=HYPERLINK(\"\"http://example.com\"\")\",1\n'+1 day,2\n-5,3\n'@sum,4\nStore 1,5\n", csv);
        Assert.Equal("x,a\n-1.5,2\n", ChartCsv.Write([("a", SeriesData.From([-1.5], [2.0]))], new CsvOptions { NewLine = "\n" })); // numbers stay numbers
    }

    [Fact]
    public void Csv_keeps_the_repeated_autumn_hour_apart()
    {
        // Stockholm leaves summer time on 25 October 2026: 02:30 comes twice, at +02:00 and then +01:00.
        var data = SeriesData.From(
            [new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(1))],
            [1.0, 2]);

        var csv = ChartCsv.Write([("t", data)], new CsvOptions { NewLine = "\n" });

        Assert.Equal("x,t\n2026-10-25T02:30:00+02:00,1\n2026-10-25T02:30:00+01:00,2\n", csv);
    }

    [Fact]
    public void Csv_uses_the_given_culture_and_delimiter()
    {
        var data = SeriesData.From([1.5], [20.25]);

        var csv = ChartCsv.Write([("Temp", data)], new CsvOptions { Delimiter = ';', Culture = CultureInfo.GetCultureInfo("sv-SE"), NewLine = "\n" });

        Assert.Equal("x;Temp\n1,5;20,25\n", csv);
    }

    [Fact]
    public void Csv_of_no_data_is_just_the_header()
    {
        Assert.Equal("x\r\n", ChartCsv.Write([]));
        Assert.Equal("x,Empty\r\n", ChartCsv.Write([("Empty", SeriesData.Empty)]));
    }
}
