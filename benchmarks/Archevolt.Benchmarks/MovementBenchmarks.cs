using System.Runtime.InteropServices;
using Archevolt;
using BenchmarkDotNet.Attributes;

namespace Archevolt.Benchmarks;

[MemoryDiagnoser]
public unsafe class MovementBenchmarks
{
    private Position* _positions;
    private Velocity* _velocities;
    private World _world = null!;
    private BoundSchedule _each = null!;
    private BoundSchedule _query = null!;

    [Params(1_000, 100_000, 1_000_000)]
    public int Count { get; set; }

    [Params(1, 4)]
    public int Concurrency { get; set; } = 1;

    [GlobalSetup]
    public void Setup()
    {
        var positionBytes = checked((nuint)Count * (nuint)sizeof(Position));
        var velocityBytes = checked((nuint)Count * (nuint)sizeof(Velocity));
        _positions = (Position*)NativeMemory.AlignedAlloc((positionBytes + 63) & ~(nuint)63, 64);
        _velocities = (Velocity*)NativeMemory.AlignedAlloc((velocityBytes + 63) & ~(nuint)63, 64);
        if (_positions == null || _velocities == null)
        {
            NativeMemory.AlignedFree(_positions);
            NativeMemory.AlignedFree(_velocities);
            _positions = null;
            _velocities = null;
            throw new OutOfMemoryException();
        }

        _world = new WorldBuilder().AddModule(MovementModule.Module).UseParallelExecution(Concurrency).Build();
        _world.InsertResource(new FrameTime(1f / 60));
        for (var i = 0; i < Count; i++)
        {
            var position = new Position(i % 97, 0, 0);
            var velocity = new Velocity(1, 2, 3);
            _positions[i] = position;
            _velocities[i] = velocity;
            _world.Spawn(new Particle(position, velocity));
        }
        _each = EachMovement.Create(_world);
        _query = QueryMovement.Create(_world);
        _each.Run();
        _query.Run();
    }

    [Benchmark(Baseline = true)]
    public void Handwritten()
    {
        var positions = new Span<Position>(_positions, Count);
        var velocities = new ReadOnlySpan<Velocity>(_velocities, Count);
        const float delta = 1f / 60;
        for (var i = 0; i < positions.Length; i++)
        {
            ref var p = ref positions[i];
            ref readonly var v = ref velocities[i];
            p = new Position(p.X + v.X * delta, p.Y + v.Y * delta, p.Z + v.Z * delta);
        }
    }

    [Benchmark]
    public void GeneratedEach() => _each.Run();

    [Benchmark]
    public void GeneratedQuery() => _query.Run();

    [GlobalCleanup]
    public void Cleanup()
    {
        _each?.Dispose();
        _query?.Dispose();
        _world?.Dispose();
        NativeMemory.AlignedFree(_positions);
        NativeMemory.AlignedFree(_velocities);
        _positions = null;
        _velocities = null;
    }
}

[Component] public readonly record struct Position(float X, float Y, float Z);
[Component] public readonly record struct Velocity(float X, float Y, float Z);
[Resource] public readonly record struct FrameTime(float Delta);
[Bundle] public readonly partial record struct Particle(Position Position, Velocity Velocity);

[Query, Write<Position>, Read<Velocity>]
public readonly ref partial struct Moving;

public static partial class Movement
{
    [Each(Partitioned = true)]
    public static void Each(scoped ref Position position, scoped in Velocity velocity, scoped Res<FrameTime> time)
    {
        var delta = time.Value.Delta;
        position = new Position(position.X + velocity.X * delta, position.Y + velocity.Y * delta, position.Z + velocity.Z * delta);
    }

    [EcsSystem]
    public static void Query(scoped Moving moving, scoped Res<FrameTime> time)
    {
        var delta = time.Value.Delta;
        foreach (var row in moving)
        {
            ref var p = ref row.Position;
            ref readonly var v = ref row.Velocity;
            p = new Position(p.X + v.X * delta, p.Y + v.Y * delta, p.Z + v.Z * delta);
        }
    }
}

[EcsModule, IncludeSystems(typeof(Movement)), IncludeBundle<Particle>, IncludeResource<FrameTime>]
public static partial class MovementModule;

[Schedule, Run(typeof(Movement), nameof(Movement.Each), Order = 0)]
public sealed partial class EachMovement;

[Schedule, Run(typeof(Movement), nameof(Movement.Query), Order = 0)]
public sealed partial class QueryMovement;
