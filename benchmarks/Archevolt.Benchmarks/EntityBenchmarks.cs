using BenchmarkDotNet.Attributes;

namespace Archevolt.Benchmarks;

// Spawn and despawn change the population, so every measured invocation gets a fresh world.
[MemoryDiagnoser, MinColumn, MaxColumn, MedianColumn]
public class EntityPopulationBenchmarks
{
    private World _world = null!;
    private Entity[] _entities = null!;
    private Particle[] _bundles = null!;
    [Params(1_000, 100_000)] public int Count { get; set; }

    [GlobalSetup(Target = nameof(SpawnBatch))]
    public void CreateBundles()
    {
        _bundles = new Particle[Count];
        for (int i = 0; i < Count; i++)
            _bundles[i] = MatrixPopulation.ParticleAt(i);
    }

    [IterationSetup(Targets = [nameof(Spawn), nameof(SpawnBatch)])]
    public void CreateEmptyWorld()
    {
        _world = MatrixPopulation.Build();
        _entities = new Entity[Count];
    }

    [IterationSetup(Target = nameof(Despawn))]
    public void CreatePopulatedWorld()
    {
        _world = MatrixPopulation.Build();
        Entity[] spawned = MatrixPopulation.Populate(_world, Count);
        MatrixPopulation.RequireParticles(_world, spawned);
        _entities = MatrixPopulation.Shuffled(spawned);
    }

    [Benchmark]
    public void Spawn()
    {
        for (int i = 0; i < _entities.Length; i++)
            _entities[i] = _world.Spawn(MatrixPopulation.ParticleAt(i));
    }

    // Bevy's spawn_batch builds each bundle from an iterator during the measurement; this case reads them from an array.
    [Benchmark]
    public void SpawnBatch() => _world.SpawnBatch<Particle>(_bundles, _entities);

    [Benchmark]
    public void Despawn()
    {
        foreach (Entity entity in _entities)
            _world.Despawn(entity);
    }

    [IterationCleanup(Targets = [nameof(Spawn), nameof(SpawnBatch)])]
    public void RequireSpawned()
    {
        try { MatrixPopulation.RequireParticles(_world, _entities); }
        finally { _world.Dispose(); }
    }

    [IterationCleanup(Target = nameof(Despawn))]
    public void RequireDespawned()
    {
        try
        {
            foreach (Entity entity in _entities)
                if (_world.Contains(entity))
                    throw new InvalidOperationException("A despawned entity is still alive.");
        }
        finally { _world.Dispose(); }
    }
}

// Each operation leaves the population as it found it, so one world serves every invocation.
[MemoryDiagnoser, MinColumn, MaxColumn, MedianColumn]
public class EntityStorageBenchmarks
{
    private World _world = null!;
    private Entity[] _entities = null!;
    private Entity[] _randomOrder = null!;
    [Params(1_000, 100_000)] public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = MatrixPopulation.Build();
        _entities = MatrixPopulation.Populate(_world, Count);
        _randomOrder = MatrixPopulation.Shuffled(_entities);
        MatrixPopulation.RequireParticles(_world, _entities);
        double expected = 0;
        for (int i = 0; i < Count; i++)
            expected += i % 97;
        if (Get() != expected)
            throw new InvalidOperationException("Get read a position other than the spawned one.");
        TagRoundTrip();
        WideRoundTrip();
        RequireUnchanged();
    }

    [Benchmark]
    public double Get()
    {
        double sum = 0;
        foreach (Entity entity in _randomOrder)
            sum += _world.Get<Position>(entity).X;
        return sum;
    }

    [Benchmark]
    public void TagRoundTrip()
    {
        foreach (Entity entity in _entities)
        {
            _world.Insert(entity, default(MatrixTag0));
            _world.Remove<MatrixTag0>(entity);
        }
    }

    [Benchmark]
    public void WideRoundTrip()
    {
        for (int i = 0; i < _entities.Length; i++)
        {
            _world.Insert(_entities[i], new WidePayload { Value = i });
            _world.Remove<WidePayload>(_entities[i]);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try { RequireUnchanged(); }
        finally { _world.Dispose(); }
    }

    private void RequireUnchanged()
    {
        MatrixPopulation.RequireParticles(_world, _entities);
        foreach (Entity entity in _entities)
            if (_world.Has<MatrixTag0>(entity) || _world.Has<WidePayload>(entity))
                throw new InvalidOperationException("A round trip left a component behind.");
    }
}
