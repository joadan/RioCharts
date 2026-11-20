namespace RioCharts.Layout;

/// <summary>Largest Triangle Three Buckets downsampling.</summary>
public static class Lttb
{
    /// <summary>
    /// Picks up to <paramref name="threshold"/> indexes from <paramref name="indices"/> (into
    /// <paramref name="points"/>) that keep the visual shape. Points must be ordered by X.
    /// The first and last points are always kept.
    /// </summary>
    public static IReadOnlyList<int> Downsample(IReadOnlyList<XYPoint> points, IReadOnlyList<int> indices, int threshold)
    {
        var count = indices.Count;
        if (threshold >= count || threshold < 3)
        {
            return indices;
        }

        var sampled = new List<int>(threshold) { indices[0] };
        var bucketSize = (double)(count - 2) / (threshold - 2);
        var a = 0; // index into indices of the previously selected point

        for (var i = 0; i < threshold - 2; i++)
        {
            // Average of the next bucket.
            var nextStart = (int)Math.Floor((i + 1) * bucketSize) + 1;
            var nextEnd = Math.Min((int)Math.Floor((i + 2) * bucketSize) + 1, count);
            double avgX = 0, avgY = 0;
            var nextLength = nextEnd - nextStart;
            for (var j = nextStart; j < nextEnd; j++)
            {
                var p = points[indices[j]];
                avgX += p.X;
                avgY += p.Y;
            }

            if (nextLength > 0)
            {
                avgX /= nextLength;
                avgY /= nextLength;
            }
            else
            {
                var last = points[indices[count - 1]];
                avgX = last.X;
                avgY = last.Y;
            }

            // Pick the point in this bucket making the largest triangle with a and the average.
            var start = (int)Math.Floor(i * bucketSize) + 1;
            var end = Math.Min((int)Math.Floor((i + 1) * bucketSize) + 1, count - 1);
            var pa = points[indices[a]];
            var maxArea = -1.0;
            var chosen = start;
            for (var j = start; j < end; j++)
            {
                var p = points[indices[j]];
                var area = Math.Abs((pa.X - avgX) * (p.Y - pa.Y) - (pa.X - p.X) * (avgY - pa.Y));
                if (area > maxArea)
                {
                    maxArea = area;
                    chosen = j;
                }
            }

            sampled.Add(indices[chosen]);
            a = chosen;
        }

        sampled.Add(indices[count - 1]);
        return sampled;
    }
}
