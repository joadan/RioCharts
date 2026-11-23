using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RioCharts.Core.Definitions;

public sealed partial class ChartDataset
{
    /// <summary>
    /// Builds a dataset from CSV text with a header row. The delimiter (comma, semicolon or tab) is
    /// detected from the header. Quoted cells may hold delimiters, quotes ("") and line breaks.
    /// Numbers use a decimal point; with a semicolon or tab delimiter a decimal comma also works
    /// (as Excel writes in many locales). ISO 8601 cells become times, empty cells are gaps.
    /// </summary>
    public static ChartDataset FromCsv(string name, string csv, string? description = null,
        IReadOnlyDictionary<string, string>? fieldDescriptions = null)
    {
        csv = csv.TrimStart('﻿');
        var delimiter = DetectDelimiter(csv);
        var records = ParseCsv(csv, delimiter);
        if (records.Count == 0)
        {
            return new ChartDataset(name, [], description);
        }

        var header = UniqueNames(records[0]);
        var decimalComma = delimiter != ',';

        // Straight into columns (the header names are already unique).
        var columns = header.Select(_ => new List<object?>(records.Count - 1)).ToArray();
        for (var r = 1; r < records.Count; r++)
        {
            var record = records[r];
            if (record.Count == 1 && string.IsNullOrWhiteSpace(record[0]))
            {
                continue; // blank line
            }

            for (var i = 0; i < header.Count; i++)
            {
                columns[i].Add(i < record.Count ? Normalize(CsvValue(record[i], decimalComma)) : null);
            }
        }

        // Without rows the columns stay, so the agent sees the fields, even without values.
        return new ChartDataset(name, [.. header.Select((h, i) => columns[i].Count == 0
            ? new DataField(h, FieldType.Text, [], fieldDescriptions?.GetValueOrDefault(h))
            : Column(h, [.. columns[i]], fieldDescriptions?.GetValueOrDefault(h)))], description);
    }

    private static object? CsvValue(string cell, bool decimalComma)
    {
        var s = cell.Trim();
        if (s.Length == 0)
        {
            return null;
        }

        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d))
        {
            return d;
        }

        if (decimalComma && DecimalCommaPattern().IsMatch(s)
            && double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
        {
            return d;
        }

        return s; // text, or an ISO time (read as a time by Normalize)
    }

    private static char DetectDelimiter(string csv)
    {
        int comma = 0, semicolon = 0, tab = 0;
        var quoted = false;
        foreach (var c in csv)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted)
            {
                if (c is '\n' or '\r')
                {
                    break;
                }

                switch (c)
                {
                    case ',': comma++; break;
                    case ';': semicolon++; break;
                    case '\t': tab++; break;
                }
            }
        }

        return tab > comma && tab >= semicolon ? '\t' : semicolon > comma ? ';' : ',';
    }

    private static List<List<string>> ParseCsv(string csv, char delimiter)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var any = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            any = true;
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                record.Add(cell.ToString());
                cell.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                {
                    i++;
                }

                record.Add(cell.ToString());
                cell.Clear();
                records.Add(record);
                record = [];
                any = false;
            }
            else
            {
                cell.Append(c);
            }
        }

        if (any)
        {
            record.Add(cell.ToString());
            records.Add(record);
        }

        return records;
    }

    private static List<string> UniqueNames(List<string> header)
    {
        var names = new List<string>(header.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            var baseName = header[i].Trim();
            if (baseName.Length == 0)
            {
                baseName = "column" + (i + 1).ToString(CultureInfo.InvariantCulture);
            }

            var name = baseName;
            for (var n = 2; !seen.Add(name); n++)
            {
                name = baseName + "_" + n.ToString(CultureInfo.InvariantCulture);
            }

            names.Add(name);
        }

        return names;
    }

    [GeneratedRegex(@"^-?\d+,\d+$")]
    private static partial Regex DecimalCommaPattern();
}
