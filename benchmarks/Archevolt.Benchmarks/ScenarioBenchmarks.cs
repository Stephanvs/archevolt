using BenchmarkDotNet.Attributes;

namespace Archevolt.Benchmarks;

[MemoryDiagnoser, MinColumn, MaxColumn, MedianColumn]
public class TraversalMatrixBenchmarks
{
    private World _world = null!;
    private BoundSchedule _schedule = null!;
    [Params(1_000, 100_000, 1_000_000)] public int Count { get; set; }
    [Params(1, 16)] public int Archetypes { get; set; }
    [Params(false, true)] public bool Wide { get; set; }
    public long NativeBytes => _world.NativeBytes;

    [GlobalSetup]
    public void Setup()
    {
        _world = MatrixPopulation.Build();
        MatrixPopulation.Populate(_world, Count, Archetypes, Wide);
        _schedule = EachMovement.Create(_world);
        _schedule.Run();
    }
    [Benchmark] public void GeneratedEach() => _schedule.Run();
    [GlobalCleanup] public void Cleanup() { _schedule?.Dispose(); _world?.Dispose(); }
}

[MemoryDiagnoser, MinColumn, MaxColumn, MedianColumn]
public class FilterBenchmarks
{
    private World _world = null!;
    private BoundSchedule _writer = null!;
    private BoundSchedule _changed = null!;
    private BoundSchedule _optional = null!;
    [Params(1_000, 100_000)] public int Count { get; set; }
    public long NativeBytes => _world.NativeBytes;
    public long LastScanCount => _world.GetResource<ScanTotal>().Count;

    [GlobalSetup]
    public void Setup()
    {
        _world = MatrixPopulation.Build();
        _world.InsertResource(new ScanTotal());
        for (int i = 0; i < Count; i++)
        {
            Entity entity = _world.Spawn(new TrackedParticle(new TrackedPosition(i % 97, 0, 0), new Velocity(1, 2, 3)));
            if ((i & 1) == 0) _world.Insert(entity, new Acceleration(0.25f));
        }
        _writer = TrackedMovement.Create(_world);
        _changed = ChangedScan.Create(_world);
        _optional = OptionalScan.Create(_world);
        _changed.Run();
        _optional.Run();
    }
    [Benchmark] public void TrackedWrite() => _writer.Run();
    [Benchmark] public void ChangedNone() => _changed.Run();
    [Benchmark] public void WriteThenChangedAll() { _writer.Run(); _changed.Run(); }
    [Benchmark] public void OptionalHalfPresent() => _optional.Run();
    [GlobalCleanup]
    public void Cleanup() { _writer?.Dispose(); _changed?.Dispose(); _optional?.Dispose(); _world?.Dispose(); }
}

[MemoryDiagnoser, MinColumn, MaxColumn, MedianColumn]
public class LifecycleBenchmarks
{
    private World _world = null!;
    [GlobalSetup]
    public void Setup()
    {
        _world = MatrixPopulation.Build();
        MatrixPopulation.Populate(_world, 1000, 16);
    }
    [Benchmark]
    public void CreateDisposeWorld()
    {
        using var world = MatrixPopulation.Build();
    }
    [Benchmark]
    public void BindDisposeSchedule()
    {
        using var schedule = EachMovement.Create(_world);
    }
    [Benchmark]
    public void BindFirstRefreshAndRun()
    {
        using var schedule = EachMovement.Create(_world);
        schedule.Run();
    }
    [GlobalCleanup] public void Cleanup() => _world?.Dispose();
}

[MemoryDiagnoser, MinColumn, MaxColumn, MedianColumn]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 8, invocationCount: 1)]
public class QueryRefreshBenchmarks
{
    private World _world = null!;
    private BoundSchedule _schedule = null!;
    [IterationSetup]
    public void Setup()
    {
        _world = MatrixPopulation.Build();
        var entities = MatrixPopulation.Populate(_world, 1_000);
        _schedule = EachMovement.Create(_world);
        _schedule.Run();
        for (int i = 0; i < entities.Length; i += 2)
            _world.Insert(entities[i], default(MatrixTag0));
    }
    [Benchmark] public void FirstRefreshAndRun() => _schedule.Run();
    [IterationCleanup] public void Cleanup() { _schedule?.Dispose(); _world?.Dispose(); }
}

[MemoryDiagnoser, MinColumn, MaxColumn, MedianColumn]
public class StructuralChurnBenchmarks
{
    private World _world = null!;
    private Entity[] _entities = null!;
    private BoundSchedule _commands = null!;
    [Params(1_000)] public int Count { get; set; }
    public long NativeBytes => _world.NativeBytes;

    [GlobalSetup]
    public void Setup()
    {
        _world = MatrixPopulation.Build(commandBytes: 65_536);
        _entities = MatrixPopulation.Populate(_world, Count);
        _world.InsertResource(new MatrixTargets(_entities));
        _commands = CommandToggle.Create(_world);
        IdleTagRoundTrip();
        DeferredTagRoundTrip();
    }
    [Benchmark]
    public void IdleTagRoundTrip()
    {
        foreach (Entity entity in _entities)
        {
            _world.Insert(entity, default(MatrixTag0));
            _world.Remove<MatrixTag0>(entity);
        }
    }
    [Benchmark] public void DeferredTagRoundTrip() => _commands.Run();
    [GlobalCleanup] public void Cleanup() { _commands?.Dispose(); _world?.Dispose(); }
}

[MemoryDiagnoser, MinColumn, MaxColumn, MedianColumn]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 8, invocationCount: 1)]
public class CommandCapacityBenchmarks
{
    private World _world = null!;
    private BoundSchedule _commands = null!;
    [Params(0, 65_536)] public int InitialCommandBytes { get; set; }
    [Params(1_000)] public int Count { get; set; }

    [IterationSetup]
    public void Setup()
    {
        _world = MatrixPopulation.Build(commandBytes: InitialCommandBytes);
        var entities = MatrixPopulation.Populate(_world, Count);
        foreach (Entity entity in entities)
        {
            _world.Insert(entity, default(MatrixTag0));
            _world.Remove<MatrixTag0>(entity);
        }
        _world.InsertResource(new MatrixTargets(entities));
        _commands = CommandToggle.Create(_world);
    }
    [Benchmark] public void FirstPlayback() => _commands.Run();
    [IterationCleanup] public void Cleanup() { _commands?.Dispose(); _world?.Dispose(); }
}
