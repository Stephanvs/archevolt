# `[Each]` callbacks outside the kernel subset, 10 November 2026

After [round two](performance-hill-climb-round-2.md), an `[Each]` callback ran as a SIMD kernel only when every float of the written component shared one formula. A callback with a branch, a read of a second field, or a call into a helper kept the row loop. None of the three `[Each]` systems in Archeoids qualified.

This round makes both changes that the earlier measurements pointed to:

- Make the row loop fast in the two situations where it ran 3 to 7 times slower than it could.
- Widen the kernels so that these callbacks leave the row loop.

Three commits carry the work. No public API changed.

| Commit | Change | `each-wrap` on `mini`, ns per row |
| --- | --- | --- |
| `cb6bdb8` | Measurement harness `benchmarks/Archevolt.EachShapes` and `scripts/each-shapes.ps1` | none |
| `c0cd260` | Row loop in a method of its own, with an inlined copy of the callback | Empty start: 2.588 to 0.590 |
| `45eb957` | Kernels for branches, reads across fields, `Min`, `Max`, `Abs`, `Sqrt`, and helper calls | Both starts: 0.590 to 0.112 |

[results/each-kernels](results/each-kernels) holds the summaries, per-process samples, and [the ledger](results/each-kernels/ledger.tsv).

## Result

Baseline `b7680be` against `45eb957` on `mini`, five processes per side, 100,000 rows, one worker. Each cell is the median ns per row before and after ([final-mini](results/each-kernels/final-mini/summary.md)). An empty start means the system ran 1,200 times on an empty world before the entities were spawned.

| Case | Callback | Empty start | Populated start |
| --- | --- | --- | --- |
| `each-wrap` | Moves a position and wraps it with two `if` statements | 2.631 to 0.112 | 0.591 to 0.113 |
| `each-wrap-helper` | The same wrap through a helper that returns a value | 2.629 to 0.112 | 0.544 to 0.113 |
| `each-delegating` | Forwards its `ref` and `in` arguments to another method | 2.626 to 0.112 | 0.589 to 0.113 |
| `each-spin` | Adds one field of a component to another | 2.170 to 0.107 | 0.394 to 0.106 |
| `each-bounce` | Negates a velocity when a position is outside a range | 1.484 to 0.117 | 0.781 to 0.116 |
| `each-clamp` | Clamps with `MathF.Min` and `MathF.Max` | 1.183 to 0.100 | 0.393 to 0.106 |
| `each-expire` | Takes an `Entity` and `Commands`, so it stays a row loop | 6.941 to 0.915 | 1.466 to 0.911 |
| `each-length` | Writes a `float` from a two-float component, so it stays a row loop | 0.994 to 0.889 | 0.888 to 0.884 |
| `each-plain` | A kernel before this work | 0.115 to 0.114 | 0.113 to 0.114 |

The first six cases now run as kernels, at the speed of the hand-written `Vector256` versions of the same loops (`vector-wrap` 0.113, `vector-spin` 0.106 to 0.108). `each-expire` and `each-length` stay row loops and gain from the first change alone. `each-plain`, the named query, and the two hand-written `[EachBlock]` cases overlap the baseline in both orders.

## Conditions and method

| Host | CPU | System | How each process ran |
| --- | --- | --- | --- |
| `mini` | AMD Ryzen AI 9 HX 370 | Debian 13, Linux 7.0.14, `performance` governor | `taskset -c 2,3`, two of the four fast cores |
| `fiji` | Intel Core i7-8559U | NixOS, Linux 7.3.1, `powersave` governor | `docker run --cpuset-cpus=2,3 debian:trixie-slim` |
| Workstation | AMD Ryzen 9 5950X | Windows 11 Pro 10.0.26300, in use | NativeAOT images only |

.NET SDK 11.0.100-rc.3.26522.148 built every binary on the workstation. The Linux binaries are self-contained `linux-x64` publishes. The NativeAOT images are `win-x64` with `OptimizationPreference=Speed` and the default instruction set, so they use 128-bit vectors.

`Archevolt.EachShapes` builds one world of 100,000 entities and runs 13 systems over it on one worker. It warms for 4.5 seconds, then takes 31 samples of 8 runs per case in rotating order and reports the median per process. `scripts/each-shapes.ps1` runs the two builds in alternating process order and reports the median of the process medians with their range. After timing, each process runs every case once over 1,027 rows whose values take both sides of every branch, and compares every entity bit for bit with a reference written without the ECS. It also checks that no case allocates managed memory.

Identical builds measured against each other differ by at most 2% on `mini`, except `block-wrap` with a populated start at 5% ([noise-mini](results/each-kernels/noise-mini/summary.md)). On `fiji` they differ by at most 5%, except `each-bounce` with a populated start at 17% ([noise-fiji](results/each-kernels/noise-fiji/summary.md)). `mini` is the primary host for that reason.

Each change was compared with the build before it on `mini`, three processes per side, and the final state was compared with the baseline again, five processes per side. `fiji` repeated the step 1 comparison and the final one.

## Row loop: its own method and an inlined callback

The generated `Run` method contained the row loop and ran every frame. Two things made that loop slow.

- **An empty start.** A system that matched no entity for its first runs reached its final tier with the loop profiled as cold code. The runtime never compiled `Run` again, so the loop stayed slow after entities appeared. `Expire` in Archeoids starts this way, because bullets and particles do not exist at first.
- **No profile.** NativeAOT compiles without a profile, and it left the callback as a call per row unless the callback was very small.

`Run` now calls `__Rows` once per block. `__Rows` is marked `NoInlining`, so the runtime compiles it when a block first has rows. In an optimized build, `__Rows` calls a private copy of the callback marked `AggressiveInlining`. The copy is a member of the callback's own type, and its generated file repeats the `using` directives of the callback's file, so every name binds as it does in the callback. `__Rows` copies its parameters into locals, because Windows x64 passes a span by reference and the JIT otherwise reads its pointer and length from memory on every row.

The generator keeps the plain call when a copy could behave differently: an `extern alias`, `using` directives on an outer namespace, a preprocessor directive in the callback, a file-local type in the file, caller information, `[Conditional]`, or `[MethodImpl]` with `NoInlining`, `NoOptimization`, or `Synchronized`. Debug builds always call the method.

Step 1 against the baseline, median ns per row ([step1-mini](results/each-kernels/step1-mini/summary.md), [step1-fiji](results/each-kernels/step1-fiji/summary.md), [step1-win-aot](results/each-kernels/step1-win-aot/summary.md)):

| Case | `mini`, empty start | `mini`, populated start | `fiji`, empty start | NativeAOT, workstation |
| --- | --- | --- | --- | --- |
| `each-wrap` | 2.588 to 0.590 | 0.591 to 0.590 | 5.939 to 1.357 | 3.679 to 0.974 |
| `each-spin` | 2.162 to 0.394 | 0.393 to 0.394 | 4.505 to 1.063 | 0.668 to 0.541, overlap |
| `each-expire` | 6.889 to 0.919 | 1.460 to 0.952 | 15.049 to 1.764 | 1.847 to 1.020, overlap |
| `each-plain` | 0.115 to 0.114 | 0.113 to 0.115 | 0.321 to 0.328 | 0.267 to 0.218, overlap |

On `mini`, every `each` case with a populated start measured 1.00 to 1.02 times the baseline, except `each-expire` at 0.65.

**Open: two cases on the Intel host.** With a populated start on `fiji`, step 1 ran `each-spin` at 0.979 ns per row against 0.649, and `each-bounce` at 1.919 against 1.402. `each-spin` measured the same without tiered PGO, 0.986 against 0.656 ([step1-fiji-nopgo](results/each-kernels/step1-fiji-nopgo/summary.md)). The baseline itself ran `each-spin` at about 0.96 in 2 of 9 processes. Neither case changed on `mini`. Both are kernels after step 2, where they run 2 and 5 times faster than the baseline on `fiji`. The cause was not investigated. A callback of that shape that stays outside the kernel subset may run slower on that CPU when entities exist from the start.

## Kernels: one formula per float

The analysis now runs the callback symbolically and keeps one formula per float of the written component.

- **Locals and assignments.** It tracks locals, assignments to them, and assignments to their fields.
- **Branches.** For an `if` or a `?:`, it computes both outcomes from the state before the branch and selects between them on the condition. Comparisons become masks, and `&&`, `||`, and `!` combine them. Both operands of `&&` and `||` are evaluated, which is safe because they compare floats.
- **Functions.** `Abs`, `Sqrt`, `Min`, and `Max` of `MathF`, `Math`, and `float` map to the vector functions. A probe over NaN, signed zero, infinity, and denormal inputs found the same bits from both forms.
- **Calls.** It follows a call into a static method of the same compilation. By-value and `in` arguments bind to values, and a `ref` argument binds to the caller's floats.
- **Static fields.** A `static readonly` float or vector field is read once per block.

A callback whose floats all share one formula still gets the flat loop of round two. Otherwise the loop holds whole rows in a vector:

- Lanes whose formulas apply the same operation share one instruction. They differ by a per-lane constant, a shuffle of the loaded vector, or a mask.
- Lanes whose operations differ are computed apart and blended.
- A row whose size does not divide the vector advances by whole rows. The lanes past the last whole row are stored back unchanged, and the next iteration computes them.
- A row wider than four floats needs 256-bit vectors. Without them, the system uses the row loop.

A formula past 512 operations keeps the row loop, because each branch doubles the formula of a float it assigns.

Step 2 against step 1 on `mini`, median ns per row ([step2-mini](results/each-kernels/step2-mini/summary.md)):

| Case | Empty start | Populated start |
| --- | --- | --- |
| `each-wrap` | 0.590 to 0.112 | 0.590 to 0.112 |
| `each-wrap-helper` | 0.543 to 0.112 | 0.543 to 0.112 |
| `each-delegating` | 0.590 to 0.112 | 0.590 to 0.112 |
| `each-spin` | 0.395 to 0.106 | 0.394 to 0.106 |
| `each-bounce` | 0.677 to 0.116 | 0.679 to 0.115 |
| `each-clamp` | 0.392 to 0.099 | 0.393 to 0.106 |
| `each-plain`, guard | 0.114 to 0.114 | 0.113 to 0.113 |
| `each-expire`, guard | 0.914 to 0.912 | 0.916 to 0.951, overlap |

The final state on the other hosts, baseline to `45eb957`, median ns per row ([final-fiji](results/each-kernels/final-fiji/summary.md), [final-win-aot](results/each-kernels/final-win-aot/summary.md)):

| Case | `fiji`, empty start | `fiji`, populated start | NativeAOT, workstation |
| --- | --- | --- | --- |
| `each-wrap` | 6.089 to 0.398 | 1.484 to 0.355 | 2.551 to 0.272 |
| `each-delegating` | 6.194 to 0.397 | 1.523 to 0.352 | 2.617 to 0.274 |
| `each-spin` | 4.652 to 0.354 | 0.649 to 0.325 | 0.496 to 0.233 |
| `each-bounce` | 4.445 to 0.388 | 1.772 to 0.348 | 2.151 to 0.312 |
| `each-clamp` | 2.355 to 0.456 | 0.664 to 0.416 | 1.659 to 0.456 |
| `each-expire` | 15.246 to 1.839 | 3.468 to 1.785 | 1.335 to 0.974 |
| `each-length` | 2.688 to 0.827 | 0.773 to 0.759, overlap | 1.634 to 1.146 |
| `each-plain` | 0.332 to 0.338, overlap | 0.296 to 0.302, overlap | 0.239 to 0.213 |

The workstation was in use. Its hand-written `vector-wrap` and `vector-spin` cases, whose code is the same on both sides, measured 0.84 and 0.88 times the baseline, so differences below about 16% mean nothing there.

Generator tests compile eleven new shapes and run each kernel against its callback bit for bit, with the data of round two: 2,051 entities over two archetypes, four workers with partitions, three steps, and values that include NaN, infinities, signed zero, and denormals. The shapes cover a read across fields, `if` and `else if` with locals on a three-float row, `?:` with `||`, `&&`, `!`, `==`, and `!=`, the four functions, helper calls by value and by `ref`, a rotation of the three lanes of a `Vector3`, a five-float row, a row with a constant lane and a resource lane, `static readonly` fields, a helper that writes through `ref` and returns the component, and a helper with a local that both outcomes of a `?:` call. Sixteen shapes outside the subset are checked to keep the row loop, including a loop, an early `return`, `%`, a recursive helper, and a call that passes the written component by `ref` and by `in`.

A review of the analysis found callbacks that the first version compiled into a kernel with a different result. Each one calls a helper that writes through a `ref` parameter:

- The helper is called in one outcome of a `?:`, or on the right of `&&` or `||`. The callback skips the write for some rows, and the kernel made it for every row.
- The helper writes floats that the same call also passes through `in`, in a nested argument or behind a `ref` return. The callee sees the write, and the kernel had bound the parameter to the earlier value.
- The helper is the instance of a member access, such as `Advance(ref position).Value.X`. The analysis evaluated the call twice.
- The helper or the callback is `[Conditional]`. The compiler removes the call, and the kernel still made it.

The first, second, and fourth now keep the row loop, and the third is evaluated once. Each has a test that fails on the first version. The generated code of the benchmark harness and of Archeoids is byte for byte the same before and after, so the measurements above stand.

## Guards

- **Existing kernel.** `each-plain` overlaps the baseline on `mini` and `fiji` in both orders.
- **Partition sweep.** `./scripts/measure.ps1 -Mode Partition -RuntimeDefaults` on the workstation, three processes per side, passed its correctness and zero-allocation checks in all 120 cases per process ([partition](results/each-kernels/partition/summary.csv)). Its `[Each]` systems forward to `Movement.Each`, so they are kernels now: the direct case over 100,000 rows on one worker fell from 357.8 to 21.1 µs. That sweep begins with 0-row workloads, which is why its `[Each]` cases measured the cold loop before. The block and vector cases, whose generated code did not change, measured 0.70 to 1.16 times the baseline on that machine.
- **Allocation.** No case allocates managed memory in any process on any host.

## Compatibility

ApiCompat in strict mode reports no change in `Archevolt.dll`, `Archeoids.dll`, or `Archeoids.Browser.dll` ([api-compat.txt](results/each-kernels/verification/api-compat.txt)). It reports three differences that are not member changes:

- **`Archevolt.Particles.dll`.** The compiler moved its `NullableContext` and `Nullable` attributes between `MotionKernel` and its members, because the type gained the private inlined copy. No member was added, removed, or retyped.
- **`Archevolt.Benchmarks.dll` and `Archevolt.Tests.dll`.** The `[Benchmark]` and `[Fact]` attributes record source paths and lines. The baseline was built in another directory, and tests were added.

An exception thrown in a callback no longer names the callback in its stack trace in an optimized build. It names a generated method.

## Verification

`./scripts/verify.ps1` passed all nine stages at `45eb957` after `dotnet build-server shutdown` ([verify.txt](results/each-kernels/verification/verify.txt)). The stages are restore and build, 236 tests, headless Archeoids, pack, package contents, the JIT and NativeAOT package consumers, the NativeAOT particle sample, and the allocation smoke check. The browser and WebAssembly target was built but not run in a browser.

A stale compiler server once produced generated files from an older generator build. Every measured build followed `dotnet build-server shutdown`.

## What remains

- **Archeoids.** `Spin` is a kernel now. `Move` wraps with float `%`, which has no vector form with the same results, so it keeps the row loop. A wrap written with a comparison and a subtraction qualifies. `Expire` takes an `Entity` and `Commands` and keeps the row loop, with the inlined copy.
- **Named queries.** A named query's loop is in user code, so the generator cannot move it. `query-wrap` on `mini` takes 1.19 ns per row after an empty start against 0.77 after a populated one.
- **Rows of different sizes.** A callback that writes a `float` from a `Vector2`, such as `each-length`, keeps the row loop.
- **`Entity` and `Commands`.** A callback with either parameter keeps the row loop.
- **Early `return` and loops.** Both keep the row loop.
- **The Intel cases above.**
- **WebAssembly.** Not measured. The NativeAOT results suggest the browser build gains the same way, which is an inference.

## Reproduce

```powershell
# Publish each side, for example at b7680be and at 45eb957:
dotnet build-server shutdown
dotnet publish benchmarks/Archevolt.EachShapes -c Release -r linux-x64 --self-contained true -o <side>/shapes-linux-jit
dotnet publish benchmarks/Archevolt.EachShapes -c Release -r win-x64 -p:EachShapesAot=true -o <side>/shapes-win-aot
# Copy both linux directories to the host, then:
./scripts/each-shapes.ps1 -Remote mini -Prefix 'taskset -c 2,3' `
    -Baseline archevolt-bench/baseline/shapes-linux-jit -Candidate archevolt-bench/step2/shapes-linux-jit `
    -Launches 5 -Output artifacts/each-kernels/final-mini
./scripts/each-shapes.ps1 -Baseline <baseline>/shapes-win-aot -Candidate <candidate>/shapes-win-aot `
    -Launches 3 -Modes populated -Output artifacts/each-kernels/final-win-aot
```

`Archevolt.EachShapes` was added before the generator changed, so the baseline side is the harness built at `cb6bdb8`, whose generator is the one at `b7680be`.
