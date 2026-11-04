```

BenchmarkDotNet v0.16.0-preview.2, Windows 11 (10.0.26300.9818)
AMD Ryzen 9 5950X 3.39GHz, 1 CPU, 32 logical and 16 physical cores
Memory: 63,94 GB Total, 29,88 GB Available
.NET SDK 11.0.100-rc.3.26522.148
  [Host]     : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3
  Job-PTHBVM : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3

LaunchCount=5

```
| Method              | Count  | Mean         | Error       | StdDev      | Median       | Min          | Max          | Allocated |
|-------------------- |------- |-------------:|------------:|------------:|-------------:|-------------:|-------------:|----------:|
| **TrackedWrite**        | **1000**   |   **1,118.2 ns** |     **6.78 ns** |    **17.13 ns** |   **1,124.8 ns** |   **1,086.0 ns** |   **1,146.7 ns** |         **-** |
| ChangedNone         | 1000   |     163.2 ns |     0.76 ns |     1.85 ns |     163.3 ns |     159.6 ns |     167.0 ns |         - |
| WriteThenChangedAll | 1000   |   2,020.7 ns |     7.91 ns |    19.85 ns |   2,026.2 ns |   1,985.1 ns |   2,057.4 ns |         - |
| **TrackedWrite**        | **100000** |  **95,934.8 ns** |   **510.64 ns** | **1,290.45 ns** |  **96,476.8 ns** |  **93,701.8 ns** |  **97,637.5 ns** |         **-** |
| ChangedNone         | 100000 |     158.5 ns |     0.54 ns |     1.36 ns |     158.4 ns |     156.1 ns |     162.1 ns |         - |
| WriteThenChangedAll | 100000 | 170,474.1 ns | 3,452.84 ns | 8,725.77 ns | 166,799.0 ns | 163,099.0 ns | 190,350.7 ns |         - |
