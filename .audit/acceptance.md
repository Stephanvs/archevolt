# Acceptance evidence

Implementation commit: `5237bf2bd21c7a921b547352db40145cf75c273f`. Final checks ran on this implementation before committing; the only subsequent source-file edits removed extra blank lines at EOF. PLAN's status line was also updated. The commit is local on `main`, which was the active checkout at commit time. Nothing was pushed.

The runtime, generator, and consumers now execute the ECS described in PLAN.md, including the original-namespace requirement. Validation limits are listed below rather than treating a passing build as proof of every failure mode.

| Gate | Observable result | Current evidence |
| --- | --- | --- |
| Scaffold | Release build, tests, one dependency-free package, compiler loading, JIT and AOT consumer execution | Final Windows scripts/verify.ps1 passed all eight gates with 127 tests. Release build had zero warnings/errors. Logs in artifacts/logs. |
| Storage | Stable world-scoped entity identities, swap removal, migration, generations, reservations, disposal, more than 64 components | StorageTests and CommandTests passed, including the final world/schedule borrow and duplicate-module regressions. |
| Reference model | Fixed-seed randomized structural traces agree with a dictionary world | StorageTests dictionary trace passed. |
| Generated queries | Typed rows, readonly enforcement, optional reads, membership changes, private invocation, scoped lifetimes | Generator compilation/execution and QueryRuntimeTests passed, including C# init-only, tuple, and scoped-escape rejection. |
| Namespace placement | Global and named declarations compile with their generated types in the same namespace | Actual generator compilation/execution tests passed for original global, block, file-scoped, escaped, and nested declarations with duplicate simple names. |
| Vertical workload | 100,000 entities move through generated Each, then tag changes alter membership | Generator tests passed the movement/tag vertical. |
| Tracking | Added and changed intervals, equal-value writes, preserved ticks on migration, skipped systems | QueryRuntimeTests and ScheduleExecutionTests passed for component intervals, skipped bodies, independent condition intervals, equal writes, and preserved migration ticks. |
| System state | Independent schedule locals, resources, ownership cleanup, message cursors and retention | StorageTests, MessageTests, and ScheduleExecutionTests passed, including partial message enumeration, missed counts, and failed owned-resource cleanup. |
| Commands | Reservations, ordered and transitive visibility, partial failure and reservation cleanup | CommandTests passed in serial and parallel, including transitive visibility through a skipped condition, partial failure, and concurrent reservation page growth. |
| Planner | Explicit ordering precedes conflict orientation, disjoint query proofs, global resource conflicts, cycles, barriers | 27 planner tests passed. Barrier validation negative control failed as expected. |
| Generator diagnostics | AV001 through AV010 contracts, generated compilation, incremental caching | 47 generator cases passed after review fixes and compiler-safety coverage. Incremental edit test verifies unchanged generated query outputs remain cached. |
| Cross-assembly | Compiled module manifests register shared types and callable systems in a packed consumer | Separate PackageLibrary and PackageConsumer build from the nupkg. Imported private system invocation and generated consumer schedule passed JIT and NativeAOT. |
| Parallel execution | Bounded owned workers, joins on faults and cancellation, affinity, partition safety, reuse stress | 80 runtime cases passed. Tracked WorkerPoolStressTests execute 3,600 varied public schedule runs across budgets 1/2/4. Earlier isolated pool probe passed 3,603 batches and 761,767 callbacks. Explicit Each partitioning forces four-way overlap and checks each row once. |
| Inspection | Plan edges, batches, barrier reasons, optional system timing | Plan inspection is public. Timing tests cover disabled/default behavior, per-partition sums, condition skips, joined failures, and owner/idle restrictions. |
| Package execution | Serial and parallel generated programs run from the packed package under JIT, trimming, and NativeAOT | Final Windows consumer JIT, consumer NativeAOT, and sample NativeAOT passed with no trimming/AOT warnings. Ubuntu 24.04 clean build, all 127 tests, pack, and clean consumer JIT also passed. Consumers execute both concurrency 1 and 4. |
| Performance | Warm allocation count, handwritten comparison, throughput and configuration recorded | Final smoke and all 42 matrix scenarios passed. Warm traversal/filter/dispatch allocated zero managed bytes on owner and all-thread counters. performance-final.md records Arch comparison, distributions, native snapshots, code sizes, build times, and JIT/AOT inspection, with separate pre-timing and final-source provenance. |
| Documentation | README and API examples describe verified behavior and disclose remaining limitations | README tutorial, docs/api.md reference, and performance report are complete. Full particle and cross-assembly examples run in package validation. |

## Review corrections

The independent read-only code review used `gpt-5.6-sol`. It found implicit partitioning despite the default false attribute, world disposal with live schedule borrows, missing compile-time validation of contradictory inferred Each filters, and duplicate module registration. All four corrections passed their public-behavior regressions. Partition opt-in cases failed before the fix. A separate Linux copy with the other three repairs removed failed exactly four cases for their intended missing exceptions/diagnostics; the primary workspace retained the fixes.

The review inspected native storage, entity lifetime, capability access, query-cache ownership, resource/message conflicts, command reservations/barriers, worker publication/join, and planner ordering. It reported no remaining critical/high defect in that scope. It was not an independent test run and preceded the optional timing addition, which the root reviewed and the final tests exercised. The Comment Sicko pass found no required comment deletions. No non-GPT reviewer was available.

## Reproduction and limits

- Windows: `./scripts/verify.ps1`. Logs are `artifacts/logs/*.txt`; all eight steps passed.
- Linux: Ubuntu 24.04 WSL, isolated temporary source copy, SDK 10.0.402 installed with Microsoft's official dotnet-install script, then `dotnet build Archevolt.slnx -c Release -warnaserror`, `dotnet test --solution Archevolt.slnx -c Release`, `dotnet pack src/Archevolt/Archevolt.csproj -c Release -o artifacts/packages`, and `dotnet run --project tests/PackageConsumer -c Release`. Final log is `artifacts/logs/linux-final-and-negative-controls.txt`; it contains the successful 127-test pass before the deliberately failing negative controls.
- Performance: `./scripts/measure.ps1 -Mode All`. The original controlled benchmark snapshot and the later final-source matrix are distinguished in performance-final.md. Raw artifacts stay ignored; measured tables and interpretation are tracked.
- Forced worker `Thread.Start` failure is not tested. Partial-start cleanup was inspected, but its presence is not runtime proof of that failure path.
- Linux NativeAOT was not run. NativeAOT execution evidence here is Windows x64.
- Structural churn allocates. Parallel dispatch has material overhead on tiny workloads. Benchmark baselines are exploratory and do not establish numeric release regression limits or cross-platform performance guarantees.
