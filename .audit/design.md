# Runtime and generated-code contract

The implementation starts from Claude's candidate 3. Candidate 1 contains access probes. Candidate 2 contributes bounded column access. These artifacts are in `E:/Code/archevolt-work/arena-contract`.

The runtime owns entity allocation, native storage, query caches, parameter binding, command playback, and execution. The generator emits typed declarations and direct system calls. Generated code stays in the user's namespace and partial owner.

## Storage

An entity contains its world identity, slot, and generation. A paged entity table records free, reserved, alive, and retired states. A frozen registry assigns world-local component IDs. Archetypes contain canonical sorted component IDs and native structure-of-arrays chunks. Migration copies retained values and ticks, initializes additions, and repairs the moved entity after swap removal.

## Generated access

`SystemBody` holds bindings for one system instance. `SystemBinder` validates bindings against the descriptor. `SystemRun` is a runtime-created ref struct carrying the instance, invocation epoch, tick interval, and assigned chunk range.

`QueryState` belongs to one instance. The owner refreshes its matching-archetype cache before dispatch. `Begin(SystemRun)` validates the lease and returns a cursor without mutating the cache. A chunk exposes bounded readonly spans, bounded untracked writable spans, and tracked accessors that stamp before returning writable references.

Column binding checks component identity, access mode, and tracking policy. Equal byte sizes do not establish type identity. Span lengths come from live chunk counts. Callers cannot supply arbitrary lengths.

Invocation epochs belong to system instances and advance after all partitions join. Completing one system cannot expire another system's lease.

## Scheduling

The portable planner compiles into both runtime and generator. It orders explicit dependencies first, then orients conflicts forward in that order. Per-query access clauses retain required and excluded sets. Resource and message conflicts remain global. Both executors consume the same batches and deferred-command barriers.

Bound schedules own local state, reader cursors, query bindings, command buffers, and successful-run ticks. A world owns the reusable worker pool. Native storage is released only after workers join.

## Rejected details

Candidate 3's unchecked row access and caller-sized spans are excluded. Its size-only type checks are insufficient. Query cache refresh cannot run concurrently in partition workers. A world-wide epoch increment after each system would invalidate other active systems.

These corrections preserve the chosen ownership model without adopting candidate 2's extra generic query-layout layer.
