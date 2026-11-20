namespace RioCharts.Core.Data;

/// <summary>
/// Largest Triangle Three Buckets downsampling on columnar data. Gaps (NaN y) are kept:
/// each run of real values is downsampled on its own and the gaps stay between them.
/// </summary>
public static class Lttb
{
    /// <summary>
    /// Returns indexes (ascending) into x/y within [start, end) that keep the shape of the data,
    /// about <paramref name="threshold"/> of them. Gap points are included so lines still break.
    /// </summary>
    public static int[] Downsample(ReadOnlySpan<double> x, ReadOnlySpan<double> y, int start, int end, int threshold)
    {
        var count = end - start;
        if (count <= 0)
        {
            return [];
        }

        if (threshold < 3 || count <= threshold)
        {
            return Enumerable.Range(start, count).ToArray();
        }

        // Split into runs of finite values.
        var runs = new List<(int Start, int End)>();
        var finite = 0;
        var runStart = -1;
        for (var i = start; i < end; i++)
        {
            if (double.IsFinite(y[i]))
            {
                finite++;
                if (runStart < 0)
                {
                    runStart = i;
                }
            }
            else if (runStart >= 0)
            {
                runs.Add((runStart, i));
                runStart = -1;
            }
        }

        if (runStart >= 0)
        {
            runs.Add((runStart, end));
        }

        var result = new List<int>(threshold + runs.Count * 2);
        var previousEnd = start;
        foreach (var (s, e) in runs)
        {
            if (s > previousEnd)
            {
                result.Add(s - 1); // one gap marker is enough to break the line
            }

            // Share of the budget proportional to the run length.
            var budget = Math.Max(2, (int)Math.Round((double)(e - s) / finite * threshold));
            DownsampleRun(x, y, s, e, budget, result);
            previousEnd = e;
        }

        return result.ToArray();
    }

    private static void DownsampleRun(ReadOnlySpan<double> x, ReadOnlySpan<double> y, int start, int end, int threshold, List<int> output)
    {
        var count = end - start;
        if (threshold >= count || threshold < 3)
        {
            for (var i = start; i < end; i++)
            {
                output.Add(i);
            }

            return;
        }

        output.Add(start);
        var bucketSize = (double)(count - 2) / (threshold - 2);
        var a = start;
        for (var b = 0; b < threshold - 2; b++)
        {
            var nextStart = start + (int)Math.Floor((b + 1) * bucketSize) + 1;
            var nextEnd = Math.Min(start + (int)Math.Floor((b + 2) * bucketSize) + 1, end);
            double avgX = 0, avgY = 0;
            var n = nextEnd - nextStart;
            for (var j = nextStart; j < nextEnd; j++)
            {
                avgX += x[j];
                avgY += y[j];
            }

            if (n > 0)
            {
                avgX /= n;
                avgY /= n;
            }
            else
            {
                avgX = x[end - 1];
                avgY = y[end - 1];
            }

            var from = start + (int)Math.Floor(b * bucketSize) + 1;
            var to = Math.Min(start + (int)Math.Floor((b + 1) * bucketSize) + 1, end - 1);
            var maxArea = -1.0;
            var chosen = from;
            for (var j = from; j < to; j++)
            {
                var area = Math.Abs((x[a] - avgX) * (y[j] - y[a]) - (x[a] - x[j]) * (avgY - y[a]));
                if (area > maxArea)
                {
                    maxArea = area;
                    chosen = j;
                }
            }

            output.Add(chosen);
            a = chosen;
        }

        output.Add(end - 1);
    }
}
