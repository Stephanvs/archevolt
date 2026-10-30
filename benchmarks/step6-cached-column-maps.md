# Step 6: cached column maps and bitmask archetype keys

This file records [ROADMAP.md](../ROADMAP.md) step 6. Structural moves, bundle writes, inserts, and `Get<T>` no longer binary-search an archetype's component IDs. Archetypes are interned by their component bitmask instead of a joined string.

## Change

- **Dense column lookup.** Each `Archetype` holds `Columns`, one entry per registered component, with the component's table column or -1. `Get<T>`, `TryGet<T>`, `WriteBundle`, `ApplyInsert`, and query matching read it instead of calling `Array.BinarySearch`.
- **Column maps on transition edges.** An add or remove transition caches an `ArchetypeEdge`: the target archetype and, for each source column, the target column or -1. A bundle insertion caches the same edge per source archetype in `BundleBinding.Destinations`. `Table.CopyRowTo` copies through that map with no lookup. Swap-removal already copied index for index within one table, so it is unchanged.
- **Bitmask interning.** The world keys archetypes by their `ulong[]` component mask. A transition clones the source mask and sets or clears one bit. A bundle destination ORs two masks. An archetype derives its sorted IDs from its mask. The LINQ ID lists that transitions built on a cache miss are gone.
- **Empty archetype.** The world creates the empty archetype when it is built, so `Spawn()` no longer builds a key string on every call.
- **One directory read in `TryGet`.** `EntityTable.TryLocate` checks the handle and returns the archetype ID and row from one entry binding. `IsAlive` calls it and discards the location.

## Conditions

| Setting | Value |
| --- | --- |
| CPU | AMD Ryzen 9 5950X 16-Core Processor |
| OS | Windows 11 Pro 10.0.26300 |
| .NET | SDK 11.0.100-rc.3.26522.148, RyuJIT x86-64-v3, Release |
| BenchmarkDotNet | 0.16.0-preview.2, default job, one launch per process run |
| Power plan | BenchmarkDotNet selects High performance and restores the previous plan |
| Before | `8a9dd22`, the tip of step 5 |
| After | The same commit with this change applied |

Both versions ran in the same dedicated worktree. Each process run used this command:

```powershell
./scripts/baseline.ps1 -Runs 1 -Classes EntityStorageBenchmarks,EntityPopulationBenchmarks
```

The six process runs alternated in the order before, after, after, before, before, after, between 21:00 and 21:19 UTC on 29 October 2026. Earlier steps ran three launches of one side and then three of the other. Interleaving spreads machine drift across both sides. Each timing is the median of three per-process means in microseconds, with their range. `scripts/compare-baselines.ps1` classifies an after median outside the before range as faster or slower. Builds and tests did not run alongside the measurements. Bevy was not rerun.

## Measurements

| Case | Before µs [min, max] | After µs [min, max] | After/before | Verdict |
| --- | ---: | ---: | ---: | --- |
| `Get(Count=1000)` | 7.847 [7.695, 8.090] | 5.663 [5.629, 5.751] | 0.72 | faster |
| `Get(Count=100000)` | 1,702 [1,698, 1,801] | 1,316 [1,315, 1,324] | 0.77 | faster |
| `TagRoundTrip(Count=1000)` | 90.92 [90.11, 104.0] | 86.64 [85.62, 88.72] | 0.95 | faster |
| `TagRoundTrip(Count=100000)` | 9,095 [8,965, 9,364] | 8,700 [8,513, 8,707] | 0.96 | faster |
| `WideRoundTrip(Count=1000)` | 96.73 [96.32, 98.02] | 92.30 [92.14, 93.46] | 0.95 | faster |
| `WideRoundTrip(Count=100000)` | 10,210 [9,973, 10,250] | 9,182 [9,086, 9,446] | 0.90 | faster |
| `Spawn(Count=1000)` | 47.30 [46.20, 47.80] | 42.33 [41.23, 42.68] | 0.89 | faster |
| `Spawn(Count=100000)` | 5,856 [5,598, 6,046] | 5,140 [5,064, 5,179] | 0.88 | faster |
| `SpawnBatch(Count=1000)` | 29.47 [29.27, 31.01] | 26.39 [25.89, 27.26] | 0.90 | faster |
| `SpawnBatch(Count=100000)` | 3,647 [3,594, 3,713] | 3,227 [3,208, 3,240] | 0.88 | faster |
| `Despawn(Count=1000)` | 24.15 [23.66, 25.74] | 23.83 [23.21, 24.14] | 0.99 | within |
| `Despawn(Count=100000)` | 4,679 [3,573, 4,843] | 4,480 [3,563, 4,851] | 0.96 | within |

`Get` gains the most, because it loses both a binary search and a second directory read. The round trips gain less, because each one still pays two row moves, two swap-removals, and two transition-dictionary lookups. Spawn and batch spawn gain from `WriteBundle`'s direct column lookup. Despawn does not touch any changed path.

Managed allocation per operation did not change in any case. `Get`, `TagRoundTrip`, and `WideRoundTrip` allocate nothing. `Spawn` allocates 16.05 KB and 1,610.49 KB, `SpawnBatch` 16.05 KB and 1,571.09 KB, and `Despawn` 8.02 KB and 1,024.18 KB, at 1,000 and 100,000 entities.

### Bevy reference, not rerun

`benchmarks/baseline.md` records Bevy 0.20.0 on 22 October 2026 on the same machine. Those medians are historical context only.

| Case | Archevolt after µs | Bevy reference µs |
| --- | ---: | ---: |
| `Get<T>`, 1,000 | 5.663 | 4.171 |
| `Get<T>`, 100,000 | 1,316 | 832.1 |
| Batch spawn, 100,000 | 3,227 | 3,222 |

## Correctness

The structural reference-model trace test, `Structural_trace_matches_dictionary_across_table_growth_migration_and_swap_removal`, passes unchanged. So does `Membership_extends_beyond_one_machine_word`, which interns archetypes whose masks span two words. `Warmed_structural_moves_allocate_no_managed_memory` still passes.

One test is new. `Bundle_insertion_over_members_already_present_overwrites_them_without_moving_other_rows` inserts a bundle whose members the entity already has. That takes the edge whose target is its own source, writes in place, and must leave a second row in the same table untouched.

`scripts/verify.ps1` passed every check.

## Gate

| Gate item | Result |
| --- | --- |
| Step 0a's tag and wide-component round trips improve | Met at both sizes. |
| `Get<T>` improves | Met at both sizes. |
| The reference-model trace tests pass | Met. |

## Saved evidence

The interleaving script, its run list, the merged before and after summaries, and the comparison are preserved locally under `artifacts/step6-cached-column-maps/`. The six raw runs are in the measurement worktree under `artifacts/baseline/20261029-210050` through `20261029-211644`.
