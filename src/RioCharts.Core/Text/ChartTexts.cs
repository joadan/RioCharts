using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace RioCharts.Core;

/// <summary>
/// The texts a chart shows or reads out itself: empty states, the legend and axis names for screen
/// readers, the chart description, "Reset zoom", the download menu and the keyboard help. Your own
/// texts (titles, series names, formats) are not here.
/// </summary>
/// <remarks>
/// <para>
/// A chart picks its texts from its culture (in Blazor: <c>Culture</c>, else the current UI culture),
/// trying the full name, then the parent cultures: first languages added with <see cref="Register"/>,
/// then the built-in ones (<see cref="BuiltIn"/>), then <see cref="English"/>.
/// </para>
/// <para>
/// Change single texts with <c>with</c>: <c>ChartTexts.Swedish with { ResetZoom = "Visa allt" }</c>.
/// Texts that are not set fall back to English. Load them from resx files or an
/// <c>IStringLocalizer</c> with <see cref="From"/>; the keys are the property names (<see cref="Keys"/>).
/// </para>
/// <para>
/// Templates use <c>{0}</c>, <c>{1}</c>… placeholders. A text with a count can have two forms,
/// "one|other": the first is used when the count is 1, e.g. <c>"{0}: {1} value|{0}: {1} values"</c>.
/// A template that cannot be filled in falls back to the English one.
/// </para>
/// </remarks>
public sealed record ChartTexts
{
    private readonly Dictionary<string, string> _texts;

    /// <summary>English texts (all others fall back to them).</summary>
    public ChartTexts()
    {
        _texts = new Dictionary<string, string>(Defaults, StringComparer.Ordinal);
    }

    private ChartTexts(ChartTexts original)
    {
        _texts = new Dictionary<string, string>(original._texts, StringComparer.Ordinal);
    }

    // ---------- Shown in the chart ----------

    /// <summary>The chart's name when it has no title (screen readers).</summary>
    public string Chart { get => Get(); init => Set(value); }

    /// <summary>In the plot when there is nothing to draw.</summary>
    public string NoData { get => Get(); init => Set(value); }

    /// <summary>In the plot when every series is hidden through the legend.</summary>
    public string AllSeriesHidden { get => Get(); init => Set(value); }

    /// <summary>In a pie or radial bar chart when every slice is hidden through the legend.</summary>
    public string AllSlicesHidden { get => Get(); init => Set(value); }

    /// <summary>The slice that collects slices too small to show.</summary>
    public string Other { get => Get(); init => Set(value); }

    /// <summary>Under the total in a donut's center.</summary>
    public string Total { get => Get(); init => Set(value); }

    /// <summary>The label of a bubble's size in its tooltip, when the series does not name it: "Size".</summary>
    public string Size { get => Get(); init => Set(value); }

    /// <summary>The Reset zoom button's tooltip and accessible name (the button is an icon).</summary>
    public string ResetZoom { get => Get(); init => Set(value); }

    /// <summary>The download menu's name.</summary>
    public string Download { get => Get(); init => Set(value); }

    /// <summary>The download button when it offers one format. {0}: the format, e.g. PNG.</summary>
    public string DownloadFormat { get => Get(); init => Set(value); }

    /// <summary>The download button when it opens the menu.</summary>
    public string DownloadChart { get => Get(); init => Set(value); }

    // ---------- Read by screen readers ----------

    /// <summary>What a keyboard-navigable chart is called (aria-roledescription), lower case.</summary>
    public string ChartRole { get => Get(); init => Set(value); }

    /// <summary>The legend.</summary>
    public string Legend { get => Get(); init => Set(value); }

    /// <summary>The x axis.</summary>
    public string XAxis { get => Get(); init => Set(value); }

    /// <summary>The y axis.</summary>
    public string YAxis { get => Get(); init => Set(value); }

    /// <summary>An axis with its first and last label. {0}: the axis, {1}: first label, {2}: last label.</summary>
    public string AxisRange { get => Get(); init => Set(value); }

    /// <summary>A series. {0}: its name, {1}: the number of points (one|other).</summary>
    public string SeriesPoints { get => Get(); init => Set(value); }

    /// <summary>Description start. {0}: the number of series (one|other).</summary>
    public string Summary { get => Get(); init => Set(value); }

    /// <summary>Description start with a title. {0}: the number of series (one|other), {1}: the title.</summary>
    public string SummaryTitled { get => Get(); init => Set(value); }

    /// <summary>Description of a series. {0}: its name, {1}: the number of values (one|other).</summary>
    public string SeriesValues { get => Get(); init => Set(value); }

    /// <summary>Description of a series' extremes. {0}: lowest value, {1}: its x, {2}: highest value, {3}: its x.</summary>
    public string LowestHighest { get => Get(); init => Set(value); }

    /// <summary>Description of a series' trend. {0}: <see cref="Rising"/>, <see cref="Falling"/> or <see cref="Stable"/>.</summary>
    public string Trend { get => Get(); init => Set(value); }

    /// <summary>An upward trend.</summary>
    public string Rising { get => Get(); init => Set(value); }

    /// <summary>A downward trend.</summary>
    public string Falling { get => Get(); init => Set(value); }

    /// <summary>No clear trend.</summary>
    public string Stable { get => Get(); init => Set(value); }

    /// <summary>A compact chart's range. {0}: lowest, {1}: highest.</summary>
    public string ValueRange { get => Get(); init => Set(value); }

    /// <summary>A compact chart's last value. {0}: the value.</summary>
    public string LastValue { get => Get(); init => Set(value); }

    /// <summary>Description of a histogram. {0}: its name, {1}: the number of values (one|other), {2} and {3}: the edges of the fullest bin.</summary>
    public string HistogramPeak { get => Get(); init => Set(value); }

    /// <summary>A range bar series in the chart description. {0}: series, {1}: bar count, {2}: earliest start, {3}: latest end, {4}: row count.</summary>
    public string RangeBarSummary { get => Get(); init => Set(value); }

    /// <summary>Units of a duration in range bar tooltips and labels, "days hours minutes seconds" separated by spaces, e.g. "d h min s" for "2 h 30 min".</summary>
    public string DurationUnits { get => Get(); init => Set(value); }

    /// <summary>Box plot tooltip: the highest value.</summary>
    public string BoxMax { get => Get(); init => Set(value); }

    /// <summary>Box plot tooltip: the upper quartile.</summary>
    public string BoxUpperQuartile { get => Get(); init => Set(value); }

    /// <summary>Box plot tooltip: the median.</summary>
    public string BoxMedian { get => Get(); init => Set(value); }

    /// <summary>Box plot tooltip: the lower quartile.</summary>
    public string BoxLowerQuartile { get => Get(); init => Set(value); }

    /// <summary>Box plot tooltip: the lowest value.</summary>
    public string BoxMin { get => Get(); init => Set(value); }

    /// <summary>Box plot tooltip: how many values a box summarizes.</summary>
    public string BoxCount { get => Get(); init => Set(value); }

    /// <summary>Box plot tooltip: the values beyond the whiskers.</summary>
    public string BoxOutliers { get => Get(); init => Set(value); }

    /// <summary>Candlestick tooltip: the period's first value.</summary>
    public string CandleOpen { get => Get(); init => Set(value); }

    /// <summary>Candlestick tooltip: the highest value.</summary>
    public string CandleHigh { get => Get(); init => Set(value); }

    /// <summary>Candlestick tooltip: the lowest value.</summary>
    public string CandleLow { get => Get(); init => Set(value); }

    /// <summary>Candlestick tooltip: the period's last value.</summary>
    public string CandleClose { get => Get(); init => Set(value); }

    /// <summary>Candlestick tooltip: the change from open to close.</summary>
    public string CandleChange { get => Get(); init => Set(value); }

    /// <summary>A compact chart's description when there is no data.</summary>
    public string NoDataShort { get => Get(); init => Set(value); }

    /// <summary>A pie chart.</summary>
    public string PieChart { get => Get(); init => Set(value); }

    /// <summary>A donut chart.</summary>
    public string DonutChart { get => Get(); init => Set(value); }

    /// <summary>A radial bar chart.</summary>
    public string RadialBarChart { get => Get(); init => Set(value); }

    /// <summary>A gauge chart.</summary>
    public string GaugeChart { get => Get(); init => Set(value); }

    /// <summary>A gauge's target value, e.g. "Target 80" in its tooltip and description.</summary>
    public string Target { get => Get(); init => Set(value); }

    /// <summary>A radar chart.</summary>
    public string RadarChart { get => Get(); init => Set(value); }

    /// <summary>A treemap.</summary>
    public string Treemap { get => Get(); init => Set(value); }

    /// <summary>Treemap tooltip: the item's share of its group.</summary>
    public string ShareOfGroup { get => Get(); init => Set(value); }

    /// <summary>Treemap tooltip: the item's share of the total.</summary>
    public string ShareOfTotal { get => Get(); init => Set(value); }

    /// <summary>Treemap path bar: the step that shows the whole tree.</summary>
    public string TreemapAll { get => Get(); init => Set(value); }

    /// <summary>Treemap: the button that shows a group (drill-down). {0}: the group.</summary>
    public string TreemapShow { get => Get(); init => Set(value); }

    /// <summary>Treemap: the path bar's name for screen readers.</summary>
    public string TreemapPathLabel { get => Get(); init => Set(value); }

    /// <summary>Treemap description: items left out. {0}: how many (one|other).</summary>
    public string TreemapLeftOut { get => Get(); init => Set(value); }

    /// <summary>Description of a pie or radial bar chart. {0}: the kind of chart, {1}: the number of items (one|other), {2}: the items.</summary>
    public string PolarItems { get => Get(); init => Set(value); }

    /// <summary>Description of an empty pie or radial bar chart. {0}: the kind of chart.</summary>
    public string PolarNoData { get => Get(); init => Set(value); }

    /// <summary>After a shortened list. {0}: how many were left out.</summary>
    public string More { get => Get(); init => Set(value); }

    /// <summary>Keyboard help: one series.</summary>
    public string KeysPoints { get => Get(); init => Set(value); }

    /// <summary>Keyboard help: several series.</summary>
    public string KeysPointsAndSeries { get => Get(); init => Set(value); }

    /// <summary>Keyboard help: pie and radial bar charts.</summary>
    public string KeysSlices { get => Get(); init => Set(value); }

    /// <summary>Keyboard help: zoom keys.</summary>
    public string KeysZoom { get => Get(); init => Set(value); }

    /// <summary>Keyboard help: when points can be clicked.</summary>
    public string KeysSelect { get => Get(); init => Set(value); }

    /// <summary>Keyboard help: leaving the chart.</summary>
    public string KeysLeave { get => Get(); init => Set(value); }

    /// <summary>Read after a point or slice. {0}: its position, {1}: how many there are.</summary>
    public string Position { get => Get(); init => Set(value); }

    // ---------- Lookup ----------

    /// <summary>The keys of all texts (the property names), e.g. for resx files.</summary>
    public static IReadOnlyList<string> Keys => KeyList;

    /// <summary>The text for a key (see <see cref="Keys"/>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown key.</exception>
    public string this[string key] => _texts[key];

    private static readonly ConcurrentDictionary<string, ChartTexts> Registered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds or replaces the texts for a culture or language ("pl", "pt-BR"), for every chart in the app.
    /// Call it at startup. Takes precedence over the built-in languages.
    /// </summary>
    public static void Register(string culture, ChartTexts texts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        ArgumentNullException.ThrowIfNull(texts);
        Registered[culture] = texts;
    }

    /// <summary>
    /// The texts for a culture: registered ones, then built-in ones, trying the culture and its parents
    /// (sv-FI, then sv); English when none match. Null: the current UI culture.
    /// </summary>
    public static ChartTexts For(CultureInfo? culture)
    {
        for (var c = culture ?? CultureInfo.CurrentUICulture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            if (Registered.TryGetValue(c.Name, out var texts) || BuiltIn.TryGetValue(c.Name, out texts))
            {
                return texts;
            }
        }

        return English;
    }

    /// <summary>
    /// Texts from a lookup by key (see <see cref="Keys"/>), e.g. resx resources or an IStringLocalizer:
    /// <c>ChartTexts.From(key => localizer[key] is { ResourceNotFound: false } s ? s.Value : null)</c>.
    /// Keys it returns null or empty for keep the text from <paramref name="fallback"/> (default English).
    /// </summary>
    public static ChartTexts From(Func<string, string?> lookup, ChartTexts? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        var texts = new ChartTexts(fallback ?? English);
        foreach (var key in Keys)
        {
            if (lookup(key) is { Length: > 0 } value)
            {
                texts._texts[key] = value;
            }
        }

        return texts;
    }

    /// <summary>
    /// Fills in a template the way charts do: <c>{0}</c>… placeholders, and with <paramref name="count"/>
    /// the first form of a "one|other" template when the count is 1. Never throws: a broken template
    /// falls back to <paramref name="fallback"/>, then to the template itself.
    /// </summary>
    internal static string Format(string template, IFormatProvider? provider, long? count, string? fallback, params object?[] args)
    {
        try
        {
            return string.Format(provider, PickForm(template, count), args);
        }
        catch (FormatException) when (fallback is not null && fallback != template)
        {
            return Format(fallback, provider, count, null, args);
        }
        catch (FormatException)
        {
            return PickForm(template, count);
        }
    }

    /// <summary>
    /// Fills in this set's template for <paramref name="key"/> (a property name, see <see cref="Keys"/>)
    /// with <paramref name="args"/>, the way charts do. Never throws: a template that cannot be
    /// filled falls back to the English one.
    /// </summary>
    /// <example><c>texts.Fill(nameof(ChartTexts.DownloadFormat), culture, "PNG")</c> gives "Download PNG".</example>
    public string Fill(string key, IFormatProvider? provider, params object?[] args) =>
        Format(_texts[key], provider, null, Defaults[key], args);

    /// <summary>Fills in a "one|other" template for <paramref name="key"/>.</summary>
    internal string FillCount(string key, long count, IFormatProvider? provider, params object?[] args) =>
        Format(_texts[key], provider, count, Defaults[key], args);

    private static string PickForm(string template, long? count)
    {
        var bar = template.IndexOf('|', StringComparison.Ordinal);
        if (bar < 0)
        {
            return template;
        }

        return count == 1 ? template[..bar] : template[(bar + 1)..];
    }

    private string Get([CallerMemberName] string key = "") => _texts[key];

    private void Set(string? value, [CallerMemberName] string key = "") =>
        _texts[key] = string.IsNullOrEmpty(value) ? Defaults[key] : value;

    /// <summary>True when both sets have the same texts.</summary>
    public bool Equals(ChartTexts? other) =>
        other is not null && (ReferenceEquals(this, other) || (_texts.Count == other._texts.Count && _texts.All(p => other._texts.TryGetValue(p.Key, out var v) && v == p.Value)));

    /// <summary>A hash of the texts, consistent with <see cref="Equals(ChartTexts?)"/>.</summary>
    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var (key, value) in _texts)
        {
            hash ^= HashCode.Combine(key, value);
        }

        return hash;
    }

    // ---------- Languages ----------

    private static readonly Dictionary<string, string> Defaults = new(StringComparer.Ordinal)
    {
        [nameof(Chart)] = "Chart",
        [nameof(NoData)] = "No data available",
        [nameof(AllSeriesHidden)] = "All series are hidden",
        [nameof(AllSlicesHidden)] = "All slices are hidden",
        [nameof(Other)] = "Other",
        [nameof(Total)] = "Total",
        [nameof(Size)] = "Size",
        [nameof(ResetZoom)] = "Reset zoom",
        [nameof(Download)] = "Download",
        [nameof(DownloadFormat)] = "Download {0}",
        [nameof(DownloadChart)] = "Download chart",
        [nameof(ChartRole)] = "chart",
        [nameof(Legend)] = "Legend",
        [nameof(XAxis)] = "X axis",
        [nameof(YAxis)] = "Y axis",
        [nameof(AxisRange)] = "{0}, from {1} to {2}",
        [nameof(SeriesPoints)] = "{0}: {1} data point|{0}: {1} data points",
        [nameof(Summary)] = "Chart with {0} series.",
        [nameof(SummaryTitled)] = "Chart \"{1}\" with {0} series.",
        [nameof(SeriesValues)] = "{0}: {1} value|{0}: {1} values",
        [nameof(LowestHighest)] = "lowest {0} at {1}, highest {2} at {3}",
        [nameof(Trend)] = "trend {0}",
        [nameof(Rising)] = "rising",
        [nameof(Falling)] = "falling",
        [nameof(Stable)] = "stable",
        [nameof(ValueRange)] = "{0} to {1}",
        [nameof(LastValue)] = "last {0}",
        [nameof(HistogramPeak)] = "{0}: {1} value, most from {2} to {3}|{0}: {1} values, most from {2} to {3}",
        [nameof(RangeBarSummary)] = "{0}: {1} bar in {4} rows, from {2} to {3}|{0}: {1} bars in {4} rows, from {2} to {3}",
        [nameof(DurationUnits)] = "d h min s",
        [nameof(NoDataShort)] = "No data.",
        [nameof(BoxMax)] = "Max",
        [nameof(BoxUpperQuartile)] = "Upper quartile",
        [nameof(BoxMedian)] = "Median",
        [nameof(BoxLowerQuartile)] = "Lower quartile",
        [nameof(BoxMin)] = "Min",
        [nameof(BoxCount)] = "Count",
        [nameof(BoxOutliers)] = "Outliers",
        [nameof(CandleOpen)] = "Open",
        [nameof(CandleHigh)] = "High",
        [nameof(CandleLow)] = "Low",
        [nameof(CandleClose)] = "Close",
        [nameof(CandleChange)] = "Change",
        [nameof(PieChart)] = "Pie chart",
        [nameof(DonutChart)] = "Donut chart",
        [nameof(RadialBarChart)] = "Radial bar chart",
        [nameof(GaugeChart)] = "Gauge",
        [nameof(Target)] = "Target",
        [nameof(RadarChart)] = "Radar chart",
        [nameof(Treemap)] = "Treemap",
        [nameof(ShareOfGroup)] = "Share of group",
        [nameof(ShareOfTotal)] = "Share of total",
        [nameof(TreemapAll)] = "All",
        [nameof(TreemapShow)] = "Show {0}",
        [nameof(TreemapPathLabel)] = "Path",
        [nameof(TreemapLeftOut)] = "{0} item with zero or negative value left out|{0} items with zero or negative values left out",
        [nameof(PolarItems)] = "{0} with {1} item: {2}|{0} with {1} items: {2}",
        [nameof(PolarNoData)] = "{0}, no data.",
        [nameof(More)] = "and {0} more",
        [nameof(KeysPoints)] = "Use Left and Right to move between points.",
        [nameof(KeysPointsAndSeries)] = "Use Left and Right to move between points, Up and Down to change series.",
        [nameof(KeysSlices)] = "Use the arrow keys to move between slices.",
        [nameof(KeysZoom)] = "Plus and minus zoom, Shift with Left and Right pans, 0 shows all data.",
        [nameof(KeysSelect)] = "Press Enter to select a point, Escape to leave.",
        [nameof(KeysLeave)] = "Press Escape to leave.",
        [nameof(Position)] = "{0} of {1}",
    };

    // Static fields initialize in order: the defaults come before every language that uses them.
    private static readonly string[] KeyList = [.. Defaults.Keys];

    /// <summary>English.</summary>
    public static ChartTexts English { get; } = new();

    /// <summary>Swedish (sv).</summary>
    public static ChartTexts Swedish { get; } = new()
    {
        Chart = "Diagram",
        NoData = "Inga data",
        AllSeriesHidden = "Alla serier är dolda",
        AllSlicesHidden = "Alla delar är dolda",
        Other = "Övrigt",
        Size = "Storlek",
        Total = "Totalt",
        ResetZoom = "Återställ zoom",
        Download = "Ladda ner",
        DownloadFormat = "Ladda ner {0}",
        DownloadChart = "Ladda ner diagram",
        ChartRole = "diagram",
        Legend = "Teckenförklaring",
        XAxis = "X-axel",
        YAxis = "Y-axel",
        AxisRange = "{0}, från {1} till {2}",
        SeriesPoints = "{0}: {1} datapunkt|{0}: {1} datapunkter",
        Summary = "Diagram med {0} serie.|Diagram med {0} serier.",
        SummaryTitled = "Diagram \"{1}\" med {0} serie.|Diagram \"{1}\" med {0} serier.",
        SeriesValues = "{0}: {1} värde|{0}: {1} värden",
        LowestHighest = "lägst {0} vid {1}, högst {2} vid {3}",
        Trend = "{0} trend",
        Rising = "stigande",
        Falling = "fallande",
        Stable = "stabil",
        ValueRange = "{0} till {1}",
        BoxMax = "Max",
        BoxUpperQuartile = "Övre kvartil",
        BoxMedian = "Median",
        BoxLowerQuartile = "Nedre kvartil",
        BoxMin = "Min",
        BoxCount = "Antal",
        BoxOutliers = "Avvikande värden",
        CandleOpen = "Öppning",
        CandleHigh = "Högsta",
        CandleLow = "Lägsta",
        CandleClose = "Stängning",
        CandleChange = "Förändring",
        LastValue = "senast {0}",
        HistogramPeak = "{0}: {1} värde, flest från {2} till {3}|{0}: {1} värden, flest från {2} till {3}",
        RangeBarSummary = "{0}: {1} stapel i {4} rader, från {2} till {3}|{0}: {1} staplar i {4} rader, från {2} till {3}",
        DurationUnits = "d h min s",
        NoDataShort = "Inga data.",
        PieChart = "Cirkeldiagram",
        DonutChart = "Ringdiagram",
        RadialBarChart = "Radiellt stapeldiagram",
        GaugeChart = "Mätare",
        Target = "Mål",
        RadarChart = "Radardiagram",
        Treemap = "Trädkarta",
        ShareOfGroup = "Andel av gruppen",
        ShareOfTotal = "Andel av totalen",
        TreemapAll = "Alla",
        TreemapShow = "Visa {0}",
        TreemapPathLabel = "Sökväg",
        TreemapLeftOut = "{0} post med noll eller negativt värde utelämnad|{0} poster med noll eller negativa värden utelämnade",
        PolarItems = "{0} med {1} post: {2}|{0} med {1} poster: {2}",
        PolarNoData = "{0}, inga data.",
        More = "och {0} till",
        KeysPoints = "Använd vänster- och högerpil för att flytta mellan punkter.",
        KeysPointsAndSeries = "Använd vänster- och högerpil för att flytta mellan punkter, upp- och nedpil för att byta serie.",
        KeysSlices = "Använd piltangenterna för att flytta mellan delarna.",
        KeysZoom = "Plus och minus zoomar, Skift med vänster- och högerpil panorerar, 0 visar all data.",
        KeysSelect = "Tryck Enter för att välja en punkt, Escape för att lämna.",
        KeysLeave = "Tryck Escape för att lämna.",
        Position = "{0} av {1}",
    };

    /// <summary>Norwegian, Bokmål (no, nb).</summary>
    public static ChartTexts Norwegian { get; } = new()
    {
        Chart = "Diagram",
        NoData = "Ingen data",
        AllSeriesHidden = "Alle serier er skjult",
        AllSlicesHidden = "Alle deler er skjult",
        Other = "Annet",
        Size = "Størrelse",
        Total = "Totalt",
        ResetZoom = "Tilbakestill zoom",
        Download = "Last ned",
        DownloadFormat = "Last ned {0}",
        DownloadChart = "Last ned diagram",
        ChartRole = "diagram",
        Legend = "Tegnforklaring",
        XAxis = "X-akse",
        YAxis = "Y-akse",
        AxisRange = "{0}, fra {1} til {2}",
        SeriesPoints = "{0}: {1} datapunkt|{0}: {1} datapunkter",
        Summary = "Diagram med {0} serie.|Diagram med {0} serier.",
        SummaryTitled = "Diagram «{1}» med {0} serie.|Diagram «{1}» med {0} serier.",
        SeriesValues = "{0}: {1} verdi|{0}: {1} verdier",
        LowestHighest = "lavest {0} ved {1}, høyest {2} ved {3}",
        Trend = "{0} trend",
        Rising = "stigende",
        Falling = "synkende",
        Stable = "stabil",
        ValueRange = "{0} til {1}",
        BoxMax = "Maks",
        BoxUpperQuartile = "Øvre kvartil",
        BoxMedian = "Median",
        BoxLowerQuartile = "Nedre kvartil",
        BoxMin = "Min",
        BoxCount = "Antall",
        BoxOutliers = "Uteliggere",
        CandleOpen = "Åpning",
        CandleHigh = "Høyeste",
        CandleLow = "Laveste",
        CandleClose = "Slutt",
        CandleChange = "Endring",
        LastValue = "sist {0}",
        HistogramPeak = "{0}: {1} verdi, flest fra {2} til {3}|{0}: {1} verdier, flest fra {2} til {3}",
        RangeBarSummary = "{0}: {1} stolpe i {4} rader, fra {2} til {3}|{0}: {1} stolper i {4} rader, fra {2} til {3}",
        DurationUnits = "d t min s",
        NoDataShort = "Ingen data.",
        PieChart = "Sektordiagram",
        DonutChart = "Ringdiagram",
        RadialBarChart = "Radielt stolpediagram",
        GaugeChart = "Måler",
        Target = "Mål",
        RadarChart = "Radardiagram",
        Treemap = "Trekart",
        ShareOfGroup = "Andel av gruppen",
        ShareOfTotal = "Andel av totalen",
        TreemapAll = "Alle",
        TreemapShow = "Vis {0}",
        TreemapPathLabel = "Sti",
        TreemapLeftOut = "{0} post med null eller negativ verdi utelatt|{0} poster med null eller negative verdier utelatt",
        PolarItems = "{0} med {1} element: {2}|{0} med {1} elementer: {2}",
        PolarNoData = "{0}, ingen data.",
        More = "og {0} til",
        KeysPoints = "Bruk venstre- og høyrepil for å flytte mellom punkter.",
        KeysPointsAndSeries = "Bruk venstre- og høyrepil for å flytte mellom punkter, opp- og nedpil for å bytte serie.",
        KeysSlices = "Bruk piltastene for å flytte mellom delene.",
        KeysZoom = "Pluss og minus zoomer, Shift med venstre- og høyrepil panorerer, 0 viser alle data.",
        KeysSelect = "Trykk Enter for å velge et punkt, Escape for å gå ut.",
        KeysLeave = "Trykk Escape for å gå ut.",
        Position = "{0} av {1}",
    };

    /// <summary>Danish (da).</summary>
    public static ChartTexts Danish { get; } = new()
    {
        Chart = "Diagram",
        NoData = "Ingen data",
        AllSeriesHidden = "Alle serier er skjult",
        AllSlicesHidden = "Alle dele er skjult",
        Other = "Andet",
        Size = "Størrelse",
        Total = "I alt",
        ResetZoom = "Nulstil zoom",
        Download = "Download",
        DownloadFormat = "Download {0}",
        DownloadChart = "Download diagram",
        ChartRole = "diagram",
        Legend = "Signaturforklaring",
        XAxis = "X-akse",
        YAxis = "Y-akse",
        AxisRange = "{0}, fra {1} til {2}",
        SeriesPoints = "{0}: {1} datapunkt|{0}: {1} datapunkter",
        Summary = "Diagram med {0} serie.|Diagram med {0} serier.",
        SummaryTitled = "Diagram \"{1}\" med {0} serie.|Diagram \"{1}\" med {0} serier.",
        SeriesValues = "{0}: {1} værdi|{0}: {1} værdier",
        LowestHighest = "lavest {0} ved {1}, højest {2} ved {3}",
        Trend = "{0} tendens",
        Rising = "stigende",
        Falling = "faldende",
        Stable = "stabil",
        ValueRange = "{0} til {1}",
        BoxMax = "Maks",
        BoxUpperQuartile = "Øvre kvartil",
        BoxMedian = "Median",
        BoxLowerQuartile = "Nedre kvartil",
        BoxMin = "Min",
        BoxCount = "Antal",
        BoxOutliers = "Afvigere",
        CandleOpen = "Åbning",
        CandleHigh = "Højeste",
        CandleLow = "Laveste",
        CandleClose = "Lukning",
        CandleChange = "Ændring",
        LastValue = "senest {0}",
        HistogramPeak = "{0}: {1} værdi, flest fra {2} til {3}|{0}: {1} værdier, flest fra {2} til {3}",
        RangeBarSummary = "{0}: {1} søjle i {4} rækker, fra {2} til {3}|{0}: {1} søjler i {4} rækker, fra {2} til {3}",
        DurationUnits = "d t min s",
        NoDataShort = "Ingen data.",
        PieChart = "Cirkeldiagram",
        DonutChart = "Ringdiagram",
        RadialBarChart = "Radialt søjlediagram",
        GaugeChart = "Måler",
        Target = "Mål",
        RadarChart = "Radardiagram",
        Treemap = "Trækort",
        ShareOfGroup = "Andel af gruppen",
        ShareOfTotal = "Andel af totalen",
        TreemapAll = "Alle",
        TreemapShow = "Vis {0}",
        TreemapPathLabel = "Sti",
        TreemapLeftOut = "{0} post med nul eller negativ værdi udeladt|{0} poster med nul eller negative værdier udeladt",
        PolarItems = "{0} med {1} element: {2}|{0} med {1} elementer: {2}",
        PolarNoData = "{0}, ingen data.",
        More = "og {0} mere",
        KeysPoints = "Brug venstre- og højrepil til at flytte mellem punkter.",
        KeysPointsAndSeries = "Brug venstre- og højrepil til at flytte mellem punkter, op- og nedpil til at skifte serie.",
        KeysSlices = "Brug piletasterne til at flytte mellem delene.",
        KeysZoom = "Plus og minus zoomer, Shift med venstre- og højrepil panorerer, 0 viser alle data.",
        KeysSelect = "Tryk Enter for at vælge et punkt, Escape for at forlade diagrammet.",
        KeysLeave = "Tryk Escape for at forlade diagrammet.",
        Position = "{0} af {1}",
    };

    /// <summary>Finnish (fi).</summary>
    public static ChartTexts Finnish { get; } = new()
    {
        Chart = "Kaavio",
        NoData = "Ei tietoja",
        AllSeriesHidden = "Kaikki sarjat on piilotettu",
        AllSlicesHidden = "Kaikki osat on piilotettu",
        Other = "Muut",
        Size = "Koko",
        Total = "Yhteensä",
        ResetZoom = "Palauta zoomaus",
        Download = "Lataa",
        DownloadFormat = "Lataa {0}",
        DownloadChart = "Lataa kaavio",
        ChartRole = "kaavio",
        Legend = "Selite",
        XAxis = "X-akseli",
        YAxis = "Y-akseli",
        AxisRange = "{0}, {1}–{2}",
        SeriesPoints = "{0}: {1} arvopiste|{0}: {1} arvopistettä",
        Summary = "Kaavio, {0} sarja.|Kaavio, {0} sarjaa.",
        SummaryTitled = "Kaavio ”{1}”, {0} sarja.|Kaavio ”{1}”, {0} sarjaa.",
        SeriesValues = "{0}: {1} arvo|{0}: {1} arvoa",
        LowestHighest = "pienin {0} kohdassa {1}, suurin {2} kohdassa {3}",
        Trend = "trendi {0}",
        Rising = "nouseva",
        Falling = "laskeva",
        Stable = "vakaa",
        ValueRange = "{0}–{1}",
        BoxMax = "Maksimi",
        BoxUpperQuartile = "Yläkvartiili",
        BoxMedian = "Mediaani",
        BoxLowerQuartile = "Alakvartiili",
        BoxMin = "Minimi",
        BoxCount = "Määrä",
        BoxOutliers = "Poikkeavat arvot",
        CandleOpen = "Avaus",
        CandleHigh = "Korkein",
        CandleLow = "Matalin",
        CandleClose = "Päätös",
        CandleChange = "Muutos",
        LastValue = "viimeisin {0}",
        HistogramPeak = "{0}: {1} arvo, eniten välillä {2}–{3}|{0}: {1} arvoa, eniten välillä {2}–{3}",
        RangeBarSummary = "{0}: {1} palkki {4} rivillä, {2}–{3}|{0}: {1} palkkia {4} rivillä, {2}–{3}",
        DurationUnits = "pv h min s",
        NoDataShort = "Ei tietoja.",
        PieChart = "Ympyräkaavio",
        DonutChart = "Rengaskaavio",
        RadialBarChart = "Säteittäinen pylväskaavio",
        GaugeChart = "Mittari",
        Target = "Tavoite",
        RadarChart = "Tutkakaavio",
        Treemap = "Puukartta",
        ShareOfGroup = "Osuus ryhmästä",
        ShareOfTotal = "Osuus kokonaisuudesta",
        TreemapAll = "Kaikki",
        TreemapShow = "Näytä {0}",
        TreemapPathLabel = "Polku",
        TreemapLeftOut = "{0} kohde, jonka arvo on nolla tai negatiivinen, jätetty pois|{0} kohdetta, joiden arvo on nolla tai negatiivinen, jätetty pois",
        PolarItems = "{0}, {1} kohde: {2}|{0}, {1} kohdetta: {2}",
        PolarNoData = "{0}, ei tietoja.",
        More = "ja {0} muuta",
        KeysPoints = "Siirry pisteestä toiseen vasemmalla ja oikealla nuolinäppäimellä.",
        KeysPointsAndSeries = "Siirry pisteestä toiseen vasemmalla ja oikealla nuolinäppäimellä, vaihda sarjaa ylä- ja alanuolella.",
        KeysSlices = "Siirry osasta toiseen nuolinäppäimillä.",
        KeysZoom = "Plus ja miinus zoomaavat, vaihtonäppäin ja vasen tai oikea nuoli siirtävät näkymää, 0 näyttää kaikki tiedot.",
        KeysSelect = "Valitse piste Enterillä, poistu Escillä.",
        KeysLeave = "Poistu Escillä.",
        Position = "{0}/{1}",
    };

    /// <summary>German (de).</summary>
    public static ChartTexts German { get; } = new()
    {
        Chart = "Diagramm",
        NoData = "Keine Daten vorhanden",
        AllSeriesHidden = "Alle Reihen sind ausgeblendet",
        AllSlicesHidden = "Alle Segmente sind ausgeblendet",
        Other = "Sonstige",
        Size = "Größe",
        Total = "Gesamt",
        ResetZoom = "Zoom zurücksetzen",
        Download = "Herunterladen",
        DownloadFormat = "{0} herunterladen",
        DownloadChart = "Diagramm herunterladen",
        ChartRole = "Diagramm",
        Legend = "Legende",
        XAxis = "X-Achse",
        YAxis = "Y-Achse",
        AxisRange = "{0}, von {1} bis {2}",
        SeriesPoints = "{0}: {1} Datenpunkt|{0}: {1} Datenpunkte",
        Summary = "Diagramm mit {0} Reihe.|Diagramm mit {0} Reihen.",
        SummaryTitled = "Diagramm „{1}“ mit {0} Reihe.|Diagramm „{1}“ mit {0} Reihen.",
        SeriesValues = "{0}: {1} Wert|{0}: {1} Werte",
        LowestHighest = "Minimum {0} bei {1}, Maximum {2} bei {3}",
        Trend = "Trend {0}",
        Rising = "steigend",
        Falling = "fallend",
        Stable = "stabil",
        ValueRange = "{0} bis {1}",
        BoxMax = "Max",
        BoxUpperQuartile = "Oberes Quartil",
        BoxMedian = "Median",
        BoxLowerQuartile = "Unteres Quartil",
        BoxMin = "Min",
        BoxCount = "Anzahl",
        BoxOutliers = "Ausreißer",
        CandleOpen = "Eröffnung",
        CandleHigh = "Hoch",
        CandleLow = "Tief",
        CandleClose = "Schluss",
        CandleChange = "Änderung",
        LastValue = "zuletzt {0}",
        HistogramPeak = "{0}: {1} Wert, die meisten von {2} bis {3}|{0}: {1} Werte, die meisten von {2} bis {3}",
        RangeBarSummary = "{0}: {1} Balken in {4} Zeilen, von {2} bis {3}|{0}: {1} Balken in {4} Zeilen, von {2} bis {3}",
        DurationUnits = "d h min s",
        NoDataShort = "Keine Daten.",
        PieChart = "Kreisdiagramm",
        DonutChart = "Ringdiagramm",
        RadialBarChart = "Radiales Balkendiagramm",
        GaugeChart = "Messanzeige",
        Target = "Ziel",
        RadarChart = "Netzdiagramm",
        Treemap = "Treemap",
        ShareOfGroup = "Anteil an der Gruppe",
        ShareOfTotal = "Anteil am Gesamtwert",
        TreemapAll = "Alle",
        TreemapShow = "{0} anzeigen",
        TreemapPathLabel = "Pfad",
        TreemapLeftOut = "{0} Eintrag mit Wert null oder negativ ausgelassen|{0} Einträge mit Wert null oder negativ ausgelassen",
        PolarItems = "{0} mit {1} Eintrag: {2}|{0} mit {1} Einträgen: {2}",
        PolarNoData = "{0}, keine Daten.",
        More = "und {0} weitere",
        KeysPoints = "Mit Links und Rechts zwischen Punkten wechseln.",
        KeysPointsAndSeries = "Mit Links und Rechts zwischen Punkten wechseln, mit Auf und Ab die Reihe wechseln.",
        KeysSlices = "Mit den Pfeiltasten zwischen Segmenten wechseln.",
        KeysZoom = "Plus und Minus zoomen, Umschalt mit Links und Rechts verschiebt, 0 zeigt alle Daten.",
        KeysSelect = "Enter wählt einen Punkt, Escape verlässt das Diagramm.",
        KeysLeave = "Escape verlässt das Diagramm.",
        Position = "{0} von {1}",
    };

    /// <summary>French (fr).</summary>
    public static ChartTexts French { get; } = new()
    {
        Chart = "Graphique",
        NoData = "Aucune donnée disponible",
        AllSeriesHidden = "Toutes les séries sont masquées",
        AllSlicesHidden = "Toutes les parts sont masquées",
        Other = "Autres",
        Size = "Taille",
        Total = "Total",
        ResetZoom = "Réinitialiser le zoom",
        Download = "Télécharger",
        DownloadFormat = "Télécharger en {0}",
        DownloadChart = "Télécharger le graphique",
        ChartRole = "graphique",
        Legend = "Légende",
        XAxis = "Axe X",
        YAxis = "Axe Y",
        AxisRange = "{0}, de {1} à {2}",
        SeriesPoints = "{0} : {1} point de données|{0} : {1} points de données",
        Summary = "Graphique avec {0} série.|Graphique avec {0} séries.",
        SummaryTitled = "Graphique « {1} » avec {0} série.|Graphique « {1} » avec {0} séries.",
        SeriesValues = "{0} : {1} valeur|{0} : {1} valeurs",
        LowestHighest = "minimum {0} à {1}, maximum {2} à {3}",
        Trend = "tendance {0}",
        Rising = "à la hausse",
        Falling = "à la baisse",
        Stable = "stable",
        ValueRange = "{0} à {1}",
        BoxMax = "Max",
        BoxUpperQuartile = "Quartile supérieur",
        BoxMedian = "Médiane",
        BoxLowerQuartile = "Quartile inférieur",
        BoxMin = "Min",
        BoxCount = "Nombre",
        BoxOutliers = "Valeurs aberrantes",
        CandleOpen = "Ouverture",
        CandleHigh = "Plus haut",
        CandleLow = "Plus bas",
        CandleClose = "Clôture",
        CandleChange = "Variation",
        LastValue = "dernière valeur {0}",
        HistogramPeak = "{0} : {1} valeur, la plupart de {2} à {3}|{0} : {1} valeurs, la plupart de {2} à {3}",
        RangeBarSummary = "{0} : {1} barre sur {4} lignes, de {2} à {3}|{0} : {1} barres sur {4} lignes, de {2} à {3}",
        DurationUnits = "j h min s",
        NoDataShort = "Aucune donnée.",
        PieChart = "Graphique en secteurs",
        DonutChart = "Graphique en anneau",
        RadialBarChart = "Graphique à barres radiales",
        GaugeChart = "Jauge",
        Target = "Cible",
        RadarChart = "Graphique en radar",
        Treemap = "Carte proportionnelle",
        ShareOfGroup = "Part du groupe",
        ShareOfTotal = "Part du total",
        TreemapAll = "Tout",
        TreemapShow = "Afficher {0}",
        TreemapPathLabel = "Chemin",
        TreemapLeftOut = "{0} élément de valeur nulle ou négative omis|{0} éléments de valeur nulle ou négative omis",
        PolarItems = "{0} avec {1} élément : {2}|{0} avec {1} éléments : {2}",
        PolarNoData = "{0}, aucune donnée.",
        More = "et {0} de plus",
        KeysPoints = "Utilisez Gauche et Droite pour passer d'un point à l'autre.",
        KeysPointsAndSeries = "Utilisez Gauche et Droite pour passer d'un point à l'autre, Haut et Bas pour changer de série.",
        KeysSlices = "Utilisez les flèches pour passer d'une part à l'autre.",
        KeysZoom = "Plus et moins zooment, Maj avec Gauche et Droite déplace la vue, 0 affiche toutes les données.",
        KeysSelect = "Appuyez sur Entrée pour sélectionner un point, sur Échap pour quitter.",
        KeysLeave = "Appuyez sur Échap pour quitter.",
        Position = "{0} sur {1}",
    };

    /// <summary>Spanish (es).</summary>
    public static ChartTexts Spanish { get; } = new()
    {
        Chart = "Gráfico",
        NoData = "No hay datos disponibles",
        AllSeriesHidden = "Todas las series están ocultas",
        AllSlicesHidden = "Todos los sectores están ocultos",
        Other = "Otros",
        Size = "Tamaño",
        Total = "Total",
        ResetZoom = "Restablecer zoom",
        Download = "Descargar",
        DownloadFormat = "Descargar {0}",
        DownloadChart = "Descargar gráfico",
        ChartRole = "gráfico",
        Legend = "Leyenda",
        XAxis = "Eje X",
        YAxis = "Eje Y",
        AxisRange = "{0}, de {1} a {2}",
        SeriesPoints = "{0}: {1} punto de datos|{0}: {1} puntos de datos",
        Summary = "Gráfico con {0} serie.|Gráfico con {0} series.",
        SummaryTitled = "Gráfico «{1}» con {0} serie.|Gráfico «{1}» con {0} series.",
        SeriesValues = "{0}: {1} valor|{0}: {1} valores",
        LowestHighest = "mínimo {0} en {1}, máximo {2} en {3}",
        Trend = "tendencia {0}",
        Rising = "al alza",
        Falling = "a la baja",
        Stable = "estable",
        ValueRange = "{0} a {1}",
        BoxMax = "Máx.",
        BoxUpperQuartile = "Cuartil superior",
        BoxMedian = "Mediana",
        BoxLowerQuartile = "Cuartil inferior",
        BoxMin = "Mín.",
        BoxCount = "Recuento",
        BoxOutliers = "Valores atípicos",
        CandleOpen = "Apertura",
        CandleHigh = "Máximo",
        CandleLow = "Mínimo",
        CandleClose = "Cierre",
        CandleChange = "Variación",
        LastValue = "último {0}",
        HistogramPeak = "{0}: {1} valor, la mayoría de {2} a {3}|{0}: {1} valores, la mayoría de {2} a {3}",
        RangeBarSummary = "{0}: {1} barra en {4} filas, de {2} a {3}|{0}: {1} barras en {4} filas, de {2} a {3}",
        DurationUnits = "d h min s",
        NoDataShort = "Sin datos.",
        PieChart = "Gráfico circular",
        DonutChart = "Gráfico de anillo",
        RadialBarChart = "Gráfico de barras radiales",
        GaugeChart = "Indicador",
        Target = "Objetivo",
        RadarChart = "Gráfico radial",
        Treemap = "Mapa de árbol",
        ShareOfGroup = "Parte del grupo",
        ShareOfTotal = "Parte del total",
        TreemapAll = "Todo",
        TreemapShow = "Mostrar {0}",
        TreemapPathLabel = "Ruta",
        TreemapLeftOut = "{0} elemento con valor cero o negativo omitido|{0} elementos con valor cero o negativo omitidos",
        PolarItems = "{0} con {1} elemento: {2}|{0} con {1} elementos: {2}",
        PolarNoData = "{0}, sin datos.",
        More = "y {0} más",
        KeysPoints = "Use Izquierda y Derecha para moverse entre puntos.",
        KeysPointsAndSeries = "Use Izquierda y Derecha para moverse entre puntos, Arriba y Abajo para cambiar de serie.",
        KeysSlices = "Use las flechas para moverse entre sectores.",
        KeysZoom = "Más y menos hacen zoom, Mayús con Izquierda y Derecha desplaza, 0 muestra todos los datos.",
        KeysSelect = "Pulse Intro para seleccionar un punto, Escape para salir.",
        KeysLeave = "Pulse Escape para salir.",
        Position = "{0} de {1}",
    };

    /// <summary>The built-in languages by culture name (en, sv, no, nb, da, fi, de, fr, es).</summary>
    public static IReadOnlyDictionary<string, ChartTexts> BuiltIn { get; } = new Dictionary<string, ChartTexts>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = English,
        ["sv"] = Swedish,
        ["no"] = Norwegian,
        ["nb"] = Norwegian,
        ["da"] = Danish,
        ["fi"] = Finnish,
        ["de"] = German,
        ["fr"] = French,
        ["es"] = Spanish,
    };
}
