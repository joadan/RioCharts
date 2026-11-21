# RioCharts benchmarks

BenchmarkDotNet benchmarks of the public APIs: Core layout, data and spec work, and Skia export.
The Blazor render tree and the JS module are not covered.

```
dotnet run -c Release --project benchmarks/RioCharts.Benchmarks -- --filter *
dotnet run -c Release --project benchmarks/RioCharts.Benchmarks -- --filter *Layout* --job short
```

Compare before and after a change on the same machine: the numbers below are from one laptop and
mean little elsewhere. The short job (3 iterations) is quick but noisy; use the default job for a
result you want to rely on.

| Class | What it measures |
| --- | --- |
| `LineLayoutBenchmarks` | `ChartLayout.Compute` for two time lines of 1k–100k points, whole range and zoomed to a tenth |
| `CategoryLayoutBenchmarks` | Grouped and stacked bars (3 × 30 categories), a pie with data labels |
| `DataBenchmarks` | `SeriesData.FromItems` (what a series does when its items change), LTTB to 1,600 points |
| `ExportBenchmarks` | `ChartExport`: scene to SVG, spec to SVG, spec to PNG (800 × 400, scale 1) |
| `SpecBenchmarks` | `ChartSpecJson.Parse`, `ChartDataset.FromCsv`, `ChartSpecBinder.Bind` (group by, day buckets, average) |

## Baseline

2026-11-21, `--job short`, BenchmarkDotNet 0.15.2, .NET 10.0.12, Windows 11, x64 (AVX-512).
Mean time and allocated memory per call.

### Layout

| Benchmark | 1k points | 10k points | 100k points |
| --- | ---: | ---: | ---: |
| Line | 1.16 ms, 396 KB | 1.94 ms, 611 KB | 7.74 ms, 610 KB |
| LineZoomed | 0.17 ms, 68 KB | 1.26 ms, 396 KB | 5.91 ms, 611 KB |

| Benchmark | Time | Allocated |
| --- | ---: | ---: |
| GroupedBars | 44 µs | 49 KB |
| StackedBars | 50 µs | 54 KB |
| Pie | 23 µs | 33 KB |

The 1k Line result had a large error (±2.4 ms); rerun with the default job before reading much into it.

### Data

| Benchmark | 10k points | 1M points |
| --- | ---: | ---: |
| FromItems | 0.26 ms, 547 KB | 90.8 ms, 53 MB |
| Lttb1600 | 0.06 ms, 13 KB | 3.7 ms, 13 KB |

### Export

| Benchmark | 1k points | 100k points |
| --- | ---: | ---: |
| SceneToSvg | 0.79 ms, 439 KB | 1.20 ms, 638 KB |
| SpecToSvg | 1.96 ms, 837 KB | 8.75 ms, 1.2 MB |
| SpecToPng | 28.6 ms, 509 KB | 32.3 ms, 675 KB |

### Specs

| Benchmark | 1k rows | 50k rows |
| --- | ---: | ---: |
| Parse | 30 µs, 10 KB | 26 µs, 10 KB |
| ReadCsv | 0.52 ms, 323 KB | 61.6 ms, 15.9 MB |
| Bind | 0.15 ms, 203 KB | 8.1 ms, 9.8 MB |
