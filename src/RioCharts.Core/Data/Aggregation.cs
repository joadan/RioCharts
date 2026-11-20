namespace RioCharts.Core.Data;

/// <summary>Built-in aggregates over a group of values (used by specs and series components).</summary>
public static class Aggregation
{
    /// <summary>
    /// Applies an aggregate to a group's values. Null and NaN values are skipped; Count counts every
    /// entry, the others give null (a gap) when no value is left.
    /// </summary>
    public static double? Apply(Aggregate aggregate, IReadOnlyCollection<double?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (aggregate == Aggregate.Count)
        {
            return values.Count;
        }

        var finite = values.Where(v => v is { } d && double.IsFinite(d)).Select(v => v!.Value).ToList();
        if (finite.Count == 0)
        {
            return null;
        }

        return aggregate switch
        {
            Aggregate.Sum => finite.Sum(),
            Aggregate.Average => finite.Average(),
            Aggregate.Min => finite.Min(),
            Aggregate.Max => finite.Max(),
            _ => finite[^1],
        };
    }

    /// <summary>
    /// The aggregate as a group function over items, reading each item's y with
    /// <paramref name="yValue"/> (not needed for Count).
    /// </summary>
    public static Func<IReadOnlyList<TItem>, double?> For<TItem>(Aggregate aggregate, Func<TItem, double?>? yValue) =>
        aggregate == Aggregate.Count
            ? group => group.Count
            : group => yValue is null ? null : Apply(aggregate, group.Select(yValue).ToList());
}
