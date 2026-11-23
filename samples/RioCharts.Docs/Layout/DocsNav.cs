namespace RioCharts.Docs.Layout;

/// <summary>The site's pages in reading order: the sidebar, and the previous/next links under each page.</summary>
public static class DocsNav
{
    public sealed record Page(string Href, string Title);

    public sealed record Group(string Name, Page[] Pages);

    public static readonly Group[] Groups =
    [
        new("Start", [new("", "Overview"), new("getting-started", "Getting started"), new("from-apexcharts", "Coming from ApexCharts")]),
        new("Charts", [new("gallery", "Gallery"), new("lines-and-areas", "Lines and areas"), new("bars", "Bars and stacking"), new("scatter", "Scatter and bubbles"), new("heatmap", "Heatmap"), new("box-plots", "Box plots"), new("histogram", "Histogram"), new("dumbbells", "Dumbbell charts"), new("range-bars", "Range bars and timelines"), new("candlesticks", "Candlesticks"), new("waterfall", "Waterfall"), new("radar", "Radar"), new("treemap", "Treemap"), new("pies", "Pie, donut and radial"), new("gauges", "Gauges"), new("bullets", "Bullet charts")]),
        new("Features",
        [
            new("axes", "Axes and time"), new("annotations", "Annotations"), new("error-bars", "Error bars"), new("tooltips", "Tooltips"),
            new("clicks-and-selection", "Clicks and selection"), new("zoom", "Zoom and pan"), new("keyboard", "Keyboard access"),
            new("live-data", "Live data"), new("large-data", "Large data"), new("compact", "Compact charts"), new("theming", "Theming"),
            new("languages", "Languages and texts"), new("export", "Export and download"),
        ]),
        new("Beyond Blazor", [new("definitions", "Chart definitions (playground)"), new("server-export", "Server-side export"), new("ai", "AI agents and MCP")]),
    ];

    public static readonly Page[] All = Groups.SelectMany(g => g.Pages).ToArray();

    public static int IndexOf(string relativePath)
    {
        var path = relativePath.Split('?', '#')[0].Trim('/');
        return Array.FindIndex(All, p => p.Href == path);
    }
}
