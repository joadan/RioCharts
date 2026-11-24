using System.Globalization;
using RioCharts.Core.Formatting;
using RioCharts.Core.Scene;
using RioCharts.Core.Series;

namespace RioCharts.Core.Layout;

internal static partial class ChartLayout
{
    /// <summary>
    /// Whether a series draws error bars: a bar, line or scatter series with error ends in its data
    /// (<see cref="Data.SeriesData.ErrorLow"/>) or a plus-or-minus amount (<see cref="SeriesOptions.Error"/>),
    /// not stacked to 100 %.
    /// </summary>
    private static bool HasErrors(Resolved r) =>
        r.Spec.Type is SeriesType.Bar or SeriesType.Line or SeriesType.Scatter && HasErrorData(r) && !r.Percent;

    private static bool HasErrorData(Resolved r) => (r.Data.ErrorLow is not null && r.Data.ErrorHigh is not null) || r.Spec.Error is > 0;

    /// <summary>A point's error bar ends as given (low, high); NaN when it has none.</summary>
    private static (double Low, double High) RawErrorAt(Resolved r, int i)
    {
        var y = r.Data.Y[i];
        if (!double.IsFinite(y))
        {
            return (double.NaN, double.NaN);
        }

        var (low, high) = r.Data.ErrorLow is { } lows && r.Data.ErrorHigh is { } highs ? (lows[i], highs[i])
            : r.Spec.Error is > 0 and var e ? (y - e, y + e)
            : (double.NaN, double.NaN);
        return double.IsFinite(low) && double.IsFinite(high) ? (Math.Min(low, high), Math.Max(low, high)) : (double.NaN, double.NaN);
    }

    /// <summary>A point's error bar ends where they are drawn: moved with its stack segment (the bar's top).</summary>
    private static (double Low, double High) ErrorAt(Resolved r, int i)
    {
        var (low, high) = RawErrorAt(r, i);
        var shift = i < r.Upper.Length && double.IsFinite(r.Upper[i]) ? r.Upper[i] - r.Data.Y[i] : 0;
        return (low + shift, high + shift);
    }

    /// <summary>The error in a tooltip: " ± 1.2" when it is the same both ways, else " (10.1 – 14.6)".</summary>
    private static string ErrorText(Resolved r, int i, CultureInfo culture, ValueFormat format)
    {
        var (low, high) = RawErrorAt(r, i);
        if (!double.IsFinite(low))
        {
            return "";
        }

        var y = r.Data.Y[i];
        var (down, up) = (y - low, high - y);
        return Math.Abs(up - down) <= 1e-9 * Math.Max(1, Math.Abs(y))
            ? $" ± {ValueFormatter.Format(up, culture, format)}"
            : $" ({ValueFormatter.Format(low, culture, format)} – {ValueFormatter.Format(high, culture, format)})";
    }

    /// <summary>
    /// The error bars of the drawn points (hits) of a vertical series: a line from low to high with a
    /// cap at each end, one path for the series. Bars get a dark bar across them (the text color), so
    /// they show on the bar; points and lines take the series color.
    /// </summary>
    private static PathNode? ErrorBars(Resolved r, SeriesBuildContext c, HitTestIndex.SeriesHits hits, StyleTable styles)
    {
        var bars = r.Type.UsesSlots;
        var cap = bars ? Math.Clamp(c.BarWidth * 0.2, 2, 6) : 4;
        var path = new PathBuilder();
        var any = false;
        for (var k = 0; k < hits.Index.Length; k++)
        {
            var (low, high) = ErrorAt(r, hits.Index[k]);
            if (!double.IsFinite(low))
            {
                continue;
            }

            if (r.Axis.Log && !(low > 0))
            {
                continue; // a low end of zero or below is not on a log axis
            }

            var x = hits.X[k] + (bars ? c.BarOffset : 0);
            var (y0, y1) = (c.MapY(low), c.MapY(high));
            if (!double.IsFinite(y0) || !double.IsFinite(y1))
            {
                continue;
            }

            path.MoveTo(x, y0).LineTo(x, y1)
                .MoveTo(x - cap, y0).LineTo(x + cap, y0)
                .MoveTo(x - cap, y1).LineTo(x + cap, y1);
            any = true;
        }

        if (!any)
        {
            return null;
        }

        var style = bars
            ? styles.Add("error-bar", new Style { Stroke = ThemeTokens.Text, StrokeWidth = 1.25f, Fill = "none" })
            : styles.Add($"series-{r.ColorIndex}-error", new Style { Stroke = r.Color, StrokeWidth = 1.5f, Fill = "none" });
        return new PathNode(path.Build(), style) { Class = "rio-error-bar" };
    }

    /// <summary>The same error bars across horizontal bars: from low to high at the bar's middle, capped.</summary>
    private static void HorizontalErrorBar(PathBuilder path, double x0, double x1, double cy, double height)
    {
        var cap = Math.Clamp(height * 0.2, 2, 6);
        path.MoveTo(x0, cy).LineTo(x1, cy)
            .MoveTo(x0, cy - cap).LineTo(x0, cy + cap)
            .MoveTo(x1, cy - cap).LineTo(x1, cy + cap);
    }
}
