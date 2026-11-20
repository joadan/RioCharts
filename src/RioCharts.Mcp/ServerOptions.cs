using System.Globalization;
using RioCharts.Skia;

namespace RioCharts.Mcp;

/// <summary>Command-line options of riocharts-mcp.</summary>
public sealed record ServerOptions
{
    public const string Usage = """
        riocharts-mcp: an MCP server (stdio) that renders charts as PNG or SVG files for AI agents.

        Usage: riocharts-mcp [options]

          --data <folder>     CSV and JSON files to offer as datasets (one per file). Default: none;
                              the agent puts small data inline in the spec.
          --out <folder>      Where images are saved. Default: ./charts
          --width <px>        Default image width. Default: 800
          --height <px>       Default image height. Default: 400
          --theme <name>      Default theme: light or dark. Default: light
          --culture <name>    Culture for labels, e.g. sv-SE or en-US. Default: the system culture
          --help              Show this help.

        Register it with your agent, e.g. in .mcp.json:
          { "mcpServers": { "riocharts": { "command": "riocharts-mcp", "args": ["--data", "data"] } } }
        """;

    public string? DataFolder { get; init; }

    public string OutputFolder { get; init; } = "charts";

    public int Width { get; init; } = 800;

    public int Height { get; init; } = 400;

    public bool Dark { get; init; }

    public CultureInfo Culture { get; init; } = CultureInfo.CurrentCulture;

    public bool Help { get; init; }

    /// <summary>The export defaults these options give.</summary>
    public ExportOptions Export => new()
    {
        Width = Width,
        Height = Height,
        Theme = Dark ? ExportTheme.Dark : ExportTheme.Light,
        Culture = Culture,
    };

    /// <summary>Parses the arguments; returns an error message for bad ones.</summary>
    public static (ServerOptions? Options, string? Error) Parse(IReadOnlyList<string> args)
    {
        var options = new ServerOptions();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help" or "/?")
            {
                return (options with { Help = true }, null);
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Count)
            {
                return (null, $"Unknown or incomplete option '{arg}'. Run riocharts-mcp --help.");
            }

            var value = args[++i];
            switch (arg)
            {
                case "--data":
                    options = options with { DataFolder = value };
                    break;
                case "--out":
                    options = options with { OutputFolder = value };
                    break;
                case "--width" or "--height":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var px) || px < 100 || px > 4000)
                    {
                        return (null, $"{arg}: '{value}' is not valid; use 100 to 4000.");
                    }

                    options = arg == "--width" ? options with { Width = px } : options with { Height = px };
                    break;
                case "--theme":
                    if (value.ToLowerInvariant() is not ("light" or "dark"))
                    {
                        return (null, $"--theme: '{value}' is not valid; use light or dark.");
                    }

                    options = options with { Dark = value.Equals("dark", StringComparison.OrdinalIgnoreCase) };
                    break;
                case "--culture":
                    try
                    {
                        options = options with { Culture = CultureInfo.GetCultureInfo(value, predefinedOnly: true) };
                    }
                    catch (CultureNotFoundException)
                    {
                        return (null, $"--culture: '{value}' is not a known culture.");
                    }

                    break;
                default:
                    return (null, $"Unknown option '{arg}'. Run riocharts-mcp --help.");
            }
        }

        return (options, null);
    }
}
