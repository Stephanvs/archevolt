using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Archevolt.Benchmarks;

// ROADMAP step 7 asks whether 32-bit change ticks would speed up tracked iteration. These cases run the per-row work
// of the generated tracked write and Changed scan over native columns, once with the runtime's 64-bit ticks and once
// with 32-bit ticks compared the way Bevy compares them. Only the tick width and comparison differ, so the gap bounds
// what 32-bit table rows could gain. Not part of the baseline set; run with --filter *TickWidthBenchmarks*.
[MemoryDiagnoser, MinColumn, MaxColumn, MedianColumn]
public unsafe class TickWidthBenchmarks
{
    // Bevy's MAX_CHANGE_AGE: ticks older than this are clamped by a periodic pass, so wrapping comparisons stay exact.
    private const uint MaxChangeAge = uint.MaxValue - (2 * (uint.MaxValue / 3) - 1);
    private const float Delta = 1f / 60;
    private TrackedPosition* _positions;
    private Velocity* _velocities;
    private ulong* _wide;
    private uint* _narrow;
    private ulong _now;
    [Params(100_000, 1_000_000)] public int Count { get; set; }
    public long LastScanCount { get; private set; }

    [GlobalSetup]
    public void Setup()
    {
        _positions = Allocate<TrackedPosition>();
        _velocities = Allocate<Velocity>();
        _wide = Allocate<ulong>();
        _narrow = Allocate<uint>();
        for (int i = 0; i < Count; i++)
        {
            _positions[i] = new TrackedPosition(i % 97, 0, 0);
            _velocities[i] = new Velocity(1, 2, 3);
        }
        // Start near the 32-bit wrap so the narrow comparisons cross it during the run.
        _now = uint.MaxValue - 1_000;
        new Span<ulong>(_wide, Count).Fill(_now);
        new Span<uint>(_narrow, Count).Fill((uint)_now);
    }

    private T* Allocate<T>() where T : unmanaged =>
        (T*)NativeMemory.AlignedAlloc(((nuint)Count * (nuint)sizeof(T) + 63) & ~(nuint)63, 64);

    [Benchmark] public void TrackedWrite64() => Write(_wide, ++_now);
    [Benchmark] public void TrackedWrite32() => Write(_narrow, (uint)++_now);

    [Benchmark]
    public void WriteThenChanged64()
    {
        ulong last = _now;
        ulong now = ++_now;
        Write(_wide, now);
        ulong scan = ++_now;
        long count = 0;
        double sum = 0;
        for (int row = 0; row < Count; row++)
        {
            ulong stamp = _wide[row];
            if (stamp > last && stamp <= scan)
            {
                count++;
                sum += _positions[row].X;
            }
        }
        Publish(count, sum);
    }

    [Benchmark]
    public void WriteThenChanged32()
    {
        uint last = (uint)_now;
        uint now = (uint)++_now;
        Write(_narrow, now);
        uint scan = (uint)++_now;
        uint sinceLast = Math.Min(scan - last, MaxChangeAge);
        long count = 0;
        double sum = 0;
        for (int row = 0; row < Count; row++)
        {
            if (sinceLast > Math.Min(scan - _narrow[row], MaxChangeAge))
            {
                count++;
                sum += _positions[row].X;
            }
        }
        Publish(count, sum);
    }

    private void Write<TTick>(TTick* ticks, TTick now) where TTick : unmanaged
    {
        for (int row = 0; row < Count; row++)
        {
            ref TrackedPosition position = ref _positions[row];
            Velocity velocity = _velocities[row];
            ticks[row] = now;
            position = new TrackedPosition(position.X + velocity.X * Delta,
                position.Y + velocity.Y * Delta, position.Z + velocity.Z * Delta);
        }
    }

    private void Publish(long count, double sum)
    {
        if (count != Count || double.IsNaN(sum))
            throw new InvalidOperationException($"Changed scan saw {count} rows, not {Count}.");
        LastScanCount = count;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        NativeMemory.AlignedFree(_positions);
        NativeMemory.AlignedFree(_velocities);
        NativeMemory.AlignedFree(_wide);
        NativeMemory.AlignedFree(_narrow);
    }
}
