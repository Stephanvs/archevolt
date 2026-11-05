# Performance hill climb, round two, 5 November 2026

After [round one](performance-hill-climb-report.md), you asked for three changes:
- End the cost that the spinning worker pool put on short single-item batches.
- Shorten the `Get<T>` lookup.
- Compile `[Each]` callbacks to SIMD wherever that gives the same result.

You deferred the change that would pass resource values to `[Each]` callbacks. It needs a public API change, and [ROADMAP.md](../ROADMAP.md#open-decisions) now lists it as an open decision.

Three commits carry the changes. Each passed the plan's gate in two batches, or, for the worker pool, checks against the code before the spin and against the spin alone. No public API, generated public member, or consumer source changed.

| Commit | Change | Primary result, median µs |
| --- | --- | --- |
| `bbb8775` | A single-item batch parks spinning workers and skips the pool lock | Short direct batches with four workers no longer run slower than before the spin |
| `8a78107` | Shorter `Get<T>` lookup | 1,000 entities: 5.77 to 5.12 and 5.87 to 5.31. 100,000 random entities: 1,391 to 1,298 and 1,413 to 1,301 |
| `846cace` | SIMD kernels for lane-uniform `[Each]` callbacks | Movement over 100,000 entities on one worker: 96.2 to 21.4 and 97.4 to 22.0. Tracked write over 100,000: 94.2 to 31.0 and 95.7 to 35.4 |

[results/hill-climb](results/hill-climb) holds the summaries, per-process samples, and [the ledger](results/hill-climb/ledger.tsv), which continues round one's.
The ledger, the raw environment files, and the verification log name `72043e8` and `3f0555a`. Those commits have the same trees as `8a78107` and `846cace`. The commits were replayed to correct a figure in the worker-pool commit message.

## Distance to Bevy

The Bevy column is the 2 November reference ([results.md](results.md)), measured on an idle machine. The Archevolt columns come from this round on the same machine while it was in use. Each value is the median of three or five process means.

| Case | Bevy 0.20 | Archevolt before this round | Archevolt now |
| --- | ---: | ---: | ---: |
| Movement, 100,000, one worker (`for_each` in Bevy) | 23.70 | 96.2, 97.4 | 21.4, 22.0 |
| Tracked write, 1,000 | 0.269 | 1.14, 1.11 | 0.46, 0.33 |
| Tracked write, 100,000 | 30.67, rerun 27.15 | 94.2, 95.7 | 31.0, 35.4 |
| Write, then `Changed<T>` over all, 100,000 | 99.46 | 167, 170 | 126, 137 |
| `Get<T>`, 1,000 | 4.04 | 5.77, 5.87 | 5.12, 5.31 |
| `Get<T>`, 100,000 random | 862 | 1,391, 1,413 | 1,298, 1,301 |

The dense movement update now matches Bevy's. The tracked write is within about 15%. The write-then-scan case still trails by about 30%. Its scan half is the `Changed<T>` named query, whose callback converts each `float` to `double`, and that conversion chain sets the pace (see [What remains](#what-remains)). `Get<T>` still trails by 1.3 to 1.5 times.

## Conditions and method

Same as round one: an AMD Ryzen 9 5950X in normal use, Windows 11 Pro 10.0.26300, .NET SDK and runtime 11.0.0-rc.3. Each candidate was built with `--no-incremental -p:EmitCompilerGeneratedFiles=true` and its whole output preserved. Every comparison ran both preserved outputs in alternating process order. The gate is round one's: the primary family at least 5% faster in two batches with process ranges that do not overlap, no reproducible guard regression above 3%, zero steady-state managed allocation, tests and API check passing.

The baseline for each experiment is the build before it.
- **Worker pool.** Compared with `b882d90`, the code before the spin, and with the spin alone (E2b).
- **`Get<T>`.** Compared with the E3 build, which has the same storage code as the branch before it.
- **Kernels.** Compared with the build that has both earlier commits.

## Worker pool: park on single-item batches

Round one's spin left workers spinning while the owner ran a single-item batch, and those batches ran up to 1.5 times slower than before the spin. E3 makes a single-item batch set a `Parked` flag on the padded signal line. Spinning workers see it and block at once. The next parallel batch clears it and wakes them through the monitor, as before the spin.

E3 removed the regression. Under default settings, though, 100-row direct batches ran 10 to 18% slower than before the spin ([E3-partition](results/hill-climb/E3-partition/comparison.txt)). The parked workers took the pool lock to wait while the owner took it for its next batch. E3b, the committed version, runs a single-item batch without the lock. That batch shares no state with the workers. The owner already owned every field it writes, and workers read those fields only after a parallel batch publishes them under the lock.

Four-worker direct cases from round one's regression table. Each cell is baseline to candidate, median µs:

| Case | E2b, no PGO (round one) | E3b, no PGO | E3b, default |
| --- | --- | --- | --- |
| `[Each]`, 1,000 | 2.30 to 2.81, slower | 2.37 to 2.84, overlap | 3.06 to 3.38, overlap |
| `Vector256` block, 4,096 | 1.28 to 1.79, slower | 1.49 to 1.04, overlap | 1.43 to 1.46, overlap |
| `Vector256` block, 16,384 | 3.65 to 5.43, slower | 6.75 to 3.64, overlap | 6.00 to 5.97, overlap |
| Indexed block, 100 | | 0.33 to 0.28, overlap | 0.37 to 0.31, overlap |
| `[Each]`, 100 | | 0.43 to 0.39, overlap | 0.60 to 0.54, overlap |

Against the code before the spin, no four-worker case ran slower in either configuration ([E3b-partition-nopgo](results/hill-climb/E3b-partition-nopgo/comparison.txt), [E3b-partition](results/hill-climb/E3b-partition/comparison.txt)). Partitioned batches of 100 to 16,384 rows stayed 2.6 to 11 times faster. At one worker, 0- to 1,000-row runs got 5 to 12% faster in 11 and 13 of 60 cases, because they no longer take the lock. The other single-worker cases overlap.

The cost is a parallel batch that follows a single-item batch. It has to wake the workers again. Against the spin alone, partitioned 100-row batches ran 1.46 to 1.54 times slower, 0.95 to 1.47 µs, and 1,000-row batches overlapped ([E3b-vs-E2b-nopgo](results/hill-climb/E3b-vs-E2b-nopgo/comparison.txt)). The sweep measures 16 runs of one case per sample, so one wake per sample adds about 0.5 µs per run. In a frame, the cost is one wake per change from single-item to parallel batches, the same wake every parallel batch paid before the spin.

The 25 repeats of the scheduling tests, which include the worker pool stress tests, passed ([E3b-scheduling-repeats.txt](results/hill-climb/verification/E3b-scheduling-repeats.txt)). The CPU cost of spinning between closely spaced parallel batches, which round one measured, is unchanged.

## `Get<T>`: shorter lookup chain

`TryGet<T>` read the component's descriptor to reject tags. It then looked up the archetype's column for the component, then the table's pointer for that column. D2 makes three changes:
- **Registry.** It keeps a second type-to-id map that marks tags as missing. One array read either returns the id or falls back to the original checks, which throw the same exceptions in the same order.
- **Archetype.** Each archetype aliases a pointer array indexed by component id. The table updates that array whenever it moves its columns.
- **Table.** `Table.Value<T>` still checks the row against the table's count.

| Case | Batch 1 | Batch 2 |
| --- | --- | --- |
| 1,000 entities | 5.77 [5.76, 5.90] to 5.12 [5.03, 5.30] | 5.87 [5.84, 5.92] to 5.31 [5.11, 5.59] |
| 100,000 random entities | 1,391 [1,376, 1,406] to 1,298 [1,262, 1,306] | 1,413 [1,404, 2,553] to 1,301 [1,300, 1,334] |

Five processes per side, all ranges separate ([D2-b1](results/hill-climb/D2-b1/summary.md), [D2-b2](results/hill-climb/D2-b2/summary.md)). Neither side allocates. Round one's D1 failure-path test came back with D2, extended to unregistered types. It covers `Get` and `TryGet` for a missing component, a stale entity, a tag, and an unregistered type. It passes on the code before and after. The extra memory is one pointer per registered component per archetype.

## `[Each]`: SIMD kernels

RyuJIT does not vectorize the scalar row loop that `[Each]` generates, and LLVM vectorizes Bevy's. That accounted for most of the gap on dense updates. Round one measured the same arithmetic written with `Vector256` in `[EachBlock]` at Bevy's speed.

In optimized builds, the generator now reads the callback's operations. It looks for one property: each float of the one written component gets the same formula over the same float of the other components, resource values, and constants. It tracks the value of every float lane through the method. The method may use:
- Locals, and float `+`, `-`, `*`, `/`, and negation.
- The `+`, `-`, and `*` operators of `Vector2`, `Vector3`, and `Vector4`.
- Positional record constructors and `with` expressions.
- Field and auto-property reads and writes.

Anything else, including a method call, a branch, a `double`, a computed property, or a formula that moves a value between lanes, keeps the call per entity.

A qualifying callback runs as a `Vector256` or `Vector128` loop over the columns, treated as flat float arrays, with a scalar tail. The kernel keeps the callback's operations and operand order. It writes negation as a sign-bit flip, which matches scalar negation, and it reads each resource value once per block. A probe checks each component's size and field order once at startup. If the probe fails, the system uses the call per entity. Tracked columns get their stamps from one `Fill` per block, the same values the row loop wrote. Debug builds always call the method. The generated members are private to the system's body class, so ApiCompat reports no change in any consumer assembly.

Of the shapes in this repository, the benchmark movement `[Each]`, the tracked `Mark` write, and the particle sample's `Integrate` qualify. Archeoids' `Move` calls `Arena.Wrap`, and `Spin` mixes two fields of one component. The partition sweep's `[Each]` systems call `Movement.Each`. All three keep the scalar loop.

Three processes per side, median µs with range ([S1-b1](results/hill-climb/S1-b1/summary.md), [S1-b2](results/hill-climb/S1-b2/summary.md)):

| Case | Batch 1 | Batch 2 |
| --- | --- | --- |
| `[Each]`, 1,000, one worker | 1.08 [1.05, 1.20] to 0.24 [0.21, 0.25] | 1.10 [1.07, 1.13] to 0.31 [0.29, 0.31] |
| `[Each]`, 100,000, one worker | 96.2 [94.6, 97.6] to 21.4 [20.9, 22.2] | 97.4 [97.3, 100.9] to 22.0 [21.6, 40.0] |
| `[Each]`, 100,000, four workers | 30.1 [27.7, 61.8] to 12.0 [7.7, 14.5] | 29.3 [27.9, 35.3] to 8.5 [7.9, 16.1] |
| Tracked write, 1,000 | 1.14 [1.08, 1.18] to 0.46 [0.38, 0.49] | 1.11 [1.08, 1.14] to 0.33 [0.33, 0.44] |
| Tracked write, 100,000 | 94.2 [93.5, 95.4] to 31.0 [29.9, 32.0] | 95.7 [93.1, 99.5] to 35.4 [29.5, 37.3] |
| Write, then `Changed<T>` over all, 100,000 | 167 [165, 171] to 126 [103, 130] | 170 [168, 180] to 137 [103, 142] |

- **Four workers, 1,000 entities.** Overlapped in both batches, at 0.76 and 0.77 times the baseline. Dispatch dominates that case.
- **1,000,000 entities.** One worker ran faster in batch 1, 1,037 against 325 µs. In batch 2 the candidate's processes spread from 285 to 1,528 µs. Four workers overlapped in batch 2. Those cases are inconclusive on this host.
- **Guards.** The `Vector256` `[EachBlock]` overlapped in every case. The handwritten loop is the same code on both sides. It ran slower once at 100,000 entities on one worker, 81.7 against 94.1 µs, and overlapped in batch 2. That is the 82-versus-94 µs layout split that this machine shows between processes, recorded in round one. No case allocates.

Generator tests compile five qualifying shapes and run each kernel against the callback bit for bit:
- **Shapes.** A float record built with a constructor, a `Vector3` record updated with `with`, a tracked record with a constant, a mutable struct with field compound assignments, negation and division, and a `Vector2` record.
- **Data.** 2,051 entities over two archetypes, four workers with partitions, three steps. Values are random and include NaN, infinities, signed zero, denormals, and a NaN resource value.
- **Comparison.** Lanes compare bit for bit, except that two NaNs count as equal, because .NET does not define which NaN payload results when two NaN operands meet.
- **Change tracking.** The tracked shape also checks that a `Changed<T>` system sees every row after each step.

Six shapes that do not qualify and a Debug build are checked to keep the scalar loop. Changing the kernel's `+` to `-` failed four of the five equivalence cases and the existing `[EachBlock]`-against-`[Each]` test. The fifth uses no `+`.

## Compatibility

ApiCompat in strict mode reports no change in `Archevolt.dll`, `Archevolt.Benchmarks.dll`, `Archeoids.dll`, `Archeoids.Browser.dll`, or `Archevolt.Particles.dll` against round one's baseline. The test assembly differs only because a test was added: xUnit v3 records each `[Fact]`'s source line, so every later test's attribute changed. The kernel commit changed no assembly's API against the build before it.

## Verification

`./scripts/verify.ps1` passed all nine stages at `846cace` after `dotnet build-server shutdown` ([verify-round2.txt](results/hill-climb/verification/verify-round2.txt)). The stages are restore and build, 190 tests, headless Archeoids, pack, package contents, the JIT and NativeAOT package consumers, the NativeAOT particle sample, whose `Integrate` now runs as a kernel, and the allocation smoke check. The browser and WebAssembly target was built but not run in a browser. No browser sample callback qualifies for a kernel.

## What remains

- **Optional term and `Changed<T>` scan.** Both trail Bevy by 1.3 to 1.9 times. Their row loops hold everything in registers. The benchmark callback converts each `float` to `double`, and RyuJIT emits `vcvtss2sd` with a false dependency on its destination register, so the conversions form a chain. This is inferred from the disassembly and not measured. It would be a RyuJIT issue to report, not an Archevolt change.
- **`Get<T>`.** Still about 1.3 to 1.5 times Bevy's. The owner-thread check, the idle check, two directory bounds checks, and two cache misses at 100,000 entities remain.
- **Kernel coverage.** A callback that calls another method, branches, or mixes lanes keeps the scalar loop. Following calls into methods of the same compilation would cover the partition sweep's delegating systems. Strided loads would cover cross-field updates such as `Spin`.
- **Resource values in `[Each]`.** Deferred as an API change. The kernels already read a resource once per block, so the change would now help only callbacks that do not qualify.
