using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Archevolt.Scheduling;

namespace Archevolt.Benchmarks;

internal static class SingleItemMeasurement
{
    private const int SampleCount = 15;
    private const int MinimumWarmupBatches = 8;
    private const double WarmupMilliseconds = 1_000;
    private const double TargetBatchMilliseconds = 50;

    private enum Workload { NoOp, Action, GeneratedQuery, GeneratedEach }
    private sealed record Case(string Name, Workload Workload, int Concurrency, int Entities, int WorkItems);
    private readonly record struct Sample(int Operations, long ElapsedTicks, double NanosecondsPerRun,
        long OwnerBytes, long AllThreadBytes, int Gen0, int Gen1, int Gen2);

    private static readonly Case[] Cases =
    [
        new("noop-c1", Workload.NoOp, 1, 0, 1),
        new("noop-c4", Workload.NoOp, 4, 0, 1),
        new("action-c1", Workload.Action, 1, 0, 1),
        new("action-c4", Workload.Action, 4, 0, 1),
        new("query-1000-c1", Workload.GeneratedQuery, 1, 1_000, 1),
        new("query-1000-c4", Workload.GeneratedQuery, 4, 1_000, 1),
        new("query-100000-c1", Workload.GeneratedQuery, 1, 100_000, 1),
        new("query-100000-c4", Workload.GeneratedQuery, 4, 100_000, 1),
        new("partitioned-1000-c4", Workload.GeneratedEach, 4, 1_000, 4),
        new("partitioned-100000-c4", Workload.GeneratedEach, 4, 100_000, 4)
    ];

    internal static void Run(string[] args)
    {
        if (args is ["--single-item", "--list"])
        {
            foreach (var item in Cases) Console.WriteLine(item.Name);
            return;
        }
        if (args.Length != 3 || args[0] != "--single-item")
            throw new ArgumentException("Usage: --single-item <case> <output.json>, or --single-item --list");

        var spec = Cases.Single(c => c.Name == args[1]);
        var started = DateTimeOffset.UtcNow;
        using var workload = new BoundWorkload(spec);
        var before = workload.Validate();
        long actionCallsBefore = workload.ActionCalls;
        long totalRuns = 0;
        var pilot = new List<Sample>();
        int operations = 16;
        while (true)
        {
            var sample = Measure(workload.Schedule, operations);
            pilot.Add(sample);
            totalRuns += operations;
            if (sample.ElapsedTicks * 1_000d / Stopwatch.Frequency >= TargetBatchMilliseconds) break;
            operations = checked(operations * 2);
        }

        var warmup = new List<Sample>();
        double warmupMilliseconds = 0;
        while (warmup.Count < MinimumWarmupBatches || warmupMilliseconds < WarmupMilliseconds)
        {
            var sample = Measure(workload.Schedule, operations);
            warmup.Add(sample);
            totalRuns += operations;
            warmupMilliseconds += sample.ElapsedTicks * 1_000d / Stopwatch.Frequency;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var samples = new Sample[SampleCount];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = Measure(workload.Schedule, operations);
            totalRuns += operations;
        }
        if (spec.Workload == Workload.Action && workload.ActionCalls - actionCallsBefore != totalRuns)
            throw new InvalidOperationException("The action did not execute exactly once per Run.");
        var after = workload.Validate();
        var result = new
        {
            Case = spec.Name,
            Workload = spec.Workload.ToString(),
            spec.Concurrency,
            spec.Entities,
            ExpectedWorkItems = spec.WorkItems,
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            Framework = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            LogicalProcessors = Environment.ProcessorCount,
            TimerFrequency = Stopwatch.Frequency,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            ReadyToRun = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"),
            ServerGC = System.Runtime.GCSettings.IsServerGC,
            LibrarySha256 = HashFile(typeof(World).Assembly.Location),
            BenchmarkSha256 = HashFile(typeof(SingleItemMeasurement).Assembly.Location),
            MinimumWarmupBatches,
            WarmupMilliseconds,
            TargetBatchMilliseconds,
            SampleCount,
            OperationsPerSample = operations,
            TotalRuns = totalRuns,
            ValidationBefore = before,
            ValidationAfter = after,
            Pilot = pilot,
            Warmup = warmup,
            Samples = samples
        };
        File.WriteAllText(args[2], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(FormattableString.Invariant($"{spec.Name}: {samples.Average(s => s.NanosecondsPerRun):F3} ns/Run; validation passed"));
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static Sample Measure(BoundSchedule schedule, int operations)
    {
        long allBefore = GC.GetTotalAllocatedBytes(precise: true);
        long ownerBefore = GC.GetAllocatedBytesForCurrentThread();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        long start = Stopwatch.GetTimestamp();
        RunBatch(schedule, operations);
        long elapsed = Stopwatch.GetTimestamp() - start;
        long ownerBytes = GC.GetAllocatedBytesForCurrentThread() - ownerBefore;
        long allBytes = GC.GetTotalAllocatedBytes(precise: true) - allBefore;
        return new Sample(operations, elapsed, elapsed * (1_000_000_000d / Stopwatch.Frequency) / operations,
            ownerBytes, allBytes, GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunBatch(BoundSchedule schedule, int operations)
    {
        for (int i = 0; i < operations; i++) schedule.Run();
    }

    private sealed class BoundWorkload : IDisposable
    {
        private readonly Case _spec;
        private readonly World _world;
        private readonly Entity[] _entities;
        internal BoundSchedule Schedule { get; }
        internal long ActionCalls { get; private set; }

        internal BoundWorkload(Case spec)
        {
            _spec = spec;
            var builder = new WorldBuilder().UseParallelExecution(spec.Concurrency);
            if (spec.Entities > 0) builder.AddModule(MovementModule.Module);
            _world = builder.Build();
            _entities = new Entity[spec.Entities];
            if (spec.Entities > 0)
            {
                _world.InsertResource(new FrameTime(1f / 60));
                for (int i = 0; i < _entities.Length; i++)
                    _entities[i] = _world.Spawn(new Particle(new Position(i % 97, 0, 0), new Velocity(1, 2, 3)));
            }
            Schedule = spec.Workload switch
            {
                Workload.NoOp => BoundSchedule.Bind(_world, SystemDescriptor.Create("noop", _ => new NoOpBody()).Build()),
                Workload.Action => BoundSchedule.Bind(_world,
                    SystemDescriptor.Create("action", _ => new ActionBody(() => ActionCalls++)).Build()),
                Workload.GeneratedQuery => QueryMovement.Create(_world),
                Workload.GeneratedEach => EachMovement.Create(_world),
                _ => throw new ArgumentOutOfRangeException(nameof(spec))
            };
        }

        internal object Validate()
        {
            if (Schedule.Plan.Steps.Count != 2 || Schedule.Plan.Steps[0].Kind != StepKind.Batch ||
                Schedule.Plan.Steps[0].Count != 1 || Schedule.Plan.Steps[1].Kind != StepKind.ApplyDeferred)
                throw new InvalidOperationException("Expected one batch with one system and a final command barrier.");
            for (int i = 0; i < _entities.Length; i++)
                _world.Insert(_entities[i], new Position(i % 97, 0, 0));
            long callsBefore = ActionCalls;
            Schedule.EnableTiming();
            Schedule.Run();
            var timing = Schedule.Timings[0];
            if (timing.BodyInvocations != _spec.WorkItems || timing.BodyFailures != 0)
                throw new InvalidOperationException("Unexpected body invocation count or failure.");
            var pool = typeof(World).GetProperty("Workers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_world)!;
            int workCount = (int)pool.GetType().GetField("_count", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pool)!;
            int ownerCount = (int)typeof(BoundSchedule).GetField("_ownerCount", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Schedule)!;
            if (workCount != _spec.WorkItems || ownerCount != 0)
                throw new InvalidOperationException("Unexpected worker-item or owner-item count.");
            Schedule.EnableTiming(false);
            if (_spec.Workload == Workload.Action && ActionCalls != callsBefore + 1)
                throw new InvalidOperationException("The action did not increment its counter.");
            const float delta = 1f / 60;
            for (int i = 0; i < _entities.Length; i++)
            {
                var expected = new Position(i % 97 + delta, 2 * delta, 3 * delta);
                if (_world.Get<Position>(_entities[i]) != expected || _world.Get<Velocity>(_entities[i]) != new Velocity(1, 2, 3))
                    throw new InvalidOperationException($"Incorrect movement output at row {i}.");
            }
            return new { Batches = 1, Systems = 1, CommandBarriers = 1, WorkItems = workCount, OwnerItems = ownerCount,
                timing.BodyInvocations, CheckedRows = _entities.Length, Passed = true };
        }

        public void Dispose() { Schedule.Dispose(); _world.Dispose(); }
    }

    private sealed class NoOpBody : SystemBody
    {
        protected override void Run(SystemRun run) { }
    }

    private sealed class ActionBody(Action action) : SystemBody
    {
        protected override void Run(SystemRun run) => action();
    }
}
