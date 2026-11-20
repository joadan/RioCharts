# RioCharts

A Blazor SVG chart component library. `Design.md` is the specification: read it
before adding or changing features, and keep it in sync when the design changes.

## Layout

- `src/RioCharts/` — the component library (Razor class library, `net10.0`,
  nullable and implicit usings enabled).
- `docs/` — documentation.
- `RioCharts.sln` — solution file.

## Build

```
dotnet build RioCharts.sln
```

## Git

- Commit and push directly to `master`; no feature branches or PRs for now.

## Architecture (from Design.md)

Keep these layers separate:

1. **Model** — `Chart`, `Axis`, `LegendSettings`, `TooltipSettings`, and series
   types implementing `ISeries` (`XYSeries`, `ValueSeries`, `XYZSeries`).
2. **Layout engine** — maps data to screen coordinates (ranges, ticks, multiple
   Y axes, LTTB downsampling, caching). Produces `SeriesLayout`. No rendering here.
3. **SVG renderer** — turns layout into SVG elements. No data math here.
4. **`<RioChart>` component** — Blazor parameters, event callbacks, lifecycle.

## Conventions

- Render SVG directly in Razor; no JS interop for rendering.
- Prefer pure Blazor event handling. Add JS interop (`RioCharts.js`) only for
  high-frequency interactions: pan/zoom, crosshair, tooltip following the mouse,
  touch gestures.
- Collections on the model default to empty lists, never null.
- Handle bad input gracefully as described in Design.md "Error Handling"
  (empty data, invalid axis range, missing Y axis reference) — do not throw.
- Use `@key` on series elements so Blazor diffs efficiently.
- Component styles go in scoped CSS (`RioChart.razor.css`).
- Layout engine logic should be plain C# and unit-testable without Blazor.
