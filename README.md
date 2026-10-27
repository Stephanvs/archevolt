<h1 align="center">
  <img src=".github/assets/archevolt-geometric.png" alt="Archevolt" width="720">
</h1>

Archevolt is a headless archetype ECS for .NET 11 and C# 15. Write components as structs and systems as C# methods. Its source generator builds typed queries, bundles, modules, and schedules; the runtime handles native component storage and serial or bounded parallel execution.

The runtime has no package dependencies. The `Archevolt` package includes its generator. Generated declarations stay in your namespace and partial types.

See the [documentation site](docs/index.mdx) for the supported declarations and runtime operations, and the [acceptance record](.audit/acceptance.md) for validation against the [v1 implementation plan](https://github.com/Stephanvs/archevolt/blob/ebacf07059fefbf9b182e40a290812be584dd9ee/PLAN.md). [ROADMAP.md](ROADMAP.md) holds the current performance plan.

## See it in action

[![Archeoids gameplay with a cyan ship, drifting asteroids, and a score and lives HUD.](samples/Archeoids/archeoids.png)](samples/Archeoids/README.md)

Play [Archeoids](samples/Archeoids/README.md), an Asteroids-style game with neon vector graphics, splitting rocks, and particle explosions. Archevolt runs the gameplay; Silk.NET handles the window, input, and rendering.

Every ship, rock, bullet, and particle is an ECS entity. Explore the sample to see spawn bundles, deferred commands, independent message readers, and invulnerability tags working together in a playable game.

```powershell
dotnet run --project samples/Archeoids -c Release
```

Use **A/D** or **Left/Right** to turn, **W** or **Up** to thrust, and **Space** to fire. See the [sample guide](samples/Archeoids/README.md) for controls, requirements, and a walkthrough of the code. Run with `-- --verify` for gameplay checks without a window.

## Run a system

Components are unmanaged structs. Systems declare component access through `ref` and `in`, or through a named query.

```csharp
using Archevolt;

namespace Example;

[Component]
public readonly record struct Position(float X);

[Component]
public readonly record struct Velocity(float X);

[Bundle]
public readonly partial record struct Particle(Position Position, Velocity Velocity);

public static partial class Motion
{
    [Each]
    public static void Move(scoped ref Position position, scoped in Velocity velocity)
    {
        position = position with { X = position.X + velocity.X };
    }
}

[EcsModule, IncludeSystems(typeof(Motion)), IncludeBundle<Particle>]
public static partial class GameEcs;

[Schedule, Run(typeof(Motion), nameof(Motion.Move), Order = 0)]
public sealed partial class Simulation;

public static class Program
{
    public static void Main()
    {
        using var world = new WorldBuilder().AddModule(GameEcs.Module).Build();
        var entity = world.Spawn(new Particle(new Position(0), new Velocity(1)));
        using var simulation = Simulation.Create(world);
        simulation.Run();
        Console.WriteLine(world.Get<Position>(entity).X);
    }
}
```

This prints `1`. The generated loop calls `Motion.Move` directly. A writable component ref can replace a readonly record struct.

For more complex loops, declare a named query:

```csharp
[Query, Write<Position>, Read<Velocity>]
public readonly ref partial struct Moving;

public static partial class Motion
{
    [EcsSystem]
    public static void MoveAll(scoped Moving moving)
    {
        foreach (var row in moving)
        {
            ref var position = ref row.Position;
            position = position with { X = position.X + row.Velocity.X };
        }
    }
}
```

Register the desired update once in the schedule. The [particle sample](samples/Archevolt.Particles/Program.cs) exercises both forms and verifies membership changes over 100,000 entities.

## Execution and ownership

Build, run, mutate, and dispose a world on its owner thread. Idle component reads return copies. Systems receive scoped capabilities. Structural changes inside systems use `Commands` and become visible at schedule barriers.

Dispose every bound schedule before disposing its world. `schedule.Plan` exposes dependencies and batches. Opt into per-system execution measurements with `schedule.EnableTiming()` and inspect `schedule.Timings` between runs.

Serial execution is the default. Enable a bounded worker pool with `.UseParallelExecution(maxConcurrency: 4)` on `WorldBuilder`. This limit includes the owner thread. Opt eligible systems into row-range partitioning with `[Each(Partitioned = true)]`. Resource and component conflicts determine which systems can run together.

`[TrackChanges]` opts components and resources into tracking. Writable access stamps a change even when the replacement value is equal. Added and changed filters use each system instance's last successful execution.

Resources are borrowed by default. `InsertOwnedResource` transfers disposal responsibility to the world. `TakeResource` transfers it back. Managed resources run exclusively on the owner thread.

Message readers keep independent cursors. Call `world.AdvanceMessages()` once per outer update to rotate retention. Messages and removal records survive two rotations. Readers expose `MissedCount` when records expire before they are read.

After an execution fault or cancellation, all active workers join before the call returns. Completed writes remain. Inspect those effects before calling `schedule.Reset()` to acknowledge the failure.

## Documentation site

The `docs/` folder is a [Blume](https://useblume.dev) site. It needs Node.js 22.19 or newer.

```bash
pnpm install
pnpm dev
pnpm build
```

`pnpm dev` serves the docs with hot reload. `pnpm build` writes static HTML to `dist/`.

## Build and verify

Package publication and changelog updates run from `main`. See [release setup and recovery](.github/RELEASING.md) for NuGet trust and Cloudflare configuration.

The SDK is pinned in `global.json`. Run verification with PowerShell 7.2 or newer:

```powershell
./scripts/verify.ps1
```

The script builds and tests, runs the headless Archeoids checks, packs the library, checks package contents, runs a clean consumer with a separate library module, publishes NativeAOT consumers, and measures warmed serial and parallel allocations. Windows NativeAOT publishing requires the Visual Studio C++ build tools.

Run the short movement measurement or the BenchmarkDotNet suite:

```powershell
dotnet run --project benchmarks/Archevolt.Benchmarks -c Release -- --quick
dotnet run --project benchmarks/Archevolt.Benchmarks -c Release -- --filter "*MovementBenchmarks*" --job short
./scripts/measure.ps1 -Mode All
```

`./scripts/baseline.ps1` records the [step 0 baseline](benchmarks/baseline.md): entity lifecycle, traversal, and filter cases next to their Bevy counterparts, with the median of three process runs.

Benchmark results are workload-specific. The [performance report](.audit/performance-final.md) records the workload matrix, handwritten and Arch comparisons, allocations, native storage, generated code sizes, and JIT/AOT inspection. Structural churn allocates, and small workloads can cost more to dispatch in parallel.
