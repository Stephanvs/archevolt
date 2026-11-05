# Performance hill climb, 5 November 2026

This report executes [performance-hill-climb-plan.md](performance-hill-climb-plan.md). One change is committed on the branch and needs your decision before it counts as accepted. Worker-pool threads now spin for up to 50 µs before they block. A four-worker update of 1,000 entities fell from about 19 to about 1.5 µs, and partitioned batches up to 100,000 rows run 1.1 to 14 times faster. The change costs CPU time while workers spin, and short single-item batches that start right after a parallel batch ran slower in every dispatch sweep. The plan sends a change with cross-workload regressions to you, so [Open decisions](#open-decisions) lays out both costs.

No generator change is retained. For the shapes inspected, generated row loops compile to the same Tier-1 instructions as the handwritten loop, and the remaining per-row gap comes from the user callback. No public API, generated public member, or consumer source changed.

[results/hill-climb](results/hill-climb) holds the summaries, raw per-process means, disassembly excerpts, and [the experiment ledger](results/hill-climb/ledger.tsv).

## Conditions

| Setting | Value |
| --- | --- |
| Campaign baseline | `b882d90`, clean tree |
| Candidate commit | `3dbb7eb`, pending your decision |
| CPU | AMD Ryzen 9 5950X 16-Core Processor |
| OS | Windows 11 Pro 10.0.26300, High performance power plan |
| .NET | SDK 11.0.100-rc.3.26522.148, runtime 11.0.0-rc.3.26522.148, x64 |
| BenchmarkDotNet | 0.16.0-preview.2, in-process toolchain, one launch per process |
| Environment variables | No `DOTNET_` or `COMPlus_` variables, except where a run names one |

The machine was in use, so the plan's idle-host condition was not met. Without a benchmark running, the processor performance counter read 15 to 19% total CPU, mostly Prepar3D, ProSimA322-System, Microsoft Teams, and a browser. The 2 November checkpoint recorded 8 to 15%. The `CpuLoadBefore` column that `ab-compare.ps1` records read 37 to 100% and disagreed with the performance counter sampled at the same time, so it is not used here.

## Method

The campaign preserved the complete baseline build output: runtime, generator, benchmark assembly, generated sources, and every consumer assembly. A forced rebuild with `-p:EmitCompilerGeneratedFiles=true` reproduced the baseline `Archevolt.dll` and `Archevolt.Benchmarks.dll` hashes exactly. Every candidate was built the same way and preserved next to it, so each comparison runs the exact binaries of both sides, generated code included.

Two scripts were added for this.

- [scripts/ab-compare.ps1](../scripts/ab-compare.ps1) runs BenchmarkDotNet in-process from two preserved outputs, one process at a time, in A-B-B-A order. It reports the per-process mean, the median and range per side, and the managed allocation. `baseline.ps1` cannot do this, because it rebuilds the current checkout.
- [scripts/api-compat.ps1](../scripts/api-compat.ps1) runs ApiCompat in strict mode, so additions fail as well as removals. It checks `Archevolt.dll` and the consumer assemblies that carry generated public members: the benchmarks, the tests, Archeoids, the browser sample, and the particle sample. A probe member added to `QueryCursor`, and a probe property added to every generated query, made it fail ([api-compat-probe.txt](results/hill-climb/verification/api-compat-probe.txt)).

A third script, [scripts/gapped-dispatch.ps1](../scripts/gapped-dispatch.ps1), times one schedule run at a time with a busy or sleeping gap before each run. BenchmarkDotNet calls a schedule back to back, which is the best case for a spinning pool. The gapped runs show the other cases and the CPU time the process uses.

The acceptance gate was fixed before any candidate ran. The primary family had to be at least 5% faster in two independent batches, outside the baseline range. Guards could not regress by more than 3% reproducibly. Steady-state managed allocation had to stay zero. Tests and the API check had to pass.

### Noise

The baseline measured against itself, three processes per side, gives the noise for the movement family ([noise-movement](results/hill-climb/noise-movement/summary.md)). Single-worker cases at 1,000 and 100,000 entities spread 2 to 16% between processes. One of them, the vector block at 100,000 entities, came out "faster" at 0.95 with ranges that do not touch, on identical binaries. Four-worker cases spread 10 to 40%. Cases at 1,000,000 entities spread up to 47%.

## Candidate: spin before blocking in the worker pool

`WorkerPool` used one monitor for everything. After the owner pulsed a new batch, each worker woke from `Monitor.Wait`, reacquired the lock, and claimed work. The owner then slept in `Monitor.Wait` until the last worker pulsed it. A four-worker batch therefore paid for at least two operating-system wakeups. For a small batch, those wakeups cost far more than the rows. The 4 November study measured 22 to 27 µs for a partitioned batch of 1,000 rows, against 0.7 to 3.6 µs for the same work on one thread.

Commit `3dbb7eb` keeps the monitors and adds a bounded spin in front of each wait.

- A worker that finishes a batch spins for up to 50 µs while it watches the batch epoch, then blocks as before.
- The owner spins for up to 50 µs while workers are still pending, then blocks as before. It reads the clock only when a worker is pending, so a single-item batch pays nothing.
- The epoch and stop flag that spinning workers read sit in a 384-byte padded struct. The owner writes the other pool fields on every `Run`, even for a single-item batch, and those writes no longer invalidate the lines the workers are spinning on.
- A pool with more threads than `Environment.ProcessorCount` never spins. The check covers one pool. Two worlds that each fit the machine still spin together, and other processes are not considered.

Cancellation, exception aggregation, owner and worker overlap, reuse, and disposal use the same code as before.

### Movement, back to back

Three independent batches compared the baseline with the candidate. Batches 1 and 2 measured the first version of the change. Batch 3 measured the fixed version, E2b in the ledger. The commit renames E2b's constant, condition, and padded struct without changing values or layout. A gapped screen of the renamed build against E2b measured 1.30 against 1.30 µs back to back and 31.4 against 30.8 µs after a 2 ms sleep ([final-vs-E2b](results/hill-climb/final-vs-E2b/summary.md)). No full batch ran on the renamed build. Each cell is the median of three process means in microseconds, with the range in brackets ([E2-b1](results/hill-climb/E2-b1/summary.md), [E2-b2](results/hill-climb/E2-b2/summary.md), [final-movement](results/hill-climb/final-movement/summary.md)).

| Case, 4 workers | Batch 1 | Batch 2 | Batch 3, E2b |
| --- | --- | --- | --- |
| `[Each]`, 1,000 | 20.77 [19.17, 27.22] to 1.27 [1.26, 1.78] | 21.84 [21.24, 24.30] to 1.58 [1.55, 2.32] | 19.44 [19.12, 19.69] to 1.55 [1.50, 1.65] |
| `Vector256` block, 1,000 | 19.84 [19.23, 21.12] to 1.08 [1.04, 1.33] | 20.61 [20.34, 25.43] to 1.44 [1.21, 1.57] | 18.94 [18.94, 19.00] to 1.14 [1.08, 1.23] |
| `[Each]`, 100,000 | 81.41 [77.18, 87.90] to 27.95 [27.21, 59.38] | 78.40 [76.83, 86.62] to 54.01 [52.79, 62.81] | 74.86 [74.86, 77.63] to 54.97 [53.93, 58.10] |
| `Vector256` block, 100,000 | 34.80 [29.40, 35.49] to 7.98 [7.85, 16.40] | 38.54 [25.33, 42.13] to 15.23 [12.55, 17.51] | 32.45 [29.14, 33.90] to 14.54 [12.75, 15.81] |

Every primary case is faster in every batch, with ranges that do not overlap. No case allocates. At 1,000,000 entities the four-worker results hold no direction. `[Each]` overlaps the baseline in batches 1 and 3 and ran faster in batch 2, 770 against 621 µs. The vector block overlaps in all three. A dedicated million-row vector check, four processes per side, measured 293 [164, 539] µs against 272 [175, 443] µs ([E2-large](results/hill-climb/E2-large/summary.md)). It neither shows nor rules out a change, like the 4 November follow-up.

The four-worker named query runs on one thread. It stayed within range at every size in batch 3, 1.03, 1.01, and 0.96 times baseline.

### Movement with gaps

`[Each]` over 1,000 entities with four workers, 2,000 timed runs per process, three processes per side, baseline against E2b ([final-gapped.csv](results/hill-climb/final-gapped.csv)). "Cores busy" is the process CPU time divided by wall time over the timed loop, gaps included.

| Before each run | Baseline µs | E2b µs | Cores busy, baseline | Cores busy, E2b |
| --- | ---: | ---: | ---: | ---: |
| Nothing | 15.3 [14.7, 18.7] | 1.3 [1.3, 1.3] | 1.93 | too short to measure |
| Owner busy for 20 µs | 18.2 [16.1, 19.5] | 1.3 [1.3, 1.4] | 1.31 | 3.57 |
| Owner busy for 200 µs | 19.0 [18.5, 19.0] | 9.2 [9.0, 9.4] | 1.10 | 1.85 |
| Owner sleeps for 2 ms | 47.7 [47.6, 48.1] | 31.8 [31.1, 31.9] | 0.006 | 0.019 |

A batch that starts within 50 µs of the previous one wakes nobody. A batch after a longer gap still wakes the workers, but the owner no longer sleeps while it waits for them, which saves 10 to 16 µs. With 20 µs of owner work between batches, the three workers spin the whole time, and the process uses 3.6 cores instead of 1.3.

### Dispatch sweep

`./scripts/measure.ps1 -Mode Partition -RuntimeDefaults` ran 120 cases per process, three processes per side. The cases are three kernels, direct and partitioned, at 0 to 1,000,000 rows, with one and four workers ([E2b-partition](results/hill-climb/E2b-partition/comparison.txt)). Every process passed the correctness and zero-allocation checks. Selected four-worker cases, in microseconds:

| Rows | Partitioned `[Each]` | Partitioned indexed block | Partitioned `Vector256` block |
| ---: | --- | --- | --- |
| 100 | 17.79 to 1.28 | 17.18 to 1.52 | 17.41 to 1.43 |
| 1,000 | 21.00 to 3.55 | 19.21 to 2.97 | 18.60 to 1.86 |
| 4,096 | 21.79 to 7.11 | 19.86 to 4.14 | 18.94 to 2.45 |
| 16,384 | 39.77 to 23.77 | 25.24 to 10.07 | 21.04 to 4.75 |
| 100,000 | 158.07 to 139.15 | 71.30 to 46.14 | 46.83 to 25.15 |
| 1,000,000 | 1360 to 1359, overlap | 553 to 546, overlap | 292 to 313, overlap |

The first version of the change failed this sweep ([E2-partition](results/hill-climb/E2-partition/comparison.txt)). Direct batches at 0 rows went from 0.12 to 0.15 µs, and direct batches at 100 rows took 1.7 to 2.3 times as long. The commit fixes two causes. The owner read the clock on every batch, even with no worker pending. The spinning workers polled fields on the cache line that the owner writes on every `Run`. After both fixes, direct batches at 0 to 100 rows match the baseline or beat it. Longer direct batches did not fully recover, as the next section describes.

## Open decisions

The plan accepts a change only when no guard regresses reproducibly by more than 3%, and it sends cross-workload regressions to you. Commit `3dbb7eb` is on the branch so you can measure or drop it. It is not an accepted improvement until you decide.

**CPU time while workers spin.** Workers spin for up to 50 µs after every parallel batch. A frame with several parallel batches now keeps the workers busy between them. On a laptop or a shared machine that costs power, and it is visible in a CPU monitor. The 50 µs window is `s_spinBeforeBlockingTicks` in `WorkerPool`. A smaller window keeps more of the 20 µs gap case and less of the 200 µs one. The check against `Environment.ProcessorCount` covers one pool, so two worlds that each fit the machine spin together. Machines with few cores and processes with several pools were not measured.

**Short single-item batches right after a parallel batch.** The dispatch sweep rotates direct and partitioned schedules over one world, so a direct batch often starts while workers still spin from the partitioned one. In each of the three sweeps, at least one short four-worker direct case ran slower with separate ranges:

| Sweep | Case | Baseline µs | Candidate µs |
| --- | --- | ---: | ---: |
| Default runtime | `[Each]`, 1,000 rows | 3.59 [2.98, 3.60] | 3.89 [3.71, 4.54] |
| `DOTNET_TieredPGO=0` | `[Each]`, 1,000 rows | 2.30 [2.29, 2.38] | 2.81 [2.77, 2.84] |
| `DOTNET_TieredPGO=0` | `Vector256` block, 4,096 rows | 1.28 [1.15, 1.32] | 1.79 [1.76, 2.06] |
| `DOTNET_TieredPGO=0` | `Vector256` block, 16,384 rows | 3.65 [3.52, 4.73] | 5.43 [5.42, 6.58] |
| Tiered compilation off | Indexed block, 1,000 rows | 1.24 [1.20, 1.38] | 1.54 [1.42, 1.60] |

Single-worker cases, which run none of the changed code, moved by up to 16% between sides in the same sweeps, in both directions. These four-worker signals are larger and repeat across configurations, so treat them as a real cost. The likely mechanism is three spinning workers sharing cores or power budget with the owner during its direct batch. That mechanism is not proven. The isolated checks used the named query, not these kernels, so they do not clear this workload. The four-worker named query stayed within range in separate processes ([E2b-query](results/hill-climb/E2b-query/summary.md)). A direct named query timed right after a partitioned `[Each]` ran no slower, at 26.8 against 27.0 µs for 32,768 rows ([E2b-interleave.csv](results/hill-climb/E2b-interleave.csv)).

**Mid-size direct batches under default PGO.** With default runtime settings, direct indexed-block and `[Each]` batches at 16,384 to 100,000 rows ran 1.3 to 1.5 times slower, three of them outside the baseline range ([E2b-partition](results/hill-climb/E2b-partition/comparison.txt)). With `DOTNET_TieredPGO=0` the same cases overlap the baseline ([E2b-partition-nopgo](results/hill-climb/E2b-partition-nopgo/comparison.txt)), and with tiered compilation off they match it or beat it ([E2b-partition-controlled](results/hill-climb/E2b-partition-controlled/comparison.txt)). These batches last far longer than the 50 µs spin. The evidence points at tiered PGO compiling the shared kernels differently once partitions really run on four threads.

**Single-worker `[Each]` at 1,000,000 entities.** Batch 3 measured 1,033 against 1,069 µs, 3.5% slower with separate ranges. Batch 1 showed the same direction and batch 2 the opposite. That path starts no worker thread, and the baseline measured against itself spread 47% on this case. It is listed here so that a later run can check it.

## Rejected experiments

| Id | Hypothesis | Change | Result |
| --- | --- | --- | --- |
| E1a | Finishing a batch wakes idle workers through `PulseAll` and makes them queue on the gate lock. | Lock-free countdown, with the owner waiting on its own monitor. | `[Each]` at 1,000 rows and four workers measured 15.40 against 14.64 µs, and the vector block 13.99 against 14.58 µs. Process ranges of 13 to 27 µs overlapped completely ([E1a-b1](results/hill-climb/E1a-b1/summary.md)). The wakeups themselves were the cost, which E2 then removed. |
| D1 | `Get<T>` pays for an out-of-line `TryGet` call and a copy through its out parameter. | `Get` and `TryGet` share one inlined lookup that returns a reference. Same checks and messages. | 6.47 against 6.75 µs at 1,000 entities, 1,508 against 1,518 µs at 100,000, five processes per side, all overlapping ([D1-b1](results/hill-climb/D1-b1/summary.md)). The JIT inlined the whole lookup into the benchmark loop and nothing got faster, so the call was not the cost. |

The D1 patch includes a test for the failure paths of `Get` and `TryGet`, which no existing test covers. It passed against the unchanged runtime. It was dropped with the experiment and is kept in the campaign artifacts if you want it.

## Plateaus

Each subsystem in the plan was inspected through warmed Tier-1 disassembly before any change was made. [disassembly.md](results/hill-climb/disassembly.md) has the listings. They cover the shapes the benchmarks exercise: a two-term movement `[Each]` with a resource, a tracked two-term `[Each]`, a one-filter `Changed<T>` named query, and `Get<T>`. Callbacks with more terms, `Entity` parameters, optional terms, or branches, and fragmented worlds, were not inspected. The conclusions below hold for the inspected shapes only.

**Scalar `[Each]`.** The generated movement loop and the handwritten loop compile to the same instructions, with no bounds checks, except that the generated loop reloads the resource value on every row. A throwaway `[Each]` system with a constant delta ran 86.5 to 108.5 µs over three processes, against 96.7 to 120.5 µs for the same body reading `Res<FrameTime>`. Within each process the resource version was 9 to 12% slower ([resource-probe-1](results/hill-climb/resource-probe-1.md)). The callback reads `time.Value.Delta` on every row, and RyuJIT cannot move that load out of the loop past the stores through `ref Position`. Removing it needs `Res<T>.Value` to return a reference into a local copy. That changes the property's ref-safety contract, so it is a public API change, and it would pay off only for code compiled against the new contract. It is not proposed.

**Tracked writes and change filters.** The tracked `[Each]` row is the movement kernel plus one 8-byte stamp store, with no checks. The `Changed<T>` scan's row is a tick load, two compares, and the benchmark's own double-precision add, whose four-cycle dependency chain bounds the scan. Neither leaves generated overhead to remove under the current contract.

**Named-query enumerator.** Step 2 already reduced the row loop to register-only ref advances. The remaining 82 against 94 µs split between processes comes from how tiered PGO lays out that loop. Step 2 measured regressions from earlier variants of this loop, and no new mechanism appeared, so it was left alone.

**Entity lookup.** `TryGet<T>` runs about 70 instructions on its hit path: the owner-thread check, the directory lookup, the tag check, and four bounds-checked internal arrays. At 1,000 cached entities, `Get` is limited by instruction throughput at about 29 cycles. At 100,000 random entities it is limited by two cache misses and about seven dependent loads. D1 removed the call and changed nothing. Further gains need unchecked internal indexing or a wider directory entry, which trade memory safety or cache footprint for a few nanoseconds.

**Per-block and per-run overhead.** A block transition costs about 14 ns, against about 12 ns per archetype in the 2 November Bevy numbers. A single-system schedule run costs about 0.1 to 0.2 µs. Removing a third of either changes the affected cases by about 5%, which is inside this machine's noise.

## Compatibility

ApiCompat in strict mode reports no change for `Archevolt.dll`, `Archevolt.Benchmarks.dll`, `Archevolt.Tests.dll`, `Archeoids.dll`, `Archeoids.Browser.dll`, and `Archevolt.Particles.dll` at `3dbb7eb`. The test assembly and the benchmark assembly compiled at `b882d90` ran against the new `Archevolt.dll` without recompiling. The tests passed 102 of 102. The benchmark's validated movement workloads completed, with one worker and with four. [verification](results/hill-climb/verification) has the ApiCompat output, the probe run, and the binary compatibility run.

## Verification

`./scripts/verify.ps1` passed all nine stages at `d4389a5`, with nothing skipped. That commit has the same `src` and `scripts` trees as `608ce3a`, which only moved the documentation edits into `3dbb7eb`. The stages were restore and build, 176 tests, headless Archeoids, pack, package contents, the JIT and NativeAOT package consumers, the NativeAOT particle sample, and the allocation smoke check. The run before it failed both consumer stages before they started, because a leftover MSBuild node from an earlier run held `ILCompiler.Build.Tasks.dll` open. The full rerun followed `dotnet build-server shutdown`. The scheduling tests ran 25 more times at `d4389a5` without a failure ([scheduling-repeats.txt](results/hill-climb/verification/scheduling-repeats.txt)). The same repeat loop ran on each earlier candidate, but only its failure count was kept. The browser and WebAssembly target was built but not run in a browser.

## Next

The largest remaining gaps to the 2 November Bevy numbers are in per-row arithmetic. LLVM vectorizes the scalar Bevy loops and RyuJIT does not. Closing that gap for arbitrary callbacks is out of scope for this plan. The next candidates are:

- Ending the spin when the owner starts a single-item batch. That targets the short-batch cost directly, and gives up the fast start only for a parallel batch that follows a single-item one.
- A narrower spin window, if the CPU cost matters more than the gaps between batches.
- A test of the short-batch mechanism with the owner pinned to one core and its sibling hyperthread kept idle.

## Reproduce

```powershell
dotnet build Archevolt.slnx -c Release -warnaserror --no-incremental -p:EmitCompilerGeneratedFiles=true
# Copy artifacts/bin/Archevolt.Benchmarks/release for each side, then:
./scripts/ab-compare.ps1 -Baseline <baseline> -Candidate <candidate> -Launches 3 `
    -Filter '*MovementBenchmarks.GeneratedEach*,*MovementBenchmarks.GeneratedBlockVector*,*MovementBenchmarks.GeneratedQuery*' `
    -Iterations '--warmupCount 6 --iterationCount 15 --iterationTime 250' -Output artifacts/ab/movement
./scripts/gapped-dispatch.ps1 -Baseline <baseline> -Candidate <candidate> -Output artifacts/ab/gapped
./scripts/measure.ps1 -Mode Partition -Runs 3 -RuntimeDefaults -NoBuild `
    -BaselineAssembly <baseline>/Archevolt.Benchmarks.dll -Output artifacts/ab/partition
# Copy artifacts/bin before the change, then after building the change:
dotnet tool install --tool-path artifacts/tools Microsoft.DotNet.ApiCompat.Tool
./scripts/api-compat.ps1 -Baseline <copied artifacts/bin>
```
