using System.Runtime.InteropServices;

namespace Archevolt.Benchmarks;

[Component] public readonly struct MatrixTag0;
[Component] public readonly struct MatrixTag1;
[Component] public readonly struct MatrixTag2;
[Component] public readonly struct MatrixTag3;
[Component, StructLayout(LayoutKind.Sequential, Size = 256)]
public struct WidePayload { public int Value; }
[Component, TrackChanges] public readonly record struct TrackedPosition(float X, float Y, float Z);
[Component] public readonly record struct Acceleration(float Value);
[Resource] public readonly record struct ScanTotal(long Count, double Sum);
[Resource] public sealed class MatrixTargets(Entity[] entities) { public Entity[] Entities { get; } = entities; }
[Bundle] public readonly partial record struct TrackedParticle(TrackedPosition Position, Velocity Velocity);

[Query, Read<TrackedPosition>, Changed<TrackedPosition>]
public readonly ref partial struct ChangedPositions;
[Query, Read<TrackedPosition>, Optional<Acceleration>]
public readonly ref partial struct OptionalAcceleration;

public static partial class MatrixSystems
{
    [Each(Partitioned = true)]
    public static void Mark(scoped ref TrackedPosition position, scoped in Velocity velocity)
    {
        const float delta = 1f / 60;
        position = new TrackedPosition(position.X + velocity.X * delta,
            position.Y + velocity.Y * delta, position.Z + velocity.Z * delta);
    }

    [EcsSystem]
    public static void Changed(scoped ChangedPositions query, scoped ResMut<ScanTotal> total)
    {
        long count = 0;
        double sum = 0;
        foreach (var row in query)
        {
            count++;
            sum += row.TrackedPosition.X;
        }
        total.Value = new ScanTotal(count, sum);
    }

    [EcsSystem]
    public static void Optional(scoped OptionalAcceleration query, scoped ResMut<ScanTotal> total)
    {
        long count = 0;
        double sum = 0;
        foreach (var row in query)
        {
            count++;
            sum += row.TrackedPosition.X;
            if (row.HasAcceleration)
                sum += row.Acceleration.Value;
        }
        total.Value = new ScanTotal(count, sum);
    }

    [EcsSystem]
    public static void Toggle(scoped ManagedRes<MatrixTargets> targets, scoped Commands commands)
    {
        foreach (Entity entity in targets.Value.Entities)
        {
            commands.Insert(entity, default(MatrixTag0));
            commands.Remove<MatrixTag0>(entity);
        }
    }

    [EcsSystem]
    public static void Empty() { }

    [EcsSystem]
    public static void EmptyCommands(scoped Commands commands) { }
}

[EcsModule, IncludeSystems(typeof(MatrixSystems)), IncludeSystems(typeof(Movement)),
    IncludeBundle<TrackedParticle>, IncludeBundle<Particle>,
    IncludeResource<FrameTime>, IncludeResource<ScanTotal>, IncludeResource<MatrixTargets>,
    IncludeComponent<MatrixTag0>, IncludeComponent<MatrixTag1>, IncludeComponent<MatrixTag2>,
    IncludeComponent<MatrixTag3>, IncludeComponent<WidePayload>, IncludeComponent<Acceleration>]
public static partial class MatrixModule;

[Schedule, Run(typeof(MatrixSystems), nameof(MatrixSystems.Mark), Order = 0)]
public sealed partial class TrackedMovement;
[Schedule, Run(typeof(MatrixSystems), nameof(MatrixSystems.Changed), Order = 0)]
public sealed partial class ChangedScan;
[Schedule, Run(typeof(MatrixSystems), nameof(MatrixSystems.Optional), Order = 0)]
public sealed partial class OptionalScan;
[Schedule, Run(typeof(MatrixSystems), nameof(MatrixSystems.Toggle), Order = 0)]
public sealed partial class CommandToggle;
[Schedule, Run(typeof(MatrixSystems), nameof(MatrixSystems.Empty), Order = 0)]
public sealed partial class EmptyUpdate;
[Schedule, Run(typeof(MatrixSystems), nameof(MatrixSystems.EmptyCommands), Order = 0),
    Run(typeof(MatrixSystems), nameof(MatrixSystems.Empty), Order = 1,
        AfterOwner = typeof(MatrixSystems), After = nameof(MatrixSystems.EmptyCommands))]
public sealed partial class EmptyBarrierUpdate;

internal static class MatrixPopulation
{
    internal static World Build(int concurrency = 1, int commandBytes = 4096) =>
        new WorldBuilder().AddModule(MatrixModule.Module).UseParallelExecution(concurrency)
            .Reserve(commandBytesPerProducer: commandBytes).Build();

    internal static Particle ParticleAt(int index) => new(new Position(index % 97, 0, 0), new Velocity(1, 2, 3));

    internal static Entity[] Populate(World world, int count, int archetypes = 1, bool wide = false)
    {
        world.InsertResource(new FrameTime(1f / 60));
        var entities = new Entity[count];
        for (int i = 0; i < count; i++)
        {
            Entity entity = world.Spawn(ParticleAt(i));
            entities[i] = entity;
            int mask = i % archetypes;
            if ((mask & 1) != 0) world.Insert(entity, default(MatrixTag0));
            if ((mask & 2) != 0) world.Insert(entity, default(MatrixTag1));
            if ((mask & 4) != 0) world.Insert(entity, default(MatrixTag2));
            if ((mask & 8) != 0) world.Insert(entity, default(MatrixTag3));
            if (wide) world.Insert(entity, new WidePayload { Value = i });
        }
        return entities;
    }

    internal static void RequireParticles(World world, Entity[] entities)
    {
        for (int i = 0; i < entities.Length; i++)
        {
            Particle expected = ParticleAt(i);
            if (world.Get<Position>(entities[i]) != expected.Position || world.Get<Velocity>(entities[i]) != expected.Velocity)
                throw new InvalidOperationException($"Entity {i} does not hold its spawned particle.");
        }
    }

    // bevy-reference shuffles with the same SplitMix64 sequence, so both sides visit entities in the same order.
    internal static T[] Shuffled<T>(T[] items)
    {
        var result = (T[])items.Clone();
        ulong state = 0;
        for (int i = result.Length - 1; i > 0; i--)
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
                int j = (int)((z ^ (z >> 31)) % (ulong)(i + 1));
                (result[i], result[j]) = (result[j], result[i]);
            }
        }
        return result;
    }
}
