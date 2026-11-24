# JSON definition format

What RioCharts 1.x promises about JSON chart definitions (`ChartDefinitionJson`), and how the
promise is checked. Users see the short version on the docs site (Definitions, "Saved definitions
keep working").

## The promise

- A definition may carry `"version": 1` (`ChartDefinitionJson.FormatVersion`). It is optional:
  missing means 1. `Serialize` writes it.
- Every RioCharts 1.x reads every version 1 definition and draws the same chart from it.
- Within 1.x, these may be **added** but never **removed** or **changed in meaning**:
  - fields, at every level (`series[].colorBands[].above`, `yAxes[].tickBase`, ...);
  - chart type names and their synonyms (`spline`, `doughnut`, `gantt`, `meter`, ...), including
    what a synonym sets (`spline` is a line with `curve: monotone`, `donut` a pie with
    `innerRadius: 0.6`);
  - the words enum fields read, and their synonyms (`avg` for `average`, `>=` for `gte`, ...);
  - default values (a changed default changes saved charts that leave the field out).
- A renamed field or word keeps the old name as a synonym.
- Limits (`MaxSeries`, `MaxInlineRows`, `MaxAnnotations`) may grow, not shrink.
- The JSON schema (`ChartDefinitionJson.Schema()`, MCP `get_chart_schema`) describes the format and
  follows the same rules.

## Tolerant reading (part of the format)

These are part of version 1 too, so definitions written by people or AI agents keep reading:

- Field names in any letter case; unknown fields are ignored; comments and trailing commas are
  allowed; numbers may be strings (`"12"`).
- Chart type names and enum words in any case and with any separators (`Stacked-Bar`,
  `stacked_bar`).
- Shape fixes, each with a warning: top-level `x`/`y` (and `type`, `name`, ...) as one series;
  `series` as a single object; `filter` as a single object; `stacking` on a series or the chart
  moves to its axis; `horizontal` on a series makes the chart horizontal.
- `version`: `1`, `"1"`, `1.0` and `"1.0"` all mean 1. A newer whole number is read as version 1
  with a warning. Anything else is left out with a warning (never an error).

## How it is checked

`DefinitionContractTests` compares `ChartDefinitionJson.ContractLines()` (every field path from
the schema, every chart type name and synonym with its target, every enum word) with
[tests/RioCharts.Tests/Snapshots/definition-contract.txt](../tests/RioCharts.Tests/Snapshots/definition-contract.txt):

- a **removed** line fails the test (keep the old name as a synonym instead);
- an **added** line fails once so it gets looked at; accept it with
  `RIOCHARTS_ACCEPT_SNAPSHOTS=1 dotnet test`.

Default values and what synonyms set are not in the file; the reference charts in
`SnapshotTests` (and their JSON tests) catch changes there.

## A version 2

Only for a change that cannot be a synonym or an addition. Then 1.x keeps reading version 1, the
new reader converts version 1 definitions, and `Serialize` writes 2.
