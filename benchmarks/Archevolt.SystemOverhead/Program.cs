using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Archevolt;
using Archevolt.Scheduling;

namespace Archevolt.SystemOverhead;

[Component] public readonly record struct State(double Value);
[Component] public readonly record struct Input(double Value);
[Resource] public readonly record struct Clock(double Delta);
[Bundle] public readonly partial record struct Part(State State, Input Input);

public readonly struct Tag0;
public readonly struct Tag1;
public readonly struct Tag2;
public readonly struct Tag3;
public readonly struct Tag4;
public readonly struct Tag5;

[Query, Write<State>, Read<Input>]
public readonly ref partial struct Parts;

// Every system adds input * delta to the state of each entity it visits. The arithmetic is double precision, so none
// of them runs as a SIMD kernel, and each stands for a small piece of model logic.
//
// Each kind exists in eight copies, and a case uses them in turn. A schedule calls every system through one virtual
// call. With one copy of a kind, the JIT learns the single target of that call and inlines the whole system into the
// executor. A schedule of many different systems never gets that.
public static partial class Model
{
    public const double RetireBelow = -1e6;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Step(ref State state, in Input input, double delta) => state = new State(state.Value + input.Value * delta);

    static void StepAll(scoped Parts parts, double delta)
    {
        foreach (var row in parts) row.State = new State(row.State.Value + row.Input.Value * delta);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void StepAndRetire(Entity entity, ref State state, in Input input, double delta, scoped Commands commands)
    {
        Step(ref state, in input, delta);
        if (state.Value < RetireBelow) commands.Despawn(entity);
    }

    [Each] public static void Integrate0(scoped ref State state, scoped in Input input, scoped Res<Clock> clock) => Step(ref state, in input, clock.Value.Delta);
    [Each] public static void Integrate1(scoped ref State state, scoped in Input input, scoped Res<Clock> clock) => Step(ref state, in input, clock.Value.Delta);
    [Each] public static void Integrate2(scoped ref State state, scoped in Input input, scoped Res<Clock> clock) => Step(ref state, in input, clock.Value.Delta);
    [Each] public static void Integrate3(scoped ref State state, scoped in Input input, scoped Res<Clock> clock) => Step(ref state, in input, clock.Value.Delta);
    [Each] public static void Integrate4(scoped ref State state, scoped in Input input, scoped Res<Clock> clock) => Step(ref state, in input, clock.Value.Delta);
    [Each] public static void Integrate5(scoped ref State state, scoped in Input input, scoped Res<Clock> clock) => Step(ref state, in input, clock.Value.Delta);
    [Each] public static void Integrate6(scoped ref State state, scoped in Input input, scoped Res<Clock> clock) => Step(ref state, in input, clock.Value.Delta);
    [Each] public static void Integrate7(scoped ref State state, scoped in Input input, scoped Res<Clock> clock) => Step(ref state, in input, clock.Value.Delta);

    [EcsSystem] public static void IntegrateQuery0(scoped Parts parts, scoped Res<Clock> clock) => StepAll(parts, clock.Value.Delta);
    [EcsSystem] public static void IntegrateQuery1(scoped Parts parts, scoped Res<Clock> clock) => StepAll(parts, clock.Value.Delta);
    [EcsSystem] public static void IntegrateQuery2(scoped Parts parts, scoped Res<Clock> clock) => StepAll(parts, clock.Value.Delta);
    [EcsSystem] public static void IntegrateQuery3(scoped Parts parts, scoped Res<Clock> clock) => StepAll(parts, clock.Value.Delta);
    [EcsSystem] public static void IntegrateQuery4(scoped Parts parts, scoped Res<Clock> clock) => StepAll(parts, clock.Value.Delta);
    [EcsSystem] public static void IntegrateQuery5(scoped Parts parts, scoped Res<Clock> clock) => StepAll(parts, clock.Value.Delta);
    [EcsSystem] public static void IntegrateQuery6(scoped Parts parts, scoped Res<Clock> clock) => StepAll(parts, clock.Value.Delta);
    [EcsSystem] public static void IntegrateQuery7(scoped Parts parts, scoped Res<Clock> clock) => StepAll(parts, clock.Value.Delta);

    [Each] public static void Retire0(Entity entity, scoped ref State state, scoped in Input input, scoped Res<Clock> clock, scoped Commands commands) => StepAndRetire(entity, ref state, in input, clock.Value.Delta, commands);
    [Each] public static void Retire1(Entity entity, scoped ref State state, scoped in Input input, scoped Res<Clock> clock, scoped Commands commands) => StepAndRetire(entity, ref state, in input, clock.Value.Delta, commands);
    [Each] public static void Retire2(Entity entity, scoped ref State state, scoped in Input input, scoped Res<Clock> clock, scoped Commands commands) => StepAndRetire(entity, ref state, in input, clock.Value.Delta, commands);
    [Each] public static void Retire3(Entity entity, scoped ref State state, scoped in Input input, scoped Res<Clock> clock, scoped Commands commands) => StepAndRetire(entity, ref state, in input, clock.Value.Delta, commands);
    [Each] public static void Retire4(Entity entity, scoped ref State state, scoped in Input input, scoped Res<Clock> clock, scoped Commands commands) => StepAndRetire(entity, ref state, in input, clock.Value.Delta, commands);
    [Each] public static void Retire5(Entity entity, scoped ref State state, scoped in Input input, scoped Res<Clock> clock, scoped Commands commands) => StepAndRetire(entity, ref state, in input, clock.Value.Delta, commands);
    [Each] public static void Retire6(Entity entity, scoped ref State state, scoped in Input input, scoped Res<Clock> clock, scoped Commands commands) => StepAndRetire(entity, ref state, in input, clock.Value.Delta, commands);
    [Each] public static void Retire7(Entity entity, scoped ref State state, scoped in Input input, scoped Res<Clock> clock, scoped Commands commands) => StepAndRetire(entity, ref state, in input, clock.Value.Delta, commands);
}
[EcsModule, IncludeSystems(typeof(Model)), IncludeBundle<Part>, IncludeResource<Clock>]
public static partial class ModelEcs;

// TVariant only makes each closed type a class of its own.
internal sealed class EmptyBody<TVariant> : SystemBody
{
    protected override void Run(SystemRun run) { }
}

internal enum Kind { Empty, Each, Query, Retire }

internal sealed record Case(string Name, int Systems, Func<int, Kind> Kinds, bool Chain, int Archetypes = 1)
{
    public BoundSchedule Bind(World world)
    {
        var descriptors = new SystemDescriptor[Systems];
        var keys = new string[Systems];
        var orders = new int[Systems];
        for (int index = 0; index < Systems; index++)
        {
            var copies = Kinds(index) switch
            {
                Kind.Each => Program.Each,
                Kind.Query => Program.Query,
                Kind.Retire => Program.Retire,
                _ => Program.Empty
            };
            descriptors[index] = copies[index % copies.Length];
            keys[index] = "system-" + index;
            orders[index] = index;
        }
        var edges = Chain ? Enumerable.Range(0, Systems - 1).Select(static index => new EdgeInput(index, index + 1)).ToArray() : [];
        return BoundSchedule.Bind(world, descriptors, edges, keys, orders);
    }
}

public static class Program
{
    const double Delta = 1.0 / 250;
    const int Samples = 31;
    const double SampleMilliseconds = 1;
    const int AllocationRuns = 16;
    const int ValidationRuns = 3;
    const int ValidationRows = 37;
    const int FragmentedSystems = 16;
    const int FragmentedArchetypes = 64;
    const int FragmentedRows = 4;
    static readonly int[] Sizes = [16, 64, 256, 1024];

    internal static readonly SystemDescriptor[] Each = [Model.Integrate0_Descriptor, Model.Integrate1_Descriptor, Model.Integrate2_Descriptor, Model.Integrate3_Descriptor, Model.Integrate4_Descriptor, Model.Integrate5_Descriptor, Model.Integrate6_Descriptor, Model.Integrate7_Descriptor];
    internal static readonly SystemDescriptor[] Query = [Model.IntegrateQuery0_Descriptor, Model.IntegrateQuery1_Descriptor, Model.IntegrateQuery2_Descriptor, Model.IntegrateQuery3_Descriptor, Model.IntegrateQuery4_Descriptor, Model.IntegrateQuery5_Descriptor, Model.IntegrateQuery6_Descriptor, Model.IntegrateQuery7_Descriptor];
    internal static readonly SystemDescriptor[] Retire = [Model.Retire0_Descriptor, Model.Retire1_Descriptor, Model.Retire2_Descriptor, Model.Retire3_Descriptor, Model.Retire4_Descriptor, Model.Retire5_Descriptor, Model.Retire6_Descriptor, Model.Retire7_Descriptor];
    internal static readonly SystemDescriptor[] Empty =
        [EmptySystem<Tag0>(), EmptySystem<Tag1>(), EmptySystem<Tag2>(), EmptySystem<Tag3>(), EmptySystem<Tag4>(), EmptySystem<Tag5>(), EmptySystem<State>(), EmptySystem<Input>()];

    static SystemDescriptor EmptySystem<TVariant>() =>
        SystemDescriptor.Create("Archevolt.SystemOverhead.Empty." + typeof(TVariant).Name, static _ => new EmptyBody<TVariant>()).Build();

    static Case[] Cases()
    {
        var cases = new List<Case>();
        foreach (int systems in Sizes)
        {
            cases.Add(new($"empty-batch/{systems}", systems, static _ => Kind.Empty, Chain: false));
            cases.Add(new($"empty-chain/{systems}", systems, static _ => Kind.Empty, Chain: true));
            cases.Add(new($"each-chain/{systems}", systems, static _ => Kind.Each, Chain: true));
            cases.Add(new($"query-chain/{systems}", systems, static _ => Kind.Query, Chain: true));
            cases.Add(new($"commands25-chain/{systems}", systems, static index => index % 4 == 3 ? Kind.Retire : Kind.Each, Chain: true));
            cases.Add(new($"commands100-chain/{systems}", systems, static _ => Kind.Retire, Chain: true));
        }
        cases.Add(new($"each-fragmented/{FragmentedSystems}", FragmentedSystems, static _ => Kind.Each, Chain: true, FragmentedArchetypes));
        cases.Add(new($"query-fragmented/{FragmentedSystems}", FragmentedSystems, static _ => Kind.Query, Chain: true, FragmentedArchetypes));
        return cases.ToArray();
    }

    public static int Main(string[] args)
    {
        string mode = Option(args, "--mode") ?? "serial";
        int rows = int.Parse(Option(args, "--rows") ?? "16");
        if (mode is not ("serial" or "parallel") || rows < 1)
        {
            Console.Error.WriteLine("Usage: Archevolt.SystemOverhead [--mode serial|parallel] [--rows count]");
            return 2;
        }
        int workers = mode == "serial" ? 1 : Math.Min(4, Environment.ProcessorCount);
        string runtime = System.Runtime.JitInfo.GetCompiledMethodCount() > 0 ? "jit" : "aot";
        Console.WriteLine($"# utc={DateTimeOffset.UtcNow:O}; runtime={RuntimeInformation.FrameworkDescription}; kind={runtime}; os={RuntimeInformation.OSDescription}; arch={RuntimeInformation.ProcessArchitecture}; logical_cpu={Environment.ProcessorCount}");
        Console.WriteLine($"# DOTNET_TieredPGO={Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "<default>"}; DOTNET_TieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "<default>"}");
        Console.WriteLine($"# mode={mode}; workers={workers}; rows={rows}; fragmented={FragmentedArchetypes}x{FragmentedRows}; samples={Samples}; rotating_case_order=true; copies_per_kind={Each.Length}");

        var cases = Cases();
        var results = Time(cases, workers, rows);
        foreach (var item in cases) Validate(item, workers);

        Console.WriteLine("case,mode,runtime,systems,rows,median_ns_per_system,min_ns_per_system,max_ns_per_system,allocated_bytes");
        bool allocationFree = true;
        for (int index = 0; index < cases.Length; index++)
        {
            var (samples, allocated) = results[index];
            int populated = cases[index].Archetypes == 1 ? rows : FragmentedArchetypes * FragmentedRows;
            Console.WriteLine(FormattableString.Invariant($"{cases[index].Name},{mode},{runtime},{cases[index].Systems},{populated},{samples[Samples / 2]:F2},{samples[0]:F2},{samples[^1]:F2},{allocated}"));
            allocationFree &= allocated == 0;
        }
        Console.WriteLine($"# zero_allocations={allocationFree.ToString().ToLowerInvariant()}; validation=ok");
        return allocationFree ? 0 : 1;
    }

    static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    static World Build(int workers) => new WorldBuilder().AddModule(ModelEcs.Module)
        .AddComponent<Tag0>(tag: true).AddComponent<Tag1>(tag: true).AddComponent<Tag2>(tag: true)
        .AddComponent<Tag3>(tag: true).AddComponent<Tag4>(tag: true).AddComponent<Tag5>(tag: true)
        .UseParallelExecution(workers).Reserve(commandBytesPerProducer: 256).Build();

    static Entity Spawn(World world, int index, int archetypes, double state)
    {
        Entity entity = world.Spawn(new Part(new State(state), new Input(1 + index % 3)));
        int pattern = index % archetypes;
        if ((pattern & 1) != 0) world.Insert(entity, default(Tag0));
        if ((pattern & 2) != 0) world.Insert(entity, default(Tag1));
        if ((pattern & 4) != 0) world.Insert(entity, default(Tag2));
        if ((pattern & 8) != 0) world.Insert(entity, default(Tag3));
        if ((pattern & 16) != 0) world.Insert(entity, default(Tag4));
        if ((pattern & 32) != 0) world.Insert(entity, default(Tag5));
        return entity;
    }

    static (double[] Samples, long Allocated)[] Time(Case[] cases, int workers, int rows)
    {
        var worlds = new World[cases.Length];
        var schedules = new BoundSchedule[cases.Length];
        for (int index = 0; index < cases.Length; index++)
        {
            worlds[index] = Build(workers);
            worlds[index].InsertResource(new Clock(Delta));
            int count = cases[index].Archetypes == 1 ? rows : cases[index].Archetypes * FragmentedRows;
            for (int row = 0; row < count; row++) Spawn(worlds[index], row, cases[index].Archetypes, row);
            schedules[index] = cases[index].Bind(worlds[index]);
        }

        Warm(schedules, 150);
        Thread.Sleep(500);
        Warm(schedules, 60);

        var runs = new int[cases.Length];
        for (int index = 0; index < cases.Length; index++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int run = 0; run < 4; run++) schedules[index].Run();
            double perRun = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 4;
            runs[index] = Math.Clamp((int)(SampleMilliseconds / perRun), 1, 4096);
        }

        var samples = new double[cases.Length][];
        for (int index = 0; index < cases.Length; index++) samples[index] = new double[Samples];
        for (int sample = 0; sample < Samples; sample++)
            for (int offset = 0; offset < cases.Length; offset++)
            {
                int index = (sample + offset) % cases.Length;
                var schedule = schedules[index];
                long start = Stopwatch.GetTimestamp();
                for (int run = 0; run < runs[index]; run++) schedule.Run();
                samples[index][sample] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / runs[index] / cases[index].Systems;
            }

        var results = new (double[], long)[cases.Length];
        for (int index = 0; index < cases.Length; index++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int run = 0; run < AllocationRuns; run++) schedules[index].Run();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Array.Sort(samples[index]);
            results[index] = (samples[index], allocated);
            schedules[index].Dispose();
            worlds[index].Dispose();
        }
        return results;
    }

    static void Warm(BoundSchedule[] schedules, int millisecondsEach)
    {
        foreach (var schedule in schedules)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < millisecondsEach) schedule.Run();
        }
    }

    static void Validate(Case item, int workers)
    {
        using var world = Build(workers);
        world.InsertResource(new Clock(Delta));
        var entities = new Entity[ValidationRows];
        var expected = new double[ValidationRows];
        var alive = new bool[ValidationRows];
        for (int index = 0; index < ValidationRows; index++)
        {
            expected[index] = index % 5 == 0 ? 2 * Model.RetireBelow - index : index * 0.5;
            alive[index] = true;
            entities[index] = Spawn(world, index, item.Archetypes, expected[index]);
        }
        using (var schedule = item.Bind(world))
            for (int run = 0; run < ValidationRuns; run++) schedule.Run();

        for (int index = 0; index < ValidationRows; index++)
        {
            double input = 1 + index % 3;
            for (int run = 0; run < ValidationRuns && alive[index]; run++)
                for (int system = 0; system < item.Systems && alive[index]; system++)
                {
                    Kind kind = item.Kinds(system);
                    if (kind == Kind.Empty) continue;
                    expected[index] += input * Delta;
                    if (kind == Kind.Retire && expected[index] < Model.RetireBelow) alive[index] = false;
                }
            if (world.Contains(entities[index]) != alive[index])
                throw new InvalidOperationException($"{item.Name}: entity {index} is {(alive[index] ? "gone" : "alive")}.");
            if (!alive[index]) continue;
            double actual = world.Get<State>(entities[index]).Value;
            if (BitConverter.DoubleToInt64Bits(actual) != BitConverter.DoubleToInt64Bits(expected[index]))
                throw new InvalidOperationException($"{item.Name}: entity {index} is {actual:R}, expected {expected[index]:R}.");
        }
    }
}
