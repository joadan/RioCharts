using RioCharts.Core;
using RioCharts.Core.Definitions;

namespace RioCharts.Tests;

/// <summary>
/// The 1.x promise of the JSON definition format (docs/definition-format.md): every field, chart type name, synonym and
/// enum word that version 1 reads is listed in <c>Snapshots/definition-contract.txt</c>. A removed line breaks saved
/// definitions and fails here; an added line is fine but fails once so it gets looked at: accept it with
/// <c>RIOCHARTS_ACCEPT_SNAPSHOTS=1 dotnet test</c>.
/// </summary>
public sealed class DefinitionContractTests
{
    [Fact]
    public void Format_keeps_everything_version_1_reads()
    {
        var file = Path.Combine(SnapshotDir(), "definition-contract.txt");
        var current = ChartDefinitionJson.ContractLines().ToList();
        var shipped = File.Exists(file)
            ? File.ReadAllLines(file).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList()
            : [];

        var removed = shipped.Except(current, StringComparer.Ordinal).ToList();
        Assert.True(removed.Count == 0,
            "The JSON format no longer reads what version 1 definitions may contain (breaks saved definitions in 1.x; keep "
            + "the old name as a synonym instead):\n" + string.Join("\n", removed));

        var added = current.Except(shipped, StringComparer.Ordinal).ToList();
        if (added.Count > 0)
        {
            if (Environment.GetEnvironmentVariable("RIOCHARTS_ACCEPT_SNAPSHOTS") != "1")
            {
                Assert.Fail("New in the JSON format (fine in 1.x; accept with RIOCHARTS_ACCEPT_SNAPSHOTS=1):\n" + string.Join("\n", added));
            }

            File.WriteAllLines(file, [
                "# The JSON definition format, version 1: what every RioCharts 1.x reads. Lines may be added, never removed.",
                "# Written by DefinitionContractTests; see docs/definition-format.md.",
                .. current,
            ]);
        }
    }

    [Fact]
    public void Missing_version_means_1()
    {
        var result = ChartDefinitionJson.Parse("""{"series":[{"type":"line","x":"t","y":"v"}]}""");
        Assert.True(result.Success);
        Assert.Null(result.Definition!.Version);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("\"1\"")]
    [InlineData("1.0")]
    [InlineData("\"1.0\"")]
    public void Version_1_in_any_spelling(string version)
    {
        var result = ChartDefinitionJson.Parse($$"""{"version":{{version}},"series":[{"type":"line","x":"t","y":"v"}]}""");
        Assert.True(result.Success);
        Assert.Equal(1, result.Definition!.Version);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Newer_version_is_read_with_a_warning()
    {
        var result = ChartDefinitionJson.Parse("""{"version":2,"series":[{"type":"line","x":"t","y":"v","laterField":true}]}""");
        Assert.True(result.Success);
        Assert.Equal(2, result.Definition!.Version);
        Assert.Contains(result.Warnings, w => w.StartsWith("version: 2 is newer", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1.5")]
    [InlineData("\"v1\"")]
    [InlineData("true")]
    [InlineData("{}")]
    public void Invalid_version_is_left_out_with_a_warning(string version)
    {
        var result = ChartDefinitionJson.Parse($$"""{"version":{{version}},"series":[{"type":"line","x":"t","y":"v"}]}""");
        Assert.True(result.Success);
        Assert.Null(result.Definition!.Version);
        Assert.Contains(result.Warnings, w => w.StartsWith("version:", StringComparison.Ordinal));
    }

    [Fact]
    public void Saving_writes_the_format_version()
    {
        var json = ChartDefinitionJson.Serialize(new ChartDefinition { Title = "Sales" });
        Assert.Contains("\"version\": 1", json, StringComparison.Ordinal);
        Assert.Equal(1, ChartDefinitionJson.Parse(json).Definition!.Version);
    }

    [Fact]
    public void Schema_lists_version()
    {
        var schema = ChartDefinitionJson.Schema();
        Assert.NotNull(schema["properties"]?["version"]);
    }

    private static string SnapshotDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RioCharts.Tests.csproj")))
            {
                return Path.Combine(dir.FullName, "Snapshots");
            }
        }

        throw new InvalidOperationException($"RioCharts.Tests.csproj not found above {AppContext.BaseDirectory}.");
    }
}
