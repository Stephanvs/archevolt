```

BenchmarkDotNet v0.16.0-preview.2, Windows 11 (10.0.26300.9818)
AMD Ryzen 9 5950X 3.39GHz, 1 CPU, 32 logical and 16 physical cores
Memory: 63,94 GB Total, 30,94 GB Available
.NET SDK 11.0.100-rc.3.26522.148
  [Host]     : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3
  Job-NJCNDL : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=8  LaunchCount=3
UnrollFactor=1  WarmupCount=3

```
| Method             | Mean     | Error     | StdDev    | Min      | Max      | Median   | Allocated |
|------------------- |---------:|----------:|----------:|---------:|---------:|---------:|----------:|
| FirstRefreshAndRun | 3.718 μs | 0.4450 μs | 0.5465 μs | 3.000 μs | 5.200 μs | 3.500 μs |     192 B |
