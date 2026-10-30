# Step 8: write immediate spawns in place

This file records [ROADMAP.md](../ROADMAP.md) step 8. `World.Spawn<TBundle>` and `World.SpawnBatch<TBundle>` no longer stage each bundle in a buffer and copy it into the table. The bundle's `Write` stores each member directly in its destination column.

## Change

- **Two targets for `BundleWriter`.** The writer either fills a staging payload, as before, or writes into one table row through a member-to-column map. In the table form, `Put<T>` stores the value in its column and stamps a tracked member's added and changed ticks. `Complete` still requires every declared member, in order.
- **Append first.** `Spawn` reserves the entity, appends its row, then runs the bundle's `Write` against that row. `SpawnBatch` reserves every entity and the table capacity first, as in step 5, then appends and writes each row in turn. The stack or pooled staging buffer is gone from both.
- **Rollback.** If `Write` or `Complete` throws, `Spawn` truncates the table to its previous count and releases the reservation. `SpawnBatch` already truncated every row of the batch and released every reservation. The change tick advances only after the write succeeds, in both.
- **No re-entry during the write.** The new row is in the table but not in the entity directory while `Write` runs, so another structural change to the same table could move or truncate it. The world now counts as running during the write, and every world operation called from inside `Write` throws through `RequireIdle`. The old staging path finished `Write` before it touched the table, so it did not need this.
- **Member columns on the binding.** `BundleBinding.Columns` holds the bundle archetype's column for each member, computed once when the world is built.
- `Commands.Spawn`, `Commands.InsertBundle`, and `World.InsertBundle` keep the staging path. A command must hold its payload until playback, and an insertion may move the entity first.

## Conditions

| Setting | Value |
| --- | --- |
| CPU | AMD Ryzen 9 5950X 16-Core Processor |
| OS | Windows 11 Pro 10.0.26300 |
| .NET | SDK 11.0.100-rc.3.26522.148, RyuJIT x86-64-v3, Release |
| BenchmarkDotNet | 0.16.0-preview.2, default job, one launch per process run |
| Power plan | BenchmarkDotNet selects High performance and restores the previous plan |
| Before | `1ea88b7`, the tip of step 6 |
| After | `0921d1a`, a working-tree snapshot of this change on `1ea88b7` |

Both versions ran in the same dedicated worktree. Each process run used this command:

```powershell
./scripts/baseline.ps1 -Runs 1 -Classes EntityStorageBenchmarks,EntityPopulationBenchmarks
```

The six process runs alternated in the order before, after, after, before, before, after, between 19:00 and 19:24 UTC on 30 October 2026. Each timing is the median of three per-process means in microseconds, with their range. `scripts/compare-baselines.ps1` classifies an after median outside the before range as faster or slower. Builds and tests did not run alongside the measurements. Bevy was not rerun.

## Measurements

| Case | Before µs [min, max] | After µs [min, max] | After/before | Verdict |
| --- | ---: | ---: | ---: | --- |
| `Spawn(Count=1000)` | 43.10 [41.44, 44.23] | 28.16 [26.49, 28.33] | 0.65 | faster |
| `Spawn(Count=100000)` | 5,404 [5,180, 6,348] | 3,999 [3,755, 4,749] | 0.74 | faster |
| `SpawnBatch(Count=1000)` | 26.73 [26.11, 26.95] | 16.16 [15.47, 17.60] | 0.60 | faster |
| `SpawnBatch(Count=100000)` | 3,193 [3,161, 3,748] | 2,201 [2,150, 2,580] | 0.69 | faster |
| `Despawn(Count=1000)` | 24.32 [24.02, 24.54] | 24.60 [23.80, 24.78] | 1.01 | slower |
| `Despawn(Count=100000)` | 3,669 [3,470, 5,116] | 3,831 [3,674, 4,036] | 1.04 | within |
| `Get(Count=1000)` | 5.969 [5.958, 6.056] | 6.032 [5.534, 6.050] | 1.01 | within |
| `Get(Count=100000)` | 1,362 [1,352, 1,407] | 1,434 [1,317, 1,482] | 1.05 | slower |
| `TagRoundTrip(Count=1000)` | 91.08 [89.69, 91.91] | 88.09 [87.26, 91.29] | 0.97 | faster |
| `TagRoundTrip(Count=100000)` | 9,040 [8,786, 10,260] | 9,232 [8,585, 9,588] | 1.02 | within |
| `WideRoundTrip(Count=1000)` | 95.57 [93.80, 95.88] | 97.93 [89.69, 101.1] | 1.02 | slower |
| `WideRoundTrip(Count=100000)` | 9,557 [9,424, 9,885] | 9,837 [9,730, 10,710] | 1.03 | within |

Both spawn forms gain about a third. Each member is now checked and stored in one pass, where it used to be checked into the buffer by `Put` and then copied out by a second walk in `WriteBundle`. The per-call staging buffer is gone too.

Despawn, `Get`, and the round trips do not run any changed code. Their benchmarks spawn only in `GlobalSetup` or `IterationSetup`, outside the measured operation. Their "faster" and "slower" verdicts sit within 5% of the before median, and every one of them overlaps the other side's range. They are run-to-run variance, not effects of this change.

Managed allocation per operation did not change in any case. `Spawn` allocates 16.05 KB and 1,610.49 KB, `SpawnBatch` 16.05 KB and 1,571.09 KB, and `Despawn` 8.02 KB and 1,024.18 KB, at 1,000 and 100,000 entities.

### Bevy reference, not rerun

`benchmarks/baseline.md` records Bevy 0.20.0 on 22 October 2026 on the same machine. Those medians are historical context only.

| Case | Archevolt after µs | Bevy reference µs |
| --- | ---: | ---: |
| Spawn, 1,000 | 28.16 | 36.46 |
| Spawn, 100,000 | 3,999 | 4,788 |
| Batch spawn, 1,000 | 16.16 | 24.08 |
| Batch spawn, 100,000 | 2,201 | 3,222 |

## Correctness

Two tests are new.

- `Spawn_that_fails_partway_leaves_no_row_or_reservation` spawns a bundle whose `Write` throws after its first member, and a bundle whose `Write` skips its second member so that `Complete` throws. A query afterwards sees only the earlier survivor, and the next spawn reuses the failed index at generation 3, which shows both reservations were released.
- `Bundle_write_cannot_spawn_into_the_world_it_is_writing_to` has a bundle whose `Write` calls `Spawn` on the same world. Both `Spawn` and `SpawnBatch` throw, and the world still holds only its earlier entity.

`Batch_spawn_that_fails_partway_leaves_no_row_or_reservation`, from step 5, passes unchanged. `scripts/verify.ps1` passed every check.

## Gate

| Gate item | Result |
| --- | --- |
| Step 0a's spawn and batch spawn cases improve at 1,000 and 100,000 entities | Met. |
| They allocate no more managed memory | Met. Allocation is unchanged. |
| Spawn, batch spawn, and command tests pass, including a partial write that leaves no row or reservation | Met. |

## Saved evidence

The interleaving script, its run list, the merged before and after summaries, and the comparison are kept locally under `artifacts/step8-in-place-spawn/`. The six raw runs are in the measurement worktree under `artifacts/baseline/20261030-190055` through `20261030-191946`.
