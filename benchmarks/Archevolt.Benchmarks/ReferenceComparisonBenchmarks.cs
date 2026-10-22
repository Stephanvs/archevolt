using System.Runtime.CompilerServices;
using Arch.Core;
using BenchmarkDotNet.Attributes;
using ArchWorld = Arch.Core.World;

namespace Archevolt.Benchmarks;

[MemoryDiagnoser, MinColumn, MaxColumn, MedianColumn]
public class ReferenceComparisonBenchmarks
{
    private MovementBenchmarks _archevolt = null!;
    private ArchWorld _arch = null!;
    private QueryDescription _description;

    [Params(100_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _archevolt = new MovementBenchmarks { Count = Count, Concurrency = 1 };
        _archevolt.Setup();
        _arch = ArchWorld.Create();
        _description = new QueryDescription().WithAll<Position, Velocity>();
        for (int i = 0; i < Count; i++)
            _arch.Create(new Position(i % 97, 0, 0), new Velocity(1, 2, 3));
        ArchInline();
    }

    [Benchmark(Baseline = true)]
    public void Handwritten() => _archevolt.Handwritten();
    [Benchmark]
    public void GeneratedEach() => _archevolt.GeneratedEach();
    [Benchmark]
    public void GeneratedQuery() => _archevolt.GeneratedQuery();
    [Benchmark]
    public void GeneratedBlock() => _archevolt.GeneratedBlock();
    [Benchmark]
    public void GeneratedBlockVector() => _archevolt.GeneratedBlockVector();
    [Benchmark]
    public void ArchInline() => _arch.InlineQuery<Integrate, Position, Velocity>(in _description);

    [GlobalCleanup]
    public void Cleanup()
    {
        _arch?.Dispose();
        _archevolt?.Cleanup();
    }

    private struct Integrate : IForEach<Position, Velocity>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(ref Position position, ref Velocity velocity)
        {
            const float delta = 1f / 60;
            position = new Position(position.X + velocity.X * delta,
                position.Y + velocity.Y * delta, position.Z + velocity.Z * delta);
        }
    }
}
