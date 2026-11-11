# Archevolt performance plan

Status: every step is done. Step 0's baselines are in `benchmarks/baseline.md`, recorded on .NET 10. Step 3's before and after numbers, on .NET 11, are in `benchmarks/step3-table-storage.md`. Later steps record fresh before and after comparisons on the same SDK. Step 3 met its gate except for named queries, whose regression came from enumerator codegen. Step 2 rewrote that enumerator and met its gate. Its numbers are in `benchmarks/step2-named-query-enumerator.md`. Step 4 replaced directory references with integers and met the median/range gate after an unchanged-code drift control. Its measurements and the remaining variance in large random reads are in `benchmarks/step4-integer-entity-directory.md`. Step 5 made reservation lock-free, added batch spawn, and met its gate. Its numbers are in `benchmarks/step5-lock-free-reservation.md`. Step 6 cached column maps, interned archetypes by bitmask, and met its gate. Its numbers are in `benchmarks/step6-cached-column-maps.md`. Step 8 made both immediate spawns write bundles in place and met its gate. Its numbers are in `benchmarks/step8-in-place-spawn.md`. Step 7 added per-column change summaries, met its gate, and kept 64-bit ticks. Its numbers are in `benchmarks/step7-block-change-summaries.md`. On 2 November 2026, every benchmark class and every Bevy counterpart ran again at `8029e7f` on an otherwise idle machine, with Bevy rebuilt by rustc 1.99.1. Those numbers are in `benchmarks/results.md` and supersede the Bevy reference in `benchmarks/baseline.md`.

The v1 implementation plan is complete. Every stage passed its acceptance gate, and the evidence is in `.audit/acceptance.md`. The [v1 plan](https://github.com/Stephanvs/archevolt/blob/ebacf07059fefbf9b182e40a290812be584dd9ee/PLAN.md) remains in history as the record of the original design, its alternatives, and its gates.

This completed plan covered the stage after v1. It made Archevolt faster at dense iteration, structural change, and entity lifecycle. Each change is grounded in measured Archevolt costs or in what Bevy changed between v0.17.3, which v1 was designed against, and v0.20.0, the current release.

Archevolt is a research project with no users. Public API, storage layout, generated code, and entity handle shape may change without compatibility shims. Remove superseded code in the same change that replaces it.

## Design decision: Roslyn source generation

Archevolt uses Roslyn source generators to give C# a programming model as close to Bevy's as the language allows. Components, bundles, queries, systems, and schedules are declared with attributes and plain methods. The generator then turns those declarations into the code that runs them. In effect, the attributes work as a domain-specific language embedded in C#.

The generator is also the main performance lever. Anything that can be decided at compile time is decided there and never runs at runtime. That covers query shapes, component access sets, column bindings, system dispatch, conflict analysis, and schedule order. The runtime keeps only what depends on the live entity population and on execution.

Every step in this plan follows that rule:

- Prefer generated, specialised code to runtime lookup, reflection, or generic dispatch.
- Prefer static memory allocation at startup time, and prefer to pool memory instead of allocating memory in the hot paths.
- When a declaration cannot be compiled into the fast form, report a generator diagnostic. Do not fall back silently at runtime.
- Keep the user-facing declarations close to their Bevy equivalent, even when the generated code underneath differs.

## Findings that shape this plan

The findings below describe the starting point before steps 0 to 8. The completed steps and amendments record the changes since then.

Bevy did not change its entity or table layout between v0.17.3 and v0.20.0. `Entity` is still a 32-bit index plus a 32-bit generation. `Entities` is still a dense `Vec<EntityMeta>`. `EntityLocation` is still four `u32` values. Tables still hold one contiguous column per component plus an entity column, growing by doubling. Bevy's gains came from four other places. It added slice-per-table iteration, a lock-free entity allocator separated from entity metadata, single-pass column merging during table moves, and opt-in per-column change summaries.

Archevolt differed from Bevy from the start. It stores components in 16 KiB native chunks, and its entity directory holds object references. Dense iteration never reads the entity directory. Bevy's native `for_each` processes eight entities per iteration with 256-bit loads. Archevolt's loops are scalar, one entity at a time.

No storage measurements are on record yet. No benchmark isolates spawn, despawn, `Get<T>(entity)`, or component add and remove. Directory and lifecycle changes therefore start without a baseline.

## Measurement rules

These rules apply to every step:

- Every change lands with before and after numbers from the same machine, SDK, runtime, and commit range. Record CPU, OS, SDK, runtime, build flags, and benchmark configuration with the results.
- Report the median of at least three separate process runs and their range. Do not report a single run.
- Correctness gates timing. The benchmark driver validates every entity's values before and after measurement. All runtime tests pass unchanged unless the step changes the tested contract.
- A warmed, fixed-capacity steady-state update allocates zero managed memory. Report structural growth, churn, and dispatch costs instead of hiding them.
- Cross-language Bevy numbers are context, not a ranking, unless the workload and compiler settings are controlled. Compare Rust builds with `-C target-cpu=native`, because RyuJIT always targets the host CPU.
- Routine .NET runs may reuse a recorded Bevy reference with its original date and environment. `scripts/baseline.ps1` runs .NET only by default; `-IncludeBevy` refreshes Bevy too. Refresh the reference when the Bevy version, workload, Rust toolchain, hardware, or relevant machine settings change, or when measuring machine drift. Reused results are historical context and must not be reported as newly measured.
- Inspect representative JIT disassembly for row-loop shape, bounds checks, and vectorization whenever a step changes generated loops.

## Step 0a. Lifecycle microbenchmarks

Add BenchmarkDotNet cases for these operations, at 1,000 and 100,000 entities:

- spawn a two-component bundle,
- spawn a batch,
- despawn,
- `Get<T>(entity)` on random live entities,
- add and remove a tag,
- add and remove a wide component.

These cases are the baseline for steps 4 to 6.

Gate: the cases run in the existing benchmark project, validate their results, and have recorded baselines.

## Step 0b. Bevy equivalents

`benchmarks/bevy-reference` covers only the movement case today: `handwritten`, `bevy_for_each`, and `bevy_for_loop`. Add a Bevy 0.20.0 counterpart for every Archevolt case that a later gate measures, unless one already exists:

- each step 0a lifecycle case, using `spawn`, `spawn_batch`, `despawn`, `get::<T>`, and `insert` and `remove`,
- the traversal matrix, with 1 and 16 archetypes and with and without the unread 256-byte payload,
- the filter cases: tracked write, `Changed<T>` with nothing changed, write then `Changed<T>` over everything, and an optional term present on half the entities,
- the movement case through `Query::contiguous_iter_mut`, the counterpart to step 1's slice form.

Mirror the Archevolt workload exactly. Use the same entity counts, the same component sizes and layouts, the same archetype mix, the same initial values, and the same per-row arithmetic. Call `World` directly, not through `Commands`, wherever Archevolt calls `World` directly. Each Bevy case checks its results the way the Archevolt case does. Build with `-C target-cpu=native`.

Where Bevy has no exact equivalent, use the closest form and record the difference beside the case. Examples are a batch-spawn API with different semantics and a change-detection tick of a different width.

Gate: every Archevolt case that a later gate compares with Bevy has a Bevy counterpart. Each counterpart passes a test asserting the same final state as its Archevolt case. Both sides have recorded baselines from the same machine.

## Step 1. Slice-per-storage-block systems

Highest expected gain. Today the system receives one row at a time. `[Each]` calls the user body per row, and the JIT emits scalar code.

Add a generated system form whose body receives whole column spans for one storage block. For example, it gets `Span<Position>` and `ReadOnlySpan<Velocity>`, plus the row count. The user can then write a `Vector256` loop, or a simple indexed loop that the JIT can vectorize. The generator binds the spans once per block and does not touch individual rows.

The form supports dense terms and archetype-level filters only. Change filters and optional terms stay in the row-based forms until step 7 supplies block-level change information. When a query cannot be expressed this way, generation fails with a diagnostic, not a runtime fallback.

Tracked components need block-level tick writes. A mutable span of a tracked component stamps the changed tick for the whole span when the system returns. This matches Bevy's `ContiguousComponentTicksMut` behaviour, which marks all rows as changed.

Parallel execution partitions by storage block, as the restricted `[Each]` partitioning already does.

Precedent: Bevy 0.19 added `Query::contiguous_iter_mut` ([#21984](https://github.com/bevyengine/bevy/pull/21984)). Bevy 0.20 added the parallel form ([#25264](https://github.com/bevyengine/bevy/pull/25264)). Bevy reported `iter_simple` falling from 5.4 to 2.8 µs.

Gate: the 100,000-entity movement case written with `Vector256` and with a plain indexed loop, both compared with `bevy_for_each` under the native target. Disassembly shows vector loads and stores in the row loop.

## Step 2. Named-query enumerator rewrite

The generated named-query enumerator keeps `_row`, `_count`, the entity column, and the column spans as struct fields. Every `Current` copies a `Row` containing two spans, the entity column, and an index. Every property access is a bounds-checked `_columnN[_row]`.

Regenerate the enumerator so the row-advance path inside a block is separate from the transition to the next block. Keep the row loop's state in locals the JIT can register-allocate. Make `Row` a thin `ref struct` that holds a reference to each column's current element, not whole spans. Bounds-check once per block, not once per access.

This step has no Bevy precedent. Bevy's `for` loop has the same per-row shape.

Gate: named query within 1.2 times `[Each]` on the 100,000-entity movement case, and the wide-component case no slower. Disassembly shows no per-row span reconstruction or `Row` copy.

Step 3 showed that the enumerator's speed depends on the PGO profile. When tiered PGO leaves `QueryCursor.Next` out of line, the enumerator stays on the stack and each row costs about 2 ns instead of 1.2 ns. One table per archetype made that the common case. Step 2's gate therefore also requires the named-query traversal cases to beat their chunked numbers in `benchmarks/step3-table-storage.md` at 1,000 and 100,000 entities, under the default runtime configuration.

## Step 3. Table storage with retained capacity

Replace 16 KiB chunks with one growing slab per archetype. An unread wide component no longer shortens the spans a hot query sees. Serial queries see one span per table. Parallel execution keeps logical blocks within a table.

Tables retain their capacity. An emptied table keeps its allocation. A rented or grown allocation is not cleared. Each new row's key, values, and ticks are initialised before the row is published. Add an explicit `World` trim operation that releases retained capacity, because an idle world now holds more native memory.

Delete the chunk pool, the per-chunk `NativeChunk` objects, and `WithChunkBytes` as a storage-size setting.

Precedent: Bevy tables have always been contiguous and keep their capacity. Bevy 0.20 stopped zero-filling ticks in `Table::allocate` ([#25353](https://github.com/bevyengine/bevy/pull/25353)).

Gate: the named query with an unread 256-byte component and the tag add/remove round trip are faster than the chunked baseline. No ordinary query regresses outside the baseline range. Growth, migration, disposal, tracking, and four-worker tests pass.

## Step 4. Integer entity directory

`EntityEntry` holds `Archetype?` and `NativeChunk?` references plus generation, state, and row (`src/Archevolt/Entities/Entity.cs:21-28`). That is about 32 bytes per entity. Every relocation writes two object references and pays two GC write barriers.

Store locations as plain integers: archetype ID, table row, generation, and state. After step 3 a location needs no chunk identity. Keep the directory paged, so pages never move while other threads hold references. Swap-remove repairs one integer row.

Precedent: Bevy's `EntityMeta` holds `Option<EntityLocation>`, four `u32` values with no references. Bevy 0.20 aligned `EntityLocation` to 8 bytes after an LLVM store-forwarding stall ([#26042](https://github.com/bevyengine/bevy/pull/26042)). Check RyuJIT codegen for the same pattern, but do not assume it applies.

Gate: step 0a's `Get<T>`, despawn, and move cases improve or hold. Stale-handle, cross-world, generation-retirement, and swap-removal tests pass.

## Step 5. Lock-free reservation and batch spawn

`EntityTable.Reserve` takes `lock(_gate)` on every spawn, including single-threaded idle spawns (`Entity.cs:50`). `Release` does the same. No batch spawn exists.

Separate allocation from directory metadata, as Bevy 0.18 did. The allocator owns the free list and the next fresh index. The directory owns generations and locations. The owner thread reserves without locking. Concurrent command-buffer reservations use one atomic operation per batch. Add a batch spawn that reserves N indices once, then writes rows directly into the destination table, reserving table capacity up front.

Precedent: Bevy 0.18 split `EntityAllocator` from `Entities` and removed the pending/flush step ([#19451](https://github.com/bevyengine/bevy/pull/19451)). Bevy reported despawn 24% faster and command spawning about twice as fast. Bevy 0.19 made allocation lock-free ([#18670](https://github.com/bevyengine/bevy/pull/18670)), and `spawn_batch` calls `alloc_many` once. Archevolt follows the split but keeps a flush. A command-buffer claim only moves the allocator's cursor, so the owner writes pending claims into the directory before playback, before discarding commands, and when a run exits. The amendments after step 5 record why.

Do not copy Bevy's 128-entry local free cache ([#22658](https://github.com/bevyengine/bevy/pull/22658)). It exists to recover the cost of Bevy's atomic remote allocation, which Archevolt does not have. Add it only if step 0a shows release-side atomics dominate despawn.

Gate: step 0a's spawn and despawn cases improve. Batch spawn beats individual spawn at 100,000 entities. Command reservation, cleanup, and concurrent-reservation tests pass.

## Step 6. Cached column maps and bitmask archetype keys

A structural move runs `Array.BinarySearch` for every source column in `CopyRowTo`. The swap-remove that fills the hole runs them again, even though source and target are the same archetype. `WriteBundle` and `Get<T>` also binary-search the archetype's component IDs. Archetypes are interned by `string.Join(",", ids)` (`src/Archevolt/World.cs:225-234`), and a bundle-less `Spawn()` builds that string on every call.

Compute the column mapping once per archetype transition and cache it on the transition edge. Within one archetype, copy columns index for index with no lookup. Give each archetype a dense component-to-column lookup for `Get<T>` and bundle writes. Intern archetypes by their component bitmask, and cache the empty archetype.

Precedent: Bevy 0.19 replaced per-column lookups during table moves with a single merge-walk over sorted columns ([#23151](https://github.com/bevyengine/bevy/pull/23151)). A cached map goes further, because Archevolt's transition edges already identify the pair of archetypes.

Gate: step 0a's tag and wide-component round trips and `Get<T>` improve. The reference-model trace tests pass.

## Step 7. Block change summaries and 32-bit ticks

Tracked components store two `ulong` ticks per row, 16 bytes. A `Changed<T>` query reads every row's tick, even when nothing in the block changed.

Keep one summary tick per tracked column per table or block, updated on any write. Change-filtered queries skip blocks whose summary predates the system's last run. Step 1's slice systems can then accept change filters at block granularity. Separately, measure 32-bit ticks with Bevy-style wraparound handling against the current 64-bit ticks. Adopt them only if the smaller rows measurably help tracked iteration.

Precedent: Bevy 0.20 added opt-in `#[component(summary_tick)]` ([#25157](https://github.com/bevyengine/bevy/pull/25157)). The first version regressed fragmented iteration 3.6 times, until [#25429](https://github.com/bevyengine/bevy/pull/25429) made the "has summary" check a compile-time constant. Archevolt's generator knows each component's tracking policy, so it emits the summary write only for tracked columns.

Gate: a sparse-change benchmark shows the skip. Untracked and dense-change iteration does not regress. Every change-detection correctness scenario from v1 still passes.

## Step 8. Write immediate spawns in place

`World.Spawn<TBundle>` and `World.SpawnBatch<TBundle>` write each bundle into a staging buffer, on the stack up to 1,024 bytes and rented above that. They then validate the buffer and copy each member into the table through `WriteBundle`. `Commands` needs that buffer to hold the payload until playback. An immediate spawn does not.

Append the row first, then point the `BundleWriter` at the row's destination columns, so the bundle's `Write` stores each member where it will live. The writer still checks member order and types. If `Write` fails, `Spawn` removes the row before releasing the reservation, and `SpawnBatch` truncates every row of the batch before releasing its reservations. `Commands` keeps the staging path.

Step 6 removes the per-member column search from `WriteBundle`. This step removes the staging copy that remains in both immediate spawns, so they share one column-targeting writer.

Precedent: seecs constructs each component in place at the end of its dense array. Bevy's bundle spawner writes each bundle component directly into its table column.

Gate: step 0a's spawn and batch spawn cases improve at 1,000 and 100,000 entities and allocate no more managed memory. Spawn, batch spawn, and command tests pass, including a bundle write that fails partway and must leave no row or reservation behind.

## Sequence

Run steps 0a and 0b first. Run step 3 before steps 1 and 2, because it decides what a storage block is and therefore what spans the generator binds. Step 4 depends on step 3, because a table row replaces the chunk reference. Steps 5 and 6 are independent of each other once step 4 lands. Step 7 comes last, because it extends step 1's slice form. Each step is its own commit or pull request with its measurements.

## Not adopted

- The 8-byte `Entity` handle. Archevolt's 16-byte handle carries a world ID so that handles from another world are rejected. Removing it is a design decision about safety, not a performance fix.
- Resources stored as entities (Bevy 0.19, [#20934](https://github.com/bevyengine/bevy/pull/20934)). It changes Bevy's resource model, not its hot paths.

## Open decisions

- Sparse-set storage for frequently toggled components. Today every insert or remove moves the entity's whole row to another archetype. A tag insert and remove took 8,776 µs for 100,000 entities on 2 November 2026, about 44 ns per operation ([results](benchmarks/results.md#entity-lifecycle)). Each tag combination also splits archetypes. A per-component sparse set, as in seecs, EnTT, and Bevy's `StorageType::SparseSet`, makes add and remove O(1) without moving the row. The cost falls on queries: a term or filter on a sparse component checks membership per row through the sparse array, which breaks the contiguous spans that `[Each]` and `[EachBlock]` rely on. The generator could fix the storage policy per component at compile time, as it does for tracking. Decide once a workload toggles markers often enough for structural moves to dominate.
- Resource values in `[Each]` callbacks. A callback that reads `time.Value.Delta` reloads the resource on every row. `Res<T>.Value` returns a reference into the resource's heap slot, and RyuJIT cannot move that load out of the loop past the stores through the callback's `ref` component. On 5 November 2026, the movement `[Each]` with `Res<FrameTime>` ran 9 to 12% slower than the same body with a constant delta in each of three processes ([resource probe](benchmarks/results/hill-climb/resource-probe-1.md)). Two fixes need a public API change. `[Each]` could accept a resource as a by-value or `in` parameter, which the generator reads once per run. Or `Res<T>.Value` could return a reference into a copy held by the `Res<T>` itself, which changes the property's ref-safety contract. Only code compiled against the new API gains. Deferred on 5 November 2026. It can still be taken as a step of its own.

## Bevy v0.20.0 sources inspected

- [`Query::contiguous_iter_mut`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/system/query.rs#L1552) and [`QueryContiguousIter`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/query/iter.rs#L1139).
- [`Entities`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/entity/mod.rs#L827), [`EntityAllocator`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/entity/mod.rs#L706), and [`EntityLocation`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/entity/mod.rs#L1211).
- [The remote allocator and its local free cache](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/entity/remote_allocator.rs#L905).
- [`spawn_batch` allocation](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/world/spawn_batch.rs#L41).
- [`Table::allocate`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/storage/table/mod.rs#L489) and [`Tables::move_row`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/storage/table/mod.rs#L743).
- [`Column` with its summary tick](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/storage/table/column.rs#L27) and [`AtomicTick::set`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/change_detection/tick.rs#L123).

## Amendments after step 3

The text above predates step 3 and is kept as written. These notes record where it no longer matches the code.

- "Findings that shape this plan" says Archevolt stores components in 16 KiB native chunks. Each archetype now owns one table.
- Step 1 says the system receives one row at a time. `[EachBlock]` landed in PR #5 and now receives one span per table, or per row range in a partitioned run.
- Step 4 says `EntityEntry` holds `Archetype?` and `NativeChunk?` references. It now holds only `Archetype?`, so a relocation pays one write barrier, not two.
- Step 6 says the swap-remove that fills a hole binary-searches every column. Step 3 made it copy index for index within one table. Only `Table.CopyRowTo` still searches.
- "Sequence" runs step 3 before steps 1 and 2. Step 1 landed first. Its generated code survived step 3 unchanged apart from a rename, and it got faster. Step 2 then landed after step 3 and met its gate; see the amendments below and [its measurements](benchmarks/step2-named-query-enumerator.md).
- Partitioned runs now split rows instead of chunks. [step3-partition-granularity.md](benchmarks/step3-partition-granularity.md) shows no measurable regression from that. It shows that a small partitioned run's cost is worker-pool dispatch. A minimum partition size and 64-row boundary rounding were measured and not adopted.

Stale references outside this file:

- `docs/concepts/architecture.mdx` links `.audit/design.md` as the runtime and generated-code contract. That file still describes chunks.
- `benchmarks/benchmark.md` says Archevolt stores components in chunks. The file records a run at `414eb10`, when that was true.

## Amendments after step 2

- Step 2 says to bind each column once per block and bounds-check once per block, which suggests a base ref plus a row index. The generated enumerator instead holds a ref to each column's current element and advances every ref by one element per row. It decrements the remaining-row count after advancing, so the per-row path is a single block ending in the loop's branch. With a conditional advance, tiered PGO chose between two equally weighted block layouts. The top-tested layout ran at about 1.6 ns per row and the bottom-tested one at 0.81 to 0.90 ns.
- `Row` holds a ref to each column's current element, as planned, plus the entity column and row index. `EntityKey` is internal to the runtime, so generated code cannot hold a ref to it, and `Entity` reads stay bounds-checked.
- `TrackedColumn<T>` exposes its `Values` and `Changed` spans, so a generated row can stamp one row per access without bounds-checking both spans.
- Step 2's measurement found that `[Each]` takes 180 to 211 µs with `DOTNET_TieredPGO=0`, instead of about 98 µs, on the 100,000-entity movement case. Its generated loop depends on the PGO profile, as the old named-query enumerator did. The third round below measured it in part.

Follow-up outside the numbered steps: [step3-partition-granularity.md](benchmarks/step3-partition-granularity.md) found that a four-worker run over 1,000 entities costs 14 to 22 µs, almost all of it worker-pool dispatch, against about 1 µs on one worker. Running a batch's single work item on the calling thread, or keeping small partitionable queries serial, targets that cost. It is an absolute cost that only matters in small worlds. Four-worker cases drifted by up to 37% between identical runs on a machine in normal use. On the idle machine on 2 November 2026, a four-worker update of 1,000 entities took 13.9 to 16.2 µs, against 0.22 to 1.08 µs on one worker ([results](benchmarks/results.md#movement-by-size-and-worker-count)).

On 4 November, single-work-item batches gained an owner-thread path that avoids waking idle workers. No row-count threshold was added. [The dispatch and partitioning study](benchmarks/query-partition-threshold.md) records the newer measurements and the inconclusive large-workload regression check.

On 5 November, [a hill-climbing campaign](benchmarks/performance-hill-climb-report.md) measured a worker pool that spins for up to 50 µs before blocking. A four-worker update of 1,000 entities fell from about 19 to about 1.5 µs when updates run back to back. The report also records the CPU cost of spinning and slower short single-item batches, which need a decision before the change counts as accepted. [Its second round](benchmarks/performance-hill-climb-round-2.md) parks spinning workers when a single-item batch starts, which removed that slowdown. It also shortens the `Get<T>` lookup and compiles qualifying `[Each]` callbacks into SIMD kernels, which brought the 100,000-entity movement update from about 96 to about 22 µs.

On 10 November, [a third round](benchmarks/each-kernels.md) moved the `[Each]` row loop into a method of its own and gave it an inlined copy of the callback. With `DOTNET_TieredPGO=0` on the Intel host, that change alone took a wrap callback from 4.93 to 1.54 ns per entity. A callback that forwards to another method stayed at 4.9, and the final state was not measured without PGO. The same round widened the kernels to callbacks with branches, reads across fields, `Min`, `Max`, `Abs`, `Sqrt`, and calls into helpers. On a separate Linux host, a wrap callback over 100,000 entities fell from 2.63 to 0.11 ns per entity when its system first ran on an empty world, and from 0.59 to 0.11 otherwise.

On 11 November, [a fourth round](benchmarks/system-overhead.md) measured what a schedule pays to run a system before the system visits a row. A run prepared every system again after every command flush, so the cost per system grew with the size of the schedule. On the same Linux host, a chain of 1,024 systems that all declare `Commands` fell from 7,074 to 50.5 ns per system per run, and a chain of 256 `[Each]` systems from 59.2 to 24.3 ns. A batch of small systems on several workers still claims its work items one at a time, which the report lists first under what remains.

## Amendments after step 4

- `EntityEntry` now contains only generation, state, archetype ID, and table row. On the measured x64 runtime it shrank from 24 to 16 bytes and no longer contains managed references. The public 16-byte `Entity` handle still includes the world ID.
- Directory pages still hold 1,024 entries and never move. `IsAlive` binds an entry once for generation and state checks, and page lookup uses unsigned slot arithmetic. Swap-removal already repaired only the row after step 3.
- The [step 4 measurements](benchmarks/step4-integer-entity-directory.md) include the initial comparison and a repeat against unchanged code after machine drift affected two cases. Random reads at 100,000 entities hold under the median/range rule, with no demonstrated speedup and substantial process-to-process variance.
- Steps 5 and 6 can now proceed independently.
- Step 6's dense component-to-column lookup has two more call sites than it lists. `ApplyInsert` binary-searches the destination archetype after every insert. `WriteBundle` searches per member on every spawn, although a bundle's destination columns never change, so `BundleBinding` can hold them. `TryGet` also binds the directory entry twice, once in `IsAlive` and once to read the location. At 1,000 entities, where everything is cached, `Get<T>` costs about 8 ns against Bevy's 4.2 ns, so the gap is instructions rather than cache misses.

## Amendments after step 5

- Step 5 says the allocator owns the free list and the next fresh index. `EntityAllocator` owns the free list and the claim cursor. The directory's length is still the next fresh index. A claim past the free list returns an offset from that length, and the owner adds those slots when it flushes. A claim never writes to the directory, so pages grow only on the owner thread.
- Step 5 says concurrent reservations use one atomic operation per batch. Each `Commands.Spawn` uses one `Interlocked.Decrement`. `Commands` has no batch spawn. `World.SpawnBatch` runs on the owner thread and uses no atomic operations.
- The owner flushes pending claims before command playback, before commands are discarded, and when a run exits. Idle-world reservation and release assume that no claim is pending.
- `SpawnBatch` still stages each bundle and binary-searches each member's column, once per entity. Step 6 removes the search. Step 8 originally assumed that batch spawn already wrote in place, so its scope and gate now name `SpawnBatch` too.
- After step 5, at 100,000 entities, batch spawn took 3.7 ms against Bevy's recorded 3.2 ms, and individual spawn 5.9 ms against 4.8 ms. Despawn took 24.66 µs and 4,934 µs at 1,000 and 100,000 entities, against Bevy's recorded 42.57 µs and 10,455 µs. Step 8 changed both spawn figures. [The 2 November results](benchmarks/results.md#entity-lifecycle) have the current ones.

## Amendments after step 6

- Step 6 asks for a dense component-to-column lookup for `Get<T>` and bundle writes. `Archetype.Columns` serves every caller: `Get<T>`, `TryGet<T>`, `WriteBundle`, `ApplyInsert`, and query matching. The amendment after step 4 suggested that `BundleBinding` hold its destination columns. A dense lookup already costs one array read, so the binding does not hold them.
- Transition edges stay in the per-archetype dictionaries, now as `ArchetypeEdge` values that carry the target and its column map. Bundle insertions cache the same edge type per source archetype. A tag or wide round trip still pays two dictionary lookups and two row moves.
- Archetypes derive their sorted component IDs from their masks. A transition flips one bit of a cloned mask instead of building an ID list with LINQ.
- The world creates the empty archetype when it is built, so it always exists, at index 0.
- `TryGet<T>` binds the directory entry once through `EntityTable.TryLocate`. `IsAlive` now calls `TryLocate` too.
- [The step 6 measurements](benchmarks/step6-cached-column-maps.md) interleave before and after process runs instead of running each side in turn. `Get<T>` fell from 7.847 to 5.663 µs at 1,000 entities and from 1,702 to 1,316 µs at 100,000. Both round trips and both spawn cases improved at both sizes. On 2 November 2026, `Get<T>` at 1,000 entities took 5.731 µs, about 5.7 ns per call, against Bevy's 4.038 µs.

## Amendments after step 8

- Step 8 says the `BundleWriter` points at the row's destination columns. The writer keeps its staging form for `Commands` and `InsertBundle` and gains a table form. `BundleBinding.Columns` holds each member's column, so the table form writes without a lookup. The amendment after step 6 said the binding did not need its columns. It needs them now, because `WriteBundle` no longer runs for immediate spawns.
- The new row sits in the table, but not yet in the directory, while user code in the bundle's `Write` runs. The world counts as running for that window, so a `Write` that reaches the same world through other state cannot move or truncate the row. Step 8's text did not anticipate this.
- [The step 8 measurements](benchmarks/step8-in-place-spawn.md) show `Spawn` falling from 43.10 to 28.16 µs at 1,000 entities and from 5,404 to 3,999 µs at 100,000. `SpawnBatch` fell from 26.73 to 16.16 µs and from 3,193 to 2,201 µs. On 2 November 2026, `Spawn` took 27.25 and 3,804 µs and `SpawnBatch` 15.25 and 2,150 µs. Bevy's `spawn` took 38.45 and 5,162 µs and its `spawn_batch` 24.29 and 3,787 µs ([results](benchmarks/results.md#entity-lifecycle)).
- Step 7 was the only step left. It landed last, as "Sequence" says.

## Amendments after step 7

- Step 7 says one summary tick per tracked column "per table or block". Each table keeps two per tracked column, the newest added stamp and the newest changed stamp. Partitioned runs share the table's summaries.
- Every stamp raises its summary. World operations and command playback store their new tick, row copies between tables raise the target to the row's ticks, and system writes raise it only when it is behind. Rows leaving a table never lower it, so a summary may cover rows that are gone. That costs a scan, not a wrong result.
- The skip lives in `QueryCursor.NextChanged`, which the generator calls only for queries with `Added<T>` or `Changed<T>` terms. A first version checked inside `Next`. Inlined through every generated enumerator, the extra code used up the JIT's inlining budget for system bodies, so `TickWindow.Contains` became a call in the change-filtered row loop, and the 16-archetype named query at 1,000 entities ran 14 to 17% slower. `[MethodImpl(AggressiveInlining)]` on `Contains` did not bring the inline back. This is the same sensitivity to inlining and PGO that steps 2 and 3 recorded.
- `[EachBlock]` accepts change filters at block granularity, as step 7 proposed. The method receives every row of a block whose table passes, and `AV011` is retired.
- Generated `[Each]` raises the summary once per block. Raising it through `TrackedColumn.Write` on every row was measurable on the tracked write case. `TrackedColumn.Write`, `StampedRef.Value`, and generated named-query row properties still check it per write. `TrackedColumn.Summary` is public for generated rows, like `Values` and `Changed`.
- 32-bit ticks were measured with a proxy that runs the generated loops over native columns and leaves out Bevy's clamping pass. On 31 October, at 1,000,000 rows, the tracked write took 1,413 µs with 64-bit ticks and 1,371 µs with 32-bit ticks, and the write-then-scan case 2,261 and 2,161 µs. At 100,000 rows the two widths overlapped. Ticks stay 64-bit. On 2 November 2026 the same cases at 1,000,000 rows took 1,587 and 1,512 µs, and 2,479 and 2,311 µs ([results](benchmarks/results.md#tick-width)). Revisit only if tracked iteration over millions of rows becomes a target workload.
- Runs taken while the machine was in normal use moved by up to 40% in cases whose code did not change, and suggested an 8 to 13% gain for 32-bit ticks that an idle rerun did not reproduce. The step 7 numbers come from an otherwise idle machine. Later gates should be measured the same way.
