```

BenchmarkDotNet v0.16.0-preview.2, Windows 11 (10.0.26300.9818)
AMD Ryzen 9 5950X 3.39GHz, 1 CPU, 32 logical and 16 physical cores
Memory: 63,94 GB Total, 30,94 GB Available
.NET SDK 11.0.100-rc.3.26522.148
  [Host]     : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3
  Job-OEWQXS : .NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348), X64 RyuJIT x86-64-v3

LaunchCount=3

```
| Method                 | Mean     | Error     | StdDev    | Min      | Max      | Median   | Gen0   | Gen1   | Allocated |
|----------------------- |---------:|----------:|----------:|---------:|---------:|---------:|-------:|-------:|----------:|
| CreateDisposeWorld     | 1.295 μs | 0.0088 μs | 0.0166 μs | 1.263 μs | 1.328 μs | 1.299 μs | 0.3796 | 0.0057 |   6.22 KB |
| BindDisposeSchedule    | 2.027 μs | 0.0086 μs | 0.0160 μs | 1.993 μs | 2.056 μs | 2.027 μs | 0.2975 |      - |    4.9 KB |
| BindFirstRefreshAndRun | 4.225 μs | 0.0145 μs | 0.0276 μs | 4.160 μs | 4.281 μs | 4.222 μs | 0.5035 |      - |   8.23 KB |
