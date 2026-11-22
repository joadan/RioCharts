using Bunit;
using Microsoft.AspNetCore.Components;

namespace RioCharts.Blazor.Tests;

/// <summary>
/// The guide for coding agents (llms-full.txt on the docs site, get_usage in the MCP server): its
/// examples compile and render, and the file matches the code. After a change to a component's
/// parameters or docs, accept the new file with <c>RIOCHARTS_ACCEPT_SNAPSHOTS=1 dotnet test</c>.
/// </summary>
public class AgentGuideTests : ChartTestBase
{
    public static TheoryData<string> Examples => [.. AgentGuide.Examples];

    [Theory]
    [MemberData(nameof(Examples))]
    public void Every_example_renders_a_chart(string name)
    {
        var type = typeof(AgentGuideTests).Assembly.GetType("RioCharts.Blazor.Tests.Guide." + name)
            ?? throw new InvalidOperationException($"Guide/{name}.razor not found.");

        var cut = Render(b =>
        {
            b.OpenComponent(0, type);
            b.CloseComponent();
        });

        Assert.Single(cut.FindAll(".rio-chart-svg"));
        if (name != "LiveData") // starts empty: points arrive from a timer
        {
            Assert.Empty(cut.FindAll(".rio-empty"));
        }
    }

    [Fact]
    public void Every_example_file_is_in_the_guide()
    {
        var files = Directory.GetFiles(Path.Combine(AgentGuide.Root, "tests", "RioCharts.Blazor.Tests", "Guide"), "*.razor")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != "_Imports");

        Assert.Equal(files.Order(), AgentGuide.Examples.Order());
    }

    [Fact]
    public void Llms_full_txt_matches_the_code()
    {
        var expected = AgentGuide.Build();
        var path = AgentGuide.OutputPath;
        var current = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;
        if (current == expected)
        {
            return;
        }

        if (current is null || Environment.GetEnvironmentVariable("RIOCHARTS_ACCEPT_SNAPSHOTS") == "1")
        {
            File.WriteAllText(path, expected);
            return;
        }

        var received = Path.Combine(Path.GetTempPath(), "llms-full.received.txt");
        File.WriteAllText(received, expected);
        var at = Enumerable.Range(0, Math.Min(current.Length, expected.Length)).FirstOrDefault(i => current[i] != expected[i], Math.Min(current.Length, expected.Length));
        Assert.Fail($"llms-full.txt is out of date from character {at}: \"{expected.Substring(at, Math.Min(80, expected.Length - at))}\" "
            + $"(new version: {received}). If the change is intended, run with RIOCHARTS_ACCEPT_SNAPSHOTS=1.");
    }
}
