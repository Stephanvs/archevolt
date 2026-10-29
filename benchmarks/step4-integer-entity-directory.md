# Step 4: integer entity directory

This file records [ROADMAP.md](../ROADMAP.md) step 4. Each entity directory entry now stores an archetype ID, table row, generation, and state. World operations resolve the ID through the world's existing archetype list. Entity lookup queries compare IDs directly.

The directory retains its 1,024-entry pages. Growing the outer page array does not move an entry. Swap-removal already repaired only the moved entity's row after step 3, and still does. Reservation locking and the public entity handle are unchanged.

## Conditions

| Setting | Value |
| --- | --- |
| CPU | AMD Ryzen 9 5950X 16-Core Processor |
| OS | Windows 11 Pro 10.0.26300 |
| .NET | SDK 11.0.100-rc.3.26522.148, runtime 11.0.0-rc.3.26522.148, RyuJIT x86-64-v3, Release |
| BenchmarkDotNet | 0.16.0-preview.2, default job, three launches |
| Power plan | BenchmarkDotNet selects High performance and restores the previous plan |
| Before | `be03359af8dac99de15d3526dac78c3c094323f3` |
| After | The same commit with this change applied |

Both versions ran in the same dedicated worktree with the same benchmark sources and command:

```powershell
./scripts/baseline.ps1 -Classes EntityPopulationBenchmarks,EntityStorageBenchmarks
```

Each reported timing is the median of three per-process means in microseconds, with their range. `scripts/compare-baselines.ps1` classifies an after median inside the before range as unchanged. The desktop remained in normal use; builds and tests did not run alongside the measurements. Bevy was not rerun because step 4's gate compares Archevolt before and after.

## Directory layout and JIT code

On this x64 runtime, `Unsafe.SizeOf<EntityEntry>()` falls from 24 to 16 bytes. `RuntimeHelpers.IsReferenceOrContainsReferences<EntityEntry>()` changes from true to false. This saves 8 KiB per full directory page, or 784 KiB across the 98 pages needed for 100,000 entities. Page headers and the outer page array are unchanged. `Entity` remains 16 bytes and retains its world identity.

BenchmarkDotNet measures the corresponding reduction during spawn:

| Entities | Before managed bytes | After managed bytes | Saved bytes |
| ---: | ---: | ---: | ---: |
| 1,000 | 24,632 | 16,440 | 8,192 |
| 100,000 | 2,451,960 | 1,649,144 | 802,816 |

`Get` allocates zero managed bytes. Tag and wide-component round trips still allocate 48 bytes per entity. Despawn allocations remain 8,392 bytes at 1,000 entities and 1,048,944 bytes at 100,000 entities. Those structural costs are included in the measurements.

A separate three-second probe repeatedly moved 4,096 entities between archetypes, read their values, checked the final sum, and despawned them. It collected optimized Tier-1 listings with:

```powershell
$env:DOTNET_JitDisasm = 'Archevolt.World:Move Archevolt.EntityTable:*'
$env:DOTNET_JitDisasmOnlyOptimized = '1'
$env:DOTNET_JitStdOutFile = '<output path>'
```

The original `Relocate` calls `CORINFO_HELP_ASSIGN_REF` to write the archetype reference. The integer version writes the archetype ID and row as separate 32-bit stores. The inspected relocation path does not combine those stores with a wider reload, so this change adds no explicit alignment workaround.

An initial integer-directory version retained repeated page lookups during `IsAlive`. Its `Get` medians were 8.328 and 1,844 microseconds at 1,000 and 100,000 entities, both above the before ranges. The final implementation binds the directory entry once for generation and state checks. It also uses unsigned division and remainder for nonnegative slot indices, allowing shifts and a mask in the page lookup.

The probe's optimized standalone `Entry` method shrinks from 82 to 56 bytes of machine code. `IsAlive` shrinks from 174 to 102 bytes, and `Relocate` from 98 to 64 bytes. These listings establish the lookup and write-barrier changes; the lifecycle benchmarks below establish their timing effect.

## Correctness

The existing storage trace exercises migration, swap-removal, component values, and stale handles against an independent dictionary model. Existing tests also cover cross-world rejection, reservation cancellation, and concurrent reservation across page growth.

Two additional tests cover gaps in the step's acceptance gate:

- Entity lookup through `QueryState.TryLocate` after migration, exclusion by a tag, swap-removal, slot reuse, and directory page growth.
- Despawn at `uint.MaxValue` generation retires the slot while preserving the entity moved by swap-removal. This test seeds the terminal generation through reflection, then exercises public world operations, without adding a runtime test hook.

The final `scripts/verify.ps1` run passed every check: all 160 tests, headless Archeoids, package contents, JIT and NativeAOT package consumers, the NativeAOT particle sample, and warmed serial and four-worker allocation checks. Both traversal forms allocated zero managed bytes in the steady-state checks.

An earlier verification run encountered an old compiler DLL held open in the consumer package cache. `dotnet build-server shutdown` released it before the successful full run.

## Measurements

The first full comparison uses the before run at 11:42 UTC and the final implementation at 12:07 UTC on 29 October 2026. Eight cases meet the original range gate. Random reads at 100,000 entities and wide-component moves at 1,000 exceed it. Those cases require the additional control below.

| Case | Before µs [min, max] | After µs [min, max] | After/before |
| --- | ---: | ---: | ---: |
| `Spawn(Count=1000)` | 51.925 [51.541, 52.214] | 50.707 [49.838, 51.768] | 0.98 |
| `Despawn(Count=1000)` | 29.835 [29.574, 29.940] | 29.388 [28.030, 32.849] | 0.99 |
| `Spawn(Count=100000)` | 6,741.349 [6,599.948, 6,770.036] | 6,379.654 [6,152.770, 6,665.671] | 0.95 |
| `Despawn(Count=100000)` | 5,505.054 [4,916.640, 6,220.927] | 4,609.925 [4,583.647, 5,085.289] | 0.84 |
| `Get(Count=1000)` | 8.109 [8.086, 8.276] | 8.001 [7.965, 8.024] | 0.99 |
| `TagRoundTrip(Count=1000)` | 98.364 [98.132, 99.885] | 98.606 [96.261, 99.773] | 1.00 |
| `WideRoundTrip(Count=1000)` | 104.767 [104.746, 104.779] | 114.205 [102.768, 115.847] | 1.09 |
| `Get(Count=100000)` | 1,667.920 [1,651.527, 1,677.724] | 1,777.200 [1,777.130, 1,944.020] | 1.07 |
| `TagRoundTrip(Count=100000)` | 10,255.161 [10,073.660, 10,575.894] | 10,087.495 [10,000.157, 10,263.618] | 0.98 |
| `WideRoundTrip(Count=100000)` | 11,210.550 [11,108.842, 11,561.318] | 10,607.163 [10,584.830, 10,830.522] | 0.95 |

## Drift control and repeat

A CPU snapshot during the noisy full pass showed 29% total utilization. To check drift, the runtime was restored to `be03359` in the measurement worktree, and the two affected workloads were repeated at both entity counts. The unchanged-code medians differed from the first baseline by 3 to 5%. Its 100,000-entity `Get` range widened from 1,652 to 1,678 microseconds to 1,708 to 1,868 microseconds.

The tested implementation then ran the same focused command immediately after the control. Both runs used the default BenchmarkDotNet job and three launches:

```powershell
./scripts/baseline.ps1 -Classes EntityStorageBenchmarks.Get,EntityStorageBenchmarks.WideRoundTrip
```

| Case | Unchanged control µs [min, max] | Final repeat µs [min, max] | After/before |
| --- | ---: | ---: | ---: |
| `Get(Count=1000)` | 8.370 [8.264, 8.400] | 8.094 [8.056, 8.254] | 0.97 |
| `WideRoundTrip(Count=1000)` | 107.839 [106.579, 112.518] | 103.121 [102.570, 104.040] | 0.96 |
| `Get(Count=100000)` | 1,723.345 [1,708.108, 1,867.667] | 1,802.389 [1,761.490, 2,454.263] | 1.05 |
| `WideRoundTrip(Count=100000)` | 11,782.588 [11,340.354, 12,132.908] | 10,963.888 [10,792.302, 11,016.339] | 0.93 |

The large random-read case holds only under the median/range rule: its median falls inside the unchanged-code control range. Its third process was substantially slower. These results establish no speedup for that case and do not establish stable latency under desktop load. The report retains both comparisons so the control does not conceal the initial failures.

## Gate

| Gate item | Result |
| --- | --- |
| Random `Get<T>` improves or holds | Met under the median/range rule. Faster at 1,000 entities; within the control range at 100,000, with the variance noted above. |
| Despawn improves or holds | Met at both sizes in the full comparison. |
| Tag and wide-component moves improve or hold | Met. Tag round trips stay within the original ranges. Both wide-component cases are faster in the focused repeat. |
| Stale-handle, cross-world, generation-retirement, and swap-removal correctness | Met. All 85 runtime and 75 generator tests pass. |
| Warmed fixed-capacity updates allocate zero managed memory | Met in serial and four-worker verification. |

## Saved evidence

Raw logs, environment records, per-process JSON measurements, and summaries are preserved locally under `artifacts/step4-integer-directory/<run>/`:

| Run | UTC start, 29 October 2026 | Source | Directory name |
| --- | --- | --- | --- |
| Before A | 11:42:36 | `be03359` | `20261029-114236` |
| Initial candidate | 11:57:09 | Integer locations with the original page lookup | `20261029-115709` |
| Final implementation, full pass | 12:07:27 | This change | `20261029-120727` |
| Unchanged-code control | 12:23:02 | `be03359` runtime | `20261029-122302` |
| Final implementation, focused repeat | 12:33:18 | This change | `20261029-123318` |

The layout probe, its source, and its JIT listings are in `artifacts/step4-probe/`. Verification logs are in `artifacts/logs/`. All timed runtime source files in the measurement worktree were checked against the implementation checkout by SHA-256 before cleanup.
