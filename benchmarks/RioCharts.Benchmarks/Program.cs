using BenchmarkDotNet.Running;

// Run all:      dotnet run -c Release --project benchmarks/RioCharts.Benchmarks -- --filter *
// Run a group:  dotnet run -c Release --project benchmarks/RioCharts.Benchmarks -- --filter *Layout*
// Quick check:  add --job short (fewer iterations, less precise).
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
