namespace RioCharts.Core.Layout;

public static partial class ChartLayout
{
    /// <summary>What a chart has room for (auto-compact). All true when auto-compact is off.</summary>
    private readonly record struct Chrome(bool Title, bool Legend, bool RightLegendToTop, bool XTitle, bool YTitles, bool XLabels, bool YLabels)
    {
        public static readonly Chrome All = new(true, true, false, true, true, true, true);
    }

    // Sizes at the default 12 px font; they scale with the font size.
    internal const double AutoCompactWidth = 120;
    internal const double AutoCompactHeight = 60;

    /// <summary>
    /// Auto-compact sheds chrome as a chart gets small, so the plot keeps most of the space: first
    /// the axis titles, then the legend (the tooltip still names the series), the title and the axis
    /// labels. Below <see cref="AutoCompactWidth"/> × <see cref="AutoCompactHeight"/> the chart is
    /// laid out as a compact chart.
    /// </summary>
    private static Chrome FitChrome(bool enabled, double width, double height, double font)
    {
        if (!enabled)
        {
            return Chrome.All;
        }

        var k = font / 12;
        return new Chrome(
            Title: height >= 120 * k,
            Legend: height >= 160 * k && width >= 200 * k,
            RightLegendToTop: width < 400 * k,
            XTitle: height >= 180 * k,
            YTitles: height >= 150 * k && width >= 200 * k,
            XLabels: height >= 90 * k,
            YLabels: width >= 200 * k);
    }

    /// <summary>Whether auto-compact lays the chart out as a compact chart (too small for any axis labels).</summary>
    private static bool AutoCompacts(ChartDefinition spec, LayoutOptions options) =>
        spec.AutoCompact && !spec.Compact
        && (options.Width < AutoCompactWidth * options.FontSize / 12 || options.Height < AutoCompactHeight * options.FontSize / 12);

    /// <summary>The legend's position: a right legend moves to the top on narrow charts.</summary>
    private static LegendPosition FitLegendPosition(LegendPosition position, Chrome chrome) =>
        position == LegendPosition.Right && chrome.RightLegendToTop ? LegendPosition.Top : position;

    /// <summary>An automatic legend that would take more than 30% of the height is left out.</summary>
    private static bool LegendTooTall(ChartDefinition spec, double legendHeight, double height) =>
        spec.AutoCompact && spec.Legend.Mode == LegendMode.Auto && legendHeight > height * 0.3;
}
