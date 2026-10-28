# Archevolt performance plan

Status: steps 0, 1, and 3 done. Step 0's baselines are in `benchmarks/baseline.md`, recorded on .NET 10. Step 3's before and after numbers, on .NET 11, are in `benchmarks/step3-table-storage.md`. They are the baseline for later steps. Step 3 met its gate except for named queries, whose regression comes from enumerator codegen that step 2 rewrites. The v1 implementation plan is complete. Every stage passed its acceptance gate, and the evidence is in `.audit/acceptance.md`. The [v1 plan](https://github.com/Stephanvs/archevolt/blob/ebacf07059fefbf9b182e40a290812be584dd9ee/PLAN.md) remains in history as the record of the original design, its alternatives, and its gates.

This plan covers the next stage. It makes Archevolt faster at dense iteration, structural change, and entity lifecycle. Each change is grounded in measured Archevolt costs or in what Bevy changed between v0.17.3, which v1 was designed against, and v0.20.0, the current release.

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

Precedent: Bevy 0.18 split `EntityAllocator` from `Entities` and removed the pending/flush step ([#19451](https://github.com/bevyengine/bevy/pull/19451)). Bevy reported despawn 24% faster and command spawning about twice as fast. Bevy 0.19 made allocation lock-free ([#18670](https://github.com/bevyengine/bevy/pull/18670)), and `spawn_batch` calls `alloc_many` once.

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

## Sequence

Run steps 0a and 0b first. Run step 3 before steps 1 and 2, because it decides what a storage block is and therefore what spans the generator binds. Step 4 depends on step 3, because a table row replaces the chunk reference. Steps 5 and 6 are independent of each other once step 4 lands. Step 7 comes last, because it extends step 1's slice form. Each step is its own commit or pull request with its measurements.

## Not adopted

- The 8-byte `Entity` handle. Archevolt's 16-byte handle carries a world ID so that handles from another world are rejected. Removing it is a design decision about safety, not a performance fix.
- Resources stored as entities (Bevy 0.19, [#20934](https://github.com/bevyengine/bevy/pull/20934)). It changes Bevy's resource model, not its hot paths.

## Bevy v0.20.0 sources inspected

- [`Query::contiguous_iter_mut`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/system/query.rs#L1552) and [`QueryContiguousIter`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/query/iter.rs#L1139).
- [`Entities`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/entity/mod.rs#L827), [`EntityAllocator`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/entity/mod.rs#L706), and [`EntityLocation`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/entity/mod.rs#L1211).
- [The remote allocator and its local free cache](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/entity/remote_allocator.rs#L905).
- [`spawn_batch` allocation](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/world/spawn_batch.rs#L41).
- [`Table::allocate`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/storage/table/mod.rs#L489) and [`Tables::move_row`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/storage/table/mod.rs#L743).
- [`Column` with its summary tick](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/storage/table/column.rs#L27) and [`AtomicTick::set`](https://github.com/bevyengine/bevy/blob/v0.20.0/crates/bevy_ecs/src/change_detection/tick.rs#L123).
