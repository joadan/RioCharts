namespace RioCharts.Core.Scales;

/// <summary>Maps a continuous domain to pixels.</summary>
public interface IScale
{
    double DomainMin { get; }
    double DomainMax { get; }
    double RangeStart { get; }
    double RangeEnd { get; }
    double Map(double value);
    double Invert(double pixel);
}

/// <summary>
/// Linear mapping. Also used for time axes, whose domain is in milliseconds
/// (see <see cref="Data.TimeValue"/>); only tick generation and formatting differ.
/// </summary>
public sealed class LinearScale : IScale
{
    public LinearScale(double domainMin, double domainMax, double rangeStart, double rangeEnd)
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

    public double DomainMin { get; }
    public double DomainMax { get; }
    public double RangeStart { get; }
    public double RangeEnd { get; }

    public double Map(double value) => RangeStart + (value - DomainMin) / (DomainMax - DomainMin) * (RangeEnd - RangeStart);

    public double Invert(double pixel) => DomainMin + (pixel - RangeStart) / (RangeEnd - RangeStart) * (DomainMax - DomainMin);
}

/// <summary>Evenly spaced bands for categories (bar charts).</summary>
public sealed class BandScale
{
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

    public IReadOnlyList<string> Categories { get; }
    public double RangeStart { get; }
    public double RangeEnd { get; }

    /// <summary>Distance between the starts of neighbouring bands.</summary>
    public double Step { get; }

    /// <summary>Width of one band.</summary>
    public double Bandwidth { get; }

    private double Offset { get; }

    public double Start(int index) => Offset + index * Step;

    public double Center(int index) => Start(index) + Bandwidth / 2;
}
