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
