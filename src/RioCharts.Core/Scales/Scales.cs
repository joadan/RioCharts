namespace RioCharts.Core.Scales;

/// <summary>Maps a continuous domain to pixels.</summary>
public interface IScale
{
    /// <summary>Lower end of the domain, in data units.</summary>
    double DomainMin { get; }

    /// <summary>Upper end of the domain, in data units.</summary>
    double DomainMax { get; }

    /// <summary>Pixel position of <see cref="DomainMin"/>.</summary>
    double RangeStart { get; }

    /// <summary>Pixel position of <see cref="DomainMax"/>.</summary>
    double RangeEnd { get; }

    /// <summary>Converts a data value to a pixel position.</summary>
    double Map(double value);

    /// <summary>Converts a pixel position back to a data value.</summary>
    double Invert(double pixel);
}

/// <summary>
/// Maps a continuous domain to pixels: linear (the constructor) or logarithmic (<see cref="Log"/>,
/// <see cref="IsLog"/>). Numeric and time axes use it; a time axis' domain is in milliseconds
/// (see <see cref="Data.TimeValue"/>), only its ticks and formatting differ. Categories use <see cref="BandScale"/>.
/// </summary>
public sealed class ContinuousScale : IScale
{
    private readonly double _logMin;
    private readonly double _logSpan;

    /// <summary>
    /// A logarithmic scale: equal ratios get equal distances (1 to 10 is as long as 10 to 100). The domain must be
    /// positive; an invalid one is widened to a decade. Zero and below have no logarithm: they map to
    /// <paramref name="rangeStart"/>, the low end, so bars and areas that start at zero start at the axis' bottom.
    /// </summary>
    public static ContinuousScale Log(double domainMin, double domainMax, double rangeStart, double rangeEnd)
    {
        if (!(domainMin > 0) || !double.IsFinite(domainMin))
        {
            domainMin = domainMax > 0 && double.IsFinite(domainMax) ? domainMax / 10 : 1;
        }

        if (!(domainMax > domainMin) || !double.IsFinite(domainMax))
        {
            domainMax = domainMin * 10;
        }

        return new ContinuousScale(domainMin, domainMax, rangeStart, rangeEnd, log: true);
    }

    private ContinuousScale(double domainMin, double domainMax, double rangeStart, double rangeEnd, bool log)
        : this(domainMin, domainMax, rangeStart, rangeEnd)
    {
        IsLog = log;
        _logMin = Math.Log10(DomainMin);
        _logSpan = Math.Log10(DomainMax) - _logMin;
    }

    /// <summary>
    /// Creates a scale from a domain in data units to a pixel range. The range may be reversed
    /// (e.g. y axes, bottom to top). An empty or invalid domain is widened to ±0.5 around
    /// <paramref name="domainMin"/> (or 0) so mapping stays defined.
    /// </summary>
    public ContinuousScale(double domainMin, double domainMax, double rangeStart, double rangeEnd)
    {
        if (!(domainMax > domainMin))
        {
            // Degenerate domain: widen so mapping stays defined.
            var center = double.IsFinite(domainMin) ? domainMin : 0;
            domainMin = center - 0.5;
            domainMax = center + 0.5;
        }

        DomainMin = domainMin;
        DomainMax = domainMax;
        RangeStart = rangeStart;
        RangeEnd = rangeEnd;
    }

    /// <inheritdoc/>
    public double DomainMin { get; }

    /// <inheritdoc/>
    public double DomainMax { get; }

    /// <inheritdoc/>
    public double RangeStart { get; }

    /// <inheritdoc/>
    public double RangeEnd { get; }

    /// <summary>True for a logarithmic scale (made with <see cref="Log"/>).</summary>
    public bool IsLog { get; }

    /// <inheritdoc/>
    public double Map(double value) => !IsLog
        ? RangeStart + (value - DomainMin) / (DomainMax - DomainMin) * (RangeEnd - RangeStart)
        : value > 0
            ? RangeStart + (Math.Log10(value) - _logMin) / _logSpan * (RangeEnd - RangeStart)
            : value <= 0 ? RangeStart : double.NaN; // no logarithm: the low end; NaN stays NaN

    /// <inheritdoc/>
    public double Invert(double pixel) => IsLog
        ? Math.Pow(10, _logMin + (pixel - RangeStart) / (RangeEnd - RangeStart) * _logSpan)
        : DomainMin + (pixel - RangeStart) / (RangeEnd - RangeStart) * (DomainMax - DomainMin);
}

/// <summary>Evenly spaced bands for categories (bar charts).</summary>
public sealed class BandScale
{
    /// <summary>Creates bands for the categories across a pixel range.</summary>
    /// <param name="categories">The categories, one band each, in order.</param>
    /// <param name="rangeStart">Pixel position where the first band's outer padding starts.</param>
    /// <param name="rangeEnd">Pixel position where the last band's outer padding ends.</param>
    /// <param name="paddingInner">Gap between bands as a fraction of <see cref="Step"/>. Default 0.2.</param>
    /// <param name="paddingOuter">Space before the first and after the last band as a fraction of <see cref="Step"/>. Default 0.1.</param>
    public BandScale(IReadOnlyList<string> categories, double rangeStart, double rangeEnd, double paddingInner = 0.2, double paddingOuter = 0.1)
    {
        Categories = categories;
        RangeStart = rangeStart;
        RangeEnd = rangeEnd;
        var n = Math.Max(1, categories.Count);

        // step * (n - paddingInner + 2 * paddingOuter) = range
        Step = (rangeEnd - rangeStart) / Math.Max(1e-9, n - paddingInner + 2 * paddingOuter);
        Bandwidth = Step * (1 - paddingInner);
        Offset = rangeStart + Step * paddingOuter;
    }

    /// <summary>The categories, in band order.</summary>
    public IReadOnlyList<string> Categories { get; }

    /// <summary>Start of the pixel range.</summary>
    public double RangeStart { get; }

    /// <summary>End of the pixel range.</summary>
    public double RangeEnd { get; }

    /// <summary>Distance between the starts of neighbouring bands.</summary>
    public double Step { get; }

    /// <summary>Width of one band.</summary>
    public double Bandwidth { get; }

    private double Offset { get; }

    /// <summary>Pixel position where the band of the category at <paramref name="index"/> starts.</summary>
    public double Start(int index) => Offset + index * Step;

    /// <summary>Pixel position of the center of the band at <paramref name="index"/>.</summary>
    public double Center(int index) => Start(index) + Bandwidth / 2;
}
