# Initial movement measurement

Command run on 2026-10-20 against the uncommitted first vertical implementation:

```powershell
dotnet run --project benchmarks/Archevolt.Benchmarks -c Release -- --quick
```

The process reported .NET 10.0.13, Windows 10.0.26300, X64, and 32 logical processors. It used 100,000 entities, 32 warmup executions per method, and 64 measured executions per method. Measurement order rotates between methods.

| Method | Nanoseconds per entity | Owner allocated bytes | Total managed allocated bytes |
| --- | ---: | ---: | ---: |
| Handwritten native SoA | 1.782 | 0 | 0 |
| Generated Each | 2.318 | 0 | 0 |
| Generated named query | 3.366 | 0 | 0 |

These are initial measurements from one process while implementation work was active. They establish a zero-allocation result for this serial movement workload. They do not establish stable regression limits, parallel allocation behavior, or performance across other workloads. Use BenchmarkDotNet for repeated measurements and distributions after the implementation stabilizes.
