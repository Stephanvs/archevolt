```

BenchmarkDotNet v0.16.0-preview.2, Windows 11 (10.0.26300.9818)
AMD Ryzen 9 5950X 3.39GHz, 1 CPU, 32 logical and 16 physical cores
Memory: 63,94 GB Total, 31,49 GB Available
.NET SDK 11.0.100-rc.3.26522.148
  [Host]     : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3
  Job-OEWQXS : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3

LaunchCount=3

```
| Method               | Count  | Mean      | Error    | StdDev   | Median    | Min       | Max       | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------------------- |------- |----------:|---------:|---------:|----------:|----------:|----------:|------:|--------:|----------:|------------:|
| Handwritten          | 100000 |  83.13 μs | 0.663 μs | 1.262 μs |  83.47 μs |  81.46 μs |  85.94 μs |  1.00 |    0.00 |         - |          NA |
| GeneratedEach        | 100000 |  94.41 μs | 0.633 μs | 1.204 μs |  93.73 μs |  92.88 μs |  96.48 μs |  1.14 |    0.02 |         - |          NA |
| GeneratedQuery       | 100000 |  83.28 μs | 0.689 μs | 1.311 μs |  83.68 μs |  81.62 μs |  85.83 μs |  1.00 |    0.02 |         - |          NA |
| GeneratedBlock       | 100000 |  83.07 μs | 0.621 μs | 1.182 μs |  83.35 μs |  81.40 μs |  85.22 μs |  1.00 |    0.02 |         - |          NA |
| GeneratedBlockVector | 100000 |  20.98 μs | 0.158 μs | 0.297 μs |  21.05 μs |  20.44 μs |  21.40 μs |  0.25 |    0.01 |         - |          NA |
| ArchInline           | 100000 | 103.95 μs | 0.804 μs | 1.510 μs | 104.17 μs | 101.99 μs | 107.03 μs |  1.25 |    0.03 |         - |          NA |
