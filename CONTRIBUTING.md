# Contributing

Issues are welcome. RioCharts is built for our own teams' needs, so there is no roadmap promise,
but bug reports and small, focused pull requests help.

**Open an issue first.** While RioCharts is in preview, every pull request must come from an issue
where the change has been discussed and agreed. Pull requests without one may be closed: APIs
and the design still move, and agreeing first saves work on both sides.

[Design.md](Design.md) is the design: read it before adding or changing features. Where the code differs from it, that is recorded in
[docs/implementation-notes.md](docs/implementation-notes.md).

RioCharts is human-led and AI-built: the design is decided by people, and most code is written by
AI coding agents working from [AGENTS.md](AGENTS.md). Contributions written with an agent are
welcome on the same terms as any other: an agreed issue, passing tests and accepted snapshots, and
a person who has read the change and stands behind it.

## Building and testing

You need the .NET 10 and .NET 11 SDKs: the libraries and tests target both, and `dotnet test` runs
every test on both.

```powershell
dotnet build RioCharts.sln
dotnet test
dotnet run --project samples/RioCharts.Demo --launch-profile http   # demo, http://localhost:5030
dotnet run --project samples/RioCharts.Docs --launch-profile http   # docs site, http://localhost:5031
dotnet run --project samples/RioCharts.RenderModes/RioCharts.RenderModes --launch-profile http   # render modes, http://localhost:5033
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
| `src/RioCharts.Core` | Scales, ticks, layout, path builders, render-agnostic scene, hit-testing, the chart definition and JSON definitions. No UI dependencies. |
| `src/RioCharts.Blazor` | `<RioChart>` and the series components, the SVG scene renderer and the JS module. |
| `src/RioCharts.Skia` | PNG and SVG export with SkiaSharp (`ChartExport`). |
| `src/RioCharts.AI` | `create_chart` and `render_chart` for agents (Microsoft.Extensions.AI and MCP), definition → Razor. |
| `src/RioCharts.Mcp` | `riocharts-mcp`: the local MCP server (stdio, .NET tool). |
| `tests/RioCharts.Tests` | xUnit tests for Core, Skia, AI and MCP, and the visual snapshots. |
| `tests/RioCharts.Blazor.Tests` | bUnit tests for `<RioChart>`, and the agent guide. |
| `tests/RioCharts.AotSmoke` | A console app published with native AOT in CI. |
| `benchmarks/RioCharts.Benchmarks` | BenchmarkDotNet; the baseline is in its README. |
| `samples/RioCharts.Demo` | Blazor Server demo. |
| `samples/RioCharts.Docs` | The docs site at https://riocharts.com (Blazor WebAssembly). |
| `samples/RioCharts.Mcp.Sample` | Sample data and agent configuration for trying `riocharts-mcp`. |
| `samples/RioCharts.RenderModes` | The same charts in every Blazor render mode (static, streaming, Server, WebAssembly, Auto; with and without prerendering), http://localhost:5033. Check a change that touches the component or its JS there: open each page and run `await rioCheck()` in the browser console. Its "Every chart type" page (`/types`) has one chart of each type; `await rioKeys()` steps through each with the keyboard and reports what screen readers hear. |
| `tools/` | `SocialCard.cs` (the docs site's link preview) and `ReadmeImage.cs` (the README picture): `dotnet run tools/ReadmeImage.cs` from the root. `prerender/` (Node + Playwright) saves each docs page as static HTML for search engines when the docs site is published. |

## Public API

The library packages (Core, Blazor, Skia, AI) list every public symbol in `PublicAPI.Shipped.txt`
next to the project, checked by `Microsoft.CodeAnalysis.PublicApiAnalyzers`
([Directory.Build.targets](Directory.Build.targets)). A public API change that the files don't list fails the build:

- **Added** (RS0016): add the symbol to `PublicAPI.Unshipped.txt`. The IDE's quick fix on the
  error does it, or from the command line:
  `dotnet format analyzers src/RioCharts.Core/RioCharts.Core.csproj --diagnostics RS0016 --severity info`
  (Razor-generated members are not covered by the fix; copy them from the error message).
- **Removed or changed** (RS0017): remove the old line too, and call out the break in
  [CHANGELOG.md](CHANGELOG.md).

The diff of these files is the public API change in a pull request. At a release, move the
Unshipped lines into Shipped (sorted). riocharts-mcp is a tool, not a library, and has no baseline.

## Releasing

Run **Create Release** ([create-release.yml](.github/workflows/create-release.yml)) by hand from
the Actions tab on `master`: it tests, packs and publishes all packages to nuget.org, then tags the
commit, creates a GitHub release and starts the **Docs site** workflow, so the site's badge and footer
show the new package (the site reads the newest release tag when it is built). The version comes from [version.json](version.json) and the git
height (Nerdbank.GitVersioning): `0.1-preview` gives `0.1.<height>-preview`. The package README on
NuGet is [assets/nuget-readme.md](assets/nuget-readme.md). Before releasing, move each package's
`PublicAPI.Unshipped.txt` lines into `PublicAPI.Shipped.txt` (see Public API).

Keep [CHANGELOG.md](CHANGELOG.md) up to date and call out breaking changes.
