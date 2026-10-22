using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
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
    private BoundSchedule _block = null!;
    private BoundSchedule _blockVector = null!;
    private Entity[] _entities = null!;

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
        _entities = new Entity[Count];
        for (var i = 0; i < Count; i++)
        {
            var position = new Position(i % 97, 0, 0);
            var velocity = new Velocity(1, 2, 3);
            _positions[i] = position;
            _velocities[i] = velocity;
            _entities[i] = _world.Spawn(new Particle(position, velocity));
        }
        _each = EachMovement.Create(_world);
        _query = QueryMovement.Create(_world);
        _block = BlockMovement.Create(_world);
        _blockVector = BlockVectorMovement.Create(_world);
        ValidateMovement("setup");
    }

    private void ValidateMovement(string phase)
    {
        const float delta = 1f / 60;
        var velocity = new Velocity(1, 2, 3);
        foreach (var (name, schedule) in new[]
        {
            (nameof(GeneratedEach), _each), (nameof(GeneratedQuery), _query),
            (nameof(GeneratedBlock), _block), (nameof(GeneratedBlockVector), _blockVector)
        })
        {
            for (var i = 0; i < Count; i++)
            {
                var position = new Position(i % 97, 0, 0);
                _positions[i] = position;
                _velocities[i] = velocity;
                _world.Insert(_entities[i], position);
                _world.Insert(_entities[i], velocity);
            }
            schedule.Run();
            Handwritten();
            for (var i = 0; i < Count; i++)
            {
                var expected = new Position(i % 97 + delta, 2 * delta, 3 * delta);
                if (_positions[i] != expected || _velocities[i] != velocity)
                    throw new InvalidOperationException($"{phase}: Handwritten produced an incorrect result at entity {i}.");
                if (_world.Get<Position>(_entities[i]) != expected || _world.Get<Velocity>(_entities[i]) != velocity)
                    throw new InvalidOperationException($"{phase}: {name} produced an incorrect result at entity {i}.");
            }
        }
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

    [Benchmark]
    public void GeneratedBlock() => _block.Run();

    [Benchmark]
    public void GeneratedBlockVector() => _blockVector.Run();

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (_blockVector is not null) ValidateMovement("cleanup");
        }
        finally
        {
            _each?.Dispose();
            _query?.Dispose();
            _block?.Dispose();
            _blockVector?.Dispose();
            _world?.Dispose();
            NativeMemory.AlignedFree(_positions);
            NativeMemory.AlignedFree(_velocities);
            _positions = null;
            _velocities = null;
        }
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

    [EachBlock(Partitioned = true)]
    public static void Block(scoped Span<Position> positions, scoped ReadOnlySpan<Velocity> velocities, scoped Res<FrameTime> time)
    {
        var delta = time.Value.Delta;
        velocities = velocities[..positions.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            ref var p = ref positions[i];
            ref readonly var v = ref velocities[i];
            p = new Position(p.X + v.X * delta, p.Y + v.Y * delta, p.Z + v.Z * delta);
        }
    }

    [EachBlock(Partitioned = true)]
    public static void BlockVector(scoped Span<Position> positions, scoped ReadOnlySpan<Velocity> velocities, scoped Res<FrameTime> time)
    {
        var p = MemoryMarshal.Cast<Position, float>(positions);
        var v = MemoryMarshal.Cast<Velocity, float>(velocities)[..p.Length];
        var delta = time.Value.Delta;
        var d = Vector256.Create(delta);
        ref var pr = ref MemoryMarshal.GetReference(p);
        ref var vr = ref MemoryMarshal.GetReference(v);
        var i = 0;
        for (; i <= p.Length - Vector256<float>.Count; i += Vector256<float>.Count)
            (Vector256.LoadUnsafe(ref pr, (nuint)i) + Vector256.LoadUnsafe(ref vr, (nuint)i) * d).StoreUnsafe(ref pr, (nuint)i);
        for (; i < p.Length; i++)
            p[i] += v[i] * delta;
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

[Schedule, Run(typeof(Movement), nameof(Movement.Block), Order = 0)]
public sealed partial class BlockMovement;

[Schedule, Run(typeof(Movement), nameof(Movement.BlockVector), Order = 0)]
public sealed partial class BlockVectorMovement;
