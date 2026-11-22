# Contributing

Issues are welcome. RioCharts is built for our own teams' needs, so there is no roadmap promise,
but bug reports and small, focused pull requests help. [Design.md](Design.md) is the design: read it
before adding or changing features. Where the code differs from it, that is recorded in
[docs/implementation-notes.md](docs/implementation-notes.md).

## Building and testing

You need the .NET 10 and .NET 11 SDKs: the libraries and tests target both, and `dotnet test` runs
every test on both.

```powershell
dotnet build RioCharts.sln
dotnet test
dotnet run --project samples/RioCharts.Demo --launch-profile http   # demo, http://localhost:5030
dotnet run --project samples/RioCharts.Docs --launch-profile http   # docs site, http://localhost:5031
```

Stop the running demo before rebuilding; it locks the library DLLs.

Two kinds of tests compare against files in the repository:

- **Visual snapshots** (`tests/RioCharts.Tests/Snapshots/`): SVGs of reference charts. A layout
  change that alters one fails with `name.received.svg` next to it.
- **The agent guide** (`samples/RioCharts.Docs/wwwroot/llms-full.txt`), built from
  `docs/llms/guide.md`, the example components and the XML docs of every component parameter.

Look at the difference, then accept it with `RIOCHARTS_ACCEPT_SNAPSHOTS=1 dotnet test` and review
the diff before committing.

## Projects

| Path | What |
| --- | --- |
| `src/RioCharts.Core` | Scales, ticks, layout, path builders, render-agnostic scene, hit-testing, the chart spec and JSON specs. No UI dependencies. |
| `src/RioCharts.Blazor` | `<RioChart>` and the series components, the SVG scene renderer and the JS module. |
| `src/RioCharts.Skia` | PNG and SVG export with SkiaSharp (`ChartExport`). |
| `src/RioCharts.AI` | `create_chart` and `render_chart` for agents (Microsoft.Extensions.AI and MCP), spec → Razor. |
| `src/RioCharts.Mcp` | `riocharts-mcp`: the local MCP server (stdio, .NET tool). |
| `tests/RioCharts.Tests` | xUnit tests for Core, Skia, AI and MCP, and the visual snapshots. |
| `tests/RioCharts.Blazor.Tests` | bUnit tests for `<RioChart>`, and the agent guide. |
| `tests/RioCharts.AotSmoke` | A console app published with native AOT in CI. |
| `benchmarks/RioCharts.Benchmarks` | BenchmarkDotNet; the baseline is in its README. |
| `samples/RioCharts.Demo` | Blazor Server demo. |
| `samples/RioCharts.Docs` | The docs site at https://riocharts.com (Blazor WebAssembly). |
| `samples/RioCharts.Mcp.Sample` | Sample data and agent configuration for trying `riocharts-mcp`. |
| `tools/` | `SocialCard.cs` (the docs site's link preview) and `ReadmeImage.cs` (the README picture): `dotnet run tools/ReadmeImage.cs` from the root. |

## Releasing

Run **Create Release** ([create-release.yml](.github/workflows/create-release.yml)) by hand from
the Actions tab on `master`: it tests, packs and publishes all packages to nuget.org, then tags the
commit and creates a GitHub release. The version comes from [version.json](version.json) and the git
height (Nerdbank.GitVersioning): `0.1-preview` gives `0.1.<height>-preview`. The package README on
NuGet is [assets/nuget-readme.md](assets/nuget-readme.md).

Keep [CHANGELOG.md](CHANGELOG.md) up to date and call out breaking changes.
