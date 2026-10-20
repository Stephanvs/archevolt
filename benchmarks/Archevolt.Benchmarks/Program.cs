using BenchmarkDotNet.Running;

if (args.Contains("--quick", StringComparer.Ordinal))
    Archevolt.Benchmarks.QuickMeasurement.Run();
else if (args.Contains("--matrix", StringComparer.Ordinal))
    Archevolt.Benchmarks.PerformanceMeasurement.Run();
else
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
