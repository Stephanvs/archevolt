# Benchmark results, 2 November 2026

This file records a run of every BenchmarkDotNet class in [Archevolt.Benchmarks](Archevolt.Benchmarks) and every criterion target in [bevy-reference](bevy-reference) available at commit `8029e7f`. Its source tree matches [release v0.5.5](https://github.com/Stephanvs/archevolt/tree/v0.5.5), at `8e2b787`. [baseline.md](baseline.md) and the step files keep the numbers each step was measured with.

This checkpoint predates the single-work-item dispatch change and the additional partition benchmarks. The [4 November dispatch and partitioning study](query-partition-threshold.md) records those later measurements. The numbers below remain evidence for the measured commit, not measurements of the updated dispatch path.

The machine-written tables are in [results/2026-11-02](results/2026-11-02):

- `run-a/` holds the default baseline classes and Bevy. `summary.md` lists the median and range of the per-process means, and one BenchmarkDotNet report per class holds the mean, error, standard deviation, and allocation.
- `run-b/` holds the remaining classes, in the same form.
- `rerun-noisy/` holds the five-process reruns described under [Reruns](#reruns).
- `rerun-despawn.csv` and `rerun-bevy.csv` hold the interleaved reruns.

[baseline.md](baseline.md#cases) maps each Archevolt case to its Bevy counterpart and describes the workloads. [Where Bevy differs](baseline.md#where-bevy-differs) lists the differences that remain after mirroring them. Numbers from the two languages describe these workloads on this machine. They do not rank the libraries.

## Conditions

| Setting | Value |
| --- | --- |
| CPU | AMD Ryzen 9 5950X 16-Core Processor, 64 GB DDR4-3200 |
| OS | Windows 11 Pro 10.0.26300.9818, High performance power plan |
| .NET | SDK 11.0.100-rc.3.26522.148, runtime 11.0.0-rc.3.26522.148, RyuJIT x86-64-v3, Release |
| BenchmarkDotNet | 0.16.0-preview.2, default job, three launches |
| Rust | rustc 1.99.1, LLVM 23.1.1, `-C target-cpu=native` |
| Bevy | `bevy_ecs` 0.20.0, criterion 0.8.2 with its defaults, three runs |
| Commit | `8029e7f`, clean detached worktree |

The machine was otherwise idle. Total CPU use, sampled for 10 seconds before each group, was 8 to 15%. The Release build, the 167 .NET tests, and the 9 Rust tests ran and passed before any measurement. One process ran at a time, in this order:

1. 21:47 to 22:51 UTC. `./scripts/baseline.ps1 -IncludeBevy`, which ran the default baseline classes, then criterion three times.
2. 22:51 to 23:36 UTC. `./scripts/baseline.ps1 -Classes MovementBenchmarks,TickWidthBenchmarks,LifecycleBenchmarks,QueryRefreshBenchmarks,StructuralChurnBenchmarks,CommandCapacityBenchmarks`.
3. 23:36 UTC. `./scripts/measure.ps1 -Mode Matrix`, which reported zero managed allocation for every steady-state traversal, filter, and dispatch case.
4. 23:38 to 01:19 UTC on 3 November. The reruns.

Unless noted otherwise, each cell is the median of three per-process means in microseconds, with the lowest and highest in brackets. Times cover one complete benchmark invocation. For a dense traversal of 100,000 entities, 100 µs is 1 ns per entity. Sparse and unchanged-filter cases visit fewer rows, and lifecycle cases include the work described in their sections. BenchmarkDotNet's `Allocated` column is managed memory per operation.

## Entity lifecycle

| Case | Entities | Archevolt µs | Archevolt allocated | Bevy µs |
| --- | ---: | ---: | ---: | ---: |
| Spawn | 1,000 | 27.25 [26.99, 28.15] | 16.05 KB | 38.45 [37.11, 38.82] |
| Spawn | 100,000 | 3,804 [3,693, 3,999] | 1,610.49 KB | 5,162 [4,952, 5,359] |
| Spawn a batch | 1,000 | 15.25 [15.15, 15.30] | 16.05 KB | 24.29 [24.22, 24.56] |
| Spawn a batch | 100,000 | 2,150 [2,133, 2,151] | 1,571.09 KB | 3,787 [3,470, 3,835] |
| Despawn | 1,000 | 24.70 [24.56, 24.77] | 8.02 KB | 41.44 [41.06, 42.26] |
| Despawn | 100,000 | 5,798 [5,607, 6,009] | 1,024.18 KB | 12,862 [12,711, 13,665] |
| `Get<T>` | 1,000 | 5.731 [5.728, 5.825] | 0 B | 4.038 [4.012, 4.099] |
| `Get<T>` | 100,000 | 1,669 [1,437, 2,967] | 0 B | 862.4 [850.8, 871.4] |
| Tag round trip | 1,000 | 87.00 [86.32, 89.90] | 0 B | 112.4 [111.0, 119.3] |
| Tag round trip | 100,000 | 8,776 [8,635, 8,811] | 0 B | 10,518 [10,470, 10,654] |
| Wide round trip | 1,000 | 95.93 [93.02, 96.03] | 0 B | 117.7 [116.6, 119.6] |
| Wide round trip | 100,000 | 9,571 [9,438, 9,609] | 0 B | 11,281 [11,261, 11,570] |

Archevolt's 100,000-entity despawn and `Get<T>` vary from process to process. See [Reruns](#reruns).

## Traversal matrix

`[Each]` runs the movement system through `TraversalMatrixBenchmarks.GeneratedEach`. The named query runs `foreach` over the same rows through `GeneratedQuery`. Bevy runs `query.iter_mut().for_each(...)`.

| Archetypes | Payload | Entities | `[Each]` µs | Named query µs | Bevy `for_each` µs |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1 | none | 1,000 | 1.061 [1.057, 1.068] | 1.069 [0.966, 1.104] | 0.273 [0.221, 0.316] |
| 1 | 256 B | 1,000 | 1.065 [1.061, 1.076] | 0.981 [0.955, 1.089] | 0.209 [0.189, 0.282] |
| 16 | none | 1,000 | 1.279 [1.272, 1.285] | 1.211 [1.202, 1.217] | 0.452 [0.440, 0.455] |
| 16 | 256 B | 1,000 | 1.317 [1.313, 1.318] | 1.235 [1.223, 1.348] | 0.455 [0.451, 0.463] |
| 1 | none | 100,000 | 94.06 [94.05, 94.10] | 94.20 [82.79, 94.29] | 24.03 [23.83, 24.38] |
| 1 | 256 B | 100,000 | 94.01 [93.48, 94.53] | 83.29 [83.03, 83.31] | 23.80 [23.80, 23.82] |
| 16 | none | 100,000 | 95.20 [95.05, 95.60] | 84.31 [84.18, 95.20] | 26.98 [26.26, 27.68] |
| 16 | 256 B | 100,000 | 95.71 [95.11, 95.90] | 95.97 [95.35, 96.15] | 27.03 [26.93, 28.08] |
| 1 | none | 1,000,000 | 1,105 [1,101, 1,109] | 977.4 [968.7, 1,006] | 637.3 [622.4, 658.4] |
| 1 | 256 B | 1,000,000 | 1,109 [1,099, 1,140] | 999.7 [996.4, 1,003] | 623.7 [614.0, 626.1] |
| 16 | none | 1,000,000 | 1,112 [1,111, 1,115] | 1,025 [1,007, 1,031] | 641.4 [634.6, 645.9] |
| 16 | 256 B | 1,000,000 | 1,118 [1,114, 1,137] | 1,019 [1,018, 1,035] | 638.5 [635.8, 675.6] |

No traversal case allocates managed memory. Each named-query process settles at about 82 to 84 µs or about 93 to 96 µs at 100,000 entities, and each process keeps its level for every iteration. Steps 2 and 3 traced this to the PGO profile and block layout of the generated loop.

## Filters

| Case | Entities | Archevolt µs | Bevy µs |
| --- | ---: | ---: | ---: |
| Tracked write | 1,000 | 1.116 [1.109, 1.127] | 0.269 [0.237, 0.294] |
| Tracked write | 100,000 | 103.3 [97.62, 103.3] | 30.67 [28.96, 62.61] |
| `Changed<T>`, nothing changed | 1,000 | 0.164 [0.161, 0.164] | 0.370 [0.368, 0.383] |
| `Changed<T>`, nothing changed | 100,000 | 0.169 [0.163, 0.195] | 29.23 [29.20, 29.67] |
| Write, then `Changed<T>` over all | 1,000 | 2.010 [2.009, 2.017] | 1.091 [1.049, 1.237] |
| Write, then `Changed<T>` over all | 100,000 | 187.1 [171.6, 204.2] | 99.46 [98.96, 103.2] |
| Optional term on half | 1,000 | 1.283 [1.280, 1.294] | 1.100 [1.099, 1.103] |
| Optional term on half | 100,000 | 138.7 [138.4, 138.9] | 105.5 [104.8, 108.7] |
| Sparse write, then `Changed<T>` | 1,000 | 0.616 [0.615, 0.634] | none |
| Sparse write, then `Changed<T>` | 100,000 | 10.88 [10.87, 11.02] | none |

No filter case allocates managed memory. The sparse case spreads tracked particles over 16 archetypes, writes one of them, then runs the `Changed<T>` scan. It has no Bevy port. Some filter cases vary between processes. See [Reruns](#reruns).

## Movement, 100,000 entities, one thread

These are `ReferenceComparisonBenchmarks` and the criterion `movement` target. [benchmark.md](benchmark.md) describes the cases.

| Case | µs | Allocated |
| --- | ---: | ---: |
| `Handwritten` | 83.06 [82.94, 83.41] | 0 B |
| `GeneratedEach` | 94.63 [93.96, 94.65] | 0 B |
| `GeneratedBlock`, indexed loop | 83.06 [82.99, 83.17] | 0 B |
| `GeneratedBlockVector`, `Vector256` | 20.96 [20.91, 21.07] | 0 B |
| `GeneratedQuery` | 83.16 [82.48, 84.21] | 0 B |
| Arch 2.1.0 `ArchInline` | 103.5 [103.5, 104.9] | 0 B |
| Rust `handwritten` | 20.50 [20.47, 20.98] | n/a |
| Bevy `bevy_for_each` | 23.70 [23.60, 24.27] | n/a |
| Bevy `bevy_contiguous` | 25.70 [25.36, 26.00] | n/a |
| Bevy `bevy_for_loop` | 117.7 [117.2, 121.6] | n/a |

`bevy_for_loop` depends on the Rust compiler version. Built with rustc 1.98.0, it measured 70.13 µs [70.01, 70.43]. See [Reruns](#reruns).

## Movement by size and worker count

`MovementBenchmarks` runs the same systems with one worker and with four. `Handwritten` is single-threaded in both rows. Named queries do not partition, so `GeneratedQuery` runs on one thread with either setting.

| Entities | Workers | `Handwritten` µs | `GeneratedEach` µs | `GeneratedBlock` µs | `GeneratedBlockVector` µs | `GeneratedQuery` µs |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1,000 | 1 | 0.821 [0.818, 0.824] | 1.062 [1.061, 1.067] | 0.946 [0.936, 0.950] | 0.227 [0.224, 0.236] | 1.004 [0.976, 1.076] |
| 1,000 | 4 | 0.823 [0.815, 0.826] | 15.12 [15.03, 15.16] | 14.83 [14.75, 15.12] | 14.15 [13.91, 14.38] | 16.01 [15.97, 16.22] |
| 100,000 | 1 | 83.75 [82.31, 83.79] | 94.69 [94.19, 94.92] | 82.82 [82.38, 84.48] | 20.94 [20.93, 21.74] | 83.52 [82.84, 83.52] |
| 100,000 | 4 | 83.07 [82.88, 83.50] | 51.35 [47.38, 51.44] | 44.86 [44.42, 46.31] | 22.62 [22.53, 22.97] | 89.93 [89.92, 90.07] |
| 1,000,000 | 1 | 1,032 [1,030, 1,042] | 1,117 [1,094, 1,122] | 1,004 [984.1, 1,011] | 503.5 [484.4, 518.3] | 1,026 [1,009, 1,045] |
| 1,000,000 | 4 | 1,038 [1,018, 1,048] | 370.7 [352.4, 417.7] | 400.3 [345.9, 400.3] | 180.8 [173.7, 305.6] | 1,018 [1,009, 1,030] |

No movement case allocates managed memory, with one worker or four. At 1,000 entities, four workers spend 14 to 16 µs per update, almost all of it in worker-pool dispatch. The four-worker cases at 1,000,000 entities vary most between processes. See [Reruns](#reruns).

## Tick width

`TickWidthBenchmarks` runs the per-row work of the generated tracked write and `Changed` scan over native columns, once with 64-bit ticks and once with 32-bit ticks compared the way Bevy compares them. [Step 7](step7-block-change-summaries.md#32-bit-ticks) describes the proxy and why ticks stay 64-bit.

| Case | Rows | 64-bit µs | 32-bit µs |
| --- | ---: | ---: | ---: |
| Tracked write | 100,000 | 132.4 [132.2, 134.9] | 131.0 [130.6, 132.6] |
| Write, then `Changed` scan | 100,000 | 210.5 [208.3, 210.8] | 206.3 [206.3, 229.2] |
| Tracked write | 1,000,000 | 1,587 [1,545, 1,608] | 1,512 [1,496, 1,519] |
| Write, then `Changed` scan | 1,000,000 | 2,479 [2,471, 2,498] | 2,311 [2,309, 2,318] |

## Setup, structural churn, and commands

| Case | µs | Allocated |
| --- | ---: | ---: |
| Create and dispose a world, `LifecycleBenchmarks.CreateDisposeWorld` | 1.300 [1.277, 1.307] | 6.22 KB |
| Bind and dispose a schedule, `LifecycleBenchmarks.BindDisposeSchedule` | 2.026 [2.014, 2.040] | 4.90 KB |
| Bind, run once, and dispose, `LifecycleBenchmarks.BindFirstRefreshAndRun` | 4.220 [4.205, 4.249] | 8.23 KB |
| First run after a new archetype, `QueryRefreshBenchmarks.FirstRefreshAndRun` | 3.800 [3.386, 3.938] | 192 B |
| Tag round trip on an idle world, 1,000 entities, `StructuralChurnBenchmarks.IdleTagRoundTrip` | 87.07 [86.57, 89.12] | 0 B |
| Tag round trip through commands, 1,000 entities, `StructuralChurnBenchmarks.DeferredTagRoundTrip` | 98.54 [97.11, 107.6] | 0 B |
| First command playback, no initial capacity, `CommandCapacityBenchmarks.FirstPlayback` | 104.8 [104.5, 106.6] | 160.12 KB |
| First command playback, 65,536 initial bytes, `CommandCapacityBenchmarks.FirstPlayback` | 100.8 [96.23, 101.4] | 232 B |

`QueryRefreshBenchmarks` and `CommandCapacityBenchmarks` time one invocation per iteration with 3 warm-up and 8 measured iterations. The command line's three launches applied to them too.

## Reruns

Cases whose three processes disagreed by more than 5%, and cases whose numbers moved from their last recorded value, ran again after the full run. Nothing else ran at the same time.

### Five processes

`./scripts/baseline.ps1 -Runs 5` over the cases below, at `8029e7f`. The full tables are in `results/2026-11-02/rerun-noisy/`.

| Case | Full run µs | Five-process rerun µs |
| --- | ---: | ---: |
| `Get<T>`, 100,000 | 1,669 [1,437, 2,967] | 1,470 [1,440, 3,392] |
| Tracked write, 100,000 | 103.3 [97.62, 103.3] | 95.84 [95.33, 96.57] |
| Write, then `Changed<T>` over all, 100,000 | 187.1 [171.6, 204.2] | 166.4 [165.4, 187.4] |
| `Changed<T>`, nothing changed, 100,000 | 0.169 [0.163, 0.195] | 0.158 [0.158, 0.159] |
| Named query, 1 archetype, no payload, 100,000 | 94.20 [82.79, 94.29] | 82.26 [80.93, 92.86] |
| Named query, 16 archetypes, no payload, 100,000 | 84.31 [84.18, 95.20] | 93.72 [82.40, 93.81] |
| Named query, 1 archetype, no payload, 1,000,000 | 977.4 [968.7, 1,006] | 888.5 [877.8, 895.8] |
| `GeneratedBlockVector`, 4 workers, 1,000,000 | 180.8 [173.7, 305.6] | 102.3 [90.45, 118.0] |
| `GeneratedBlock`, 4 workers, 1,000,000 | 400.3 [345.9, 400.3] | 302.2 [294.3, 415.4] |
| `GeneratedEach`, 4 workers, 1,000,000 | 370.7 [352.4, 417.7] | 437.4 [371.5, 488.7] |

Most of these cases vary more between processes than within a process. One `Get<T>` process in each run averaged 2.97 or 3.39 ms where the others averaged 1.44 to 1.67 ms, as step 4 also recorded. The full run's slow `Get<T>` process also varied within the process, with a standard deviation of about 25% of its mean. The tracked-write rerun median, 95.84 µs, is close to step 7's 96.61 µs, which sits just above the rerun range of 95.33 to 96.57 µs. The write-then-scan rerun range includes step 7's median of 169.2 µs. The four-worker cases at 1,000,000 entities have no stable median across these runs. Across the two runs, `GeneratedBlockVector` processes measured from 90.45 to 305.6 µs.

### Despawn at the step 8 tip and at `8029e7f`

The 100,000-entity despawn took 5,798 µs in the full run, against 3,831 µs when step 8 recorded it. Six one-launch processes interleaved the step 8 tip, `bc62d14`, and `8029e7f` in A-B-B-A-A-B order:

| Commit | Process means, 100,000 entities, µs | Process means, 1,000 entities, µs |
| --- | --- | --- |
| `bc62d14` | 3,480, 3,494, 4,355 | 24.36, 25.79, 24.66 |
| `8029e7f` | 4,726, 4,867, 3,585 | 25.14, 25.12, 24.90 |

Both commits produce the same two kinds of process. In one, every iteration takes about 3.4 to 3.7 ms, with a standard deviation near 50 µs. In the other, iterations spread from about 3.7 ms up to 5.7 to 7.4 ms, with a standard deviation of 500 to 870 µs. All three processes of the full run were of the second kind. The rerun does not show a difference between the commits.

### Bevy on rustc 1.98.0 and 1.99.1

Bevy's earlier reference was built with rustc 1.98.0 and LLVM 22.1.8. Six criterion runs of the `lifecycle` and `movement` targets interleaved rustc 1.98.0 and rustc 1.99.1 in A-B-B-A-A-B order, both with `-C target-cpu=native`. Each cell is the median of three runs with their range.

| Case | rustc 1.98.0 µs | rustc 1.99.1 µs |
| --- | ---: | ---: |
| `bevy_for_loop`, 100,000 | 70.13 [70.01, 70.43] | 115.0 [114.9, 115.0] |
| `bevy_for_each`, 100,000 | 23.19 [23.03, 23.24] | 23.99 [23.08, 24.02] |
| `bevy_contiguous`, 100,000 | 24.09 [23.89, 24.91] | 24.34 [23.93, 24.65] |
| Rust `handwritten`, 100,000 | 20.25 [20.21, 20.28] | 19.92 [19.76, 19.96] |
| Spawn, 100,000 | 4,664 [4,634, 5,094] | 4,886 [4,856, 5,026] |
| Spawn a batch, 100,000 | 3,157 [3,125, 3,178] | 3,421 [3,329, 3,456] |
| Despawn, 100,000 | 9,830 [9,826, 10,110] | 10,161 [10,121, 10,187] |
| `get`, 100,000 | 822.3 [803.7, 824.5] | 816.8 [802.9, 821.1] |

`bevy_for_loop` takes 70 µs with rustc 1.98.0 and 115 µs with rustc 1.99.1, and the ranges do not overlap. For spawn and `get`, the two compilers' ranges overlap. For batch spawn and despawn, they do not. The rerun's Bevy despawn, 9.8 to 10.2 ms, sits below the full run's 12.7 to 13.7 ms on either compiler.

### Bevy traversal and filters

Three further criterion runs of the `matrix` target on rustc 1.99.1. The full list is in `results/2026-11-02/rerun-bevy.csv`.

| Case | Full run µs | Rerun µs |
| --- | ---: | ---: |
| Tracked write, 100,000 | 30.67 [28.96, 62.61] | 27.15 [25.08, 28.93] |
| Traversal, 1 archetype, no payload, 1,000,000 | 637.3 [622.4, 658.4] | 406.4 [402.2, 406.9] |
| Traversal, 16 archetypes, 256 B payload, 1,000,000 | 638.5 [635.8, 675.6] | 436.9 [435.5, 447.6] |

The full run's 62.61 µs tracked write was one run. The 1,000,000-entity traversal ran at about 630 µs in the full run and 400 to 450 µs in the rerun, with the same binary. The step 0 baseline recorded the same two levels, 264 to 728 µs. Archevolt's 1,000,000-entity cases moved as well. `TickWidthBenchmarks`, which runs no Archevolt runtime code, took 1,587 µs for the 64-bit tracked write, against 1,413 µs in step 7. The five-process rerun moved single-thread 1,000,000-entity cases in both directions, for example the named query from 977.4 to 888.5 µs and `GeneratedBlock` from 1,004 to 1,088 µs. The 1,000,000-entity numbers on both sides depend on the state of the machine more than on the code.
