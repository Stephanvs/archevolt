**Release performance evidence, 20 October 2026**

The measured fixed-capacity traversal paths allocate zero managed bytes, including the four-worker paths. The short BenchmarkDotNet comparison places generated `Each` near the handwritten baseline at 100,000 entities, while the named query costs about twice as much. This is an initial baseline with visible timing variation, not a numeric regression policy or a claim of general superiority over another ECS.

**Reproduce and identify the measured source**

Run `pwsh -NoProfile -File scripts/measure.ps1 -Mode All` from the repository. `Matrix`, `Bdn`, and `Disassembly` can also run separately; `-NoBuild` uses the existing Release binary. The script fails if any command fails or BenchmarkDotNet does not execute all four comparison cases. The other BenchmarkDotNet fixture classes remain available through the regular benchmark CLI for longer selected runs.

The original results used by the tables below are preserved under `artifacts/performance-before-timing`: `matrix.csv`, `bdn.txt`, `bdn/results/*`, `jit-disassembly.txt`, `jit-selected-methods.txt`, `jit-method-sizes.csv`, `dotnet-info.txt`, `cpu.txt`, `os.txt`, environment files, source hashes, working-tree status, assembly/generated-source/AOT size tables, and command durations. New runs use `artifacts/performance`; the final-source confirmation below is separate from the original comparison. These generated artifacts are not committed. The tables below preserve the measured results in this tracked report.

The measured working tree was based on commit `5360f908004ad4345924efeed393c74884c2fd48` plus the implementation changes listed in `artifacts/performance-before-timing/working-tree-All.txt`. Its `source-hashes-All.csv` identifies the C# and project files actually present before the run. This measurement predates the subsequently added optional schedule-timing API; timings were disabled because that feature was not yet present. It is not represented as a measurement of a later final commit.

Machine: AMD Ryzen 9 5950X, 16 physical / 32 logical cores, Windows 11 Pro 10.0.26300.9711, x64, approximately 64 GiB RAM. SDK 10.0.402, MSBuild 18.9.15, runtime .NET 10.0.13, C# 14, Release, deterministic build, warnings as errors, BenchmarkDotNet 0.15.8, concurrent workstation GC. BenchmarkDotNet selected its High performance power plan and restored Balanced afterward. CPU affinity and clock frequency were not fixed. No other repository build/test work was running during the timed BenchmarkDotNet run; this was still a normal desktop session.

**Comparable dense movement**

All four cases use the same `Position` and `Velocity` structs, initial values, three scalar `position += velocity * (1f / 60)` updates, and untracked components. The handwritten baseline uses two contiguous 64-byte-aligned native arrays. Archevolt uses aligned native columns in its chunks and includes schedule dispatch, resource access, lease checks, and query traversal. These layout and safety costs remain part of the result; the handwritten baseline has no scheduler. The named query and `Each` share the same world/storage implementation.

Arch is pinned to **2.1.0**, only in the benchmark project. Its reference uses `World.InlineQuery<Integrate, Position, Velocity>` with a struct implementing `IForEach<Position, Velocity>`, the documented inlinable query path. Arch owns its normal managed chunk storage; the two engines do not offer identical alignment, scheduling, or access-guard semantics. This is a secondary reference, not a controlled substitution of one storage allocator. Sources: [NuGet package/version](https://www.nuget.org/packages/Arch/2.1.0), [official inline-query documentation](https://github.com/genaray/Arch/wiki/Performance-Features), [official world/query setup](https://github.com/genaray/Arch/wiki/Quickstart).

BenchmarkDotNet used one launch, three warmup iterations and eight measured iterations, with its default tiered-JIT settings. Each operation updates all 100,000 entities. “Error” is the half-width of its 99.9% confidence interval, not standard deviation.

| Case | Mean µs | StdDev µs | Median µs | Min-max µs | Error µs | Mean ns/entity | Managed B/op |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Handwritten | 104.279 | 11.503 | 102.792 | 90.614-120.560 | 21.994 | 1.043 | 0 |
| Generated Each | 108.866 | 6.659 | 107.107 | 101.542-121.253 | 12.732 | 1.089 | 0 |
| Generated named query | 210.411 | 12.125 | 205.083 | 199.741-230.562 | 23.183 | 2.104 | 0 |
| Arch 2.1.0 inline query | 146.054 | 24.043 | 153.150 | 111.106-170.636 | 45.971 | 1.461 | 0 |

The generated `Each` mean is 1.044 times the handwritten mean in this sample. The named-query mean is 2.018 times that baseline. The confidence intervals and Arch spread rule out treating these four short runs as a universal ranking.

**Broader workload matrix**

The custom matrix performs 32 warmup calls and 64 separately timed calls per case. It disables tiered compilation and ReadyToRun for immediate fully optimized JIT code, so its absolute times must not be mixed with the BenchmarkDotNet table. It uses a 10 MHz Stopwatch, records median/p95/max, and measures both owner-thread and process-wide managed allocation. All allocation figures in this run matched between those two counters. Small empty-schedule timings are near the timer's granularity. Cases run sequentially, so cache state, CPU boost and desktop noise can affect comparisons.

The matrix includes 1,000 through 1,000,000 entities, one versus sixteen tag-defined archetypes, an additional unaccessed 256-byte component, tracked writes, empty/all changed filters, 50% optional-component presence, registration, cache refresh, structural churn, command capacity and worker/barrier overhead. Named-query bodies remain a single system invocation under four-worker execution; only the opted-in `Each` body partitions its chunks. There is no claim of named-query row parallelism.

Registration creates and disposes a new world with already-warmed CLR statics. It does not measure process startup or the first static initialization. Bind/first-refresh timings include the work named in the case. The dedicated first-query-refresh case binds and warms before setup creates another matching archetype, then times the next traversal; its 200 allocated bytes are cache refresh, outside the warmed fixed-shape target.

Structural cases perform one tag insert and one tag removal for each of 1,000 entities. Native chunk/archetype paths are warmed first. The first-command-playback cases rebuild the world outside each measured interval and compare zero initial command capacity against 65,536 reserved bytes. Both still include structural work and retained chunk-wrapper allocation. Reservation reduces measured managed allocation from 259,880 to 96,144 bytes; the short timing samples do not establish a throughput improvement. Warmed playback still allocates 96,000 bytes per 1,000 round trips, and is not described as allocation-free.

The `native` column is an idle **post-run snapshot** of `World.NativeBytes`: allocated native chunk bytes, including retained chunks. It excludes managed metadata, managed resources and external reference-library storage. It is not process memory, live component payload, or a peak. Blank means the harness did not collect a native snapshot for that case.

| Case | Entities | Mean µs | Median µs | p95 µs | Max µs | Managed B/op | Native bytes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| movement-handwritten | 1000 | 1.345 | 1.100 | 1.600 | 9.300 | 0.000 |  |
| movement-generated-each | 1000 | 2.950 | 2.850 | 3.600 | 3.800 | 0.000 |  |
| movement-generated-query | 1000 | 3.363 | 3.200 | 3.300 | 7.900 | 0.000 |  |
| movement-arch-2.1.0-inline | 1000 | 1.269 | 1.300 | 1.300 | 1.400 | 0.000 |  |
| movement-handwritten | 100000 | 83.941 | 80.400 | 105.600 | 108.300 | 0.000 |  |
| movement-generated-each | 100000 | 196.020 | 191.500 | 217.500 | 287.100 | 0.000 |  |
| movement-generated-query | 100000 | 296.870 | 284.100 | 341.800 | 634.600 | 0.000 |  |
| movement-arch-2.1.0-inline | 100000 | 101.259 | 99.950 | 104.800 | 134.900 | 0.000 |  |
| movement-handwritten | 1000000 | 1563.673 | 1572.050 | 1865.700 | 1959.500 | 0.000 |  |
| movement-generated-each | 1000000 | 4074.778 | 4086.350 | 5102.600 | 6104.100 | 0.000 |  |
| movement-generated-query | 1000000 | 4659.480 | 4623.600 | 6061.200 | 6509.900 | 0.000 |  |
| movement-arch-2.1.0-inline | 1000000 | 2777.338 | 2703.300 | 3259.400 | 3972.700 | 0.000 |  |
| movement-generated-each-concurrency-4 | 1000 | 34.161 | 32.550 | 49.300 | 56.400 | 0.000 |  |
| movement-generated-query-concurrency-4 | 1000 | 27.383 | 25.600 | 40.700 | 71.800 | 0.000 |  |
| movement-generated-each-concurrency-4 | 100000 | 110.105 | 106.400 | 141.000 | 203.600 | 0.000 |  |
| movement-generated-query-concurrency-4 | 100000 | 320.856 | 300.750 | 434.200 | 593.200 | 0.000 |  |
| movement-generated-each-concurrency-4 | 1000000 | 1292.144 | 1247.500 | 1721.600 | 1999.400 | 0.000 |  |
| movement-generated-query-concurrency-4 | 1000000 | 4506.577 | 4421.850 | 5616.000 | 6347.700 | 0.000 |  |
| traversal-archetypes-1-payload-0 | 100000 | 195.788 | 188.300 | 208.600 | 401.300 | 0.000 | 3211264 |
| traversal-archetypes-1-payload-256 | 100000 | 322.016 | 314.850 | 381.400 | 447.500 | 0.000 | 28935296 |
| traversal-archetypes-16-payload-0 | 100000 | 242.622 | 202.250 | 343.500 | 353.500 | 0.000 | 3407872 |
| traversal-archetypes-16-payload-256 | 100000 | 581.245 | 469.150 | 1181.900 | 1596.300 | 0.000 | 29048832 |
| tracked-write | 1000 | 3.733 | 3.550 | 4.300 | 13.500 | 0.000 | 64896 |
| changed-none | 1000 | 1.695 | 1.700 | 2.000 | 2.100 | 0.000 | 64896 |
| write-then-changed-all | 1000 | 6.067 | 4.900 | 9.300 | 20.300 | 0.000 | 64896 |
| optional-half-present | 1000 | 4.870 | 4.900 | 5.300 | 5.500 | 0.000 | 64896 |
| tracked-write | 100000 | 313.700 | 291.600 | 432.100 | 453.100 | 0.000 | 5030592 |
| changed-none | 100000 | 152.539 | 150.000 | 222.600 | 329.800 | 0.000 | 5030592 |
| write-then-changed-all | 100000 | 721.172 | 806.800 | 926.300 | 1225.100 | 0.000 | 5030592 |
| optional-half-present | 100000 | 519.597 | 519.050 | 544.600 | 570.300 | 0.000 | 5030592 |
| world-register-create-dispose | 0 | 3.333 | 3.400 | 3.800 | 4.000 | 5336.000 |  |
| schedule-bind-dispose | 1000 | 4.727 | 4.700 | 5.200 | 6.000 | 4856.000 |  |
| schedule-bind-first-refresh-run-dispose | 1000 | 13.048 | 12.950 | 13.900 | 34.100 | 8376.000 |  |
| first-query-refresh-after-new-archetype | 1000 | 4.139 | 4.600 | 6.000 | 8.100 | 200.000 |  |
| warmed-idle-tag-roundtrip | 1000 | 437.742 | 368.500 | 537.600 | 1738.700 | 96000.000 | 49152 |
| warmed-deferred-tag-roundtrip | 1000 | 496.095 | 534.200 | 631.500 | 871.100 | 96000.000 | 49152 |
| first-command-playback-capacity-0 | 1000 | 498.595 | 538.700 | 574.600 | 601.600 | 259880.000 |  |
| first-command-playback-capacity-65536 | 1000 | 515.270 | 541.650 | 726.100 | 864.900 | 96144.000 |  |
| empty-schedule-concurrency-1 | 0 | 0.230 | 0.200 | 0.400 | 0.800 | 0.000 | 0 |
| two-empty-systems-command-barrier-concurrency-1 | 0 | 0.558 | 0.600 | 0.700 | 0.800 | 0.000 | 0 |
| empty-schedule-concurrency-4 | 0 | 48.316 | 42.000 | 63.400 | 262.100 | 0.000 | 0 |
| two-empty-systems-command-barrier-concurrency-4 | 0 | 83.842 | 79.450 | 103.600 | 286.700 | 0.000 | 0 |

An empty schedule on four-worker execution has tens of microseconds of wake/join cost in this run. The two-empty-system case adds a real command barrier and another batch; it is not an isolated subtraction estimate of barrier cost. Tiny workloads should use serial execution until an adaptive dispatch policy has been measured.

**Code size, build time and generated machine code**

The benchmark assembly emitted 23 generated C# files totaling 34,069 bytes. Release assembly file sizes were 107,520 bytes for `Archevolt.dll`, 108,032 for `Archevolt.Generators.dll`, and 64,000 for `Archevolt.Benchmarks.dll`. These PE file sizes include metadata and headers, not just IL method bodies. The last source-changing benchmark build took 2.325 seconds wall time including `dotnet` startup (MSBuild reported 1.95 seconds); the following no-change build took 1.354 seconds wall time (MSBuild 1.01 seconds). This measures an incremental/no-change command, not per-transform Roslyn generator timing or IDE keystroke latency. Earlier runs and validation attempts remain visible in `durations.csv`.

The verified Windows NativeAOT sample image was 1,806,336 bytes; the packed-consumer image was 1,851,392 bytes. These whole executable sizes include the native runtime and application, and are not generated-kernel sizes. PDBs are excluded. Those AOT images were produced by the separate successful package/sample validation before the benchmark run.

The representative disassembly run explicitly uses `DOTNET_TieredCompilation=0`, `DOTNET_ReadyToRun=0` and `DOTNET_JitDisasm`. It is **FullOpts without PGO**, not the tiered code used in the BenchmarkDotNet table. Selected method machine-code sizes:

| Method | Bytes |
| --- | ---: |
| Handwritten movement | 125 |
| Generated Each body, including boundary guards | 1,123 |
| Generated named-query invocation wrapper | 306 |
| User named-query traversal method | 227 |
| Arch specialized inline-query method | 325 |

In this non-PGO capture the generated `Each` loop calls the user `Each` method once per row. It has a span-length-checked fast loop and a fallback with row bounds checks; boundary validation and cold throw paths account for part of its larger code body. The named-query wrapper makes a direct call to the query method, whose row loop still contains accessor/enumerator calls and bounds-check failure paths. The handwritten, named-query and Arch arithmetic show scalar `vmulss`/`vaddss`, not packed multi-entity SIMD arithmetic. Vector moves in guard/state-copy code are not evidence of vectorized entity updates. Do not extrapolate these non-PGO call shapes to the tiered benchmark or NativeAOT.

The Windows NativeAOT sample's generated `MotionKernel.__Archevolt_IntegrateBody.Run` was also inspected in the original `aot-disassembly.txt`. It starts at object offset `0xFA00`; its fast row loop at `0xFB65` through `0xFBAA` contains `mulps`/`addps` with no call per row. This sample uses `System.Numerics.Vector3`, unlike the three-scalar benchmark structs. Those instructions operate on the three coordinates of one entity, not multiple entities at once. Chunk access and a fallback path retain bounds checks. The captured object came from the verified sample NativeAOT publish.

To reproduce that inspection after publishing the sample, run Visual Studio's `dumpbin.exe /disasm artifacts/obj/Archevolt.Particles/release_win-x64/native/Archevolt.Particles.obj`, saving stdout. The tool used was `C:/Program Files/Microsoft Visual Studio/18/Professional/VC/Tools/MSVC/14.51.36231/bin/Hostx64/x64/dumpbin.exe`. Object offsets can change after rebuilding; find the generated `IntegrateBody__Run` symbol again.

This report does not claim cross-entity AOT vectorization, peak native memory, a tuned chunk-size sweep, statistically stable tail latency, or an agreed regression threshold. The benchmark fixtures and scripts make those follow-up measurements possible.

**Final-source confirmation**

Implementation commit `5237bf2bd21c7a921b547352db40145cf75c273f` contains the final measured code; extra EOF blank lines were removed after measurement. After the optional schedule-timing API and final review fixes landed, all eight Windows verification steps passed, including 127 tests, packed-consumer JIT/NativeAOT execution, sample NativeAOT execution and the serial/four-worker allocation smoke check. The separate Linux run passed 127 tests and the packed-consumer JIT check.

The final source then ran all 42 matrix scenarios successfully with schedule timing disabled. `artifacts/performance/source-hashes-Matrix.csv` identifies that source snapshot, and `artifacts/performance/matrix.csv` contains its full results. Every warmed traversal, filter and dispatch case still measured zero managed bytes on both the owner-thread and process-wide counters. Binding now allocates 5,016 bytes per schedule, and bind/first-refresh/run/dispose allocates 8,536 bytes, each 160 bytes above the earlier table because of the new preallocated timing arrays. Warmed structural churn remains 96,000 bytes per 1,000 tag round trips. The BenchmarkDotNet comparison was not repeated after that API addition; its earlier provenance remains explicit.

Final Release file sizes are 112,640 bytes for the runtime, 108,032 for the generator and 64,000 for the benchmark assembly. Final NativeAOT executable sizes are 1,817,600 bytes for the sample and 1,860,096 bytes for the packed consumer. Refreshed NativeAOT disassembly is at `artifacts/performance/aot-disassembly.txt`: `IntegrateBody__Run` now starts at object offset `0x10AA0`; the fast loop still contains `mulps` at `0x10C30` and `addps` at `0x10C33`, with the same within-entity Vector3 SIMD and chunk-boundary calls described above.
