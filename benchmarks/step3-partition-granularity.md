# Step 3: partition granularity

This file records a follow-up measurement for [ROADMAP.md](../ROADMAP.md) step 3. Step 3 changed how a partitioned `[Each]` or `[EachBlock]` run is split. Chunked storage split matched chunks into `min(chunks, workers)` ranges. Table storage splits matched rows into `min(rows, workers)` ranges (`src/Archevolt/Execution/BoundSchedule.cs`). [step3-table-storage.md](step3-table-storage.md) recorded no four-worker numbers, so this file compares the two schemes and two candidate changes.

## Conditions

| Setting | Value |
| --- | --- |
| CPU | AMD Ryzen 9 5950X 16-Core Processor |
| OS | Windows 11 Pro 10.0.26300 |
| .NET | SDK 11.0.100-rc.2.26504.105, Release |
| BenchmarkDotNet | 0.16.0-preview.2, default job, three launches, High performance power plan |
| Benchmark | `MovementBenchmarks.GeneratedEach`, `GeneratedBlock`, and `GeneratedBlockVector` |

Each variant ran from a clean worktree on 2026-10-28, one after the other:

| Run | Commit | Change | Started (UTC) |
| --- | --- | --- | --- |
| Chunks | `ffabaeb` | none | 14:47 |
| Tables A | `1435981` | none | 15:37 |
| Minimum rows | `1435981` | `partitions = Math.Clamp(rows / 16_384, 1, workers)` | 16:32 |
| 64-row rounding | `1435981` | inner partition boundaries rounded down to a multiple of 64 rows | 17:40 |
| Tables B | `1435981` | none | 18:25 |

Each run used `dotnet <Archevolt.Benchmarks.dll> --filter '*MovementBenchmarks.GeneratedEach*' '*MovementBenchmarks.GeneratedBlock*' --launchCount 3 --exporters json`. Each cell is the median of three per-process means in microseconds, with the range in brackets, computed the way `scripts/baseline.ps1` computes them. The main checkout could not run BenchmarkDotNet, because an ignored copy of the repository under `artifacts/` holds a second `Archevolt.Benchmarks.csproj`.

The machine was in normal desktop use during these runs. Tables A and Tables B ran identical code, so their difference measures drift. Four-worker cases moved by up to 37% between them, and one-worker cases by up to 9%. Changes smaller than that are not evidence.

## Four workers

| Case | Entities | Chunks | Tables A | Tables B | Minimum rows | 64-row rounding |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `[Each]` | 1,000 | 19.50 [19.36–19.79] | 20.00 [19.94–20.16] | 14.87 [14.54–15.07] | 22.10 [21.97–22.21] | 15.09 [15.01–15.12] |
| `[EachBlock]`, indexed | 1,000 | 19.01 [18.63–19.17] | 19.91 [19.66–20.02] | 14.85 [14.84–14.97] | 21.85 [21.85–22.12] | 20.18 [20.15–20.29] |
| `[EachBlock]`, `Vector256` | 1,000 | 21.18 [20.93–21.20] | 18.92 [18.83–18.92] | 14.11 [13.98–14.23] | 20.90 [20.32–21.28] | 19.48 [19.25–20.03] |
| `[Each]` | 100,000 | 93.08 [92.96–96.08] | 81.53 [81.50–82.70] | 51.56 [50.22–53.37] | 83.68 [79.92–90.13] | 49.58 [46.97–49.63] |
| `[EachBlock]`, indexed | 100,000 | 85.35 [84.43–85.37] | 47.74 [45.36–49.33] | 48.72 [47.81–49.34] | 79.79 [73.42–87.76] | 43.01 [42.61–43.20] |
| `[EachBlock]`, `Vector256` | 100,000 | 37.01 [36.85–40.06] | 32.04 [22.60–32.90] | 23.23 [23.22–23.63] | 38.35 [37.85–38.87] | 21.85 [21.76–21.85] |

## One worker, for reference

| Case | Entities | Chunks | Tables A | Tables B |
| --- | ---: | ---: | ---: | ---: |
| `[Each]` | 1,000 | 1.273 [1.242–1.278] | 1.106 [1.079–1.110] | 1.068 [1.063–1.103] |
| `[EachBlock]`, indexed | 1,000 | 1.161 [1.159–1.227] | 0.980 [0.964–0.992] | 0.933 [0.928–0.935] |
| `[EachBlock]`, `Vector256` | 1,000 | 0.256 [0.255–0.258] | 0.236 [0.235–0.237] | 0.229 [0.227–0.239] |
| `[Each]` | 100,000 | 120.1 [119.3–129.4] | 102.1 [102.1–108.8] | 93.02 [92.72–93.06] |
| `[EachBlock]`, indexed | 100,000 | 109.5 [109.5–111.0] | 85.08 [84.91–86.06] | 81.53 [81.42–82.10] |
| `[EachBlock]`, `Vector256` | 100,000 | 29.72 [29.37–32.04] | 21.05 [20.83–21.08] | 20.29 [20.28–20.40] |

## Findings

Row partitioning did not measurably regress the small case. At 1,000 entities, Tables A ran 3% to 5% slower than chunks for `[Each]` and the indexed `[EachBlock]`, and Tables B ran 22% to 33% faster. Both differences lie inside the drift between the two table runs.

At 100,000 entities, tables beat chunks under four workers in both table runs. The indexed `[EachBlock]` ran at 0.56 to 0.57 times its chunked time.

The cost of a small partitioned run is dispatch, not partition count. With four workers, 1,000 entities cost 14 to 22 µs on every variant, against about 1 µs on one worker. `WorkerPool.Run` wakes every worker for each batch, however many work items the batch holds (`src/Archevolt/Execution/WorkerPool.cs`).

The minimum-rows change did not help. It gave the 1,000-entity case a single work item, and that case still took 20.90 to 22.10 µs. At 100,000 entities it produced the same four partitions as HEAD, so its numbers there measure drift only. It also changes a tested contract, because the partition tests expect one invocation per worker for 200 and 10,000 entities. It was not adopted.

The 64-row rounding gave no result outside drift. Against Tables B, it ran 0.88 to 0.96 times as long at 100,000 entities with four workers, and 1.01 to 1.38 times as long at 1,000. It was not adopted.

A change that runs a batch's single work item on the owner thread without waking the pool, or that keeps small partitionable queries serial, targets the measured cost. It is not measured here.
