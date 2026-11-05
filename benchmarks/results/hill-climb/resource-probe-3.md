```

BenchmarkDotNet v0.16.0-preview.2, Windows 11 (10.0.26300.9818)
AMD Ryzen 9 5950X 3.39GHz, 1 CPU, 32 logical and 16 physical cores
Memory: 63,94 GB Total, 13,94 GB Available
.NET SDK 11.0.100-rc.3.26522.148
  [Host]    : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3
  InProcess : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3

Job=InProcess  Runtime=.NET 11.0  Toolchain=InProcessEmitToolchain  
IterationCount=15  IterationTime=250ms  WarmupCount=6  

```
| Method   | Mean     | Error    | StdDev   |
|--------- |---------:|---------:|---------:|
| Constant | 86.53 μs | 3.562 μs | 3.332 μs |
| Resource | 97.12 μs | 3.114 μs | 2.600 μs |
