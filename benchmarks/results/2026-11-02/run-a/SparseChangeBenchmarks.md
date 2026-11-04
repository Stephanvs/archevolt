```

BenchmarkDotNet v0.16.0-preview.2, Windows 11 (10.0.26300.9818)
AMD Ryzen 9 5950X 3.39GHz, 1 CPU, 32 logical and 16 physical cores
Memory: 63,94 GB Total, 31,49 GB Available
.NET SDK 11.0.100-rc.3.26522.148
  [Host]     : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3
  Job-OEWQXS : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3

LaunchCount=3

```
| Method                 | Count  | Mean        | Error    | StdDev    | Median      | Min         | Max         | Allocated |
|----------------------- |------- |------------:|---------:|----------:|------------:|------------:|------------:|----------:|
| **SparseWriteThenChanged** | **1000**   |    **621.8 ns** |  **4.96 ns** |   **9.44 ns** |    **616.9 ns** |    **611.4 ns** |    **639.6 ns** |         **-** |
| **SparseWriteThenChanged** | **100000** | **10,924.1 ns** | **73.60 ns** | **140.03 ns** | **10,902.7 ns** | **10,749.4 ns** | **11,202.9 ns** |         **-** |
