# Step 7: block change summaries and 32-bit ticks

This file records [ROADMAP.md](../ROADMAP.md) step 7. Change-filtered queries now skip whole tables in which nothing was added or changed since the system last ran. The 32-bit tick experiment did not justify a change, and ticks stay 64-bit.

## Change

- **One summary per tracked column per table.** Each table keeps the newest added stamp and the newest changed stamp of every tracked column. Partitioned runs split a table into row ranges that share its summaries.
- **Every stamp raises its summary.** World operations and command playback store their new tick, which is the newest in the world. A row copied into another table raises the target's summaries to the row's own ticks. System writes raise a summary only when it is behind, so partitioned workers do not trade its cache line. Rows leaving a table never lower a summary, so it may cover rows that are gone. That costs a scan and does not change any result.
- **`QueryCursor.NextChanged`.** It wraps `Next` and skips a block when a filtered column's summary is at or before the system's last run. The generator calls it only for queries with `Added<T>` or `Changed<T>` terms, in `[Each]`, `[EachBlock]`, and named-query enumerators. `Next` is unchanged.
- **`[EachBlock]` accepts change filters.** The method receives every block whose table passes the summary check, including rows in it that did not change. `AV011` is retired.
- **Generated `[Each]` raises the summary once per block.** It stamps each row through the column's spans, then calls the method. `TrackedColumn.Write`, `StampedRef.Value`, and generated named-query row properties check the summary on every write. `TrackedColumn.Summary` is public for generated rows, as `Values` and `Changed` already were.

## Conditions

| Setting | Value |
| --- | --- |
| CPU | AMD Ryzen 9 5950X 16-Core Processor |
| OS | Windows 11 Pro 10.0.26300 |
| .NET | SDK 11.0.100-rc.3.26522.148, RyuJIT x86-64-v3, Release |
| BenchmarkDotNet | 0.16.0-preview.2, default job, one launch per process run |
| Power plan | BenchmarkDotNet selects High performance and restores the previous plan |
| Before | `0a4c07f`: `bc62d14`, the tip of step 8, plus this step's benchmark changes only |
| After | `d0da09c`: a working-tree snapshot of this change on `bc62d14` |

Both versions ran in the same dedicated worktree. Each process run used this command:

```powershell
./scripts/baseline.ps1 -Runs 1 -Classes FilterBenchmarks,SparseChangeBenchmarks,TraversalMatrixBenchmarks,ReferenceComparisonBenchmarks
```

The six process runs alternated in the order before, after, after, before, before, after, between 03:46 and 05:10 UTC on 31 October 2026, with the machine otherwise unused. Total CPU use, sampled over 10 seconds before each run, was 8 to 12%. Each timing is the median of three per-process means in microseconds, with their range. `scripts/compare-baselines.ps1` classifies an after median outside the before range as faster or slower. Builds and tests did not run alongside the measurements. Bevy was not rerun.

Runs on the previous evening, with the machine in normal use, took more than twice as long per process and moved by up to 40% in cases whose code did not change. They are not reported.

## Measurements

| Case | Before µs [min, max] | After µs [min, max] | After/before | Verdict |
| --- | ---: | ---: | ---: | --- |
| `FilterBenchmarks.ChangedNone(Count=1000)` | 0.610 [0.609, 0.627] | 0.163 [0.162, 0.179] | 0.27 | faster |
| `FilterBenchmarks.ChangedNone(Count=100000)` | 47.02 [46.90, 48.11] | 0.162 [0.156, 0.166] | 0.00 | faster |
| `FilterBenchmarks.OptionalHalfPresent(Count=1000)` | 1.262 [1.259, 1.284] | 1.258 [1.258, 1.260] | 1.00 | faster |
| `FilterBenchmarks.OptionalHalfPresent(Count=100000)` | 134.4 [125.4, 136.0] | 135.8 [135.5, 135.9] | 1.01 | within |
| `FilterBenchmarks.TrackedWrite(Count=1000)` | 1.153 [1.152, 1.153] | 1.164 [1.157, 1.189] | 1.01 | slower |
| `FilterBenchmarks.TrackedWrite(Count=100000)` | 96.48 [95.85, 96.57] | 96.61 [96.21, 97.33] | 1.00 | slower |
| `FilterBenchmarks.WriteThenChangedAll(Count=1000)` | 2.129 [2.041, 2.311] | 2.120 [2.109, 2.126] | 1.00 | within |
| `FilterBenchmarks.WriteThenChangedAll(Count=100000)` | 169.9 [169.3, 177.6] | 169.2 [168.8, 170.1] | 1.00 | faster |
| `ReferenceComparisonBenchmarks.ArchInline(Count=100000)` | 96.17 [95.06, 109.6] | 105.0 [96.49, 106.9] | 1.09 | within |
| `ReferenceComparisonBenchmarks.GeneratedBlock(Count=100000)` | 84.36 [84.15, 85.34] | 84.52 [84.37, 84.71] | 1.00 | within |
| `ReferenceComparisonBenchmarks.GeneratedBlockVector(Count=100000)` | 20.73 [20.62, 20.94] | 20.68 [20.56, 20.92] | 1.00 | within |
| `ReferenceComparisonBenchmarks.GeneratedEach(Count=100000)` | 97.97 [96.95, 98.00] | 95.96 [95.59, 96.17] | 0.98 | faster |
| `ReferenceComparisonBenchmarks.GeneratedQuery(Count=100000)` | 83.85 [83.59, 84.47] | 83.99 [83.77, 84.58] | 1.00 | within |
| `ReferenceComparisonBenchmarks.Handwritten(Count=100000)` | 84.41 [84.28, 84.83] | 83.89 [83.63, 84.15] | 0.99 | faster |
| `SparseChangeBenchmarks.SparseWriteThenChanged(Count=1000)` | 1.034 [1.026, 1.039] | 0.600 [0.575, 0.638] | 0.58 | faster |
| `SparseChangeBenchmarks.SparseWriteThenChanged(Count=100000)` | 79.57 [58.24, 80.55] | 12.79 [11.50, 12.90] | 0.16 | faster |
| `TraversalMatrixBenchmarks.GeneratedEach(Count=1000, Archetypes=1, Wide=False)` | 1.082 [1.082, 1.095] | 1.092 [1.080, 1.096] | 1.01 | within |
| `TraversalMatrixBenchmarks.GeneratedEach(Count=1000, Archetypes=1, Wide=True)` | 1.085 [1.074, 1.116] | 1.098 [1.075, 1.116] | 1.01 | within |
| `TraversalMatrixBenchmarks.GeneratedEach(Count=1000, Archetypes=16, Wide=False)` | 1.317 [1.298, 1.358] | 1.309 [1.302, 1.334] | 0.99 | within |
| `TraversalMatrixBenchmarks.GeneratedEach(Count=1000, Archetypes=16, Wide=True)` | 1.341 [1.335, 1.342] | 1.330 [1.326, 1.337] | 0.99 | faster |
| `TraversalMatrixBenchmarks.GeneratedEach(Count=100000, Archetypes=1, Wide=False)` | 96.38 [96.37, 98.53] | 96.19 [96.06, 96.20] | 1.00 | faster |
| `TraversalMatrixBenchmarks.GeneratedEach(Count=100000, Archetypes=1, Wide=True)` | 98.89 [98.40, 100.5] | 97.08 [95.83, 102.3] | 0.98 | faster |
| `TraversalMatrixBenchmarks.GeneratedEach(Count=100000, Archetypes=16, Wide=False)` | 97.64 [96.97, 98.09] | 97.16 [97.10, 97.29] | 1.00 | within |
| `TraversalMatrixBenchmarks.GeneratedEach(Count=100000, Archetypes=16, Wide=True)` | 96.63 [96.59, 97.21] | 97.11 [96.61, 97.55] | 1.00 | within |
| `TraversalMatrixBenchmarks.GeneratedEach(Count=1000000, Archetypes=1, Wide=False)` | 1,008 [977.9, 1,015] | 997.7 [992.5, 1,053] | 0.99 | within |
| `TraversalMatrixBenchmarks.GeneratedEach(Count=1000000, Archetypes=1, Wide=True)` | 996.5 [979.8, 1,037] | 983.2 [979.0, 1,018] | 0.99 | within |
| `TraversalMatrixBenchmarks.GeneratedEach(Count=1000000, Archetypes=16, Wide=False)` | 997.9 [997.8, 1,000] | 996.3 [986.9, 997.8] | 1.00 | faster |
| `TraversalMatrixBenchmarks.GeneratedEach(Count=1000000, Archetypes=16, Wide=True)` | 993.0 [989.6, 994.9] | 989.5 [989.2, 990.0] | 1.00 | faster |
| `TraversalMatrixBenchmarks.GeneratedQuery(Count=1000, Archetypes=1, Wide=False)` | 0.998 [0.997, 1.003] | 1.012 [0.995, 1.013] | 1.01 | slower |
| `TraversalMatrixBenchmarks.GeneratedQuery(Count=1000, Archetypes=1, Wide=True)` | 1.009 [0.968, 1.011] | 0.997 [0.968, 1.165] | 0.99 | within |
| `TraversalMatrixBenchmarks.GeneratedQuery(Count=1000, Archetypes=16, Wide=False)` | 1.234 [1.215, 1.250] | 1.232 [1.221, 1.261] | 1.00 | within |
| `TraversalMatrixBenchmarks.GeneratedQuery(Count=1000, Archetypes=16, Wide=True)` | 1.266 [1.253, 1.276] | 1.252 [1.247, 1.254] | 0.99 | faster |
| `TraversalMatrixBenchmarks.GeneratedQuery(Count=100000, Archetypes=1, Wide=False)` | 100.1 [84.06, 104.4] | 84.16 [83.71, 100.00] | 0.84 | within |
| `TraversalMatrixBenchmarks.GeneratedQuery(Count=100000, Archetypes=1, Wide=True)` | 84.05 [83.66, 84.06] | 84.57 [83.90, 89.61] | 1.01 | slower |
| `TraversalMatrixBenchmarks.GeneratedQuery(Count=100000, Archetypes=16, Wide=False)` | 100.8 [85.66, 101.6] | 101.4 [100.5, 101.6] | 1.01 | within |
| `TraversalMatrixBenchmarks.GeneratedQuery(Count=100000, Archetypes=16, Wide=True)` | 86.23 [85.82, 101.4] | 86.06 [85.97, 101.1] | 1.00 | within |
| `TraversalMatrixBenchmarks.GeneratedQuery(Count=1000000, Archetypes=1, Wide=False)` | 867.0 [862.0, 878.8] | 860.5 [852.3, 903.4] | 0.99 | faster |
| `TraversalMatrixBenchmarks.GeneratedQuery(Count=1000000, Archetypes=1, Wide=True)` | 864.5 [844.8, 866.5] | 885.3 [876.1, 890.2] | 1.02 | slower |
| `TraversalMatrixBenchmarks.GeneratedQuery(Count=1000000, Archetypes=16, Wide=False)` | 875.1 [873.1, 885.5] | 889.8 [880.8, 890.3] | 1.02 | slower |
| `TraversalMatrixBenchmarks.GeneratedQuery(Count=1000000, Archetypes=16, Wide=True)` | 880.9 [879.3, 894.0] | 888.6 [873.1, 890.5] | 1.01 | within |

`SparseChangeBenchmarks` spreads tracked particles over 16 archetypes. Each iteration writes the one archetype that carries all four tags, then runs the `Changed<TrackedPosition>` scan. Before this change the scan read every row's tick. Now it reads one table of 16. At 100,000 entities that takes 0.16 of the time, and what remains is the write and the scan of 6,250 rows. At 1,000 entities it takes 0.58, because per-table and dispatch costs are a larger share there. `ChangedNone`, which scans with nothing changed, no longer reads any row: 0.16 µs at both sizes, against 47 µs before at 100,000 entities. The sparse case's before range is wide, 58.2 to 80.6 µs, because one process ran fast. The after range lies well below it either way.

Untracked and dense-change iteration holds. Every `TraversalMatrixBenchmarks`, `ReferenceComparisonBenchmarks`, `TrackedWrite`, `WriteThenChangedAll`, and `OptionalHalfPresent` case is within 2% of its before median, and each of their "faster" and "slower" verdicts sits within that 2%. Unfiltered queries run the same `Next` as before, so those verdicts are run-to-run variance. `ArchInline` is a third-party comparison that runs no Archevolt code. Its 1.09 lies within its before range.

Managed allocation per operation is zero in every measured case, before and after.

## Two regressions found and fixed

The first version of this change checked the summaries inside `QueryCursor.Next`, and generated `[Each]` raised the summary through `TrackedColumn.Write` on every row. Interleaved runs on the idle machine showed two regressions:

- `GeneratedQuery` over 16 archetypes at 1,000 entities was 14 to 17% slower. Every run after was slower than the slowest run before.
- `WriteThenChangedAll` at 1,000 entities was bimodal, about 2.1 µs in some processes and 3.0 µs in others, against a steady 2.1 µs before.

JIT disassembly of the generated `Changed` system body explained both. `Next` is inlined, through the enumerator's `NextBlock`, into every system body that runs a query. Its extra code used up the JIT's inlining budget for the body, so `TickWindow.Contains` stopped being inlined into the row loop. The loop then called it once per row and kept the enumerator's fields on the stack. Fast and slow processes produced the same instructions; only the loop's alignment differed, 32-byte aligned in the fast processes and not in the slow ones.

Moving the check into `NextChanged`, which only change-filtered generated code calls and which the JIT does not inline, restored the old row loop. `[MethodImpl(AggressiveInlining)]` on `Contains` did not: the call stayed. Raising the summary once per block in generated `[Each]` took the per-row summary check out of the tracked write loop. Its Tier1 row loop has one tick store and no bounds checks, and the summary check sits before the loop.

## 32-bit ticks

`TickWidthBenchmarks` runs the per-row work of the generated tracked write and `Changed` scan over native columns. One version uses 64-bit ticks. The other uses 32-bit ticks compared the way Bevy compares them, by wrapping subtraction clamped to `MAX_CHANGE_AGE`, and starts close to the 32-bit wrap. Only the tick width and the comparison differ. The proxy leaves out the periodic clamping pass that real 32-bit ticks need, so it overstates their gain.

Three process runs on the idle machine, at `1e73e43`, between 03:00 and 03:08 UTC on 31 October 2026:

| Case | 64-bit µs [min, max] | 32-bit µs [min, max] | 32/64 |
| --- | ---: | ---: | ---: |
| Tracked write, 100,000 rows | 133.0 [132.7, 134.9] | 133.7 [133.6, 134.0] | 1.01 |
| Write then `Changed` scan, 100,000 rows | 211.7 [207.7, 212.3] | 210.4 [210.4, 211.5] | 0.99 |
| Tracked write, 1,000,000 rows | 1,413 [1,411, 1,414] | 1,371 [1,350, 1,402] | 0.97 |
| Write then `Changed` scan, 1,000,000 rows | 2,261 [2,223, 2,266] | 2,161 [2,157, 2,178] | 0.96 |

At 100,000 rows the two widths are indistinguishable. At 1,000,000 rows the narrower ticks save at most 3 to 4%. Adopting them would add a periodic pass over every tracked column and change the public `ulong` tick type, which this gain does not justify, so ticks stay 64-bit. A run the previous evening, with the machine in use, showed 8 to 13% at 1,000,000 rows. The idle rerun did not reproduce it.

Reproduce with:

```powershell
./scripts/baseline.ps1 -Runs 3 -Classes TickWidthBenchmarks
```
