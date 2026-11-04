# Direct dispatch and query partitioning

Single-work-item batches now execute on the owner without waking the worker threads. The change stays in `WorkerPool.Run`. Cancellation, exception aggregation, and cleanup still use the same code as parallel execution. A batch with both owner work and one ordinary work item still dispatches workers so those bodies can overlap.

No small-query threshold is added. A row count does not describe the cost of an arbitrary user system. These measurements compare scalar entity callbacks, indexed blocks, and vectorized blocks before choosing any policy.

## Method

The measurements ran on 2026-11-04 on an AMD Ryzen 9 5950X, Windows build 26300, x64 .NET 11.0.0-rc.3.26522.148. The baseline library is commit `8e2b78790ad914043d1aff2f71fb324a43aa3c1f`. Baseline and candidate use identical benchmark assemblies within each comparison. The candidate library changes only the worker-dispatch predicate.

`QueryPartitionBenchmarks` compares six generated schedules. Direct and partitioned schedules call the same movement kernels in the same world. Each world has one table, 12-byte positions, 12-byte velocities, and a readonly frame-time resource. Concurrency is 1 or 4. Row counts are 0, 1, 100, 1,000, 4,096, 16,384, 32,768, 65,536, 100,000, and 1,000,000.

Each process takes 32 samples of 16 operations per case and rotates case order. Medians below are medians of three process medians, in microseconds per schedule run. Bracketed ranges are the minimum and maximum process medians. CSV p95 and maximum values describe batch averages, not individual-run tail latency. These are local desktop measurements, not a controlled laboratory or a hardware-independent policy.

Two configurations are retained in [results/query-partition](results/query-partition):

- `controlled` disables tiered compilation and ReadyToRun and warms each case for 128 calls.
- `default` uses default runtime compilation settings and at least one second plus 128 cycles of warmup per world. Positions reset after warmup. This prevents warmup accumulation from exceeding exact integer representation in the floats.

Allocation checks use separate untimed batches of 128 operations, counting owner-thread and process-wide managed bytes. Setup, world creation, query refresh, and worker creation occur before these checks. Native allocator activity is not measured. The runner validates every entity after timed and allocation batches against independent expected positions. It also resets and checks every schedule separately during setup and cleanup. Empty worlds measure scheduler overhead without row work.

A preliminary default-runtime run with only 128 warmups recorded 24 process-wide bytes in the unchanged baseline, at 16,384 partitioned rows. That run failed the allocation gate. Its [raw output](results/query-partition/preliminary/short-warmup-baseline-allocation.csv) is retained. Small-case timings also changed as runtime compilation progressed, so the final default-runtime run uses the longer warmup. The allocation's source was not traced.

## Measured direct-path gain

At 1,000 rows and concurrency 4, the final default-runtime runs measured:

| Kernel | Baseline, Âµs | Direct dispatch, Âµs | Median reduction |
| --- | ---: | ---: | ---: |
| each | 17.29 [15.53, 20.34] | 3.55 [3.43, 4.11] | 79.5% |
| block | 22.35 [19.25, 24.13] | 1.57 [1.54, 1.88] | 93.0% |
| vector | 21.28 [17.73, 24.07] | 0.66 [0.56, 0.82] | 96.9% |

All 720 cells in the final default-runtime comparison reported zero owner-thread and process-wide managed allocation. All 720 cells in the controlled comparison also reported zero. These totals include baseline and candidate across three launches each.

## Partition crossover

Candidate timings with default runtime compilation are shown below. Each cell contains direct / partitioned microseconds. These are sampled crossover intervals, not proposed threshold constants.

| Rows | Each | Indexed block | Vector block |
| ---: | ---: | ---: | ---: |
| 1,000 | 3.55 / 27.25 | 1.57 / 24.08 | 0.66 / 22.21 |
| 4,096 | 12.09 / 20.74 | 5.56 / 20.82 | 1.58 / 19.46 |
| 16,384 | 51.42 / 41.26 | 20.95 / 27.95 | 5.00 / 21.15 |
| 32,768 | 90.28 / 50.49 | 32.49 / 32.86 | 10.24 / 23.96 |
| 65,536 | 184.63 / 74.22 | 61.65 / 38.76 | 18.64 / 28.69 |
| 100,000 | 312.18 / 166.88 | 121.47 / 83.83 | 38.12 / 47.67 |
| 1,000,000 | 3162.11 / 1221.13 | 1564.51 / 661.98 | 914.36 / 538.80 |

Each favors direct execution at 4,096 rows and partitioning at 16,384. Indexed blocks remain near the crossover at 32,768. Vector blocks favor direct execution through 100,000 and partitioning at 1,000,000. A more expensive user callback can cross earlier. Different CPUs and concurrency budgets can move these intervals. The measurements do not justify one library-wide small-query threshold.

## Large-workload regression check

Partitioned workloads retain their existing dispatch path. The additional predicate is the only production change, but that does not establish performance equivalence. The observed before/after results are:

| Runtime configuration | Rows | Kernel | Baseline, Âµs | Candidate, Âµs | Median change |
| --- | ---: | --- | ---: | ---: | ---: |
| controlled | 100,000 | each | 105.78 [93.83, 134.22] | 97.73 [96.13, 153.99] | -7.6% |
| controlled | 100,000 | block | 64.93 [61.35, 79.23] | 65.65 [57.01, 103.87] | +1.1% |
| controlled | 100,000 | vector | 39.22 [35.77, 43.59] | 41.91 [38.52, 65.81] | +6.9% |
| controlled | 1,000,000 | each | 976.47 [870.90, 1201.69] | 977.85 [894.80, 1275.12] | +0.1% |
| controlled | 1,000,000 | block | 720.67 [655.24, 739.83] | 763.67 [751.09, 779.51] | +6.0% |
| controlled | 1,000,000 | vector | 435.49 [354.91, 532.45] | 542.08 [414.40, 561.90] | +24.5% |
| default | 100,000 | each | 186.20 [117.12, 192.27] | 166.88 [148.14, 172.34] | -10.4% |
| default | 100,000 | block | 97.03 [66.22, 100.18] | 83.83 [78.28, 87.67] | -13.6% |
| default | 100,000 | vector | 49.65 [42.48, 52.58] | 47.67 [43.11, 56.18] | -4.0% |
| default | 1,000,000 | each | 1458.35 [916.20, 1575.00] | 1221.13 [1020.29, 1413.22] | -16.3% |
| default | 1,000,000 | block | 745.03 [483.71, 917.50] | 661.98 [605.69, 805.27] | -11.1% |
| default | 1,000,000 | vector | 405.44 [298.65, 551.85] | 538.80 [334.15, 601.49] | +32.9% |

**The large-workload performance gate is inconclusive.** The million-row vector median is slower in both configurations, by 24.5% with tiering disabled and 32.9% with default compilation. Its process ranges overlap substantially. Other large cases change direction across configurations. The data cannot distinguish a regression from machine or scheduling variation, and this report does not claim a regression-free result. The isolated follow-up below also failed to establish stable timings. Performance equivalence for large workloads remains unproven under the current host conditions.

## Isolated regression follow-up

`LargePartitionRegressionBenchmarks` isolates the million-row vector kernel at concurrency four. Four fresh processes ran in baseline, candidate, candidate, baseline order. BenchmarkDotNet used five warmup iterations and fifteen measurement iterations of approximately 500 ms each. The in-process toolchain executes the preserved library binary instead of rebuilding the current checkout for the baseline. Library SHA-256 hashes matched those from the matrix runs.

| Process | Variant | Mean, µs | Standard deviation, µs |
| ---: | --- | ---: | ---: |
| 1 | Baseline | 472.5 | 171.6 |
| 2 | Candidate | 480.5 | 127.9 |
| 3 | Candidate | 1,202 | 378.0 |
| 4 | Baseline | 727.7 | 309.2 |

The longer runs have large within-process variation as well as between-process drift. This follow-up cannot confirm or rule out the regression suggested by the matrix. All four BenchmarkDotNet runs report zero managed allocation. [Raw BenchmarkDotNet exports](results/query-partition/isolated) retain the individual measurements.

Run the focused check against each preserved benchmark assembly with:

```powershell
dotnet <assembly> --filter '*LargePartitionRegressionBenchmarks*' --inProcess --warmupCount 5 --iterationCount 15 --iterationTime 500 --exporters json
```

## Design choice

Parallel design exploration compared a `BoundSchedule` bypass with a `WorkerPool` dispatch gate. An independent review selected the pool gate. It avoids duplicating cancellation and exception behavior and retains owner/worker overlap. The schedule bypass could also avoid initial pool creation, but this investigation measures warmed execution. Existing locks, atomics, and lazy pool construction remain.

## Reproduce

```powershell
dotnet build benchmarks/Archevolt.Benchmarks -c Release
./scripts/measure.ps1 -Mode Partition -Runs 3
./scripts/measure.ps1 -Mode Partition -Runs 3 -RuntimeDefaults -Output artifacts/partition-study/default
```

To compare a change, preserve a complete benchmark output directory built against the baseline library. Pass its `Archevolt.Benchmarks.dll` with `-BaselineAssembly`. Use identical benchmark code in both output directories. The script alternates process order, records binary and source hashes, saves raw CSV files, and produces `samples.csv` and `summary.csv`. It fails on incorrect results, missing cases, or any observed managed allocation. `-NoBuild` reuses the current candidate binary.

## Verification

`scripts/verify.ps1` passed all nine stages, including 172 tests, headless Archeoids, package checks, JIT and NativeAOT consumers, the NativeAOT particle sample, and allocation smoke checks. The later benchmark-only warmup change rebuilt without warnings and was exercised by the repeated comparison. After script consolidation, `measure.ps1 -Mode Partition -Runs 1` completed the preserved-baseline comparison with 240 allocation-free cases and the same summary schema. The existing `-Mode Matrix` also completed successfully.

The added behavioral cases protect cancellation during ordinary and owner-thread bodies, failure precedence, exception metadata, fault/reset, and reuse. Existing tests still verify mixed owner/worker overlap and thousands of alternating batch sizes. No partition-count expectations or consumer contracts were changed.
