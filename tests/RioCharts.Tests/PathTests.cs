using System.Globalization;
using RioCharts.Blazor.Rendering;
using RioCharts.Core;
using RioCharts.Core.Paths;
using RioCharts.Core.Scene;
using RioCharts.Core.Text;

namespace RioCharts.Tests;

public class PathTests
{
    [Fact]
    public void Svg_numbers_are_invariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
            var path = new PathBuilder().MoveTo(0, 0).LineTo(10.25, 3.333).Build();

            Assert.Equal("M0 0 L10.25 3.33", SvgWriter.PathD(path));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Theme_tokens_become_css_variables_with_defaults()
    {
        Assert.Equal("var(--rio-series-1, var(--_rio-series-1))", SvgWriter.Color("--rio-series-1"));
        Assert.Equal("red", SvgWriter.Color("red"));
        Assert.Equal("none", SvgWriter.Color(null));
    }

    [Fact]
    public void Full_circle_arc_is_two_svg_arcs()
    {
        var path = new PathBuilder().Arc(50, 50, 10, 0, Math.PI * 2).Build();

        var d = SvgWriter.PathD(path);

        Assert.StartsWith("M50 40", d);
        Assert.Equal(2, d.Count(c => c == 'A'));
    }

    [Fact]
    public void Arc_after_a_point_connects_with_a_line()
    {
        var path = new PathBuilder().MoveTo(50, 50).Arc(50, 50, 10, 0, Math.PI / 2).Close().Build();

        Assert.Equal("M50 50 L50 40 A10 10 0 0 1 60 50 Z", SvgWriter.PathD(path));
    }

    [Fact]
    public void Monotone_curve_does_not_overshoot()
    {
        double[] x = [0, 10, 20, 30];
        double[] y = [0, 0, 10, 10];

        var path = LineGenerator.Line(x, y, CurveType.Monotone);

        // Control point y values stay within the data range.
        for (var i = 0; i < path.Commands.Count; i++)
        {
            Assert.NotEqual(PathCommand.Arc, path.Commands[i]);
        }

        Assert.All(path.Coords.Where((_, i) => i % 2 == 1), v => Assert.InRange(v, 0f, 10f));
    }

    [Fact]
    public void Step_curve_has_only_horizontal_and_vertical_moves()
    {
        var path = LineGenerator.Line([0.0, 10, 20], [0.0, 5, 5], CurveType.Step);
        var d = SvgWriter.PathD(path);

        Assert.Equal("M0 0 L10 0 L10 5 L20 5 L20 5", d);
    }

    [Fact]
    public void Area_closes_down_to_baseline_per_segment()
    {
        var path = LineGenerator.Area([0.0, 10, 20, 30], [5.0, 6, double.NaN, 7], 100, CurveType.Linear);

        Assert.Equal(2, path.Commands.Count(c => c == PathCommand.Close));
        Assert.Equal("M0 100 L0 5 L10 6 L10 100 Z M30 100 L30 7 L30 7 L30 100 Z", SvgWriter.PathD(path));
    }

    [Fact]
    public void Width_table_measurer_uses_measured_widths()
    {
        var widths = Enumerable.Repeat(0.5, 95).ToArray();
        widths['W' - 32] = 1.0;
        var measurer = new WidthTableTextMeasurer(widths);

        Assert.Equal(12, measurer.Measure("WW", 6), 9);
        Assert.Equal(6, measurer.Measure("ab", 6), 9);
    }
}
