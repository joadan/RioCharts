using System.Globalization;
using RioCharts.Core;
using RioCharts.Core.Data;
using RioCharts.Core.Layout;

namespace RioCharts.Tests;

public class Milestone4Tests
{
    private static readonly LayoutOptions Options = new() { Width = 600, Height = 300, Culture = CultureInfo.InvariantCulture };

    // ---------- Zoom ----------

    [Fact]
    public void Visible_range_sets_the_x_domain_and_the_y_axis_fits_the_visible_data()
    {
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From([0.0, 1, 2, 3, 4, 5], [1.0, 2, 3, 100, 200, 300]) };
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a" }] };

        var all = ChartLayout.Compute(spec, data, Options);
        var zoomed = ChartLayout.Compute(spec, data, Options with { XRange = (0, 2) });

        Assert.False(all.Zoomed);
        Assert.True(zoomed.Zoomed);
        Assert.Equal((0, 2), (zoomed.XScale!.DomainMin, zoomed.XScale.DomainMax));
        Assert.Equal(all.XExtent, zoomed.XExtent); // the full range stays known for reset and clamping
        Assert.True(zoomed.YScale.DomainMax < 10);
        Assert.True(all.YScale.DomainMax >= 300);
    }

    [Fact]
    public void Zooming_into_large_data_downsamples_only_the_visible_points()
    {
        const int n = 100_000;
        var x = Enumerable.Range(0, n).Select(i => (double)i).ToArray();
        var y = x.Select(v => Math.Sin(v / 50)).ToArray();
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a" }] };
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From(x, y) };

        var zoomed = ChartLayout.Compute(spec, data, Options with { XRange = (1000, 1400) });
        var hits = zoomed.HitTest.Series[0];

        // 401 visible points fit under the threshold: all of them are drawn, none outside.
        Assert.InRange(hits.X.Length, 399, 402);
        Assert.All(hits.X, px => Assert.InRange(px, zoomed.Plot.X - 1, zoomed.Plot.Right + 1));
    }

    // ---------- Streaming ----------

    [Fact]
    public void Buffer_keeps_its_time_window()
    {
        var buffer = new SeriesBuffer(timeWindow: TimeSpan.FromMinutes(1));
        var start = new DateTime(2026, 1, 1);
        for (var s = 0; s < 300; s++)
        {
            buffer.Append(start.AddSeconds(s), s);
        }

        var data = buffer.ToSeriesData();

        Assert.Equal(61, data.Count); // 240 s .. 299 s
        Assert.Equal(TimeValue.FromDateTime(start.AddSeconds(239)), data.X[0]);
        Assert.Equal(XKind.Time, data.Kind);
    }

    [Fact]
    public void Buffer_drops_the_oldest_points_at_capacity_and_grows_until_then()
    {
        var buffer = new SeriesBuffer(capacity: 3000, kind: XKind.Numeric);
        var changes = 0;
        buffer.Changed += () => changes++;
        for (var i = 0; i < 5000; i++)
        {
            buffer.Append(i, i * 2);
        }

        var data = buffer.ToSeriesData();

        Assert.Equal(3000, data.Count);
        Assert.Equal(2000, data.X[0]);
        Assert.Equal(9998, data.Y[^1]);
        Assert.Equal(5000, changes);
    }

    [Fact]
    public void Buffer_snapshot_is_sorted_even_when_points_arrive_out_of_order()
    {
        var buffer = new SeriesBuffer(kind: XKind.Numeric);
        buffer.AppendRange([3.0, 1, 2], [30.0, 10, 20]);
        var version = buffer.Version;

        var data = buffer.ToSeriesData();

        Assert.Equal([1.0, 2, 3], data.X);
        Assert.Equal([10.0, 20, 30], data.Y);
        buffer.Clear();
        Assert.True(buffer.Version > version);
        Assert.Equal(0, buffer.ToSeriesData().Count);
    }

    [Fact]
    public async Task Buffer_consumes_an_async_stream()
    {
        var buffer = new SeriesBuffer(kind: XKind.Numeric);

        await buffer.ConsumeAsync(Values(), v => v, v => v * 10);

        Assert.Equal(5, buffer.Count);

        static async IAsyncEnumerable<double> Values()
        {
            for (var i = 0; i < 5; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }
    }

    // ---------- Annotations ----------

    private static IEnumerable<Core.Scene.Node> All(Core.Scene.Node node) =>
        node is Core.Scene.GroupNode g ? g.Children.SelectMany(All).Prepend(g) : [node];

    [Fact]
    public void Point_and_callout_annotations_are_drawn_and_kept_in_view()
    {
        var start = new DateTime(2026, 3, 1);
        var data = new Dictionary<string, SeriesData>
        {
            ["a"] = SeriesData.From(Enumerable.Range(0, 24).Select(h => start.AddHours(h)).ToArray(), Enumerable.Range(0, 24).Select(h => 20.0 + h % 3).ToArray()),
        };
        var spec = new ChartSpec
        {
            Series = [new SeriesSpec { Id = "a" }],
            Annotations =
            [
                new AnnotationSpec { Type = AnnotationType.Point, X = start.AddHours(5), Y = 30.0, Label = "Alarm" },
                new AnnotationSpec { Type = AnnotationType.Callout, X = "2026-03-01T10:00", Y = 21.0, Label = "Filter changed" },
            ],
        };

        var result = ChartLayout.Compute(spec, data, Options);
        var nodes = All(result.Scene.Series).ToList();

        Assert.True(result.YScale.DomainMax >= 30); // the point expands the y domain
        Assert.Equal(2, nodes.Count(n => n.Class == "rio-annotation-marker"));
        Assert.Contains(nodes, n => n is Core.Scene.TextNode { Text: "Alarm" });
        var box = nodes.OfType<Core.Scene.RectNode>().Single(r => r.Class == "rio-callout-box");
        Assert.True(result.Plot.Contains(box.Bounds.X, box.Bounds.Y));
        Assert.Contains(nodes, n => n.Class == "rio-callout-connector");
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Draggable_lines_report_a_handle_with_their_scale()
    {
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From([0.0, 10], [10.0, 30]) };
        var spec = new ChartSpec
        {
            Series = [new SeriesSpec { Id = "a" }],
            Annotations = [new AnnotationSpec { Id = "sp", Value = 21, Draggable = true }, new AnnotationSpec { Value = 15 }],
        };

        var result = ChartLayout.Compute(spec, data, Options);
        var handle = Assert.Single(result.DragHandles);

        Assert.Equal("sp", handle.Id);
        Assert.False(handle.IsX);
        Assert.Equal(result.YScale.Map(21), handle.Pixel, 0.01);
        Assert.Equal(result.YScale.DomainMin, handle.DomainMin);
        Assert.Contains(All(result.Scene.Series), n => n is Core.Scene.GroupNode { Key: "annotation:sp" } g && g.Class!.Contains("rio-draggable-y"));
    }

    // ---------- Time zones ----------

    [Fact]
    public void Hourly_data_across_the_autumn_dst_change_is_evenly_spaced_and_labelled_in_the_zone()
    {
        // Stockholm leaves summer time on 2026-10-25 at 03:00 local (01:00 UTC): 02:00 happens twice.
        var utc = Enumerable.Range(0, 8).Select(h => new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero).AddHours(h)).ToArray();
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From(utc, utc.Select((_, i) => (double)i).ToArray()) };
        var spec = new ChartSpec { TimeZone = "Europe/Stockholm", Series = [new SeriesSpec { Id = "a" }] };

        var result = ChartLayout.Compute(spec, data, Options with { ViewerTimeZone = "Europe/Stockholm" });
        var s = result.Tooltip.Series[0];

        var gaps = s.X.Zip(s.X.Skip(1), (a, b) => b - a).ToList();
        Assert.All(gaps, g => Assert.Equal(gaps[0], g, 0.01)); // instants: never doubles back
        Assert.Equal(2, s.XLabels.Count(l => l.EndsWith("02:00", StringComparison.Ordinal)));
        Assert.Equal("10/25/2026 00:00", s.XLabels[0]); // 22:00 UTC is midnight in Stockholm (UTC+2)
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Times_are_shown_as_given_without_a_zone()
    {
        var times = new[] { new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(5)), new DateTimeOffset(2026, 1, 1, 13, 0, 0, TimeSpan.FromHours(5)) };
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From(times, [1.0, 2]) };

        var result = ChartLayout.Compute(new ChartSpec { Series = [new SeriesSpec { Id = "a" }] }, data, Options);

        Assert.Equal("01/01/2026 12:00", result.Tooltip.Series[0].XLabels[0]);
        Assert.Null(result.Time);
    }

    [Fact]
    public void The_zone_is_shown_when_it_differs_from_the_viewers_and_unspecified_times_follow_the_option()
    {
        var local = new[] { new DateTime(2026, 7, 1, 12, 0, 0), new DateTime(2026, 7, 1, 13, 0, 0) }; // Kind Unspecified
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From(local, [1.0, 2]) };
        var spec = new ChartSpec { TimeZone = "Europe/Stockholm", UnspecifiedTimes = UnspecifiedTimes.DisplayZone, Series = [new SeriesSpec { Id = "a" }] };

        var result = ChartLayout.Compute(spec, data, Options with { ViewerTimeZone = "America/New_York" });

        Assert.Equal("07/01/2026 12:00 UTC+02:00", result.Tooltip.Series[0].XLabels[0]);
        Assert.Equal(TimeValue.FromDateTime(new DateTime(2026, 7, 1, 10, 0, 0)), result.XExtent!.Value.Min, 1);
        Assert.Empty(result.Warnings);

        var unset = ChartLayout.Compute(spec with { UnspecifiedTimes = null }, data, Options);
        Assert.Contains(unset.Warnings, w => w.StartsWith("unspecifiedTimes", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_zones_are_reported_and_times_shown_as_given()
    {
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.From([new DateTime(2026, 1, 1)], [1.0]) };

        var result = ChartLayout.Compute(new ChartSpec { TimeZone = "Mars/Olympus", Series = [new SeriesSpec { Id = "a" }] }, data, Options);

        Assert.Null(result.Time);
        Assert.Contains(result.Warnings, w => w.StartsWith("timeZone", StringComparison.Ordinal));
    }

    [Fact]
    public void Visible_range_is_ignored_on_category_axes()
    {
        var spec = new ChartSpec { Series = [new SeriesSpec { Id = "a", Type = SeriesType.Bar }] };
        var data = new Dictionary<string, SeriesData> { ["a"] = SeriesData.FromCategories(["A", "B"], [1.0, 2]) };

        var result = ChartLayout.Compute(spec, data, Options with { XRange = (0, 1) });

        Assert.False(result.Zoomed);
        Assert.Null(result.XExtent);
    }
}
