# Step 5: lock-free reservation and batch spawn

This file records [ROADMAP.md](../ROADMAP.md) step 5. Entity reservation no longer takes a lock. A new `World.SpawnBatch` spawns many bundles with one reservation pass and one table growth.

## Change

`EntityAllocator` now owns the free list and a signed claim cursor. `EntityTable`, the directory, owns generations, states, locations, and its length. The length is also the next fresh index.

- **Owner thread, idle world.** `Spawn`, `SpawnBatch`, `Despawn`, and command playback change the free list and the directory with plain reads and writes. Before this change, both `Reserve` and `Release` took `lock(_gate)`.
- **Systems during a run.** `Commands.Spawn` claims a slot with one `Interlocked.Decrement` of the cursor. A result `n >= 0` takes free-list entry `n`. A result `-k` takes the k-th slot past the directory's end and generation 1. A claim does not write to the directory, so concurrent `IsAlive` and `TryLocate` calls see a claimed slot as not alive, as before.
- **Flush.** The owner records pending claims as `Reserved` directory entries. It also adds the fresh slots and any pages they need. This happens before command playback, before commands are discarded, and when a run exits. Releases only happen on the owner after a flush, so the free list never changes under a claim.
- **`SpawnBatch<TBundle>(ReadOnlySpan<TBundle>, Span<Entity>)`.** The batch reserves every handle up front, reusing free slots before fresh ones, and adds directory pages once. It grows the destination table once, appends each row, and writes each bundle through one staging buffer for the whole batch. All rows share one change tick. If any bundle's `Write` throws, the appended rows are truncated, every reservation is released with a new generation, and the handle span is cleared. Directory entries become `Alive` only after every row has been written.

Steps 6 and 8 remove the per-member column search and the staging copy that `SpawnBatch` still performs.

## Conditions

| Setting | Value |
| --- | --- |
| CPU | AMD Ryzen 9 5950X 16-Core Processor |
| OS | Windows 11 Pro 10.0.26300 |
| .NET | SDK 11.0.100-rc.3.26522.148, runtime 11.0.0-rc.3.26522.148, RyuJIT x86-64-v3, Release |
| BenchmarkDotNet | 0.16.0-preview.2, default job, three launches |
| Power plan | BenchmarkDotNet selects High performance and restores the previous plan |
| Before | `ccb54e80aa686d23bdd250fa94dc9eafcbd5660d` |
| After | The same commit with this change applied |

Both versions ran one after the other, in the same dedicated worktree, with this command:

```powershell
./scripts/baseline.ps1 -Classes EntityPopulationBenchmarks
```

Each timing is the median of three per-process means in microseconds, with their range. `scripts/compare-baselines.ps1` classifies an after median outside the before range as faster or slower. Builds and tests did not run alongside the measurements. Bevy was not rerun.

`SpawnBatch` reads bundles that `GlobalSetup` prepared in an array. Bevy's `spawn_batch` builds each bundle from an iterator inside the measurement. Each measured invocation gets a fresh world, as the `Spawn` case does. `IterationCleanup` checks that every handle holds its particle.

## Measurements

Runs started at 20:19 and 20:20 UTC on 29 October 2026.

| Case | Before µs [min, max] | After µs [min, max] | After/before | Verdict |
| --- | ---: | ---: | ---: | --- |
| `Spawn(Count=1000)` | 52.47 [51.49, 57.29] | 48.55 [48.53, 51.26] | 0.93 | faster |
| `Despawn(Count=1000)` | 31.13 [30.32, 33.29] | 24.66 [24.40, 25.16] | 0.79 | faster |
| `Spawn(Count=100000)` | 6,361 [6,089, 6,421] | 5,905 [5,791, 5,967] | 0.93 | faster |
| `Despawn(Count=100000)` | 5,986 [5,591, 6,769] | 4,934 [4,337, 6,570] | 0.82 | faster |
| `SpawnBatch(Count=1000)` | | 32.50 [31.37, 34.97] | | new |
| `SpawnBatch(Count=100000)` | | 3,737 [3,727, 3,756] | | new |

Batch spawn takes 0.67 times as long as individual spawn at 1,000 entities and 0.63 times at 100,000.

The 100,000-entity despawn case is noisy on both sides. Its per-iteration times in the after run ranged from 3.3 to 9.8 ms. Its after median falls below the before range, but its after range overlaps the before range.

Managed allocation per operation:

| Case | Before | After |
| --- | ---: | ---: |
| `Spawn(Count=1000)` | 16.05 KB | 16.05 KB |
| `SpawnBatch(Count=1000)` | | 16.05 KB |
| `Despawn(Count=1000)` | 8.20 KB | 8.02 KB |
| `Spawn(Count=100000)` | 1,610.49 KB | 1,610.49 KB |
| `SpawnBatch(Count=100000)` | | 1,571.09 KB |
| `Despawn(Count=100000)` | 1,024.36 KB | 1,024.18 KB |

Spawn allocations are the directory pages. Batch spawn allocates the outer page array once rather than once per new page. Despawn allocations are free-list growth. The world in these cases reserves no entity capacity.

### Bevy reference, not rerun

`benchmarks/baseline.md` records Bevy 0.20.0 on 22 October 2026 on the same machine, with rustc 1.98.0 and `-C target-cpu=native`. Those medians are historical context only.

| Case | Archevolt after µs | Bevy reference µs |
| --- | ---: | ---: |
| Spawn, 1,000 | 48.55 | 36.46 |
| Spawn, 100,000 | 5,905 | 4,788 |
| Batch spawn, 1,000 | 32.50 | 24.08 |
| Batch spawn, 100,000 | 3,737 | 3,222 |
| Despawn, 1,000 | 24.66 | 42.57 |
| Despawn, 100,000 | 4,934 | 10,455 |

## Correctness

Existing tests cover command reservation across directory page growth with four parallel producers. They also cover cancellation of an unplayed spawn after playback fails, and discarding a failed producer's reservation. Three tests are new:

- `Batch_spawn_reuses_free_slots_before_growing_the_directory_across_pages` checks the slot order, generations, values, and `Added` ticks of a 2,500-entity batch that reuses two freed slots and adds three directory pages.
- `Batch_spawn_that_fails_partway_leaves_no_row_or_reservation` has the third of three bundles throw from `Write`. It checks that no row remains in the table, the handle span is cleared, and the released slots are reused. It also checks that a span length mismatch is rejected.
- `Parallel_producers_claim_free_slots_before_fresh_ones_and_discarded_claims_return_them` has four parallel producers claim 100 slots after 40 despawns. One producer fails the first run. The test checks that claims take free slots before fresh ones, and that discarded claims stay dead. After a successful second run, the 100 spawned indices must be exactly the 40 freed slots plus 60 fresh ones. An idle spawn after the run must not alias a claimed slot.

The concurrency tests and the batch tests also ran 60 times in a loop with no failures.

`scripts/verify.ps1` passed every check: all 164 tests (89 runtime, 75 generator), headless Archeoids, package contents, the JIT and NativeAOT package consumers, the NativeAOT particle sample, and the warmed allocation checks. The steady-state traversal forms allocated zero managed bytes.

## Gate

| Gate item | Result |
| --- | --- |
| Step 0a spawn and despawn cases improve | Met at both sizes. |
| Batch spawn beats individual spawn at 100,000 entities | Met: 3,737 against 5,905 µs. |
| Command reservation, cleanup, and concurrent-reservation tests pass | Met. |

## Saved evidence

Raw logs, environment records, per-process JSON, summaries, and the comparison are preserved locally under `artifacts/step5-lock-free-reservation/`. The before run is in `20261029-201923` and the after run in `20261029-202023`. The verification log is `verify.txt`.
