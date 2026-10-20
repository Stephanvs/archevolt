# API reference

Archevolt targets .NET 10 and C# 14. Public runtime types and declaration attributes use the `Archevolt` namespace. The package includes the source generator.

Generated declarations remain in the namespace and partial owner of the original declaration. Global declarations remain global. Containing types must also be non-generic partial declarations.

## Components, bundles, and modules

```csharp
using Archevolt;

[Component, TrackChanges]
public readonly record struct Position(float X);

[Component]
public readonly record struct Velocity(float X);

[Component]
public readonly struct Frozen;

[Bundle]
public readonly partial record struct Particle(Position Position, Velocity Velocity);
```

Components are unmanaged value types. The generator rejects reference-containing components and components that implement `IDisposable`. Native pointers and external handles remain the application's responsibility. A component with no instance fields is a membership tag. Tags support membership and tracked change filters, but have no readable or writable value column.

Bundles are unmanaged partial structs. Their generated `Descriptor` and `IBundle<T>` implementation flatten nested bundles. Repeated component types are errors. A bundle describes insertion into an entity and does not provide entity identity.

```csharp
[EcsModule]
[IncludeSystems(typeof(Motion))]
[IncludeBundle<Particle>]
[IncludeResource<FrameTime>]
public static partial class GameEcs;
```

`GameEcs.Module` is a generated `ModuleDescriptor`. The module includes components, resources, and messages referenced by its included systems. Bundles used by spawn commands require explicit inclusion. `IncludeComponent<T>`, `IncludeMessage<T>`, and `IncludeResource<T>` register otherwise unused types. `IncludeModule(typeof(OtherModule))` includes another generated module. `IncludeModule<T>` is available for module types that C# permits as generic arguments.

Generated modules expose versioned manifests for referenced assemblies. Compatible component and resource registrations share one world registration. Registering the same named module twice is an error. Conflicting component or resource metadata and incompatible module ABIs fail validation.

The equivalent runtime registration API is `ModuleBuilder`, with `Component<T>`, `Tag<T>`, `Bundle<T>`, `Resource<T>`, `ManagedResource<T>`, `Message<T>`, `System`, and `Include` methods. `WorldBuilder` also accepts individual component, bundle, resource, managed-resource, and message registrations.

## World and entity operations

`new WorldBuilder().AddModule(GameEcs.Module).Build()` creates a world owned by the calling thread. Registration is fixed at construction.

| API | Behavior |
| --- | --- |
| `Spawn()` | Creates an entity with no components. |
| `Spawn<TBundle>(in bundle)` | Creates an entity with the registered bundle's components. |
| `Insert<T>(entity, in value)` | Adds or replaces one registered component. |
| `InsertBundle<TBundle>(entity, in bundle)` | Adds or replaces bundle members and preserves unrelated components. |
| `Remove<T>(entity)` | Removes a component and returns whether it was present. An invalid entity is an error. |
| `Despawn(entity)` | Removes a live entity. Returns `false` for a non-live handle. |
| `Contains(entity)` | Reports whether the handle identifies a live entity in this world. |
| `Has<T>(entity)` | Tests component membership on a live entity. |
| `Get<T>(entity)` / `TryGet<T>(entity, out value)` | Returns a component copy. Tags use `Has<T>`. |
| `NativeBytes` | Reports native bytes held by the world's chunk allocator. |

Entity identity contains `WorldId`, `Index`, and `Generation`. `default(Entity)` is null. A stale handle cannot identify a later occupant of its slot or an entity in another world.

World inspection and immediate mutation require an idle world and its owner thread. Capturing a `World` inside a system does not bypass those checks. Structural edits during execution use `Commands`.

`Reserve(entities, commandBytesPerProducer, messagesPerType)` reserves initial capacities. Capacities can grow. `WithChunkBytes(bytes)` sets the target chunk size, with a minimum of 64 bytes. Large component layouts can require a larger allocation for a single row.

## Named queries and filters

```csharp
[Query, Write<Position>, Optional<Velocity>, Without<Frozen>, Changed<Position>]
public readonly ref partial struct Moving;

public static partial class Motion
{
    [EcsSystem]
    public static void Move(scoped Moving moving)
    {
        foreach (var row in moving)
        {
            ref var position = ref row.Position;
            if (row.HasVelocity)
                position = position with { X = position.X + row.Velocity.X };
        }
    }
}
```

A named query must be a `readonly ref partial struct`. Its generated `Shape`, enumerator, and `Row` are concrete types.

| Declaration | Membership and row access |
| --- | --- |
| `[Read<T>]` | Requires `T`. The row property returns `ref readonly T`. |
| `[Write<T>]` | Requires `T`. The row property returns `ref T`. |
| `[Optional<T>]` | Does not require `T`. `HasT` reports presence. The readonly property throws when absent. |
| `[With<T>]` | Requires membership without exposing a value property. |
| `[Without<T>]` | Excludes entities with `T`. |
| `[Added<T>]` | Requires tracked `T` and selects additions in this system instance's change interval. |
| `[Changed<T>]` | Requires tracked `T` and selects changes in this system instance's change interval. |

Filters combine with AND. An optional-only query matches entities without the optional components. `Added<T>` and `Changed<T>` do not expose a value property unless a value access is also declared. Optional writes and general Boolean filters are not supported.

Every row exposes `Entity`. Value properties use the component's simple name by default. `Name = "Alias"` on `Read<T>`, `Write<T>`, or `Optional<T>` changes the property name. An optional alias also changes its presence property to `HasAlias`.

Multiple mutable query parameters require a structural proof that their entity sets cannot overlap. `With<Player>` against `Without<Player>` proves separation. Different required tags alone do not. The planner retains every query's predicate separately. Resource and message conflicts remain global.

## Systems and component traversal

`[EcsSystem]` marks a synchronous static `void` method. Supported parameters are scoped named queries and the capabilities listed below. Private methods are callable through generated invokers inside their partial owner. Overloads, generic methods, async methods, iterators, and ref returns are rejected.

```csharp
public static partial class MotionKernel
{
    [Each(Partitioned = true), Without<Frozen>]
    public static void Move(scoped ref Position position, scoped in Velocity velocity)
    {
        position = position with { X = position.X + velocity.X };
    }
}
```

`[Each]` calls the method once per matching entity. Component parameters use `scoped ref T` for writes and `scoped in T` for reads. An optional `Entity` parameter supplies identity. Membership and change-filter attributes apply to the method. Components passed by value are rejected.

`[Each]` executes one traversal by default. `[Each(Partitioned = true)]` opts into splitting matched chunks across the world's execution budget. Partitioned methods accept component refs, `Entity`, and readonly unmanaged resources, including optional resources without owner-thread affinity. Locals, commands, messages, managed resources, mutable resources, and named queries are excluded.

The generator exposes each method as `Owner.MethodName_Descriptor`. Runtime composition accepts these descriptors through `BoundSchedule.Bind`.

## Resources and local state

```csharp
[Resource, TrackChanges]
public readonly record struct FrameTime(float DeltaSeconds);

[Resource(OwnerThread = true)]
public readonly record struct WindowHandle(nint Value);
```

| System parameter | Capability |
| --- | --- |
| `scoped Res<T>` | Readonly access to a required unmanaged resource. |
| `scoped ResMut<T>` | Writable access to a required unmanaged resource. |
| `scoped OptionalRes<T>` | Readonly access to a registered resource that may be absent. `HasValue` guards `Value`. |
| `scoped ManagedRes<T>` | Access to a managed resource class, exclusively on the owner thread. |
| `scoped Local<T>` | Mutable `Value` belonging to this system instance, initially `default(T)`. |

`[Resource(OwnerThread = true)]` keeps an unmanaged resource's system on the owner thread. Disjoint worker work may overlap that system. Managed resources and reference-containing locals make their system exclusive.

Required resources must be present at binding and before each execution. An optional resource still requires type registration. `Res<T>` and `OptionalRes<T>` expose `IsAdded` and `IsChanged` when tracking is enabled.

`world.InsertResource(value)` borrows the value. `InsertOwnedResource(value)` transfers ownership of a managed `IDisposable` value to the world. Replacement and `RemoveResource<T>()` dispose a previous owned value once. `TakeResource<T>()` removes a value and transfers its ownership to the caller. `GetResource<T>()` returns the resource value. World disposal attempts every owned cleanup and aggregates cleanup failures.

Each repeated registration and each bound schedule has independent locals, query change cursors, and message-reader cursors.

## Change tracking

`[TrackChanges]` enables added and changed stamps for a component or resource. A change filter on an untracked type is an error.

A system observes stamps satisfying `lastSuccessfulRun < stamp <= thisRun`. A skipped or failed body does not advance its last successful tick. Conditions have separate ticks and cursors.

Insertion stamps added and changed. Replacement preserves added and stamps changed, including equal-value replacement. Moving an entity between archetypes preserves surviving components' stamps. Readonly access does not stamp a change.

A generated writable row property stamps before returning its ref. `[Each]` stamps tracked writable refs before the method call. `ResMut<T>.Value` follows the same rule. Taking a writable ref can therefore count as a change even when the method leaves the value unchanged.

## Deferred commands

`scoped Commands` records structural operations in a system's private command buffer.

| Command | Behavior at playback |
| --- | --- |
| `Spawn<TBundle>(in bundle)` | Materializes the returned reserved entity handle with its bundle. |
| `Insert<T>(entity, in value)` | Adds or replaces one component. |
| `InsertBundle<TBundle>(entity, in bundle)` | Adds or replaces bundle members. |
| `Remove<T>(entity)` / `Despawn(entity)` | Performs a strict removal. Missing targets or components are errors. |
| `TryRemove<T>(entity)` / `TryDespawn(entity)` | Allows an absent target or component without an error. |

A reserved handle can appear in later commands. It remains absent from queries until playback. Discarding an unmaterialized spawn invalidates its reservation.

Planner barriers join active workers, play buffers in stable plan order and local recording order, and refresh query caches. A dependent consumer observes its producer's commands, including transitive dependencies and skipped conditions. The schedule also flushes at completion.

Playback stops at the first failure. Earlier effects remain. The unapplied suffix is discarded, and unmaterialized reservations are cancelled. `CommandPlaybackException` identifies `Producer` and `CommandIndex`. Playback does not roll back or retry.

## Messages and removal records

An unmanaged `[Message]` value is registered through an included system parameter, `IncludeMessage<T>`, or `WorldBuilder.AddMessage<T>()`.

`scoped MessageWriter<T>` sends values with `Send(in value)`. `scoped MessageReader<T>` enumerates unread retained values and exposes `MissedCount`. Readers own independent cursors. Writers conflict with readers and other writers of the same message type. An explicitly ordered reader sees a writer's messages in the same schedule execution.

`world.SendMessage(in value)` sends while idle. `world.AdvanceMessages()` rotates retention while idle. Current and previous rotations remain available. A message expires on the second rotation after it was sent. Schedules do not rotate buffers automatically.

`scoped RemovedComponents<T>` enumerates `Removal` records with `Entity` and `Tick`. It has the same retention and missed-count behavior. Recording for a component begins when its removal reader is bound.

## Conditions and schedule order

```csharp
public static partial class Conditions
{
    [RunCondition]
    public static bool Enabled(scoped OptionalRes<FrameTime> time) => time.HasValue;
}

[Schedule]
[Run(typeof(Motion), nameof(Motion.Move), Order = 0,
    ConditionOwner = typeof(Conditions), Condition = nameof(Conditions.Enabled))]
public sealed partial class Simulation;
```

A `[RunCondition]` method is synchronous, static, and returns `bool`. It may read queries, unmanaged resources, messages, and removal records, and mutate its own locals. Writable world access, managed resources, and commands are rejected. The condition's required types must be included in the world's module registrations.

The condition evaluates once when its node is ready, after predecessor barriers. A false result completes dependency ordering without running the body or advancing the body's change cursor. Successful false evaluations advance the condition's own cursor. A partitioned body still evaluates its condition only once.

`Simulation.Create(world)` returns a `BoundSchedule`. Every `Run` declaration requires a unique `Order` and instance key. The default key is the qualified owner and method name. Repeated registrations use distinct `Instance` values.

`Order` breaks ties among ready nodes. It does not replace an explicit semantic dependency. `Before` and `After` identify an instance key or an unambiguous method name. `BeforeOwner` and `AfterOwner` qualify method references. `ConditionOwner` defaults to the run's owner.

`[Stage("name", Order = n)]` and `[SystemSet("name", Order = n)]` define ordered groups. Their names and orders must be unique within each group kind. `Run.Stage` and `Run.Set` select membership. Declaring stages requires every run to name a known stage. Set membership is optional. Earlier groups precede later groups through dependency edges. Contradictory group and explicit dependencies are cycles.

Runtime composition exposes the same planner:

```csharp
using var schedule = BoundSchedule.Bind(world,
    [Motion.Move_Descriptor, MotionKernel.Move_Descriptor],
    [new Archevolt.Scheduling.EdgeInput(0, 1)],
    instanceKeys: ["motion", "kernel"],
    orders: [0, 1],
    conditions: [Conditions.Enabled_Descriptor, null]);
```

Edge endpoints index the supplied system array. Optional keys, orders, and conditions arrays must match its length. Omitting orders uses array order. A runtime condition descriptor is built with `AsCondition()` and a factory returning `ConditionBody`.

`BoundSchedule.Plan` exposes immutable node order, edges with reasons, batch membership, barriers, and diagnostics. Both serial and parallel execution follow this plan.

`schedule.EnableTiming()` enables preallocated inspection metrics. `schedule.Timings` returns a readonly span of immutable `SystemTiming` entries in input-system order. Both operations require an idle world and its owner thread. Calling `EnableTiming` clears previous measurements, including when timing is disabled with `EnableTiming(false)`.

Metrics describe the most recent run. `BodyInvocations` counts executed callbacks, so a partitioned invocation contributes one callback per dispatched partition. `BodyFailures` counts thrown callbacks. `SkippedByCondition` identifies a false condition. `ConditionEvaluations` and `ConditionFailures` describe the node's separate condition instance.

`BodyElapsedTicks` sums callback durations across partitions, excluding time waiting for dispatch. It measures combined execution time rather than wall-clock latency. `ConditionElapsedTicks` measures condition evaluation. Raw ticks use `System.Diagnostics.Stopwatch.Frequency`. `BodyElapsed` and `ConditionElapsed` expose the same values as `TimeSpan`.

Measurements are available after joined failures as well as successful runs. A copied `SystemTiming` remains a value snapshot. The returned span is a view that changes after the next measured run or timing configuration change. Disabled timing reports zero counters and durations, takes no timestamps, and allocates no measurement objects during execution.

## Execution, faults, and lifetimes

Serial execution is the default. `WorldBuilder.UseParallelExecution(maxConcurrency)` enables the world-owned reusable worker pool. The positive limit includes the owner thread. A bound schedule borrows this pool, so disposing one schedule does not stop other schedules.

`schedule.Run(CancellationToken)` completes synchronously. Cancellation stops new work between systems or chunk ranges. Active user code must return before join completes. The owner reports execution failures after active workers join. `AggregateException` contains `SystemExecutionException` entries with the instance, system name, and chunk range.

Failure or cancellation sets `IsFaulted`. Completed writes, local changes, message cursors, and earlier command playback remain. Unapplied commands are discarded. `Reset()` acknowledges partial effects and permits another run. It does not retry automatically. Disposal is allowed after a fully joined failure without resetting first.

World and schedule control require the world's owner thread. Concurrent or nested runs against one world and disposal during execution are rejected. Every bound schedule must be disposed before its world. World disposal joins its workers before releasing native storage and owned resources. Repeated disposal is safe.

Queries, rows, component refs, and capability wrappers belong to the current synchronous invocation. Default query values and default or expired execution tokens fail validation. `scoped` and ref-struct signatures restrict escape, but do not make captured mutable statics, unsafe pointers, or external object graphs safe for parallel use. Those remain application responsibilities.

## Diagnostics

All listed generator diagnostics are errors except `AV008`, which is a warning. Runtime descriptor composition also rejects invalid metadata and dependencies.

| Code | Contract |
| --- | --- |
| `AV001` | Invalid partial declaration, containing owner, or generated member shape. |
| `AV002` | Unsupported managed or owning component, bundle, resource, or message type. |
| `AV003` | Duplicate fields or members, conflicting query terms, or contradictory membership. |
| `AV004` | Mutable query, resource, message, or command capabilities overlap. |
| `AV005` | Unsupported system signature, parameter, method shape, or tag value access. |
| `AV006` | Change filter references an untracked component. |
| `AV007` | Unknown or ambiguous system, condition, group, dependency, duplicate order or key, or schedule cycle. |
| `AV008` | Conflicting systems have no explicit dependency. The planner supplies a conservative order. |
| `AV009` | Missing or incompatible module or system metadata, registration, or export. |
| `AV010` | Unsafe partition request or condition capability. |

The [particle sample](../samples/Archevolt.Particles/Program.cs) contains complete generated-query and partitioned traversal programs. [PLAN.md](../PLAN.md) records the intended release gates. The [acceptance record](../.audit/acceptance.md) tracks validation evidence separately from the API contract.
