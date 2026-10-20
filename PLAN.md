# Archevolt implementation plan

Status: implemented. Windows and Linux build/test/package checks and Windows NativeAOT checks pass. The acceptance evidence, performance measurements, and remaining validation limits are recorded in `.audit/acceptance.md`; this document remains the target contract.

Archevolt is a standalone, Bevy-inspired entity component system for .NET 10 and C# 14. Its runtime depends only on the .NET standard library. There are no BRUTAL dependencies, including no dependency on its memory utilities, collections, numerics, or job scheduler.

Use a Roslyn incremental generator to specialize queries, system invocation, bundles, registration, and declarative schedules. Archevolt owns entity allocation, archetype membership, memory, change tracking, and both serial and parallel scheduling.

The generator knows what a system can access. The runtime knows which entities currently have those components. Generate the work known at compilation; retain runtime handling for changing entity populations and actual execution.

This document is the complete plan for the new repository. The project layout below is proposed. Only README.md and LICENSE existed at the inspected initial commit, `059e830ccb04245ff4ec8c1f3d757d2743389596`.

Bevy semantics are based on inspected release **v0.17.3**, commit `706fdd9800c4bf335d67f127ff6e5a93deadabb9`. Bevy is a design reference, not a code or runtime dependency.

## Scope and decisions

- Ship a headless ECS library. Applications own their main loop, rendering, input, physics, and persistence.
- Prefer `readonly record struct` components in examples. Support other unmanaged structs when their layout or update style is useful.
- Require explicit read/write capabilities in query declarations and system parameters. Component immutability does not grant or remove access.
- Generate named query types with concrete, strongly typed rows. Provide `[Each]` for specialized single-query loops.
- Use dense archetype storage in v1. Do not generate every possible component combination.
- Implement the scheduler inside Archevolt. Serial execution is the reference behavior; parallel execution is opt-in.
- Deliver one runtime NuGet package containing the compiler analyzer. Tests and benchmarks remain development dependencies.
- Treat allocation-free steady-state execution, AOT support, and performance as acceptance criteria to verify, not existing results.

## The programming model

The following is the proposed public API. During implementation, compile and run the complete example as a clean package-consumer test. The attributes and generated members shown here are design contracts.

```csharp
using Archevolt;
using System.Numerics;

[Component]
public readonly record struct Position(Vector3 Value);

[Component]
public readonly record struct Velocity(Vector3 Value);

[Component]
public readonly struct Frozen { }

[Resource]
public readonly record struct FrameTime(float DeltaSeconds);

[Bundle]
public readonly partial record struct Particle(Position Position, Velocity Velocity);

[Query]
[Write<Position>]
[Read<Velocity>]
[Without<Frozen>]
public readonly ref partial struct Moving { }

public static partial class Motion
{
    [EcsSystem]
    public static void Integrate(scoped Moving moving, scoped Res<FrameTime> time)
    {
        foreach (var row in moving)
        {
            ref var position = ref row.Position;
            position = position with
            {
                Value = position.Value
                    + row.Velocity.Value * time.Value.DeltaSeconds
            };
        }
    }
}

[EcsModule]
[IncludeSystems(typeof(Motion))]
[IncludeBundle<Particle>]
[IncludeResource<FrameTime>]
public static partial class GameEcs { }

[Schedule]
[Run(typeof(Motion), nameof(Motion.Integrate), Order = 0)]
public sealed partial class Simulation { }

public static class Program
{
    public static void Main()
    {
        using var world = new WorldBuilder().AddModule(GameEcs.Module).Build();
        world.InsertResource(new FrameTime(1f / 60));
        world.Spawn(new Particle(
            new Position(Vector3.Zero),
            new Velocity(Vector3.UnitX)));
        using var simulation = Simulation.Create(world);
        simulation.Run();
    }
}
```

`Moving` is a concrete generated query, with a stack-only enumerator and row. `row.Position` returns `ref Position`; `row.Velocity` returns `ref readonly Velocity`. Component field names can have explicit aliases when simple type names collide. Query declarations are reusable across systems, while each system instance owns its own query state and change cursor.

Generated declarations stay in the namespace of their user-written declaration. Global-namespace declarations remain in the global namespace. Do not introduce a separate generated namespace. Put generated invokers and helpers inside their partial owner where possible.

Systems remain ordinary synchronous methods. Parameters declare access, as in Bevy. A system runs once per schedule invocation and controls its own query loops. Resources are separate from entity components. Applications own the main loop and can run fixed-update, simulation, and render-preparation schedules independently.

For the common single-query update, offer a second declaration that lets the generator emit the entire traversal:

```csharp
public static partial class MotionKernel
{
    [Each]
    [Without<Frozen>]
    public static void Integrate(
        scoped ref Position position,
        scoped in Velocity velocity,
        scoped Res<FrameTime> time)
    {
        position = position with
        {
            Value = position.Value + velocity.Value * time.Value.DeltaSeconds
        };
    }
}
```

Register either example for a given update, not both. `[Each]` invokes the method once per matching entity. `ref T` means write access; `in T` means read access. `Entity` is an optional identity parameter, and resource wrappers bind once per invocation. Reject component parameters passed by value because accidental copies obscure access intent. `[Each]` supports only one traversal and a restricted parameter set; complex algorithms use `[EcsSystem]` and named queries.

The generator calls the user's method directly. It does not copy or rewrite the method body. Generate an invoker inside the same partial owner so private methods also work. Public methods in the example make `nameof` usable in an external schedule declaration; private methods can be selected by a validated constant name or a declaration inside the same owner. V1 rejects overloaded system names within an owner to keep schedule references unambiguous.

## Records and explicit mutable access

Use records for concise value declarations and equality. Keep that separate from system access permission.

| Declaration | Value or reference | Default positional properties | Component policy |
| --- | --- | --- | --- |
| `readonly record struct Position(...)` | Value | Init-only | Recommended when fields are unmanaged |
| `record struct Position(...)` | Value | Mutable get/set | Supported when fields are unmanaged |
| `struct Position` | Value | As declared | Supported when fields are unmanaged |
| `record Position(...)` or `record class Position(...)` | Reference | Init-only by default | Excluded from native component columns |

Readonly declarations provide shallow immutability. Record equality compares members according to their equality implementations; it is not arbitrary deep object-graph equality. Record structs containing references remain managed. These distinctions follow the [C# records reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record).

The storage slot for a readonly value can still be replaced. In the example, `ref var position = ref row.Position` acquires explicit writable access, and `position = position with { ... }` replaces the stored value. An `[Each]` method does the same through `scoped ref Position`. A `Read<Position>` accessor returns `ref readonly Position` and permits neither field mutation nor replacement.

The `with` expression copies the struct value; it does not inherently allocate a managed object for an unmanaged record struct. Large values may still incur copying costs. Derived cached fields can become inconsistent after a `with` update, so calculate derived values on access or maintain them explicitly. See the [C# with-expression reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/with-expression).

`unmanaged` is a storage requirement. Every instance field must satisfy it, including compiler-generated backing fields. Generated equality methods do not make a record's storage managed. See the [C# unmanaged-type reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/unmanaged-types).

Synthesized record-struct equality uses each instance field's `EqualityComparer<T>.Default`. It can be customized. Archevolt must not substitute byte comparison or assume equality has no user-defined cost. See the [record-struct equality specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/structs#16533-equality-members).

Keep change tracking based on writable access. Replacing a component with an equal record value still counts as a write. Do not compare every component on every update. An equality-based `SetIfChanged` may be a later explicit API with its own read-before-stamp contract.

Readonly records also do not provide Rust's borrow checker. Replacing the same component from two systems is still a write conflict. Native pointers, owning handles, mutable statics, and external state require explicit ownership or synchronization beyond component declaration syntax.

## How a query becomes a typed row

The user-written `Moving` declaration is one part of a partial type. The generator emits another part containing `GetEnumerator()`, an `Enumerator`, and a nested `Row`.

`Enumerator.Current` returns `Moving.Row`. Consequently, `foreach (var row in moving)` is statically typed exactly like `foreach (Moving.Row row in moving)`. `Moving` represents many matching entities; its row represents one entity's selected components.

The generated row has this shape, with tracking and lease checks omitted:

```csharp
public readonly ref partial struct Moving
{
    // GetEnumerator() and Enumerator are generated alongside Row.
    public readonly ref struct Row
    {
        private readonly ref Position _position;
        private readonly ref readonly Velocity _velocity;

        internal Row(ref Position position, in Velocity velocity)
        {
            _position = ref position;
            _velocity = ref velocity;
        }

        public ref Position Position => ref _position;
        public ref readonly Velocity Velocity => ref _velocity;
    }
}
```

The row refers directly to component columns. It is a stack-only view, with no per-entity object allocation or component lookup by name. Copying a row copies its references, not the component values. Copying or replacing a component is still ordinary C# value semantics.

The attributes alone do not create members. The generated source joins the compilation, and the C# compiler resolves the properties and enforces their access types. The enumerator uses the `foreach` pattern rather than an interface-based `IEnumerable<T>` path.

## Projects, dependencies, and distribution

Implement everything under Archevolt's own namespace and repository. No BRUTAL adapter or compatibility layer is needed.

| Proposed project | Owns | Dependencies |
| --- | --- | --- |
| `src/Archevolt` | Attributes, world/entities, storage, queries, resources, messages, commands, schedule planning, serial executor, parallel executor | .NET 10 BCL only |
| `src/Archevolt.Generators` | Incremental source generation, diagnostics, generated manifests | Roslyn compiler packages, private to the build; `netstandard2.0` host target |
| `tests/Archevolt.Tests` | Storage, lifecycle, scheduling, and executor conformance | Runtime and a test framework/runner |
| `tests/Archevolt.Generators.Tests` | Generated compilation, diagnostics, incremental behavior, multi-assembly consumers | Generator, Roslyn, test framework |
| `benchmarks/Archevolt.Benchmarks` | Throughput, allocation, build/code-size and executor overhead | Runtime and BenchmarkDotNet |
| `samples/Archevolt.Particles` | Headless consumer exercising the packed package | Archevolt; BCL `System.Numerics` |

Keep internal runtime responsibilities visible in folders without turning each into a public assembly:

```text
src/Archevolt/
  Entities/       identity, reservations, generation and location
  Storage/        registry, archetypes, native chunks, transitions
  Queries/        binding/cache/lease contracts used by generated code
  Systems/        resources, local state, parameter binding
  Commands/       typed buffers and exclusive playback
  Messages/       buffers, cursors, retention
  Scheduling/    plans, conflict analysis, conditions, barriers
  Execution/     serial execution, worker pool, faults, joining
```

The compiler project reads runtime attribute names as metadata. It does not reference or load the runtime assembly. Generated application code does reference the runtime, the BCL, and the application's component types.

Keep the pure schedule-planning algorithm in `shared/Scheduling` and compile the same portable source into the generator and runtime. It accepts normalized descriptors and emits plan data without referencing Roslyn or runtime world types. This avoids two implementations of conflict analysis and barrier insertion, without introducing another runtime assembly or analyzer-loading dependency.

Ship one customer package:

```text
Archevolt.nupkg
  lib/net10.0/Archevolt.dll
  analyzers/dotnet/cs/Archevolt.Generators.dll
```

Roslyn, test, and benchmark packages must not become runtime dependencies. Do not ship host-provided Microsoft.CodeAnalysis assemblies beside the generator or copy them into application output. Package any additional analyzer-private helper dependencies only if required, and test their compiler-host loading. Prefer a self-contained generator assembly beyond host Roslyn APIs.

The generator's `netstandard2.0` target concerns host compatibility; consumers still need the selected C# 14/.NET 10 toolchain. Pin the SDK and development package versions when establishing the build. Verify generator loading from a packed NuGet package in a separate consumer, including referenced library modules.

The serial and parallel executors both belong to `Archevolt`. A generated kernel never depends on a specific vector library, windowing system, or external scheduler.

## What the compiler generates

| Input known at compilation | Generated result | Runtime work that remains |
| --- | --- | --- |
| Component declarations | Typed descriptors and copy/clear operations; tracking policy | Bind world-local IDs; allocate columns |
| Query fields and filters | Concrete row/enumerator, match predicate, read/write sets, typed fetches | Match newly created archetypes and cache column bindings |
| System signature | Parameter binder and direct invoker | Locate resources and persistent system state |
| `[Each]` method | Specialized chunk loop and direct per-row call | Traverse currently matching chunks |
| Bundle declaration | Flattened component list and typed insertion path | Find/create destination archetype and allocate a row |
| Supported command payload | Typed queue encoding and playback entry | Record and apply actual operations at a barrier |
| Module declaration | Explicit registration and versioned metadata manifest | Bind modules to this world and reject duplicates |
| Declarative schedule | Edges, conservative conflicts, batches, barriers, diagnostics | Bind state, evaluate conditions, dispatch/join workers |

For an untracked `Position`, the `[Each]` loop has this shape. This is an explanatory expansion, not the final runtime API:

```csharp
// Resource and matching-archetype bindings are already resolved.
foreach (ref readonly var match in state.Matches.AsSpan())
{
    foreach (var chunk in match.Chunks)
    {
        Span<Position> positions = chunk.Column<Position>(match.PositionSlot);
        ReadOnlySpan<Velocity> velocities = chunk.Column<Velocity>(match.VelocitySlot);
        for (int i = 0; i < chunk.Count; i++)
            MotionKernel.Integrate(ref positions[i], in velocities[i], time);
    }
}
```

There is no component registry lookup, reflection, delegate invocation, interface dispatch, or structural-filter check per entity in this dense path. Column binding and optional-column presence are resolved outside the row loop. Tracked writes add only their required tick stores; `Changed<T>` adds its required row tests.

Generate only declared query shapes and registered system wrappers. Do not emit every generic arity or every combination of components. Reuse identical query shapes within an assembly where this reduces code size without obscuring diagnostics. Emit readable `.g.cs` files with stable names and support `EmitCompilerGeneratedFiles`.

Use `IIncrementalGenerator` and `ForAttributeWithMetadataName`. Convert symbols into small immutable, equatable models before emission. Keep component/query transforms separate from schedule aggregation so an unrelated method-body edit does not regenerate the whole ECS. Resolve symbols semantically, including aliases and fully qualified names. Propagate cancellation and produce diagnostics at the user's declaration.

Source generators add source; they do not rewrite arbitrary user code or reliably consume another generator's output during the same run. Query marker types must therefore exist in user source. One ECS generator derives local descriptors and manifests from the same model. Its graph cannot depend on discovering its own emitted methods afterward.

## Storage and lifecycle contracts

The following invariants make the generated code safe to execute.

| State | Owner and rule |
| --- | --- |
| Entity identity | `Entity` contains a world identity, slot index, and generation. Default is invalid. Worlds never reuse their identity. A slot whose generation would wrap is retired. |
| Entity lifecycle | `Free -> Reserved -> Alive -> Free`, with generation advanced on cancellation or despawn. Public component access requires `Alive`. |
| Location | The world stores each live entity's archetype, chunk, and row. Every swap-remove repairs the moved entity's location. |
| Component identity | Registry assigns dense IDs per world at build time. A generated module binds its types once; IDs are not process-global constants or persistence IDs. |
| Archetype identity | Runtime interns the canonical exact component set. New combinations can appear at any structural barrier. |
| Chunk memory | One owner manages each fixed-capacity allocation. Pointers do not move during execution. Allocation, migration, and reclamation require exclusive structural access. |
| Query state | Bound to a world and system instance. Caches matching archetypes and column slots, plus the last observed archetype generation. |
| System state | Schedule instance owns query bindings, last-run tick, local state, message cursors, and command buffers. |

V1 uses unmanaged value-type components in dense structure-of-arrays chunks. Zero-data tags occupy membership bits without allocating a value column. Tags can still require tick metadata when tracked. Tag value access through `Read<Tag>`, `Write<Tag>`, or `[Each]` refs is rejected; use membership and change filters. Components that own disposable native handles are excluded from automatic lifetime ownership in v1; store non-owning handles and keep ownership in an explicit resource. Byte-copying such a handle must never silently imply transferring ownership.

Start with a configurable target chunk payload around 16 KiB and measure alternatives. Include entity indices, tracked ticks, padding, and column alignment in capacity calculations. Large components still get at least one row. Actual sizes and offsets use runtime `Unsafe.SizeOf<T>()` and target-platform layout during registration, never generator-host `Marshal.SizeOf` constants. Do not field-split arbitrary structs in v1; C# code can depend on their layout and take refs to fields.

Archevolt owns a small native chunk allocator using BCL `NativeMemory` APIs. Pair aligned allocation with `NativeMemory.AlignedFree` and ordinary allocation with its matching free. Keep unsafe operations in storage and generated access internals. Use checked byte-count arithmetic, explicit allocation failure handling, and one owner for each allocation. Core APIs expose entity handles and scoped typed views rather than allocator pointers.

Implement the generation-bearing entity allocator and chunk swap-removal in Archevolt. Use BCL collections for cold metadata where suitable, and generated span/index loops for hot traversal. Do not resize or relocate live component columns under an active execution lease. Archetype/cache growth and entity reservation must preserve this invariant.

Insertion into an existing component replaces its value. Adding/removing components moves the entity once to the resulting archetype, preserves surviving component values and ticks, initializes added components, and repairs the source chunk's moved row. A bundle is an insertion description, never entity identity. Flatten nested bundles and reject repeated component types.

A transition cache maps an archetype plus add/remove set to its destination. Query caches process only new archetypes after a structural generation change; they do not rescan all archetypes each frame. Keep empty archetype records for stable cache IDs while reclaiming empty chunk allocations under a documented capacity policy. Use growable bitsets so the design has no accidental 64-component ceiling.

`World.Dispose` is idempotent and frees all owned storage. Running or externally borrowed worlds cannot be disposed. All public operations except repeated disposal reject a disposed world. Never allow concurrent schedule runs against one world in v1. Different worlds can run independently.

## Queries and C# reference safety

V1 supports required reads/writes, `Entity`, `With<T>`, `Without<T>`, optional reads, and tracked `Added<T>`/`Changed<T>`. Optional read access does not require presence. A query with only optional fields matches all entities. Optional values use a generated `HasX` flag and a guarded readonly accessor, with presence decided per archetype. Defer optional writes and general Boolean filters until the conservative conflict model has corresponding tests.

Structural filters select archetypes. Change filters imply required membership and select rows within those archetypes. `Changed<T>` remains O(number of structurally matching entities) without a separate dirty-index structure. Generate no row-level presence branch for required components. Resolve optional bindings once per archetype, and specialize present/absent traversal paths where that saves work without excessive code duplication.

Reject read/write or write/write overlaps within a system unless a supported structural proof establishes disjointness. `With<Player>` and `With<Enemy>` do not prove disjointness, since one entity can have both. `With<Player>` and `Without<Player>` do. Required/excluded sets are separate from access sets; optional access never contributes a required bit. Resource conflicts always remain global to that resource.

Keep access clauses per query in generated manifests. A system with multiple queries accesses the union of their entity sets. A cross-system disjointness proof must succeed for every conflicting clause pair; combining all query filters into one system-wide intersection would be unsound. Serialize cases the supported proof cannot establish.

A named query can expose `TryGet(Entity, out Row)` with world/generation validation. Returned rows are scoped to the query's execution. Query methods remain read/write capabilities declared by the system signature, even when the caller fetches only one entity. V1 `[Each]` partitioning does not accept arbitrary mutable queries or lookups, which could write outside the assigned chunk.

`ref struct` and `scoped` constrain escape; they do not create a Rust borrow checker. In particular, C# can retain a raw ref after disposing an object that provided it. Therefore the public API must not expose an idle `world.GetRef<T>()` or a freely disposable query lease as its safety boundary.

Default query values are legal C#. Validate a query's execution lease when enumeration or lookup begins rather than treating `default(Moving)` as a valid empty view. Tracking filters also declare read access to their component's tick metadata, even when no value field is fetched.

Normal systems receive only declared capabilities. Idle-world inspection returns value copies or runs a synchronous callback with scoped access under a world lease. Every public structural entry point checks the world's active execution/borrow state in release builds, including calls through a separately captured `World`. Structural changes go through deferred commands while a system runs. Public async, iterator, ref-returning, and open-generic systems are rejected.

Consumer-generated invokers live in the consumer assembly, so they need a narrow public contract for generated-code access. Its execution tokens have runtime-controlled construction; binding checks the live world/epoch, declared access, and assigned range. Default or expired tokens fail before storage access. Hide these APIs from ordinary discovery if useful, but do not pretend C# `internal` is visible to arbitrary consumers or depend on `InternalsVisibleTo`. Hoist validation to invocation/chunk binding instead of repeating it per row.

Components are unmanaged so a readonly component ref cannot hide mutation of a managed object graph. Unsafe pointers, mutable statics, and external native state still require author discipline. The scheduler's safety guarantee covers declared ECS accesses. Do not attach unproven `noalias` assumptions to generated pointers.

## Changes, resources, local state, and messages

`[TrackChanges]` explicitly opts a component or resource into added/changed stamps. A change filter on an untracked type is a compile error. This policy is shared through manifests, so a library cannot silently omit tracking needed by a consumer. Untracked types pay no tick-column cost.

Use monotonically increasing checked `ulong` ticks. Assign a tick to each system execution and each idle-world mutation. Command playback receives a fresh tick after the preceding batch completes and before the next consumer's execution tick is assigned. Never reuse the command producer's earlier tick: the producer itself must observe its deferred spawn on its next change-filtered run. Insert sets both added and changed. Replace preserves added and advances changed. Migration preserves stamps of retained components. Never use a single frame number for all systems: ordered writes within one frame must be distinguishable.

Each system instance remembers its last successful execution. A change matches when `lastRun < componentStamp <= thisRun`. Skipping a system preserves its last-run value. Conditions and repeated registrations have separate persistent state. Overflow fails before issuing a duplicate tick; the implementation does not reset or silently wrap the counter.

Generated writable query properties stamp a tracked component before returning its mutable ref. `[Each]` stamps every admitted tracked writable component before calling the user's method. A tracked `ResMut<T>.Value` follows the same rule; `Res<T>.Value` leaves stamps unchanged. Merely assigning the same value can count as a change. An unused writable ref can also count. Do not claim the generator can detect arbitrary memory writes or equality changes in user code. No public mutable access to a tracked type bypasses stamping.

`Res<T>` reads one world-owned resource; `ResMut<T>` writes it. Required resources are validated when a schedule binds and before execution if idle editing removed one. Missing optional resources are explicit. `Local<T>` belongs to one system instance and persists across calls. Managed resources and locals require an explicit capability that obtains exclusive world access and runs on the owner thread in v1, since `in` is not deep immutability and different object graphs may share mutable references. Thread affinity alone does not exclude workers. Ordinary parallel resource parameters use unmanaged values. An unmanaged main-thread resource pins its system to the owner thread while retaining normal access-conflict checks.

Resource registration states ownership. `InsertResource` borrows the value and never disposes it. `InsertOwnedResource` transfers a managed disposable owner to the world, and the caller must not dispose it afterward. Use an owned managed resource for a native allocation's lifetime; ordinary unmanaged resource values remain non-owning. Replacement/removal publishes the new slot state while idle, then disposes the previous owned value exactly once. `TakeResource` explicitly transfers ownership back. World disposal attempts all owned cleanup even if one cleanup fails, then reports aggregated failures. Do not infer ownership just from `IDisposable`. Test borrowed and owned replacement, removal, take, and repeated world disposal.

Buffered communication uses `MessageReader<T>` and `MessageWriter<T>`, following Bevy 0.17 terminology. Each system instance owns its reader cursor. Same-type writers conflict with readers and other writers; multiple readers can run together because their cursors are independent. An explicitly ordered writer then reader can communicate within one schedule execution.

The application calls `world.AdvanceMessages()` once per outer update, outside active schedules. Retain messages for two rotations and report missed counts to readers that fall behind. Schedules do not implicitly rotate buffers, because fixed updates and render updates can run at different rates. Start with unmanaged message values and pooled buffers. Removed-component readers can use a separate typed removal log with the same explicit retention contract, carrying entity identity and removal tick rather than an invalid component ref.

Immediate observer events, relationships, required-component injection, serialization, sparse-set storage, arbitrary dynamic queries, hot-loading, and `ParamSet` are follow-up extensions. The v1 feature set remains a complete ECS without them.

## Archevolt's scheduler

Archevolt implements its own scheduler, including dependency analysis, dispatch, synchronization, and fault handling. It uses .NET threading primitives and has no external scheduling package.

### Planning and execution have separate owners

| Internal concept | Responsibility |
| --- | --- |
| `SchedulePlan` | Immutable system nodes, access clauses, explicit edges, conservative conflict edges, conditions, barriers, thread-affinity metadata |
| `BoundSchedule` | World-specific component bindings, query states, system instances, last-run ticks, local values, reader cursors, command buffers |
| `SerialExecutor` | Reference behavior and low-overhead execution for small workloads |
| `ParallelExecutor` | Ready batches, range assignment, bounded worker dispatch, completion, fault collection |
| `WorkerPool` | World-owned reusable worker threads, blocking wake/join protocol, orderly shutdown |

Generate a `SchedulePlan` for declarative schedules. For runtime composition, build the same representation once while the world is idle. Shared immutable plans are allowed; mutable bound state belongs to a world and individual system instance. Do not rebuild the dependency graph every frame.

Serial and parallel execution follow the same compiled batch and barrier sequence. Serial executes each batch's members in stable order. Changing the executor must not move command flushes or change structural visibility.

Serial execution is the default. An application can opt in when constructing its world:

```csharp
using var world = new WorldBuilder()
    .AddModule(GameEcs.Module)
    .UseParallelExecution(maxConcurrency: 4)
    .Build();
```

`maxConcurrency` includes the owner thread. Four permits at most three background workers plus the owner. One uses inline serial execution; nonpositive values are invalid. Allocate a world's worker pool lazily or at construction, then reuse it across that world's schedules. Multiple worlds require an explicit application-level CPU budget.

### Construct a correct dependency graph

Declarative schedules use `[Run(typeof(Systems), methodName, Order = n)]` entries and before/after edges identifying an owner type and method or instance key. Static classes cannot be generic arguments, so system-owner attributes accept `typeof` values. Generic component attributes remain valid.

Validate method names, unique run ordinals, unique instance keys, modules, required parameters, and affinity. Repeated registrations of one method require distinct instance keys, with separate local values, query cursors, last-run ticks, and command buffers.

Build the plan in this order:

1. Resolve system, condition, set/stage, and explicit dependency declarations. Expand set/stage ordering into system edges and reject cycles.
2. Compute a stable topological order of explicit edges, breaking ties by declared run ordinal.
3. Compare access clauses for otherwise unordered systems. Read/read is compatible. Read/write and write/write conflict unless every relevant component-clause pair has a supported disjointness proof. Resource and message conflicts remain global.
4. Include run-condition reads in the node's access requirements. Add missing conflict edges forward in the established topological order.
5. Report unordered conflicts as diagnostics so applications can state meaningful order explicitly. The default conservative orientation keeps execution valid.
6. Insert `ApplyDeferred` barriers for command visibility and schedule completion, then derive compatible ready batches.

Do not orient conflicts independently by raw declaration order. For declaration order A,B,C, explicit C-before-A, and conflicts A/B and B/C, that could introduce a cycle. Orienting forward in a stable topological order preserves the explicit graph.

Preserve per-query predicates in manifests. A system with several queries accesses their union. Disjointness must hold for every conflicting pair, not an intersection of all filters within a system. Serialize cases the supported proof cannot establish.

V1 supports ordered stages/sets and synchronous read-only run conditions. Evaluate a condition when its node is ready, after predecessor barriers. A condition has its own persistent state. A false result completes dependency ordering without advancing the skipped body's last-run tick. Do not evaluate every condition at frame start.

### Choose a bounded executor

| Executor design | Benefits | Costs and decision |
| --- | --- | --- |
| Reusable dedicated .NET worker threads, owned by a world | Explicit concurrency budget, stable workers, no dependence on unrelated ThreadPool load | Requires startup/shutdown and wake/join implementation; chosen for opt-in v1 parallel execution |
| Cached BCL ThreadPool work items | Fewer owned OS-thread lifecycle responsibilities | Shares pool capacity with application work and needs explicit execution-context/starvation policy; a later interchangeable executor if measured useful |
| One Task per entity or small query operation | Familiar task API | Excess scheduling/allocation overhead; excluded |

The serial executor is always available and is the conformance reference. Parallel execution initially dispatches conflict-free system batches. Add restricted `[Each]` chunk partitioning only after the system-level executor passes correctness tests.

Pool a bounded set of work descriptors containing durable execution handles, system IDs, and chunk ranges. Generated worker invokers construct query rows, spans, and resource views inside execution. Ref structs cannot be stored in work items or captured by closures.

The parallel executor publishes one immutable batch at a time. Workers claim system/chunk ranges with an atomic index. The owner may claim general work while handling owner-thread-affine nodes itself. Never dispatch one work item per entity. Use measured thresholds to keep small batches inline.

The initial partitioned `[Each]` parameter whitelist is `Entity`, required component `ref`/`in` parameters, and read-only unmanaged resources. Exclude mutable resources and locals, message readers and writers, commands, arbitrary queries, and lookups. Inter-system conflict checks do not prevent races between partitions of the same system. Evaluate its condition once before splitting, assign one body tick to all partitions, and commit the system's last-run tick once on the owner only if the entire invocation succeeds.

### Publication, completion, and thread affinity

Use one private object monitor with a batch epoch, a published batch reference, a stop/fault state, and a pending-worker count. Workers wait in a predicate loop with `Monitor.Wait`; the owner publishes under the gate and uses `Monitor.PulseAll`. An epoch change distinguishes new work from a repeated wakeup. The wait releases and then reacquires the monitor, as specified by [.NET Monitor.Wait](https://learn.microsoft.com/en-us/dotnet/api/system.threading.monitor.wait?view=net-10.0).

Initialize the pending count to the full participant set under the gate before publishing the batch. Do not increment it lazily as workers wake. All participating workers signal completion exactly once in `finally`, including workers that claim no ranges. The owner waits in a predicate loop for pending workers to reach zero. Use the gate and appropriate atomic operations to publish work and make completed writes visible before a dependent batch starts. Avoid unbounded polling and busy waits.

Never hold the publication monitor while running user code, reserving entities, applying commands, or joining threads. Read cancellation/fault flags outside the gate only through a consistent atomic/volatile protocol. Keep outcome storage per work item so workers do not append to a shared exception list.

The pool must not publish the next epoch or reuse a batch descriptor until the previous epoch has fully joined. Workers recheck the epoch predicate before waiting, so a wakeup racing their transition back to idle cannot lose work. Start workers with a readiness handshake; construction cleans up partially started threads if startup fails.

Owner-thread affinity is distinct from exclusivity. A thread-affine unmanaged resource can run on the owner alongside disjoint worker work. An exclusive node, including managed-resource access, runs only after all workers join and occupies its own execution boundary. No worker directly calls thread-affine APIs.

The thread that builds the world is its owner. V1 `Run`, idle mutation, and disposal execute on that owner thread and reject calls from another thread. Background workers receive only scoped capabilities issued for an active run. Do not silently marshal onto an assumed synchronization context. This rule also keeps owned thread-affine resource cleanup on its required thread.

Assign body execution ticks on the owner immediately before each batch, in stable plan order, rather than reserving ticks for the entire schedule. Conditions receive their own execution ticks. A barrier receives a fresh playback tick after its predecessor batch joins; only afterward assign ticks to consumers. This ordering prevents deferred changes from falling outside the consumer's change interval.

Workers do not implicitly inherit application task-local state as an ECS contract. Put required data in declared resources or system-local state. Document any explicit execution-context support if introduced later.

### Deferred commands and visibility

Ordering includes structural visibility. If B is after A and A can record commands, insert `ApplyDeferred` on the path from A to B, including transitive paths. A barrier joins every job holding query references, applies commands exclusively, refreshes affected bindings, and then releases downstream nodes. Flush at schedule completion too.

`Commands` records typed spawn, insert, remove, and despawn operations in per-producer buffers. It does not grant immediate arbitrary world mutation. `Spawn(bundle)` reserves a real `Entity` handle in a synchronized allocator and records creation. Reserved handles can be used in later commands but are absent from queries until playback. Cancellation invalidates the reservation and advances its generation.

Separate reservation state from live component locations. Use stable paged entity metadata, safely published at growth, so reserving a new handle cannot invalidate a running lookup. Publish the reserved entity's live location only during exclusive playback.

Within a flush, order producers by the stable dependency-respecting plan ordinal, then partition ordinal, then local command sequence. A command using a reserved entity must follow its creation or be ordered after its creator. Validate each command before its structural change. Stale targets and duplicate despawns are errors; explicit `Try*` commands provide no-op semantics where intended.

On playback failure, stop, retain already completed changes, discard the unapplied suffix, cancel unmaterialized reservations, and report the producer/command index. Playback is not transactional. Do not automatically retry user commands.

Stable playback order does not guarantee bitwise determinism. Parallel reservation may assign different numeric entity IDs; floating-point reductions and external state can also vary. Serial execution is the deterministic reference for deterministic user code. Compare parallel results by logical identity where physical IDs can vary.

### Fault, cancellation, and disposal contracts

Every worker catches user exceptions before they can escape its thread. The owner uses the same capture-and-join path if an owner-thread system or condition throws while workers are active. Record failure with the system instance and work range. Stop claiming new work once failure/cancellation is observed, allow running synchronous work to finish, and always signal completion.

The owner joins the entire batch before reporting failures, applying structural changes, or releasing memory. Discard unapplied command buffers and cancel their reservations after the join. Completed component/message writes, local changes, reader cursors, and earlier flushes remain; no rollback is promised. Mark the schedule faulted until explicitly reset after inspection. A failed body's last-run tick does not advance; completed systems retain their documented successful state. Reset acknowledges partial effects and never retries automatically.

Cancellation is cooperative between systems/chunk ranges. It never aborts a thread in the middle of user code. A system that does not return can prevent join and shutdown; cancellation cannot safely reclaim its memory. Use external watchdog reporting rather than freeing storage under it.

Reject concurrent or reentrant schedule execution against one world. A bound schedule transitions from ready to running and then ready, faulted, or cancelled. A healthy worker pool returns to idle after join even when user code failed; reserve a broken-pool state for execution-infrastructure failure. Pool transitions are idle, running/joining, idle or broken, then stopping and disposed. The owner controls transitions. Resets and plan replacement require a quiescent world.

World, schedule, and executor disposal while running is rejected. Once idle, world disposal signals stopping under the gate, wakes every worker, releases the gate, and joins all worker threads before releasing native storage and resources. A schedule borrows its world's pool; disposing that schedule must not terminate the pool. Dispose repeatedly without double-free. Never dispose or join the pool from one of its own worker callbacks. [.NET Thread.Join](https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.join?view=net-10.0) supplies the thread-termination wait.

After a faulted or cancelled run has fully joined, disposal is legal without resetting the schedule. A broken executor cannot resume dispatch, but cleanup must still join any surviving workers before freeing storage.

Scheduler diagnostics expose plan edges, reasons for conflicts/barriers, batch membership, and per-system timing through optional inspection hooks. Keep tracing disabled or preallocated in the steady-state fast path.

## Cross-assembly and AOT design

Each module emits two things from one model:

1. A public typed registration entry point and generated invokers, usable by the runtime without reflection scanning.
2. A versioned assembly metadata manifest containing canonical component/system identities, accesses, filters, and callable entry points, readable through Roslyn by a consuming compilation.

Local generation reads original symbols. Consumers read compiled manifests, not implementation bodies. Export wrappers for private systems from their partial owners. Types crossing an assembly boundary must have sufficient accessibility, with a diagnostic for an exported query that exposes an inaccessible component. Reject incompatible manifest/runtime versions rather than guessing their layout.

The world builder explicitly includes modules and freezes the registry before execution. It coalesces identical descriptions of the same CLR component type across modules and rejects inconsistent metadata. System instance keys must be unique within one bound schedule, not across the world. Two schedules may use the same method/key with independent state. Generated `typeof(T)` handles can identify types at registration without reflective discovery. Dense IDs remain world-local, and all generated modules bind against that same registry. Never use independently assigned integer constants from separate assemblies as shared component IDs.

Module registration includes every component referenced by its included systems, queries, filters, and bundles, including optional fields and tags that appear only in `Without<T>`. Command payload types must be declared by an included bundle/component or an explicit module inclusion. Do not infer a complete registry by analyzing arbitrary system method bodies. Dynamic add/remove can form new combinations of registered types, but introducing a new component type requires rebuilding an idle world in v1.

V1 supports closed registered component/system shapes. Open generic declarations are rejected or explicitly closed through a declaration visible to the generator. No `Reflection.Emit`, `DynamicMethod`, runtime expression compilation, or `MakeGenericType` is needed. Generic code reachable only through descriptors must be rooted by generated typed references.

Run the actual packed consumer under trimming and NativeAOT with both serial and parallel execution. A reflection-free design alone is not evidence that publishing succeeds. Generated registrations must retain all invoked component, query, system, and schedule code.

## Alternatives considered

| Shape | Example | Assessment |
| --- | --- | --- |
| Generic runtime query engine | `Query<Read<Velocity>, Write<Position>>` backed by reusable fetch abstractions | Familiar syntax, but does not naturally generate named typed rows for arbitrary arities; risks generic/interface work and wrapper complexity. Suitable as a future dynamic API, not the primary generated path. |
| Named generated queries plus `[Each]` | `Moving` query parameter or `Integrate(ref Position, in Velocity)` | Chosen. Concrete types exist before generation; generated ref properties are feasible; the fast loop is directly inspectable. Requires a query declaration for custom loops. |
| Entire world specialized to declared bundles | One generated storage type per declared spawn bundle | Maximum static layout knowledge, but breaks natural add/remove composition or requires an exponential set of combinations. Reject as the default ECS. |

Do not show `Query<(Ref<Position>, RefReadonly<Velocity>)>` with stack-only ref wrappers as valid ordinary C#. `ValueTuple` cannot accept those ref structs as type arguments. Include this expected compiler rejection in generator/language tests.

Dense-only storage is an intentional v1 choice. Bevy distinguishes archetypes from tables when sparse components are present. Adding sparse storage later changes query iteration, migration, filters, and disjointness analysis; it is not a free annotation. Bevy also already specializes query code through Rust generics. This proposal moves C# binding and scheduling work into generated code without claiming that Bevy uses reflection in its inner loops.

No automatic system fusion, arbitrary body-to-SIMD rewriting, or target-specific field layout generation is planned. Fusion can change observable order, messages, random-number use, and change semantics. Inlining, vectorization, bounds-check removal, and instruction selection remain JIT/AOT decisions. Measure before adding opt-in SIMD or reductions.

## Implementation sequence and completion criteria

Each stage produces a working Archevolt increment. The production v1 is complete only after all stages pass.

| Stage | Deliverable | Acceptance gate |
| --- | --- | --- |
| 0. Reproducible build | Standalone solution, SDK/dependency pins, package layout, minimal generator, headless consumer | Clean build and packed-package consumer run on Linux and Windows; runtime package/output contains no external framework or Roslyn dependency |
| 1. World/storage | Entity states, registry, archetypes/chunks, bundle insertion, migration, disposal | Random operation traces match a dictionary-based reference model; stale/cross-world handles, swap-removal, generations, and disposal tested |
| 2. Generated execution | Named queries, typed rows, readonly-record replacement, `[Each]`, typed invokers, filters, cache refresh | Consumer compiles/runs; readonly access violations fail compilation; new archetypes join cached queries; no per-row boxing/interface/delegate dispatch |
| 3. Useful serial ECS | Resources, locals, commands/reservations, tracking, messages, removal logs, serial schedules | Ordered spawn-to-query visibility; skipped-system changes; independent message cursors; invalid commands and reservation cleanup tested |
| 4. Schedule compilation | Declarative plans, conditions, conflict proofs, runtime composition, module manifests | Cycles and alias errors diagnosed; two-assembly consumer works; separate system instances retain independent state |
| 5. Owned parallel scheduler | Worker startup/shutdown, batch publication, affinity, fault/cancellation handling, restricted `[Each]` partitioning | Serial/parallel conformance; no lost wakeups; join before flush/free; empty/single/multiple-worker, throwing-system, cancellation, and reuse stress tests |
| 6. Shipping validation | Sample, reference/API docs, benchmarks, trimming/AOT, package tests | Packed consumer runs under JIT and AOT; allocation/code-size/performance results recorded; every promised v1 feature covered |

Start implementation with one vertical case: spawn 100,000 Position+Velocity entities, run a generated `[Each]` movement system, then add/remove a tag and verify query membership. Keep it single-threaded initially. This validates the core storage/generation contract before scheduling complexity grows.

Required correctness scenarios include:

- Spawn A+B, execute a query, then spawn A+B+C. Both match on the next run without rebuilding the world or recompiling.
- Move/despawn the first, middle, and final rows across chunk boundaries; resolve every survivor and verify values and ticks.
- Replace a readonly record component through a generated writable ref and verify its stored value. Reject init-only member mutation and replacement through readonly access.
- Accept unmanaged readonly records; reject record classes and record structs with managed fields from native columns.
- Replace a component without changing its added tick. Replacing with an equal value still marks a tracked write. Readonly iteration does not.
- Add/remove unrelated components without spuriously marking survivors changed.
- Skip a change-filtered system for several executions. It observes intervening changes when resumed. Ordered writes in one frame remain observable.
- Reject overlapping mutable query parameters, including optional-field and multi-query cases. Accept a supported `With<X>`/`Without<X>` proof. Never ignore a resource conflict.
- Given declaration order A,B,C, explicit C-before-A, and conflicts A/B and B/C, construct an acyclic conflict plan that preserves C-before-A.
- A spawning A followed by B-after-A lets B observe the entity, including a transitive dependency path and a skipped condition. A also observes its deferred spawn on its next `Added<T>` run.
- Throw from one parallel worker while another is active. No storage is freed, commands applied, or owner-thread exception raised before all workers finish.
- Reuse thousands of empty, small, and large batches while perturbing wake/claim/completion timing. Prove no lost work, duplicate execution, lost wakeup, or descriptor reuse before join.
- Check startup failure, cancellation before dispatch and during a batch, shutdown of idle workers, repeated disposal, and rejection of nested/concurrent execution.
- Run affinity and exclusive-resource tests with conflicting and disjoint work. Observe maximum concurrency under the configured total budget.
- Reject run, idle mutation, and disposal from a non-owner thread, including cleanup of owned thread-affine resources.
- Reject mutable-resource and message-reader parameters in partitioned `[Each]` systems. Verify one condition evaluation and one successful last-run update across all partitions.
- Two independent message readers observe each message once; a delayed reader receives an explicit missed count after retention expiry.
- Accessors cannot escape through an accepted system signature; immediate mutation through a captured World is rejected during execution in release mode.
- Component counts above 64, conflicting exported manifests, shared components across modules, two worlds, repeated system registrations, and missing resources behave as specified. A tag referenced only by `Without<T>` is registered correctly.
- Two schedules can bind the same system method/key without sharing local state, cursors, or last-run ticks. Duplicate keys within one schedule fail.

Generator diagnostics should cover at least these contracts:

| Diagnostic family | Trigger |
| --- | --- |
| AV001 | Missing partial owner/query, unsupported declaration, or inaccessible generated member |
| AV002 | Managed/reference-containing component or unsupported owning component contract |
| AV003 | Duplicate component fields, bundle members, or conflicting component metadata |
| AV004 | Overlapping mutable accesses without a supported disjointness proof |
| AV005 | Async, iterator, ref-returning, overloaded, open-generic, or unsupported system parameter |
| AV006 | Change filter on a type without tracking |
| AV007 | Unknown system/instance, schedule cycle, or invalid dependency |
| AV008 | Unordered conflicting systems; warning requesting explicit semantic order |
| AV009 | Incompatible manifest ABI, inaccessible export, or inconsistent registration |
| AV010 | Unsafe partition request, thread-affinity violation, or undeclared capability |

An analyzer can catch direct forbidden APIs and explicitly declared unsupported ownership policies. It cannot generally identify an owning native handle or prove arbitrary callees free of global side effects. Runtime structural checks and documented external-state requirements remain necessary. Test diagnostics and generated compilation, not only source snapshots. Track incremental generator steps to test that unrelated edits preserve cached outputs.

## Performance evidence required before release

Use a handwritten typed SoA loop as the first baseline. Add the named-query and `[Each]` versions with identical data, math, storage alignment, and tracking policy. Compare against a pinned established C# ECS as a secondary reference. Treat cross-language Bevy numbers as contextual unless the entire workload and compiler settings are controlled.

Benchmark entity counts from small worlds to millions, one versus many archetypes, narrow versus wide components, structural churn, optional/change filters, and serial versus parallel execution. Include cold registration and query-cache refresh separately from steady-state updates. Measure command growth separately from reserved-capacity playback.

Record throughput, frame-time distribution, allocations across all participating managed threads, native-memory use, generated source/IL/native code size, incremental build time, and worker/barrier overhead. Inspect representative JIT/AOT disassembly for call shape, bounds checks, and vectorization. Save SDK/runtime, CPU, OS, commit, build flags, and benchmark configuration with results.

Acceptance targets are zero managed allocation for a warmed, fixed-capacity steady-state update, and generated dense traversal close to the handwritten loop under equal semantics. These are targets, not measured claims. Agree on numeric regression limits after obtaining stable baselines. Tracking, scheduler dispatch, initialization, and structural growth are real costs and must be reported.

## Verification and implementation workflow

This plan is one root document. It does not depend on companion files from the earlier proposal or claim that implementation tests already exist in this repository.

During stage 0, add rerunnable build, test, pack, and clean-consumer checks. Keep generated snapshots paired with compilation/execution tests. Exercise the actual generated binder and column-access calls from another assembly, not just attribute discovery. Test package dependency/output contents directly; the runtime must not pull in BRUTAL, Roslyn, or test/benchmark assemblies.

Use language tests for the named partial query, `Moving.Row`, readonly-record replacement through writable refs, init-only/readonly compiler errors, private system invocation, ref-struct tuple rejection, static-owner attribute arguments, and scoped lifetime restrictions.

During plan preparation, an isolated hand-authored query/row probe passed under SDK 10.0.402, runtime/reference pack 10.0.13, and C# 14. It verified readonly-record replacement through row and scoped refs, readonly reads, equal-value replacement stamps, and expected compiler failures for init-only writes, readonly replacement, and managed component types. This verifies selected language mechanisms only. The probe is not an implemented source generator or scheduler, and its temporary files are not part of this single-file deliverable.

For runtime correctness, create a dictionary-based reference world and replay randomized structural traces against both models. Preserve failing seeds. For scheduler correctness, execute the same explicitly ordered workloads serially and in parallel, instrument allowed concurrency and barriers, and force the failure/cancellation cases listed above.

Benchmark the actual generated output and packed consumers. Publish serial and parallel examples under trimming and NativeAOT. Keep the SDK, OS, CPU, build flags, workload, and commit with each result. A successful compile alone does not establish memory safety, correct scheduling, or a performance advantage.

Implementation proceeds through the staged acceptance gates above. Do not advertise a production v1 until all promised storage, query, lifecycle, scheduler, package, and consumer checks pass.

## Inspected Bevy sources

These references explain the semantics selected above:

- [Archetypes and their relationship to tables](https://github.com/bevyengine/bevy/blob/706fdd9800c4bf335d67f127ff6e5a93deadabb9/crates/bevy_ecs/src/archetype.rs#L1).
- [Table columns and swap-removal](https://github.com/bevyengine/bevy/blob/706fdd9800c4bf335d67f127ff6e5a93deadabb9/crates/bevy_ecs/src/storage/table/mod.rs#L174).
- [World-bound query state and cached matches](https://github.com/bevyengine/bevy/blob/706fdd9800c4bf335d67f127ff6e5a93deadabb9/crates/bevy_ecs/src/query/state.rs#L51).
- [Query access and filter disjointness](https://github.com/bevyengine/bevy/blob/706fdd9800c4bf335d67f127ff6e5a93deadabb9/crates/bevy_ecs/src/query/access.rs#L904).
- [Changed filters and their per-row cost](https://github.com/bevyengine/bevy/blob/706fdd9800c4bf335d67f127ff6e5a93deadabb9/crates/bevy_ecs/src/query/filter.rs#L854).
- [Deferred execution and ordered visibility](https://github.com/bevyengine/bevy/blob/706fdd9800c4bf335d67f127ff6e5a93deadabb9/crates/bevy_ecs/src/schedule/executor/mod.rs#L136).
- [Mutable-access change detection](https://github.com/bevyengine/bevy/blob/706fdd9800c4bf335d67f127ff6e5a93deadabb9/crates/bevy_ecs/src/change_detection.rs#L431).
- [Resources and system parameter access](https://github.com/bevyengine/bevy/blob/706fdd9800c4bf335d67f127ff6e5a93deadabb9/crates/bevy_ecs/src/system/system_param.rs#L752).
- [Buffered messages](https://github.com/bevyengine/bevy/blob/706fdd9800c4bf335d67f127ff6e5a93deadabb9/crates/bevy_ecs/src/message/messages.rs#L17) and [observer events](https://github.com/bevyengine/bevy/blob/706fdd9800c4bf335d67f127ff6e5a93deadabb9/crates/bevy_ecs/src/event/mod.rs#L14).
- [Bundle semantics](https://github.com/bevyengine/bevy/blob/706fdd9800c4bf335d67f127ff6e5a93deadabb9/crates/bevy_ecs/src/bundle/mod.rs#L90).
