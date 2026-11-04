using Archevolt;
using BenchmarkDotNet.Attributes;

namespace Archevolt.Benchmarks;

[MemoryDiagnoser]
public sealed class QueryPartitionBenchmarks
{
    private World _world = null!;
    private Entity[] _entities = [];
    private BoundSchedule[] _schedules = [];

    [Params(0, 1, 100, 1_000, 4_096, 16_384, 32_768, 65_536, 100_000, 1_000_000)]
    public int Count { get; set; }

    [Params(1, 4)]
    public int Concurrency { get; set; } = 1;

    [GlobalSetup]
    public void Setup()
    {
        _world = new WorldBuilder().AddModule(PartitionMovementModule.Module)
            .UseParallelExecution(Concurrency).Build();
        _world.InsertResource(new FrameTime(1));
        _entities = new Entity[Count];
        for (int i = 0; i < Count; i++)
            _entities[i] = _world.Spawn(new Particle(new Position(i % 97, 0, 0), new Velocity(1, 2, 3)));
        _schedules =
        [
            PartitionedEachStudy.Create(_world), DirectEachStudy.Create(_world),
            PartitionedBlockStudy.Create(_world), DirectBlockStudy.Create(_world),
            PartitionedVectorStudy.Create(_world), DirectVectorStudy.Create(_world)
        ];
        ValidateVariants("setup");
    }

    internal PartitionMeasurementCase[] MeasurementCases() =>
    [
        new("each-partitioned", Count, Concurrency, PartitionedEach),
        new("each-direct", Count, Concurrency, DirectEach),
        new("block-partitioned", Count, Concurrency, PartitionedBlock),
        new("block-direct", Count, Concurrency, DirectBlock),
        new("vector-partitioned", Count, Concurrency, PartitionedVector),
        new("vector-direct", Count, Concurrency, DirectVector)
    ];

    [Benchmark] public void PartitionedEach() => _schedules[0].Run();
    [Benchmark] public void DirectEach() => _schedules[1].Run();
    [Benchmark] public void PartitionedBlock() => _schedules[2].Run();
    [Benchmark] public void DirectBlock() => _schedules[3].Run();
    [Benchmark] public void PartitionedVector() => _schedules[4].Run();
    [Benchmark] public void DirectVector() => _schedules[5].Run();

    internal void ValidateState(int operations, string phase)
    {
        for (int i = 0; i < Count; i++)
        {
            var expected = new Position(i % 97 + operations, 2 * operations, 3 * operations);
            if (_world.Get<Position>(_entities[i]) != expected ||
                _world.Get<Velocity>(_entities[i]) != new Velocity(1, 2, 3))
                throw new InvalidOperationException($"{phase}: incorrect movement at entity {i} after {operations} operations.");
        }
    }

    internal void ResetPositions()
    {
        for (int i = 0; i < Count; i++)
            _world.Insert(_entities[i], new Position(i % 97, 0, 0));
    }

    private void ValidateVariants(string phase)
    {
        for (int variant = 0; variant < _schedules.Length; variant++)
        {
            ResetPositions();
            _schedules[variant].Run();
            ValidateState(1, $"{phase}, variant {variant}");
        }
        ResetPositions();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (_schedules.Length != 0) ValidateVariants("cleanup");
        }
        finally
        {
            foreach (var schedule in _schedules) schedule.Dispose();
            _schedules = [];
            _world?.Dispose();
        }
    }
}

public static partial class PartitionMovement
{
    [Each(Partitioned = true)]
    public static void PartitionedEach(scoped ref Position position, scoped in Velocity velocity, scoped Res<FrameTime> time)
        => Movement.Each(ref position, in velocity, time);

    [Each]
    public static void DirectEach(scoped ref Position position, scoped in Velocity velocity, scoped Res<FrameTime> time)
        => Movement.Each(ref position, in velocity, time);

    [EachBlock(Partitioned = true)]
    public static void PartitionedBlock(scoped Span<Position> positions, scoped ReadOnlySpan<Velocity> velocities, scoped Res<FrameTime> time)
        => Movement.Block(positions, velocities, time);

    [EachBlock]
    public static void DirectBlock(scoped Span<Position> positions, scoped ReadOnlySpan<Velocity> velocities, scoped Res<FrameTime> time)
        => Movement.Block(positions, velocities, time);

    [EachBlock(Partitioned = true)]
    public static void PartitionedVector(scoped Span<Position> positions, scoped ReadOnlySpan<Velocity> velocities, scoped Res<FrameTime> time)
        => Movement.BlockVector(positions, velocities, time);

    [EachBlock]
    public static void DirectVector(scoped Span<Position> positions, scoped ReadOnlySpan<Velocity> velocities, scoped Res<FrameTime> time)
        => Movement.BlockVector(positions, velocities, time);
}

[EcsModule, IncludeSystems(typeof(PartitionMovement)), IncludeBundle<Particle>, IncludeResource<FrameTime>]
public static partial class PartitionMovementModule;

[Schedule, Run(typeof(PartitionMovement), nameof(PartitionMovement.PartitionedEach))]
public sealed partial class PartitionedEachStudy;

[Schedule, Run(typeof(PartitionMovement), nameof(PartitionMovement.DirectEach))]
public sealed partial class DirectEachStudy;

[Schedule, Run(typeof(PartitionMovement), nameof(PartitionMovement.PartitionedBlock))]
public sealed partial class PartitionedBlockStudy;

[Schedule, Run(typeof(PartitionMovement), nameof(PartitionMovement.DirectBlock))]
public sealed partial class DirectBlockStudy;

[Schedule, Run(typeof(PartitionMovement), nameof(PartitionMovement.PartitionedVector))]
public sealed partial class PartitionedVectorStudy;

[Schedule, Run(typeof(PartitionMovement), nameof(PartitionMovement.DirectVector))]
public sealed partial class DirectVectorStudy;


[MemoryDiagnoser]
public class LargePartitionRegressionBenchmarks
{
    private readonly QueryPartitionBenchmarks _workload = new() { Count = 1_000_000, Concurrency = 4 };

    [GlobalSetup] public void Setup() => _workload.Setup();
    [Benchmark] public void PartitionedVector() => _workload.PartitionedVector();
    [GlobalCleanup] public void Cleanup() => _workload.Cleanup();
}
