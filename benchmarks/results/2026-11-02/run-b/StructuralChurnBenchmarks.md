```

BenchmarkDotNet v0.16.0-preview.2, Windows 11 (10.0.26300.9818)
AMD Ryzen 9 5950X 3.39GHz, 1 CPU, 32 logical and 16 physical cores
Memory: 63,94 GB Total, 30,94 GB Available
.NET SDK 11.0.100-rc.3.26522.148
  [Host]     : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3
  Job-OEWQXS : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3

LaunchCount=3

```
| Method               | Count | Mean      | Error    | StdDev   | Median   | Min      | Max       | Allocated |
|--------------------- |------ |----------:|---------:|---------:|---------:|---------:|----------:|----------:|
| IdleTagRoundTrip     | 1000  |  87.59 μs | 0.846 μs | 1.610 μs | 87.74 μs | 85.49 μs |  90.80 μs |         - |
| DeferredTagRoundTrip | 1000  | 101.09 μs | 2.535 μs | 4.822 μs | 98.54 μs | 95.60 μs | 108.38 μs |         - |
