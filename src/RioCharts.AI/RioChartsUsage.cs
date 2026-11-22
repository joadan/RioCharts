using System.Text;

namespace RioCharts.AI;

/// <summary>
/// The guide for coding agents that write RioCharts code (get_usage): setup, rules, compiled
/// examples and every component parameter. The same text as llms-full.txt on the docs site,
/// embedded in this assembly, so it matches this version of the packages.
/// </summary>
public static class RioChartsUsage
{
    private static readonly Lazy<string> Text = new(Load);

    /// <summary>The whole guide (Markdown, about 80 KB).</summary>
    public static string Full => Text.Value;

    /// <summary>
    /// The guide without the reference (setup, rules, which series to use, examples, common
    /// mistakes), plus the names of the reference sections to ask for with <see cref="Get"/>.
    /// </summary>
    public static string Overview
    {
        get
        {
            var full = Full;
            var reference = full.IndexOf("\n## Reference", StringComparison.Ordinal);
            if (reference < 0)
            {
                return full;
            }

            var sb = new StringBuilder(full[..reference].TrimEnd()).Append("\n\n## Reference\n\n");
            sb.Append("Ask for a topic to get one section: a component (e.g. \"RioChart\", \"LineSeries\", \"Parameters of every series\") ");
            sb.Append("or a type (e.g. \"AxisSpec\", \"ColorBand\"). Topics: ");
            sb.Append(string.Join(", ", Headings(full[reference..], "#### ").Select(h => h.Title)));
            return sb.Append(".\n").ToString();
        }
    }

    /// <summary>
    /// One section of the guide, e.g. "LineSeries", "&lt;BarSeries&gt;", "AxisSpec", "Setup" or
    /// "Common mistakes" (case-insensitive; a component or type name, or a heading); null or empty
    /// gives <see cref="Overview"/>. An unknown topic gives the overview with a note.
    /// </summary>
    public static string Get(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return Overview;
        }

        var full = Full;
        var wanted = Normalize(topic);
        foreach (var level in new[] { "#### ", "### ", "## " })
        {
            foreach (var (title, start) in Headings(full, level))
            {
                if (Normalize(title) == wanted)
                {
                    return Section(full, start, level);
                }
            }
        }

        foreach (var level in new[] { "#### ", "### ", "## " })
        {
            if (Headings(full, level).FirstOrDefault(h => Normalize(h.Title).Contains(wanted, StringComparison.Ordinal)) is { Title: not null } match)
            {
                return Section(full, match.Start, level);
            }
        }

        return $"No section \"{topic}\" in the guide. Here is the overview.\n\n" + Overview;
    }

    private static string Load()
    {
        using var stream = typeof(RioChartsUsage).Assembly.GetManifestResourceStream("RioCharts.AI.llms-full.txt")
            ?? throw new InvalidOperationException("The RioCharts guide is not embedded in RioCharts.AI.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    /// <summary>The headings of one level ("#### ") with where they start, outside code blocks.</summary>
    private static List<(string Title, int Start)> Headings(string text, string level)
    {
        var found = new List<(string, int)>();
        var inCode = false;
        var position = 0;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
            }
            else if (!inCode && line.StartsWith(level, StringComparison.Ordinal))
            {
                found.Add((line[level.Length..].Trim(), position));
            }

            position += line.Length + 1;
        }

        return found;
    }

    /// <summary>From a heading to the next heading of the same or a higher level.</summary>
    private static string Section(string text, int start, string level)
    {
        var depth = level.Trim().Length;
        var inCode = false;
        var position = start;
        var first = true;
        foreach (var line in text[start..].Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
            }
            else if (!first && !inCode && line.StartsWith('#'))
            {
                var hashes = line.TakeWhile(c => c == '#').Count();
                if (hashes <= depth && line.Length > hashes && line[hashes] == ' ')
                {
                    break;
                }
            }

            first = false;
            position += line.Length + 1;
        }

        return text[start..Math.Min(position, text.Length)].TrimEnd() + "\n";
    }

    // "`<LineSeries>` (`LineSeries<TItem>`)" → "lineseries": the first name, no markup.
    private static string Normalize(string title)
    {
        var name = title.Split(" (", 2)[0];
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == ' ')
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        var text = sb.ToString().Trim();
        return text.EndsWith("titem", StringComparison.Ordinal) ? text[..^5] : text;
    }
}
