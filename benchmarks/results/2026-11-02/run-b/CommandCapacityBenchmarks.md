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
| Method        | InitialCommandBytes | Count | Mean      | Error    | StdDev   | Min       | Max      | Median   | Allocated |
|-------------- |-------------------- |------ |----------:|---------:|---------:|----------:|---------:|---------:|----------:|
| **FirstPlayback** | **0**                   | **1000**  | **105.25 μs** | **2.410 μs** | **2.960 μs** | **102.00 μs** | **115.7 μs** | **104.8 μs** |  **163968 B** |
| **FirstPlayback** | **65536**               | **1000**  |  **99.48 μs** | **2.283 μs** | **2.718 μs** |  **95.70 μs** | **104.6 μs** | **100.0 μs** |     **232 B** |
