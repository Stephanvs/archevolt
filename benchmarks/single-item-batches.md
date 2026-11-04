# Single-item batch measurement

The singleton fast path is already on main through commit `8353c3d`. The current change adds measurement tooling, regression coverage, and this historical report. The numbers below compare the older `8e2b787` implementation with the original singleton candidate, not current main with this branch.

The singleton dispatch change measured in this historical experiment reduced warmed `BoundSchedule.Run` cost when a four-worker world had one worker item. The no-op improved by 137.8x and the existing 1,000-entity GeneratedQuery workload improved by 15.95x in this run. Their slowest paired improvements were still 124.4x and 12.11x, respectively.

One-worker no-op and action cases improved by 8.10% and 18.22% at their medians. Every adjacent pair favored the candidate. The useful one-worker query cases are inconclusive under the observed drift. The four-worker, 100,000-entity query favored the candidate in all six adjacent pairs, but its 28.10% median reduction is a noisy estimate. No multi-item improvement is established.

Measured on 2026-11-04 from 09:12:47 to 09:19:15 UTC. All evidence is under `artifacts/single-item-batches/run-20261104-02/` in this checkout.

Times below are microseconds per public `BoundSchedule.Run`, including the final command barrier. Each cell is the median of six independent process means, followed by their minimum and maximum. The speedup divides those medians. The paired range uses the six adjacent baseline/candidate pairs in the ABBA blocks. Every timed sample is retained.

| Workload | Concurrency | Baseline us, range | Candidate us, range | Speedup | Time reduction | Paired speedup range |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| No-op singleton | 1 | 0.1193 [0.1139, 0.1332] | 0.1097 [0.1063, 0.1153] | 1.088x | 8.10% | 1.049x to 1.155x |
| No-op singleton | 4 | 14.7370 [13.1702, 23.8544] | 0.1069 [0.1040, 0.1235] | 137.827x | 99.27% | 124.364x to 214.064x |
| Action singleton | 1 | 0.1196 [0.1152, 0.1457] | 0.0978 [0.0950, 0.1112] | 1.223x | 18.22% | 1.100x to 1.310x |
| Action singleton | 4 | 23.9537 [16.4976, 31.6028] | 0.1416 [0.1102, 0.2257] | 169.164x | 99.41% | 133.655x to 187.149x |
| GeneratedQuery, 1,000 entities | 1 | 1.3372 [1.0855, 1.5769] | 1.2692 [1.0943, 1.3902] | 1.054x | 5.08% | 0.972x to 1.134x |
| GeneratedQuery, 1,000 entities | 4 | 21.8271 [18.1861, 32.1632] | 1.3687 [1.2179, 1.5017] | 15.947x | 93.73% | 12.110x to 21.657x |
| GeneratedQuery, 100,000 entities | 1 | 91.6787 [86.3221, 253.4474] | 87.0897 [85.1286, 314.7708] | 1.053x | 5.01% | 0.519x to 1.106x |
| GeneratedQuery, 100,000 entities | 4 | 139.0680 [105.0585, 201.5999] | 99.9839 [91.5847, 120.1323] | 1.391x | 28.10% | 1.032x to 1.980x |
| Partitioned GeneratedEach, 1,000 entities | 4 | 20.7399 [16.5975, 46.1141] | 18.0618 [16.6958, 35.4825] | 1.148x | 12.91% | 0.967x to 1.300x |
| Partitioned GeneratedEach, 100,000 entities | 4 | 94.6475 [85.0911, 328.8821] | 94.9622 [81.4999, 110.2454] | 0.997x | -0.33% | 0.796x to 3.918x |

The larger partitioned control has effectively equal medians. The smaller control's apparent 12.91% improvement does not establish a benefit. Both controls still dispatch four worker items, and the small control's adjacent pairs cross 1.0x. The large control includes a 328.882 us baseline launch, retained in the raw data. Treat these controls as evidence of noise, not as additional optimization wins.

The one-worker 100,000-entity query illustrates the limit of a single summary. Its median ratio is 1.053x, but its paired geometric mean is 0.883x. The last block slowed both variants substantially. Neither a speedup nor a regression is established there. Process standard deviations, individual sample dispersion, paired ratios, and block ratios are available in `summary.json` and `launch-summary.csv`.

## Measurement method

This experiment used the `--single-item` runner in the existing benchmark executable. The existing `--quick` and `--matrix` runners time individual operations and cannot resolve the smallest singleton cost as well as batched timing. A direct runner also executes the two frozen binaries without BenchmarkDotNet regenerating projects against the current source. The [earlier partition report](step3-partition-granularity.md) recorded duplicate-project discovery problems with repository copies. This run created no repository copies, project files, or worktrees under artifacts.

The method was fixed before the successful measurement run.

1. Build Release baseline and candidate into distinct `ArtifactsPath` directories, including separate intermediate output. Only the WorkerPool source differs between builds. Both builds use the same benchmark code.
2. For each case, launch baseline, candidate, candidate, baseline in four separate processes. Repeat three blocks. Reverse the case order in the middle block. Wait for each process to exit before starting the next. There are 120 launches, six per variant per case.
3. Validate the workload before timing. Pilot with 16 operations, doubling until a batch takes at least 50 ms. Retain that process's operation count for warmup and measurement. Counts can differ between variants.
4. Warm up for at least eight batches and at least 1,000 ms of measured workload time. Actual warmup counts were 8 to 23, lasting 1,000.327 to 1,123.095 ms. Perform a full collection outside timing.
5. Record 15 batches per process, giving 1,800 measured batches. Each timed loop calls `BoundSchedule.Run` directly. Divide elapsed stopwatch ticks by the operation count. There is no empty-loop subtraction, per-operation stopwatch call, setup, validation, reflection, or serialization inside the timed interval.
6. Check the action's exact invocation total and validate the workload again. Record the runtime, binary hashes, warmup, pilot, every sample, and before/after validation in that process's JSON file.

These are throughput measurements of repeated, warmed schedule calls. The sample ranges describe batch averages, not individual-call tail latency. There is no claimed confidence interval or statistical significance test. The script retains all observations rather than deleting slow launches.

The no-op is a `SystemBody` with an empty body. The action is a `SystemBody` invoking a delegate that increments a counter. Both use public `BoundSchedule.Bind`. Movement cases reuse `MovementModule`, `QueryMovement`, and `EachMovement` from `MovementBenchmarks.cs`, with the same Position, Velocity, FrameTime, and single-archetype population. The nonpartitioned query is the same generated workload used by `MovementBenchmarks.GeneratedQuery`.

Each process verifies one system batch followed by its normal final command barrier. Outside timing, public schedule timing counters and private field inspection verify exactly one worker item for singleton cases, four worker items for controls, and zero owner items. Every movement row is reset, run, and compared with its expected Position and unchanged Velocity before and after measurement. Schedule timing is disabled during measurement. The action counter is also checked against all pilot, warmup, and measured invocations. All 120 processes passed.

## Allocations and environment

Every timed owner-thread allocation counter was zero. Every concurrency-1 process also recorded zero process-wide allocated bytes. Every concurrency-4 process recorded 336 process-wide managed bytes across its entire 15-sample measurement window, in both variants. The source of that fixed delta was not traced. There were no timed GC collections. No allocation improvement is established, and native allocation was not measured.

Per-run process-wide allocation values depend on the independently calibrated operation counts. The maximum per-process values are below. Raw integer byte deltas and operation counts remain in `samples.csv`.

| Concurrency-4 case | Baseline maximum B/Run | Candidate maximum B/Run |
| --- | ---: | ---: |
| No-op | 0.005469 | 0.000043 |
| Action | 0.010938 | 0.000085 |
| Query, 1,000 | 0.010938 | 0.000684 |
| Query, 100,000 | 0.043750 | 0.043750 |
| Partitioned, 1,000 | 0.010938 | 0.010938 |
| Partitioned, 100,000 | 0.087500 | 0.043750 |

| Setting | Recorded value |
| --- | --- |
| CPU | AMD Ryzen 9 5950X, 16 cores, 32 logical processors |
| OS | Windows, build 10.0.26300, x64 |
| SDK | 11.0.100-rc.3.26522.148 |
| Runtime | .NET 11.0.0-rc.3.26522.148, x64 |
| GC | Workstation |
| Power plan | Existing High performance plan, unchanged |
| Timer | Stopwatch, 10,000,000 ticks per second |
| JIT settings | `DOTNET_TieredCompilation=0`, `DOTNET_ReadyToRun=0` for every measured child |
| Scheduling | Normal inherited priority and affinity, no CPU isolation |

Disabling tiered compilation avoids tier transitions during a short measurement, but these results do not establish the exact magnitude under default dynamic PGO, ReadyToRun, NativeAOT, other hardware, or other runtimes. The machine was not reserved or background-load controlled. ABBA ordering reduces time-order bias but does not remove interference. Construction, cold start, mixed singleton/multi-item transitions, cancellation, and failure paths are outside this benchmark.

## Source identity and preservation

Baseline WorkerPool is the exact Git blob from HEAD `8e2b78790ad914043d1aff2f71fb324a43aa3c1f`. The candidate is the uncommitted file captured before the build. HEAD is not the candidate. The candidate suppresses dispatch only when `count == 1 && ownerWork is null`, preserves the epoch in that case, sets pending workers to zero, and skips `Monitor.PulseAll`.

| Identity | SHA-256 |
| --- | --- |
| HEAD WorkerPool bytes | `217AAA432102533BB6F70DE19EDAA09237030B43A1D7DF6777FE5A27B5E129E3` |
| Candidate WorkerPool bytes, before and after | `2DB5A844FEC3C9170FFCAB4CCD9D383476BF6994A74605371703303C3F69E75D` |
| Baseline loaded Archevolt.dll | `6AF90280FD388A7BB38FA49C62A0CC39A3F3933EDBA695A9A02D2F92387FD80E` |
| Candidate loaded Archevolt.dll | `B8A5DA9D982D53579C3C447F99FEAD58DE6043FC8D542D5B1806582D74FC1655` |

The build script captures candidate bytes in memory and on disk. It writes the baseline only for its build, restores the exact candidate bytes in `finally`, and compares SHA-256 hashes. It then builds the candidate separately. No tests ran while baseline source was present. Every measured process reports the loaded library and benchmark assembly hashes, and the analysis checks them against the manifest. All captured source hashes remained unchanged across the builds and measurements.

`preexisting-changes-check.json` verifies that WorkerPool, execution documentation, and all three preexisting modified test files remain byte-identical to the task's initial snapshot. `git diff --check` passed. Both benchmark builds passed with zero warnings and errors. No library implementation or test edits were made for this measurement. No unit-test suite was rerun. The executed benchmark validations check the new runner against the real scheduler.

The earlier `run-20261104-01` aborted before collecting samples because the runner initially asserted a plan without its final command barrier. That assertion was corrected, both variants were rebuilt, and only the complete `run-20261104-02` contributes results.

## Reproduction and artifacts

To measure the current build from the repository root, run:

```powershell
./scripts/measure.ps1 -Mode SingleItem
```

`-Mode All` includes SingleItem alongside Matrix, Partition, Bdn, and Disassembly. Add `-NoBuild` to reuse the existing Release build. SingleItem runs the same ten cases in three sequential processes per case with tiered compilation and ReadyToRun disabled. Each invocation creates a fresh `artifacts/performance/single-item/<UTC timestamp>/` directory with raw JSON, logs, source hashes, Git revision, and environment metadata.

`summary.csv` uses the columns `Harness,Case,Runs,MedianUs,MinUs,MaxUs`. Each timing is the median, minimum, or maximum of the three process means in microseconds. Compare two saved runs with `./scripts/compare-baselines.ps1 -Before <before-directory> -After <after-directory>`.

The current mode measures one build. It does not swap WorkerPool sources, build a HEAD comparison, or reproduce the six-launch-per-variant ABBA experiment below.

### Historical ABBA experiment

These were the exact top-level commands used for the successful historical run. The two standalone scripts have since been removed. Byte-identical snapshots remain at `artifacts/single-item-batches/run-20261104-02/sources/measure-single-item.ps1.txt` and `artifacts/single-item-batches/run-20261104-02/sources/analyze-single-item.py.txt`.

```powershell
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath 'C:\Users\stephanvs\.t3\worktrees\archevolt\t3-7dc38ec4'
./scripts/measure-single-item.ps1 -Phase Build -OutputDirectory 'artifacts/single-item-batches/run-20261104-02'
./scripts/measure-single-item.ps1 -Phase Measure -Blocks 3 -OutputDirectory 'artifacts/single-item-batches/run-20261104-02'
python ./scripts/analyze-single-item.py 'artifacts/single-item-batches/run-20261104-02'
```

The archived measurement script built HEAD and the then-uncommitted WorkerPool into separate output directories before running ABBA blocks. The archived analysis script calculated the six-process and paired summaries. Repeating that historical comparison requires those scripts and the preserved source variants, plus an unused output directory. The current `measure.ps1 -Mode SingleItem` does not perform that comparison.

The artifact root is `C:\Users\stephanvs\.t3\worktrees\archevolt\t3-7dc38ec4\artifacts\single-item-batches\run-20261104-02`.

- `raw/*-baseline.json` and `raw/*-candidate.json` contain the 120 independent process results, with stdout and stderr alongside them.
- `samples.csv` contains all 1,800 timed observations in nanoseconds per Run, plus ticks, operation counts, allocation deltas, and GC counters.
- `launch-summary.csv` contains process means, medians, standard deviations, ranges, allocations, and launch timestamps.
- `summary.json` contains the comparison, process-level standard deviations, six paired ratios, and three block ratios per case.
- `results.md` is the generated numeric table. `analysis-validation.json` records successful source-identity, ordering, warmup, and workload checks.
- `manifest.json`, `sources-before.json`, `source-restoration.json`, and `preexisting-changes-check.json` preserve source, binary, and environment evidence.
- `sources/` contains byte-preserved WorkerPool variants and the runner/script snapshots, with `.txt` suffixes to avoid project discovery.
- `builds/baseline/` and `builds/candidate/` retain separate binaries and intermediates. `build-baseline.log` and `build-candidate.log` retain compiler output. `commands.txt` records build arguments and each process's arguments and environment.

The current entry point is `scripts/measure.ps1 -Mode SingleItem`, backed by `SingleItemMeasurement.cs` and the Program dispatch branch. The historical scripts remain archived with the original evidence.
