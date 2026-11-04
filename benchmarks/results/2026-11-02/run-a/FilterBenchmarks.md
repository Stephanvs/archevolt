```

BenchmarkDotNet v0.16.0-preview.2, Windows 11 (10.0.26300.9818)
AMD Ryzen 9 5950X 3.39GHz, 1 CPU, 32 logical and 16 physical cores
Memory: 63,94 GB Total, 31,49 GB Available
.NET SDK 11.0.100-rc.3.26522.148
  [Host]     : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3
  Job-OEWQXS : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3

LaunchCount=3

```
| Method              | Count  | Mean         | Error       | StdDev       | Median       | Min          | Max          | Allocated |
|-------------------- |------- |-------------:|------------:|-------------:|-------------:|-------------:|-------------:|----------:|
| **TrackedWrite**        | **1000**   |   **1,117.4 ns** |     **9.34 ns** |     **17.76 ns** |   **1,118.3 ns** |   **1,091.1 ns** |   **1,146.2 ns** |         **-** |
| ChangedNone         | 1000   |     162.9 ns |     1.02 ns |      1.94 ns |     162.9 ns |     159.3 ns |     166.1 ns |         - |
| WriteThenChangedAll | 1000   |   2,012.0 ns |    14.61 ns |     27.80 ns |   2,003.4 ns |   1,977.1 ns |   2,066.4 ns |         - |
| OptionalHalfPresent | 1000   |   1,285.7 ns |     4.59 ns |      8.50 ns |   1,281.1 ns |   1,274.4 ns |   1,300.4 ns |         - |
| **TrackedWrite**        | **100000** | **101,397.2 ns** | **1,543.16 ns** |  **2,936.03 ns** | **102,446.1 ns** |  **95,465.7 ns** | **105,346.7 ns** |         **-** |
| ChangedNone         | 100000 |     175.8 ns |     7.69 ns |     14.44 ns |     168.2 ns |     161.0 ns |     196.8 ns |         - |
| WriteThenChangedAll | 100000 | 187,657.0 ns | 7,160.84 ns | 13,624.25 ns | 187,273.1 ns | 168,196.6 ns | 207,681.9 ns |         - |
| OptionalHalfPresent | 100000 | 138,662.2 ns |   499.54 ns |    950.44 ns | 138,637.5 ns | 136,728.8 ns | 140,469.1 ns |         - |
