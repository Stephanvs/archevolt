# Step 0 baseline: Archevolt and Bevy

This file records [ROADMAP.md](../ROADMAP.md) step 0. Step 0a adds BenchmarkDotNet cases for entity lifecycle operations. Step 0b adds a `bevy_ecs` 0.20.0 counterpart for every Archevolt case that a later step compares with Bevy. Later steps report their before and after numbers against the results below.

The .NET cases live in [Archevolt.Benchmarks](Archevolt.Benchmarks). The Rust cases live in [bevy-reference](bevy-reference). [benchmark.md](benchmark.md) describes the dense-movement comparison and the conditions that differ between the two harnesses.

## Cases

| Roadmap case | Archevolt, BenchmarkDotNet | Bevy, criterion | Entities |
| --- | --- | --- | --- |
| Spawn a two-component bundle | `EntityPopulationBenchmarks.Spawn` | `lifecycle/spawn` | 1,000 and 100,000 |
| Spawn a batch | none until step 5 | `lifecycle/spawn_batch` | 1,000 and 100,000 |
| Despawn | `EntityPopulationBenchmarks.Despawn` | `lifecycle/despawn` | 1,000 and 100,000 |
| `Get<T>(entity)` on random live entities | `EntityStorageBenchmarks.Get` | `lifecycle/get` | 1,000 and 100,000 |
| Add and remove a tag | `EntityStorageBenchmarks.TagRoundTrip` | `lifecycle/tag_round_trip` | 1,000 and 100,000 |
| Add and remove a wide component | `EntityStorageBenchmarks.WideRoundTrip` | `lifecycle/wide_round_trip` | 1,000 and 100,000 |
| Traversal matrix | `TraversalMatrixBenchmarks.GeneratedEach` | `traversal/archetypes_{1,16}_payload_{0,256}` | 1,000, 100,000, and 1,000,000 |
| Tracked write | `FilterBenchmarks.TrackedWrite` | `filters/tracked_write` | 1,000 and 100,000 |
| `Changed<T>` with nothing changed | `FilterBenchmarks.ChangedNone` | `filters/changed_none` | 1,000 and 100,000 |
| Write, then `Changed<T>` over everything | `FilterBenchmarks.WriteThenChangedAll` | `filters/write_then_changed_all` | 1,000 and 100,000 |
| Optional term on half the entities | `FilterBenchmarks.OptionalHalfPresent` | `filters/optional_half_present` | 1,000 and 100,000 |
| Movement through per-table slices | none until step 1 | `movement/bevy_contiguous` | 100,000 |

Archevolt has no batch spawn API. Until step 5 adds one, `EntityPopulationBenchmarks.Spawn` is the batch baseline. Step 1 adds the Archevolt slice form. Until then, `ReferenceComparisonBenchmarks.GeneratedEach` is the movement baseline, as step 1's gate states.

## Workloads

Every case calls `World` directly on one thread. Archevolt worlds come from `MatrixPopulation.Build`, which registers `MatrixModule` with a concurrency of one. Bevy schedules use `SingleThreadedExecutor`.

- Particle `i` is `Position(i % 97, 0, 0)` and `Velocity(1, 2, 3)`. Both components are three 32-bit floats.
- `Spawn` spawns particles 0 to `Count - 1`, one call each, into an empty world. Each measured operation gets a new world.
- `Despawn` despawns every entity of a world that holds `Count` particles, in shuffled order. Each measured operation gets a new world.
- `Get` reads `Position` from every entity in shuffled order and returns the sum of `X`.
- `TagRoundTrip` visits every entity in spawn order, inserts the zero-size `MatrixTag0`, then removes it.
- `WideRoundTrip` visits every entity in spawn order, inserts the 256-byte `WidePayload` with value `i`, then removes it.
- The traversal matrix spawns particle `i`, then inserts tag `k` when bit `k` of `i % Archetypes` is set, then inserts `WidePayload` when the payload is on. The measured operation runs the `[Each]` movement system once. The system reads `Position` and `Velocity` only.
- The filter cases spawn `TrackedPosition(i % 97, 0, 0)` and `Velocity(1, 2, 3)`, then insert `Acceleration(0.25)` on even entities. Setup runs the changed scan and the optional scan once each.

The shuffled order is a Fisher-Yates shuffle driven by SplitMix64 from seed 0. `MatrixPopulation.Shuffled` and `lifecycle::shuffled` implement the same sequence. The Rust test `shuffle_matches_the_archevolt_order` pins the permutation that the .NET implementation printed for 10 items.

## Validation

The .NET cases throw from setup or cleanup when the world is wrong, which fails the benchmark:

- `Spawn` checks every entity's `Position` and `Velocity` after each measured operation.
- `Despawn` checks every entity before the operation, and checks that none is alive after it.
- `EntityStorageBenchmarks` checks every entity in global setup, runs each case once, checks the `Get` sum against the sum of `i % 97`, and checks again in global cleanup that no tag or payload remains.

`TraversalMatrixBenchmarks` and `FilterBenchmarks` predate step 0 and do not validate under BenchmarkDotNet. The `--matrix` run checks the filter scan counts.

`cargo test` in `bevy-reference` asserts the same final state for each Bevy counterpart over 1,000 entities. The traversal test also checks every entity's tags and payload. The filter tests check the scan counts and sums.

## Where Bevy differs

These differences remain after mirroring the workload:

- **Change ticks.** Bevy stores 32-bit added and changed ticks for every component and writes them on spawn, insert, and mutable access. Archevolt stores 64-bit ticks for `[TrackChanges]` components only. Of the components here, only `TrackedPosition` is tracked.
- **Spawn.** Building an Archevolt world creates the archetype of every registered bundle. A Bevy world creates the particle archetype and table on its first spawn, inside the measured operation.
- **Batch spawn.** `World::spawn_batch` reserves table storage for the iterator's size hint and allocates all entities with one `alloc_many` call.
- **Storage.** Archevolt stores components in 16 KiB chunks. An unread `WidePayload` shares the chunk, so each chunk holds fewer rows. Bevy stores each component in its own contiguous table column, so the payload does not change the `Position` and `Velocity` columns. Step 3 replaced the chunks with one table per archetype, so this difference no longer holds after it.
- **Entity handle.** Archevolt's `Entity` is 16 bytes and carries a world ID. Bevy's `Entity` is 8 bytes.
- **Slices.** `bevy_contiguous` writes through `ContiguousMut`, which marks every row of the slice as changed.
- **Statistics.** BenchmarkDotNet reports a mean per launch. Criterion reports a mean per run. Both time one operation per fresh world for spawn and despawn, and dispose or drop the world outside the timing.

## Run the baseline

Run from the repository root with PowerShell 7.2 or newer:

```powershell
./scripts/baseline.ps1
```

The default run builds the .NET benchmarks and runs the BenchmarkDotNet cases above and `ReferenceComparisonBenchmarks` with three launches. It does not invoke Rust tools, read Criterion artifacts, or modify saved Bevy results. Results go to `artifacts/baseline/<UTC timestamp>/`. `summary.md` lists the median of the three per-process means and their range and states whether Bevy was measured. `-Runs 5` takes more samples. `-Quick` shortens the selected measurements to check the script, and its numbers are not baselines.

To refresh the Bevy reference alongside .NET, opt in explicitly:

```powershell
./scripts/baseline.ps1 -IncludeBevy
```

This also runs `cargo bench --locked` three times with `-C target-cpu=native`. Criterion runs under the High performance power plan, which BenchmarkDotNet also selects, and the script restores the previous plan. `environment.txt` records the locked `bevy_ecs` version and Rust compiler. The crate pins `bevy_ecs` 0.20.0, the [latest stable Bevy release](https://github.com/bevyengine/bevy/releases/tag/v0.20.0) checked on 28 October 2026. Updating that pin and its lockfile is a separate, deliberate change.

Select a smaller .NET group with `-Classes`. `-Benches` selects Rust targets and requires `-IncludeBevy`:

```powershell
./scripts/baseline.ps1 -Classes ReferenceComparisonBenchmarks
./scripts/baseline.ps1 -IncludeBevy -Classes ReferenceComparisonBenchmarks -Benches movement
```

### Compare with a saved Bevy reference

Keep a completed full run's result directory, including `summary.csv` and `environment.txt`, and compare later .NET-only runs against it:

```powershell
./scripts/compare-baselines.ps1 -Before <saved-full-run> -After <new-dotnet-run>
```

The report compares the .NET measurements and retains the old Bevy medians and ranges as `Bevy reference; not rerun`. It leaves their after values and ratios empty. Those rows keep the original run's date and environment; the new run's summary contains only measurements made in that run. The script matches cases within each harness; use the case mapping above to compare .NET with the corresponding Bevy operation. It does not compute cross-language ratios.

Reuse the reference while the Bevy version, workload, Rust toolchain, hardware, and relevant machine settings remain unchanged. Refresh it when those conditions change or when a controlled comparison needs to measure machine drift. A saved reference cannot establish today's machine drift. The tracked results below and the newer [step 3 results](step3-table-storage.md#against-bevy-after-step-3) also retain Bevy 0.20.0 timings if the raw artifacts are unavailable.

BenchmarkDotNet finds the project to build by searching the directory that holds `Archevolt.slnx`. It stops when two files named `Archevolt.Benchmarks.csproj` exist below that directory, for example in a copy of the repository under `artifacts/`. Run the script from a clean clone in that case.

## Results

Recorded 2026-10-22 by `./scripts/baseline.ps1` with three runs, from a clean clone at `d289549` plus the step 0 changes.

| Setting | Value |
| --- | --- |
| CPU | AMD Ryzen 9 5950X 16-Core Processor |
| OS | Windows 11 Pro 10.0.26300 |
| .NET SDK | 10.0.402, Release |
| Rust | rustc 1.98.0, LLVM 22.1.8, `-C target-cpu=native` |
| Bevy | `bevy_ecs` 0.20.0 |

Each cell is the median of three per-process means in microseconds, with the range in brackets.

### Lifecycle

| Case | Entities | Archevolt | Bevy |
| --- | ---: | ---: | ---: |
| Spawn | 1,000 | 424.1 [423.2–427.6] | 36.46 [36.30–41.86] |
| Spawn | 100,000 | 6,528 [6,487–6,589] | 4,788 [4,705–4,824] |
| Spawn a batch | 1,000 | none | 24.08 [23.04–29.67] |
| Spawn a batch | 100,000 | none | 3,222 [3,202–3,267] |
| Despawn | 1,000 | 332.0 [319.5–334.8] | 42.57 [42.36–42.58] |
| Despawn | 100,000 | 6,611 [6,498–7,161] | 10,455 [10,256–10,793] |
| `Get<T>` | 1,000 | 9.223 [9.156–9.550] | 4.171 [4.146–4.254] |
| `Get<T>` | 100,000 | 2,131 [2,112–2,718] | 832.1 [824.8–840.1] |
| Tag round trip | 1,000 | 285.3 [283.6–287.8] | 121.3 [118.5–123.3] |
| Tag round trip | 100,000 | 29,422 [29,237–29,613] | 10,928 [10,855–11,520] |
| Wide round trip | 1,000 | 308.2 [304.7–318.4] | 134.9 [120.7–135.3] |
| Wide round trip | 100,000 | 31,734 [30,597–32,346] | 11,990 [11,520–12,550] |

### Traversal matrix

| Archetypes | Payload | Entities | Archevolt | Bevy |
| ---: | --- | ---: | ---: | ---: |
| 1 | none | 1,000 | 1.266 [1.261–1.299] | 0.215 [0.212–0.285] |
| 1 | 256 B | 1,000 | 1.593 [1.571–1.759] | 0.211 [0.210–0.219] |
| 16 | none | 1,000 | 1.659 [1.658–1.664] | 0.451 [0.445–0.469] |
| 16 | 256 B | 1,000 | 1.940 [1.924–1.951] | 0.480 [0.479–0.481] |
| 1 | none | 100,000 | 123.1 [122.6–124.5] | 23.73 [23.72–23.82] |
| 1 | 256 B | 100,000 | 184.5 [183.1–187.7] | 23.91 [23.83–24.14] |
| 16 | none | 100,000 | 121.3 [119.8–122.4] | 27.62 [26.88–29.17] |
| 16 | 256 B | 100,000 | 202.4 [201.2–203.7] | 28.60 [28.21–28.68] |
| 1 | none | 1,000,000 | 1,589 [1,584–1,600] | 272.2 [264.6–638.8] |
| 1 | 256 B | 1,000,000 | 4,459 [4,089–4,744] | 268.0 [263.4–727.1] |
| 16 | none | 1,000,000 | 1,518 [1,512–1,643] | 392.9 [343.2–532.7] |
| 16 | 256 B | 1,000,000 | 4,708 [4,635–4,729] | 455.2 [421.8–564.2] |

### Filters

| Case | Entities | Archevolt | Bevy |
| --- | ---: | ---: | ---: |
| Tracked write | 1,000 | 1.265 [1.250–1.282] | 0.268 [0.215–0.274] |
| Tracked write | 100,000 | 114.6 [113.7–114.6] | 28.26 [25.56–30.18] |
| `Changed<T>`, nothing changed | 1,000 | 0.669 [0.666–0.683] | 0.368 [0.367–0.368] |
| `Changed<T>`, nothing changed | 100,000 | 49.66 [48.56–50.15] | 29.49 [29.19–29.61] |
| Write, then `Changed<T>` over all | 1,000 | 2.428 [2.248–2.514] | 1.071 [1.046–1.115] |
| Write, then `Changed<T>` over all | 100,000 | 232.3 [217.1–233.3] | 102.2 [101.1–104.4] |
| Optional term on half | 1,000 | 1.387 [1.352–1.411] | 1.121 [1.087–1.127] |
| Optional term on half | 100,000 | 138.9 [133.8–142.1] | 107.4 [104.7–115.6] |

### Movement, 100,000 entities

| Case | Median | Range |
| --- | ---: | ---: |
| Archevolt `Handwritten` | 87.37 | 87.07–87.39 |
| Archevolt `GeneratedEach` | 120.4 | 118.8–122.5 |
| Archevolt `GeneratedQuery` | 207.3 | 205.8–211.0 |
| Arch `ArchInline` | 106.9 | 105.6–109.7 |
| Rust `handwritten` | 20.68 | 20.52–20.81 |
| Bevy `bevy_for_each` | 24.85 | 23.60–25.66 |
| Bevy `bevy_contiguous` | 25.06 | 24.44–26.45 |
| Bevy `bevy_for_loop` | 76.19 | 72.64–79.52 |

BenchmarkDotNet warned that the spawn and despawn iterations run for less than 100 ms. Each of those iterations times one operation on a fresh world, so the per-iteration time cannot be raised without changing the case.
