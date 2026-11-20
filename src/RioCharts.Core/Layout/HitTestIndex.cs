namespace RioCharts.Core.Layout;

/// <summary>A point found by hit-testing.</summary>
/// <param name="Index">Index into the series' <see cref="Data.SeriesData"/>.</param>
/// <param name="SourceIndex">Index into the original input (items list or columns).</param>
public readonly record struct HitResult(string SeriesId, int Index, int SourceIndex, float X, float Y, float Distance);

/// <summary>
/// Pixel positions of drawn points per series, sorted by x, for nearest-point lookup by
/// binary search. Works server-side too; the same data is what gets sent to JS.
/// </summary>
public sealed class HitTestIndex
{
    private readonly List<SeriesHits> _series = [];

    public IReadOnlyList<SeriesHits> Series => _series;

    public sealed record SeriesHits(string SeriesId, float[] X, float[] Y, int[] Index, int[] SourceIndex);

    internal void Add(SeriesHits hits)
    {
        // Binary search needs ascending x (category series may arrive in another order).
        for (var i = 1; i < hits.X.Length; i++)
        {
            if (hits.X[i] < hits.X[i - 1])
            {
                var order = Enumerable.Range(0, hits.X.Length).OrderBy(k => hits.X[k]).ToArray();
                hits = new SeriesHits(hits.SeriesId,
                    order.Select(k => hits.X[k]).ToArray(), order.Select(k => hits.Y[k]).ToArray(),
                    order.Select(k => hits.Index[k]).ToArray(), order.Select(k => hits.SourceIndex[k]).ToArray());
                break;
            }
        }

        _series.Add(hits);
    }

    /// <summary>Nearest point per series at a horizontal position (for shared tooltips).</summary>
    public List<HitResult> FindAtX(float px)
    {
        var results = new List<HitResult>();
        foreach (var s in _series)
        {
            var i = NearestX(s.X, px);
            if (i >= 0)
            {
                results.Add(new HitResult(s.SeriesId, s.Index[i], s.SourceIndex[i], s.X[i], s.Y[i], Math.Abs(s.X[i] - px)));
            }
        }

        return results;
    }

    /// <summary>Closest point to (px, py) across all series, or null when none is within maxDistance.</summary>
    public HitResult? FindNearest(float px, float py, float maxDistance = float.PositiveInfinity)
    {
        HitResult? best = null;
        foreach (var s in _series)
        {
            var i = NearestX(s.X, px);
            if (i < 0)
            {
                continue;
            }

            // Check a few neighbours: the nearest x is not always the nearest point.
            for (var j = Math.Max(0, i - 2); j <= Math.Min(s.X.Length - 1, i + 2); j++)
            {
                var d = MathF.Sqrt((s.X[j] - px) * (s.X[j] - px) + (s.Y[j] - py) * (s.Y[j] - py));
                if (d <= maxDistance && (best is null || d < best.Value.Distance))
                {
                    best = new HitResult(s.SeriesId, s.Index[j], s.SourceIndex[j], s.X[j], s.Y[j], d);
                }
            }
        }

        return best;
    }

    private static int NearestX(float[] xs, float px)
    {
        if (xs.Length == 0)
        {
            return -1;
        }

        var i = Array.BinarySearch(xs, px);
        if (i >= 0)
        {
            return i;
        }

        i = ~i;
        if (i == 0)
        {
            return 0;
        }

        if (i >= xs.Length)
        {
            return xs.Length - 1;
        }

        return px - xs[i - 1] <= xs[i] - px ? i - 1 : i;
    }
}
