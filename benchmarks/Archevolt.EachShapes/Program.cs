using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Archevolt;

namespace Archevolt.EachShapes;

[Component] public readonly record struct Position(float X, float Y);
[Component] public readonly record struct Velocity(float X, float Y);
[Component] public readonly record struct Rotation(float Angle, float Speed);
[Component] public readonly record struct Lifetime(float Remaining, float Total);
[Component] public readonly record struct Speed(float Value);
[Resource] public readonly record struct FrameTime(float Delta);
[Bundle] public readonly partial record struct Particle(Position Position, Velocity Velocity, Rotation Rotation, Lifetime Lifetime, Speed Speed);

[Query, Write<Position>, Read<Velocity>]
public readonly ref partial struct Moving;

public static class Arena
{
    public const float Width = 1280f;
    public const float Height = 640f;
    public const float Limit = 1000f;
    public static float Wrap(float value, float size) => value >= size ? value - size : value;
}

// Each callback is a shape that game code writes. The names say what keeps it from being a plain per-float formula.
public static partial class Shapes
{
    [Each]
    public static void Plain(scoped ref Position position, scoped in Velocity velocity, scoped Res<FrameTime> time)
        => position = new Position(position.X + velocity.X * time.Value.Delta, position.Y + velocity.Y * time.Value.Delta);

    [Each]
    public static void Wrap(scoped ref Position position, scoped in Velocity velocity, scoped Res<FrameTime> time)
    {
        float x = position.X + velocity.X * time.Value.Delta;
        float y = position.Y + velocity.Y * time.Value.Delta;
        if (x >= Arena.Width) x -= Arena.Width;
        if (y >= Arena.Height) y -= Arena.Height;
        position = new Position(x, y);
    }

    [Each]
    public static void WrapHelper(scoped ref Position position, scoped in Velocity velocity, scoped Res<FrameTime> time)
        => position = new Position(Arena.Wrap(position.X + velocity.X * time.Value.Delta, Arena.Width),
            Arena.Wrap(position.Y + velocity.Y * time.Value.Delta, Arena.Height));

    [Each]
    public static void Delegating(scoped ref Position position, scoped in Velocity velocity, scoped Res<FrameTime> time)
        => Wrap(ref position, in velocity, time);

    [Each]
    public static void Spin(scoped ref Rotation rotation, scoped Res<FrameTime> time)
        => rotation = rotation with { Angle = rotation.Angle + rotation.Speed * time.Value.Delta };

    [Each]
    public static void Bounce(scoped ref Velocity velocity, scoped in Position position)
    {
        float x = velocity.X;
        float y = velocity.Y;
        if (position.X < 0f || position.X >= Arena.Width) x = -x;
        if (position.Y < 0f || position.Y >= Arena.Height) y = -y;
        velocity = new Velocity(x, y);
    }

    [Each]
    public static void Clamp(scoped ref Velocity velocity)
        => velocity = new Velocity(MathF.Max(-Arena.Limit, MathF.Min(Arena.Limit, velocity.X)),
            MathF.Max(-Arena.Limit, MathF.Min(Arena.Limit, velocity.Y)));

    [Each]
    public static void Length(scoped ref Speed speed, scoped in Velocity velocity)
        => speed = new Speed(MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y));

    [Each]
    public static void Expire(Entity entity, scoped ref Lifetime lifetime, scoped Res<FrameTime> time, scoped Commands commands)
    {
        lifetime = lifetime with { Remaining = lifetime.Remaining - time.Value.Delta };
        if (lifetime.Remaining <= 0) commands.Despawn(entity);
    }

    [EcsSystem]
    public static void WrapQuery(scoped Moving moving, scoped Res<FrameTime> time)
    {
        float delta = time.Value.Delta;
        foreach (var row in moving)
        {
            float x = row.Position.X + row.Velocity.X * delta;
            float y = row.Position.Y + row.Velocity.Y * delta;
            if (x >= Arena.Width) x -= Arena.Width;
            if (y >= Arena.Height) y -= Arena.Height;
            row.Position = new Position(x, y);
        }
    }

    [EachBlock]
    public static void WrapBlock(scoped Span<Position> positions, scoped ReadOnlySpan<Velocity> velocities, scoped Res<FrameTime> time)
    {
        float delta = time.Value.Delta;
        velocities = velocities[..positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            float x = positions[i].X + velocities[i].X * delta;
            float y = positions[i].Y + velocities[i].Y * delta;
            if (x >= Arena.Width) x -= Arena.Width;
            if (y >= Arena.Height) y -= Arena.Height;
            positions[i] = new Position(x, y);
        }
    }

    [EachBlock]
    public static void WrapVector(scoped Span<Position> positions, scoped ReadOnlySpan<Velocity> velocities, scoped Res<FrameTime> time)
    {
        var p = MemoryMarshal.Cast<Position, float>(positions);
        var v = MemoryMarshal.Cast<Velocity, float>(velocities)[..p.Length];
        float delta = time.Value.Delta;
        ref float pr = ref MemoryMarshal.GetReference(p);
        ref float vr = ref MemoryMarshal.GetReference(v);
        int i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            var d = Vector256.Create(delta);
            var limit = Vector256.Create(Arena.Width, Arena.Height, Arena.Width, Arena.Height, Arena.Width, Arena.Height, Arena.Width, Arena.Height);
            for (; i <= p.Length - Vector256<float>.Count; i += Vector256<float>.Count)
            {
                var next = Vector256.LoadUnsafe(ref pr, (nuint)i) + Vector256.LoadUnsafe(ref vr, (nuint)i) * d;
                Vector256.ConditionalSelect(Vector256.GreaterThanOrEqual(next, limit), next - limit, next).StoreUnsafe(ref pr, (nuint)i);
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            var d = Vector128.Create(delta);
            var limit = Vector128.Create(Arena.Width, Arena.Height, Arena.Width, Arena.Height);
            for (; i <= p.Length - Vector128<float>.Count; i += Vector128<float>.Count)
            {
                var next = Vector128.LoadUnsafe(ref pr, (nuint)i) + Vector128.LoadUnsafe(ref vr, (nuint)i) * d;
                Vector128.ConditionalSelect(Vector128.GreaterThanOrEqual(next, limit), next - limit, next).StoreUnsafe(ref pr, (nuint)i);
            }
        }
        for (; i < p.Length; i++)
        {
            float size = (i & 1) == 0 ? Arena.Width : Arena.Height;
            float next = p[i] + v[i] * delta;
            p[i] = next >= size ? next - size : next;
        }
    }

    [EachBlock]
    public static void SpinVector(scoped Span<Rotation> rotations, scoped Res<FrameTime> time)
    {
        var r = MemoryMarshal.Cast<Rotation, float>(rotations);
        float delta = time.Value.Delta;
        ref float rr = ref MemoryMarshal.GetReference(r);
        int i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            var d = Vector256.Create(delta);
            var speedLanes = Vector256.Create(1, 1, 3, 3, 5, 5, 7, 7);
            var angleLanes = Vector256.Create(-1, 0, -1, 0, -1, 0, -1, 0).AsSingle();
            for (; i <= r.Length - Vector256<float>.Count; i += Vector256<float>.Count)
            {
                var pairs = Vector256.LoadUnsafe(ref rr, (nuint)i);
                var stepped = pairs + Vector256.Shuffle(pairs, speedLanes) * d;
                Vector256.ConditionalSelect(angleLanes, stepped, pairs).StoreUnsafe(ref rr, (nuint)i);
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            var d = Vector128.Create(delta);
            var speedLanes = Vector128.Create(1, 1, 3, 3);
            var angleLanes = Vector128.Create(-1, 0, -1, 0).AsSingle();
            for (; i <= r.Length - Vector128<float>.Count; i += Vector128<float>.Count)
            {
                var pairs = Vector128.LoadUnsafe(ref rr, (nuint)i);
                var stepped = pairs + Vector128.Shuffle(pairs, speedLanes) * d;
                Vector128.ConditionalSelect(angleLanes, stepped, pairs).StoreUnsafe(ref rr, (nuint)i);
            }
        }
        for (; i < r.Length; i += 2) r[i] += r[i + 1] * delta;
    }
}

[EcsModule, IncludeSystems(typeof(Shapes)), IncludeBundle<Particle>, IncludeResource<FrameTime>]
public static partial class ShapesEcs;

[Schedule, Run(typeof(Shapes), nameof(Shapes.Plain), Order = 0)] public sealed partial class PlainSchedule;
[Schedule, Run(typeof(Shapes), nameof(Shapes.Wrap), Order = 0)] public sealed partial class WrapSchedule;
[Schedule, Run(typeof(Shapes), nameof(Shapes.WrapHelper), Order = 0)] public sealed partial class WrapHelperSchedule;
[Schedule, Run(typeof(Shapes), nameof(Shapes.Delegating), Order = 0)] public sealed partial class DelegatingSchedule;
[Schedule, Run(typeof(Shapes), nameof(Shapes.Spin), Order = 0)] public sealed partial class SpinSchedule;
[Schedule, Run(typeof(Shapes), nameof(Shapes.Bounce), Order = 0)] public sealed partial class BounceSchedule;
[Schedule, Run(typeof(Shapes), nameof(Shapes.Clamp), Order = 0)] public sealed partial class ClampSchedule;
[Schedule, Run(typeof(Shapes), nameof(Shapes.Length), Order = 0)] public sealed partial class LengthSchedule;
[Schedule, Run(typeof(Shapes), nameof(Shapes.Expire), Order = 0)] public sealed partial class ExpireSchedule;
[Schedule, Run(typeof(Shapes), nameof(Shapes.WrapQuery), Order = 0)] public sealed partial class WrapQuerySchedule;
[Schedule, Run(typeof(Shapes), nameof(Shapes.WrapBlock), Order = 0)] public sealed partial class WrapBlockSchedule;
[Schedule, Run(typeof(Shapes), nameof(Shapes.WrapVector), Order = 0)] public sealed partial class WrapVectorSchedule;
[Schedule, Run(typeof(Shapes), nameof(Shapes.SpinVector), Order = 0)] public sealed partial class SpinVectorSchedule;

internal record struct Snapshot(Position Position, Velocity Velocity, Rotation Rotation, Lifetime Lifetime, Speed Speed, bool Alive);

internal delegate void Reference(ref Snapshot row, float delta);

internal sealed record Case(string Name, Func<World, BoundSchedule> Create, Reference Expected);

public static class Program
{
    const float Delta = 1f / 120;
    const int EmptyRounds = 6;
    const int EmptyRunsPerRound = 200;
    const int Samples = 31;
    const int RunsPerSample = 8;
    const int AllocationRuns = 64;
    const int ValidationRows = 1_027;

    static readonly Case[] Cases =
    [
        new("each-plain", PlainSchedule.Create, Move),
        new("each-wrap", WrapSchedule.Create, MoveAndWrap),
        new("each-wrap-helper", WrapHelperSchedule.Create, MoveAndWrap),
        new("each-delegating", DelegatingSchedule.Create, MoveAndWrap),
        new("each-spin", SpinSchedule.Create, Turn),
        new("each-bounce", BounceSchedule.Create, Reflect),
        new("each-clamp", ClampSchedule.Create, Limit),
        new("each-length", LengthSchedule.Create, Measure),
        new("each-expire", ExpireSchedule.Create, Age),
        new("query-wrap", WrapQuerySchedule.Create, MoveAndWrap),
        new("block-wrap", WrapBlockSchedule.Create, MoveAndWrap),
        new("vector-wrap", WrapVectorSchedule.Create, MoveAndWrap),
        new("vector-spin", SpinVectorSchedule.Create, Turn),
    ];

    public static int Main(string[] args)
    {
        string mode = Option(args, "--mode") ?? "populated";
        int rows = int.Parse(Option(args, "--rows") ?? "100000");
        if (mode is not ("populated" or "empty"))
        {
            Console.Error.WriteLine("Usage: Archevolt.EachShapes [--mode populated|empty] [--rows count]");
            return 2;
        }
        string runtime = System.Runtime.JitInfo.GetCompiledMethodCount() > 0 ? "jit" : "aot";
        Console.WriteLine($"# utc={DateTimeOffset.UtcNow:O}; runtime={RuntimeInformation.FrameworkDescription}; kind={runtime}; os={RuntimeInformation.OSDescription}; arch={RuntimeInformation.ProcessArchitecture}; logical_cpu={Environment.ProcessorCount}");
        Console.WriteLine($"# vector256={Vector256.IsHardwareAccelerated}; vector128={Vector128.IsHardwareAccelerated}; DOTNET_TieredPGO={Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "<default>"}; DOTNET_TieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "<default>"}");
        Console.WriteLine($"# mode={mode}; rows={rows}; samples={Samples}; runs_per_sample={RunsPerSample}; empty_runs={(mode == "empty" ? EmptyRounds * EmptyRunsPerRound : 0)}; rotating_case_order=true; one_world=true; workers=1");

        var results = Time(mode, rows);
        // Validation runs after timing. Running a system on a populated world first would give the JIT the row profile
        // that the empty mode withholds.
        Validate();

        Console.WriteLine("case,mode,runtime,rows,median_ns_per_row,min_ns_per_row,max_ns_per_row,allocated_bytes");
        bool allocationFree = true;
        foreach (var (name, samples, allocated) in results)
        {
            Console.WriteLine(FormattableString.Invariant($"{name},{mode},{runtime},{rows},{samples[Samples / 2]:F4},{samples[0]:F4},{samples[^1]:F4},{allocated}"));
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

    static (string Name, double[] Samples, long Allocated)[] Time(string mode, int rows)
    {
        using var world = new WorldBuilder().AddModule(ShapesEcs.Module).Build();
        world.InsertResource(new FrameTime(Delta));
        var schedules = Cases.Select(item => item.Create(world)).ToArray();
        if (mode == "empty")
        {
            // A game runs its systems before the first bullet or particle exists. The sleeps give the runtime time
            // to promote the methods it saw during those runs.
            for (int round = 0; round < EmptyRounds; round++)
            {
                for (int run = 0; run < EmptyRunsPerRound; run++)
                    foreach (var schedule in schedules) schedule.Run();
                Thread.Sleep(400);
            }
        }
        for (int i = 0; i < rows; i++)
            world.Spawn(new Particle(new Position(i % 97, i % 31), new Velocity(1 + i % 3, 2), new Rotation(i % 7, 0.5f), new Lifetime(100_000f, 100_000f), new Speed(0)));

        Warm(schedules, 3000);
        Thread.Sleep(500);
        Warm(schedules, 1500);

        var samples = new double[schedules.Length][];
        for (int index = 0; index < schedules.Length; index++) samples[index] = new double[Samples];
        for (int sample = 0; sample < Samples; sample++)
            for (int offset = 0; offset < schedules.Length; offset++)
            {
                int index = (sample + offset) % schedules.Length;
                var schedule = schedules[index];
                long start = Stopwatch.GetTimestamp();
                for (int run = 0; run < RunsPerSample; run++) schedule.Run();
                samples[index][sample] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / RunsPerSample / rows;
            }

        var results = new (string, double[], long)[schedules.Length];
        for (int index = 0; index < schedules.Length; index++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int run = 0; run < AllocationRuns; run++) schedules[index].Run();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Array.Sort(samples[index]);
            results[index] = (Cases[index].Name, samples[index], allocated);
        }
        foreach (var schedule in schedules) schedule.Dispose();
        return results;
    }

    static void Warm(BoundSchedule[] schedules, int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
            foreach (var schedule in schedules) schedule.Run();
    }

    // Each case runs once over a small world whose values take both sides of every branch, and every entity is
    // compared bit for bit with a reference written without the ECS.
    static void Validate()
    {
        foreach (var item in Cases)
        {
            using var world = new WorldBuilder().AddModule(ShapesEcs.Module).Build();
            world.InsertResource(new FrameTime(Delta));
            var entities = new Entity[ValidationRows];
            var expected = new Snapshot[ValidationRows];
            for (int i = 0; i < ValidationRows; i++)
            {
                var row = new Snapshot(
                    new Position(i * 37 % 1400 - 60, i * 53 % 700 - 30),
                    new Velocity((i % 13 - 6) * 400, (i % 7 - 3) * 0.5f),
                    new Rotation(i % 11, i % 5 - 2),
                    new Lifetime(i % 10 == 0 ? 0.004f : 100 + i, 200),
                    new Speed(0), Alive: true);
                entities[i] = world.Spawn(new Particle(row.Position, row.Velocity, row.Rotation, row.Lifetime, row.Speed));
                item.Expected(ref row, Delta);
                expected[i] = row;
            }
            using (var schedule = item.Create(world)) schedule.Run();
            for (int i = 0; i < ValidationRows; i++)
            {
                bool alive = world.Contains(entities[i]);
                if (alive != expected[i].Alive)
                    throw new InvalidOperationException($"{item.Name}: entity {i} is {(alive ? "alive" : "gone")}.");
                if (!alive) continue;
                var actual = new Snapshot(world.Get<Position>(entities[i]), world.Get<Velocity>(entities[i]),
                    world.Get<Rotation>(entities[i]), world.Get<Lifetime>(entities[i]), world.Get<Speed>(entities[i]), Alive: true);
                if (!Same(actual, expected[i]))
                    throw new InvalidOperationException($"{item.Name}: entity {i} is {actual}, expected {expected[i]}.");
            }
        }
    }

    static bool Same(Snapshot left, Snapshot right) =>
        MemoryMarshal.AsBytes(new ReadOnlySpan<Snapshot>(in left)).SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<Snapshot>(in right)));

    static void Move(ref Snapshot row, float delta)
        => row.Position = new Position(row.Position.X + row.Velocity.X * delta, row.Position.Y + row.Velocity.Y * delta);

    static void MoveAndWrap(ref Snapshot row, float delta)
    {
        float x = row.Position.X + row.Velocity.X * delta;
        float y = row.Position.Y + row.Velocity.Y * delta;
        if (x >= Arena.Width) x -= Arena.Width;
        if (y >= Arena.Height) y -= Arena.Height;
        row.Position = new Position(x, y);
    }

    static void Turn(ref Snapshot row, float delta)
        => row.Rotation = new Rotation(row.Rotation.Angle + row.Rotation.Speed * delta, row.Rotation.Speed);

    static void Reflect(ref Snapshot row, float delta)
    {
        float x = row.Velocity.X;
        float y = row.Velocity.Y;
        if (row.Position.X < 0f || row.Position.X >= Arena.Width) x = -x;
        if (row.Position.Y < 0f || row.Position.Y >= Arena.Height) y = -y;
        row.Velocity = new Velocity(x, y);
    }

    static void Limit(ref Snapshot row, float delta)
        => row.Velocity = new Velocity(MathF.Max(-Arena.Limit, MathF.Min(Arena.Limit, row.Velocity.X)),
            MathF.Max(-Arena.Limit, MathF.Min(Arena.Limit, row.Velocity.Y)));

    static void Measure(ref Snapshot row, float delta)
        => row.Speed = new Speed(MathF.Sqrt(row.Velocity.X * row.Velocity.X + row.Velocity.Y * row.Velocity.Y));

    static void Age(ref Snapshot row, float delta)
    {
        row.Lifetime = new Lifetime(row.Lifetime.Remaining - delta, row.Lifetime.Total);
        if (row.Lifetime.Remaining <= 0) row.Alive = false;
    }
}
