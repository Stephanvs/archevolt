using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Archevolt.Benchmarks;

public static class PerformanceMeasurement
{
    private const int Warmups = 32;
    private const int Samples = 64;

    public static void Run()
    {
        Console.WriteLine($"# Runtime={RuntimeInformation.FrameworkDescription}; OS={RuntimeInformation.OSDescription}; arch={RuntimeInformation.ProcessArchitecture}; logical_cpu={Environment.ProcessorCount}; timer_hz={Stopwatch.Frequency}");
        Console.WriteLine($"# DOTNET_TieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "<default>"}; DOTNET_ReadyToRun={Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") ?? "<default>"}");
        Console.WriteLine($"# warmups={Warmups}; samples={Samples}; one operation per sample; process-wide allocated-byte counters; native bytes are post-run snapshots, not peaks");
        Console.WriteLine("scenario,entities,mean_us,median_us,p95_us,max_us,owner_bytes_per_op,all_thread_bytes_per_op,native_bytes_after");
        foreach (int count in new[] { 1_000, 100_000, 1_000_000 })
        {
            var comparison = new ReferenceComparisonBenchmarks { Count = count };
            comparison.Setup();
            try
            {
                Measure("movement-handwritten", count, comparison.Handwritten);
                Measure("movement-generated-each", count, comparison.GeneratedEach);
                Measure("movement-generated-query", count, comparison.GeneratedQuery);
                Measure("movement-generated-block", count, comparison.GeneratedBlock);
                Measure("movement-generated-block-vector", count, comparison.GeneratedBlockVector);
                Measure("movement-arch-2.1.0-inline", count, comparison.ArchInline);
            }
            finally { comparison.Cleanup(); }
        }
        foreach (int count in new[] { 1_000, 100_000, 1_000_000 })
        {
            var parallel = new MovementBenchmarks { Count = count, Concurrency = 4 };
            parallel.Setup();
            try
            {
                Measure("movement-generated-each-concurrency-4", count, parallel.GeneratedEach);
                Measure("movement-generated-query-concurrency-4", count, parallel.GeneratedQuery);
                Measure("movement-generated-block-concurrency-4", count, parallel.GeneratedBlock);
                Measure("movement-generated-block-vector-concurrency-4", count, parallel.GeneratedBlockVector);
            }
            finally { parallel.Cleanup(); }
        }
        foreach (int archetypes in new[] { 1, 16 })
        foreach (bool wide in new[] { false, true })
        {
            var traversal = new TraversalMatrixBenchmarks { Count = 100_000, Archetypes = archetypes, Wide = wide };
            traversal.Setup();
            try
            {
                Measure($"traversal-archetypes-{archetypes}-payload-{(wide ? 256 : 0)}",
                    traversal.Count, traversal.GeneratedEach, () => traversal.NativeBytes);
            }
            finally { traversal.Cleanup(); }
        }
        foreach (int count in new[] { 1_000, 100_000 })
        {
            var filters = new FilterBenchmarks { Count = count };
            filters.Setup();
            try
            {
                Measure("tracked-write", count, filters.TrackedWrite, () => filters.NativeBytes);
                Measure("changed-none", count, filters.ChangedNone, () => filters.NativeBytes);
                if (filters.LastScanCount != 0) throw new InvalidOperationException("Changed scan should consume the prior writes.");
                Measure("write-then-changed-all", count, filters.WriteThenChangedAll, () => filters.NativeBytes);
                if (filters.LastScanCount != count) throw new InvalidOperationException("Changed scan omitted written rows.");
                Measure("optional-half-present", count, filters.OptionalHalfPresent, () => filters.NativeBytes);
                if (filters.LastScanCount != count) throw new InvalidOperationException("Optional scan omitted rows.");
            }
            finally { filters.Cleanup(); }
        }
        var lifecycle = new LifecycleBenchmarks();
        lifecycle.Setup();
        try
        {
            Measure("world-register-create-dispose", 0, lifecycle.CreateDisposeWorld);
            Measure("schedule-bind-dispose", 1_000, lifecycle.BindDisposeSchedule);
            Measure("schedule-bind-first-refresh-run-dispose", 1_000, lifecycle.BindFirstRefreshAndRun);
        }
        finally { lifecycle.Cleanup(); }
        var refresh = new QueryRefreshBenchmarks();
        Measure("first-query-refresh-after-new-archetype", 1_000, refresh.FirstRefreshAndRun,
            setup: refresh.Setup, cleanup: refresh.Cleanup);
        var churn = new StructuralChurnBenchmarks { Count = 1_000 };
        churn.Setup();
        try
        {
            Measure("warmed-idle-tag-roundtrip", churn.Count, churn.IdleTagRoundTrip, () => churn.NativeBytes);
            Measure("warmed-deferred-tag-roundtrip", churn.Count, churn.DeferredTagRoundTrip, () => churn.NativeBytes);
        }
        finally { churn.Cleanup(); }
        foreach (int capacity in new[] { 0, 65_536 })
        {
            var commands = new CommandCapacityBenchmarks { Count = 1_000, InitialCommandBytes = capacity };
            Measure($"first-command-playback-capacity-{capacity}", commands.Count, commands.FirstPlayback,
                setup: commands.Setup, cleanup: commands.Cleanup);
        }
        foreach (int concurrency in new[] { 1, 4 })
        {
            using var world = MatrixPopulation.Build(concurrency);
            using var schedule = EmptyUpdate.Create(world);
            using var barrier = EmptyBarrierUpdate.Create(world);
            Measure($"empty-schedule-concurrency-{concurrency}", 0, () => schedule.Run(), () => world.NativeBytes);
            Measure($"two-empty-systems-command-barrier-concurrency-{concurrency}", 0, () => barrier.Run(), () => world.NativeBytes);
        }
        Console.WriteLine("# matrix=ok");
    }

    private static void Measure(string name, int entities, Action operation, Func<long>? nativeBytes = null,
        Action? setup = null, Action? cleanup = null)
    {
        for (int i = 0; i < Warmups; i++)
        {
            setup?.Invoke();
            try { operation(); }
            finally { cleanup?.Invoke(); }
        }
        var durations = new double[Samples];
        long ownerBytes = 0, allBytes = 0;
        for (int i = 0; i < Samples; i++)
        {
            setup?.Invoke();
            try
            {
                long ownerBefore = GC.GetAllocatedBytesForCurrentThread();
                long allBefore = GC.GetTotalAllocatedBytes(precise: true);
                long start = Stopwatch.GetTimestamp();
                operation();
                durations[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                ownerBytes += GC.GetAllocatedBytesForCurrentThread() - ownerBefore;
                allBytes += GC.GetTotalAllocatedBytes(precise: true) - allBefore;
            }
            finally { cleanup?.Invoke(); }
        }
        Array.Sort(durations);
        double mean = durations.Average();
        double median = (durations[(Samples / 2) - 1] + durations[Samples / 2]) / 2;
        double p95 = durations[(int)Math.Ceiling(Samples * 0.95) - 1];
        string native = nativeBytes is null ? "" : nativeBytes().ToString(CultureInfo.InvariantCulture);
        Console.WriteLine(FormattableString.Invariant(
            $"{name},{entities},{mean:F3},{median:F3},{p95:F3},{durations[^1]:F3},{(double)ownerBytes / Samples:F3},{(double)allBytes / Samples:F3},{native}"));
    }
}
