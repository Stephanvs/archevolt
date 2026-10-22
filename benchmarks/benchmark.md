# Dense movement: Archevolt and Bevy

This benchmark runs one workload in two ECS libraries. The .NET side is `ReferenceComparisonBenchmarks` in [Archevolt.Benchmarks](Archevolt.Benchmarks/ReferenceComparisonBenchmarks.cs), measured with BenchmarkDotNet. The Rust side is [bevy-reference](bevy-reference/src/lib.rs), a port of the same workload to `bevy_ecs`, measured with criterion.

The two sides differ in language, compiler, storage layout, and harness. [ROADMAP.md](../ROADMAP.md) treats cross-language Bevy numbers as contextual unless the workload and compiler settings are controlled, so this file lists which conditions match and which do not. The results describe this one workload; they do not rank the engines.

## Workload

- 100,000 entities, each with a `Position` and a `Velocity`. Both components are three 32-bit floats, 12 bytes.
- Entity `i` starts at `Position(i % 97, 0, 0)` with `Velocity(1, 2, 3)`.
- One operation sets `position = position + velocity * (1 / 60)` on every entity, on one thread.
- The Archevolt and Bevy systems read the delta from a `FrameTime` resource. The handwritten loops and the Arch callback use a constant.

## Cases

| .NET | Rust | What runs |
| --- | --- | --- |
| `Handwritten` | `handwritten` | A loop over two arrays, without an ECS or scheduler. |
| `GeneratedEach` | `bevy_for_each` | The ECS drives the loop and runs a per-entity body: Archevolt's generated `[Each]` system and Bevy's `query.iter_mut().for_each(...)`. |
| `GeneratedQuery` | `bevy_for_loop` | The system iterates its query: `foreach` over an Archevolt named query and `for` over a Bevy `Query`. |
| `ArchInline` | none | Arch 2.1.0 `InlineQuery` with a struct callback, without a scheduler. |

The Archevolt and Bevy cases run one system through a schedule on one thread. Archevolt uses concurrency 1; Bevy uses `SingleThreadedExecutor`. Their times include schedule dispatch and resource access. [Cargo.toml](bevy-reference/Cargo.toml) pins `bevy_ecs` to 0.20.0 with its default features.

`cargo test` in `bevy-reference` runs each Bevy system once over 1,000 entities and checks that every position equals the result of the handwritten loop.

## Running

The .NET side, from the repository root:

```powershell
dotnet run --project benchmarks/Archevolt.Benchmarks -c Release -- --filter "*ReferenceComparisonBenchmarks*"
```

The Rust side, once per build target (see [Rust build target](#rust-build-target)):

```powershell
cd benchmarks/bevy-reference
cargo test
cargo bench
$env:RUSTFLAGS = '-C target-cpu=native'; cargo bench; Remove-Item Env:RUSTFLAGS
```

In a POSIX shell, the last line is `RUSTFLAGS="-C target-cpu=native" cargo bench`. Changing `RUSTFLAGS` rebuilds all dependencies.

BenchmarkDotNet switches Windows to the High performance power plan while it runs and restores the previous plan afterwards. Criterion leaves the plan alone. To run criterion under the same plan, run `powercfg /setactive SCHEME_MIN` before `cargo bench`, then reactivate the previous plan, for example `powercfg /setactive SCHEME_BALANCED`.

## Rust build target

`cargo bench` compiles for baseline x86-64, which includes SSE2 but not AVX2. BenchmarkDotNet reports RyuJIT generating x86-64-v3 code, which includes AVX2. `-C target-cpu=native` gives the Rust build the host's instruction set. On the test machine, `-C target-cpu=x86-64-v3` matched the `native` results within run-to-run variation.

The target changes the code LLVM generates for `handwritten` and `bevy_for_each`, as the assembly from `cargo rustc --release --lib -- --emit asm` shows. With the baseline target, both loops handle four entities per iteration and assemble them from scalar loads and SSE2 shuffles; both took longer than `bevy_for_loop`. With `native`, both loops handle eight entities per iteration with contiguous 256-bit loads and stores, and `bevy_for_each` writes the eight change ticks with one store; both ran 4.0 to 4.7 times faster than with the baseline target.

`bevy_for_loop` compiles to per-entity code with both the baseline and `native` targets, and measured 73.646 to 81.938 µs across all three targets.

The .NET JIT always compiles for the host CPU, so the .NET side has no equivalent setting. The disassembly notes in [.audit/performance-final.md](../.audit/performance-final.md), captured with tiered compilation disabled, show the .NET loops processing one entity at a time.

## What else differs

- Archevolt stores components in chunks with 64-byte-aligned native columns. Bevy stores each component of a table in one contiguous column. The .NET handwritten loop uses 64-byte-aligned native arrays; the Rust handwritten loop uses `Vec`.
- Bevy writes a change tick on every mutable access. The Archevolt components in this workload are untracked, so Archevolt writes no change stamps.
- .NET uses RyuJIT with tiered compilation and dynamic PGO. Rust uses LLVM with cargo's default release profile; the crate does not change `lto` or `codegen-units`.
- BenchmarkDotNet reports the arithmetic mean of its iterations. Criterion reports the slope of a linear regression of time against iteration count, with a 95% confidence interval. In the second x86-64 run, criterion's slope and mean differed by up to 7%.

## Results

The runs took place on 21 October 2026 at commit `414eb10`, with the `bevy-reference` crate added:

- AMD Ryzen 9 5950X (16 cores, 32 threads), Windows 11 Pro 10.0.26300.9711, High performance power plan, normal desktop session.
- .NET SDK 10.0.402, .NET 10.0.13, BenchmarkDotNet 0.15.8 with its default job.
- rustc 1.98.0, `bevy_ecs` 0.20.0, criterion 0.8.2 with its defaults: 3 s warm-up, 5 s measurement, 100 samples.

The harnesses took turns: criterion run 1, BenchmarkDotNet run 1, criterion run 2 plus the single x86-64-v3 run, then BenchmarkDotNet run 2. Every operation updates 100,000 entities, so 100 µs is 1 ns per entity.

### .NET, BenchmarkDotNet mean in µs

| Case | Run 1 | Run 2 |
| --- | ---: | ---: |
| `Handwritten` | 86.91 | 90.30 |
| `GeneratedEach` | 120.04 | 125.86 |
| `GeneratedQuery` | 211.03 | 223.38 |
| `ArchInline` | 107.35 | 119.52 |

No case allocated managed memory.

### Rust, criterion estimate in µs

| Case | x86-64, run 1 | x86-64, run 2 | native, run 1 | native, run 2 | x86-64-v3 |
| --- | ---: | ---: | ---: | ---: | ---: |
| `handwritten` | 99.443 | 99.822 | 21.886 | 21.181 | 21.947 |
| `bevy_for_each` | 105.55 | 115.09 | 26.486 | 25.266 | 26.406 |
| `bevy_for_loop` | 80.601 | 73.646 | 81.938 | 81.084 | 79.585 |

### Ratio to the handwritten loop of the same language and run

| Case | Run 1 | Run 2 |
| --- | ---: | ---: |
| `GeneratedEach` | 1.38 | 1.40 |
| `GeneratedQuery` | 2.43 | 2.48 |
| `ArchInline` | 1.24 | 1.33 |
| `bevy_for_each`, x86-64 | 1.06 | 1.15 |
| `bevy_for_each`, native | 1.21 | 1.19 |
| `bevy_for_loop`, x86-64 | 0.81 | 0.74 |
| `bevy_for_loop`, native | 3.74 | 3.83 |

BenchmarkDotNet computed the .NET ratios. The Rust ratios divide criterion estimates from the same run.

Within a run, BenchmarkDotNet's standard deviation was 1.3 to 6.1 µs, except 21.2 µs for `GeneratedQuery` in run 2, and every criterion confidence interval stayed within 6% of its estimate. Between runs, the same case differed by up to 11%.

## Observations

The ranges below compare each Rust run with each .NET run.

- With the baseline x86-64 target, `bevy_for_each` took 0.84 to 0.96 times as long as `GeneratedEach`. With `native`, it took 0.20 to 0.22 times as long.
- `bevy_for_loop` took 0.33 to 0.39 times as long as `GeneratedQuery` under every target.
- `bevy_for_each` stayed between 1.06 and 1.21 times its handwritten loop under every target. The `bevy_for_loop` ratio moved from below 1 to almost 4 because the handwritten loop changed with the target and `bevy_for_loop` did not.
- The earlier BenchmarkDotNet short job in [.audit/performance-final.md](../.audit/performance-final.md), also shown in [docs/performance.mdx](../docs/performance.mdx), measured ratios of 1.044 for `GeneratedEach` and 2.018 for `GeneratedQuery`, with 3 warm-up and 8 measured iterations. The default-job runs above measured 1.38 to 1.40 and 2.43 to 2.48.

## Not covered

The workload has one archetype, two small components, one thread, and steady-state updates only. The .NET [workload matrix](Archevolt.Benchmarks/PerformanceMeasurement.cs) also measures multiple archetypes, wide components, change and optional filters, structural changes, and parallel execution; none of those have a Bevy port. All results come from one machine.
