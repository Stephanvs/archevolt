/**
 * Shared evidence for the landing page alternatives under /alt.
 *
 * Every number here has a source. Keep each number with its qualifiers (date, machine, scope).
 * The 2 November rows come from the benchmark checkpoint at commit 8029e7f (release v0.5.5), on an idle
 * AMD Ryzen 9 5950X. The 5 November rows come from the second performance hill climb on the same machine
 * while it was in normal use, with alternating process runs. The SIMD kernel is on main and is not yet in
 * a NuGet release (latest release v0.6.6).
 */

export const install = "dotnet add package Archevolt";
export const runSample = "dotnet run --project samples/Archeoids -c Release";
export const rerunBenchmarks = "dotnet run --project benchmarks/Archevolt.Benchmarks -c Release -- --quick";

export const links = {
  quickstart: "/docs/getting-started/quickstart",
  installation: "/docs/getting-started/installation",
  performance: "/docs/performance",
  validation: "/docs/validation",
  diagnostics: "/docs/reference/diagnostics",
  architecture: "/docs/concepts/architecture",
  systems: "/docs/guides/systems",
  simdKernels: "/docs/guides/systems#simd-kernels",
  queries: "/docs/guides/queries",
  schedules: "/docs/guides/schedules",
  execution: "/docs/guides/execution",
  messages: "/docs/guides/messages",
  changeTracking: "/docs/guides/change-tracking",
  guides: "/docs/guides/components-and-bundles",
  sample: "/docs/samples/archeoids",
  changelog: "/docs/changelog",
  browserGame: "https://stephanvs.github.io/archevolt/",
  github: "https://github.com/Stephanvs/archevolt",
  nuget: "https://www.nuget.org/packages/Archevolt",
  roadmap: "https://github.com/Stephanvs/archevolt/blob/main/ROADMAP.md",
  results: "https://github.com/Stephanvs/archevolt/blob/main/benchmarks/results.md",
  roundOne: "https://github.com/Stephanvs/archevolt/blob/main/benchmarks/performance-hill-climb-report.md",
  roundTwo: "https://github.com/Stephanvs/archevolt/blob/main/benchmarks/performance-hill-climb-round-2.md",
  whatRemains: "https://github.com/Stephanvs/archevolt/blob/main/benchmarks/performance-hill-climb-round-2.md#what-remains",
};

/* Code. heroCode, moduleCode, and runCode are the current landing page's snippets. */

export const heroCode = `[Component]
public readonly record struct Position(float X);

[Component]
public readonly record struct Velocity(float X);

public static partial class Motion
{
    [Each]
    public static void Move(scoped ref Position position, scoped in Velocity velocity)
    {
        position = position with { X = position.X + velocity.X };
    }
}`;

export const moduleCode = `[Bundle]
public readonly partial record struct Particle(Position Position, Velocity Velocity);

[EcsModule, IncludeSystems(typeof(Motion)), IncludeBundle<Particle>]
public static partial class GameEcs;

[Schedule, Run(typeof(Motion), nameof(Motion.Move), Order = 0)]
public sealed partial class Simulation;`;

export const runCode = `using var world = new WorldBuilder().AddModule(GameEcs.Module).Build();
var entity = world.Spawn(new Particle(new Position(0), new Velocity(1)));

using var simulation = Simulation.Create(world);
simulation.Run();

Console.WriteLine(world.Get<Position>(entity).X); // 1`;

/**
 * Real generator output for heroCode's Motion.Move, captured on 5 November 2026 from main (419d066)
 * in a Release build. Excerpt: namespace prefixes and the Vector128 branch are removed.
 * To regenerate it, build a consumer of heroCode with -c Release -p:EmitCompilerGeneratedFiles=true and open
 * the Motion.Move *.Each.g.cs file under the generated files output.
 */
export const generatedKernel = `// Motion.Move.g.cs, Release build (excerpt)
static void __Kernel(Span<float> __column0, ReadOnlySpan<float> __column1)
{
    int __count = __column0.Length;
    ref float __base0 = ref MemoryMarshal.GetReference(__column0);
    ref float __base1 = ref MemoryMarshal.GetReference(__column1);
    int __index = 0;
    if (Vector256.IsHardwareAccelerated)
    {
        for (; __index <= __count - Vector256<float>.Count; __index += Vector256<float>.Count)
        {
            var __load0 = Vector256.LoadUnsafe(ref __base0, (nuint)__index);
            var __load1 = Vector256.LoadUnsafe(ref __base1, (nuint)__index);
            Vector256.StoreUnsafe((__load0 + __load1), ref __base0, (nuint)__index);
        }
    }
    for (; __index < __count; __index++)
        __column0[__index] = (__column0[__index] + __column1[__index]);
}`;

/** The same generated file's Run method. Excerpt: namespace prefixes removed. */
export const generatedRun = `protected override void Run(SystemRun run)
{
    var cursor = _query0.Begin(run);
    while (cursor.Next(out var block))
    {
        Span<Position> _column0 = block.Write<Position>(0);
        ReadOnlySpan<Velocity> _column1 = block.Read<Velocity>(1);
        int count = block.Count;
        if (__kernel)
        {
            __Kernel(MemoryMarshal.Cast<Position, float>(_column0),
                     MemoryMarshal.Cast<Velocity, float>(_column1));
            continue;
        }
        for (int row = 0; row < count; row++)
            Move(ref _column0[row], in _column1[row]);
    }
}`;

/** docs/guides/systems.mdx, [EachBlock]. */
export const eachBlockCode = `public static partial class MotionKernel
{
    [EachBlock(Partitioned = true), Without<Frozen>]
    public static void MoveBlock(scoped Span<Position> positions, scoped ReadOnlySpan<Velocity> velocities)
    {
        for (int i = 0; i < positions.Length; i++)
            positions[i] = positions[i] with { X = positions[i].X + velocities[i].X };
    }
}`;

/** Real diagnostic text, src/Archevolt.Generators/QueryGeneration.cs line 21, for a Changed<Position> filter. */
export const av006 = "error AV006: Position needs [TrackChanges] for a Changed filter.";

/* Performance */

export interface MovementRow {
  name: string;
  us: number;
  range: [number, number];
  /** "2 Nov" rows: idle machine, commit 8029e7f. "5 Nov" rows: same machine in use, alternating processes. */
  date: "2 Nov" | "5 Nov";
  kind: "archevolt" | "reference" | "baseline";
}

/**
 * Moving 100,000 entities on one worker, median µs per update, with the fastest and slowest process in brackets.
 * Sources: benchmarks/results.md (2 Nov) and benchmarks/performance-hill-climb-round-2.md, batch 2 (5 Nov).
 * Archevolt's times include schedule dispatch, resource access, lease checks, and query traversal.
 * The handwritten C# loop has no scheduler.
 */
export const movement: MovementRow[] = [
  { name: "Archevolt `[Each]`, SIMD kernel", us: 22.0, range: [21.6, 40.0], date: "5 Nov", kind: "archevolt" },
  { name: "Bevy 0.20 `for_each`", us: 23.7, range: [23.6, 24.27], date: "2 Nov", kind: "reference" },
  { name: "Rust handwritten loop", us: 20.5, range: [20.47, 20.98], date: "2 Nov", kind: "reference" },
  { name: "C# handwritten loop", us: 83.06, range: [82.94, 83.41], date: "2 Nov", kind: "baseline" },
  { name: "Archevolt `[Each]`, before kernels", us: 97.4, range: [97.3, 100.9], date: "5 Nov", kind: "archevolt" },
  { name: "Arch 2.1.0 inline query", us: 103.5, range: [103.5, 104.9], date: "2 Nov", kind: "reference" },
];

export const movementNote =
  "Medians of three process runs on one AMD Ryzen 9 5950X with Windows 11 and .NET 11 rc.3, Release builds. " +
  "2 November rows: idle machine, commit 8029e7f. 5 November rows: the same machine in normal use, before and after " +
  "the SIMD kernel, in alternating processes. The kernel is on main and not yet in a NuGet release. One machine, not a ranking.";

/** Headline facts with their exact scope. Use the wording in `scope` next to the number. */
export const perfFacts = {
  kernel: {
    before: 97.4,
    after: 22.0,
    scope: "Moving 100,000 entities with the same [Each] method, one worker, 5 November 2026.",
    source: links.roundTwo,
  },
  bevyParity: {
    claim: "The dense movement update now matches Bevy's.",
    archevolt: 22.0,
    bevy: 23.7,
    scope: "Bevy 0.20 for_each measured 2 November on the same machine while idle. Archevolt measured 5 November while the machine was in use.",
    source: links.roundTwo,
  },
  dispatch: {
    before: 19,
    after: 1.5,
    scope: "A four-worker update of 1,000 entities, updates back to back. About 32 µs when the calling thread sleeps 2 ms between updates.",
    source: links.roundOne,
  },
  trackedWrite: {
    before: 95.7,
    after: 35.4,
    bevy: 30.67,
    scope: "Tracked write over 100,000 entities. Within about 15% of Bevy.",
    source: links.roundTwo,
  },
  allocation: {
    value: "0 B",
    scope: "Managed memory allocated by a warmed, fixed-capacity update, with one worker or four. Spawning and other structural changes can grow storage and allocate.",
    source: links.performance,
  },
  unchangedScan: {
    value: 0.16,
    scope: "A Changed<T> scan with nothing changed, at 1,000 or 100,000 entities.",
    source: links.performance,
  },
};

/** Where Archevolt is still slower, from the round two report's "What remains". Publish these, do not hide them. */
export const gaps = [
  { case: "Write, then a `Changed<T>` scan over 100,000 entities", now: "137 µs", bevy: "99.46 µs", note: "About 30% behind. The benchmark callback's float to double conversions set the pace." },
  { case: "`Get<T>` on 1,000 entities", now: "5.12 to 5.31 µs", bevy: "4.04 µs", note: "1.3 to 1.5 times Bevy. An owner-thread check, an idle check, and two bounds checks remain." },
  { case: "Callbacks that branch, call a method, or mix fields", now: "per-entity call", bevy: "", note: "They keep the scalar loop. Following calls and strided loads would widen kernel coverage." },
];

/** How performance work is measured. Round one and round two reports, "Method". */
export const perfProcess = [
  "The acceptance gate is fixed before any candidate runs.",
  "A change must be at least 5% faster in two independent batches, with process ranges that do not overlap.",
  "No guard case may regress by more than 3%, and steady-state managed allocation must stay zero.",
  "Both builds are preserved and run in alternating process order, A, B, B, A.",
  "The baseline is measured against itself first, to know the noise.",
  "Tests and a strict public API check must pass. Rejected experiments stay in the ledger.",
];

/** SIMD kernel scope, docs/guides/systems.mdx "SIMD kernels". */
export const kernelScope = {
  qualifies:
    "An [Each] method that writes one component with the same float formula for every lane: float +, -, *, /, negation, Vector2/3/4 operators, constants, locals, fields, records, and with expressions.",
  doesNot: "A method call, a branch, a conversion to double, or a formula that mixes X into Y keeps the call per entity.",
  correctness:
    "The kernel performs the same IEEE operations in the same order, so it writes the same bits. Generator tests compare each kernel with the method bit for bit over 2,051 entities, including NaN, infinities, signed zero, and denormals.",
  debug: "Debug builds always call the method, so a breakpoint in it is hit.",
};

/* Ergonomics and flexibility */

export const diagnostics = { total: 10, errors: 9, warning: "AV008" };

export const facts = {
  engine: "No window, input, rendering, or audio. Pair it with a library such as Silk.NET, or run simulations with no graphics.",
  package: "One package, Archevolt, with the runtime and the Roslyn source generator. The runtime has no package dependencies.",
  deployment: "Verified in clean consumers under JIT, trimming, and NativeAOT. NativeAOT evidence is Windows x64.",
  threads: "Serial by default. Opt into a bounded worker pool and mark eligible systems as partitioned.",
  requirements: ".NET 11 SDK, a project targeting net11.0, and C# 15. Install release candidates while .NET 11 is in preview.",
  status: "Research project with no users. Public API, storage layout, and generated code can change without compatibility shims.",
  generated: "Generated members go into your partial types and your namespace, so generated types must be partial.",
  structural: "Spawning, despawning, and adding components go through Commands inside systems and apply at schedule barriers.",
  ownership: "A world has one owner thread. Registration is fixed when the world is built. Storage is archetype tables.",
  comparisons: "The benchmark suite records Bevy 0.20 and Arch 2.1.0 counterparts on the same machine. One machine, not a ranking.",
  license: "MIT. The source is on GitHub.",
};

export const sample = {
  name: "Archeoids",
  summary: "A playable Asteroids style game. Every ship, rock, bullet, and particle is an entity.",
  hosts:
    "Silk.NET draws it on the desktop. The browser build runs the same simulation on .NET WebAssembly with WebGPU. Both share Game.cs, GameModel.cs, GameSystems.cs, and FrameGeometry.cs.",
  browserNote: "Needs WebGPU and a keyboard. Built from the latest commit on main, so it can be ahead of the NuGet release.",
};
