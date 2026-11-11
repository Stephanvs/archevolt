# The cost of running a system, 11 November 2026

The first three rounds made rows fast. Since [the third round](each-kernels.md), a dense `[Each]` update costs about 0.11 ns per entity. None of them measured what a schedule pays to run a system before the system visits a row. A schedule of many small systems pays that cost many times in every run, and it paid more per system the larger the schedule grew.

This round measures that fixed cost and removes most of it. No public API changed, and the generator did not change. The work is in the executor and in the query types that generated systems call.

Twelve commits carry the work.

| Commit | Change | On `mini`, ns per system per run |
| --- | --- | --- |
| `2774683` | Measurement harness `benchmarks/Archevolt.SystemOverhead`. `scripts/each-shapes.ps1` runs either harness | none |
| `b961166` | 1. Prepare systems only when the world has changed | `commands100-chain/1024`: 7,071 to 2,156 |
| `1f91a60` | 2. Flush only the systems that ran since the last flush | `commands100-chain/1024`: 2,154 to 74.8 |
| `0ef94f1` | 3. Read the plan from arrays | `each-chain/256`: 48.2 to 43.3 |
| `c7cce8c` | 4. Run an owner batch without the worker pool | `empty-chain/256`: 30.2 to 18.2 |
| `ebab5bf` | 5. Finish a work item without an interlocked decrement | `empty-batch/256`: 15.7 to 12.4 |
| `8dbb6a1` | 6. One counter says whether a system finished | `empty-chain/256`: 16.9 to 16.3 |
| `2998842` | 7. Run a batch of one system without work lists | `each-chain/256`: 33.0 to 25.7 |
| `668e401` | 8. Cursors and blocks keep the tick window, not the run | `query-fragmented/16`: 651 to 445 |
| `84f06ca` | 9. Review edits. The worker pool loses its serial path | every case overlaps |
| `98de1e1` | 10. Inline the column check of a block | without a profile, `each-fragmented/16`: 796 to 624 |
| `e93c062` | 11. Check for cancellation after the worker pool exists | every case overlaps |

[results/system-overhead](results/system-overhead) holds the summaries, per-process samples, and [the ledger](results/system-overhead/ledger.tsv). The ledger names each commit by the hash it had when it was measured. [commits.tsv](results/system-overhead/commits.tsv) pairs those hashes with the commits above. Within each pair, `src` and `tests` are identical.

## Why this area

A profile of the Archeoids late-sector workload came first. One simulation tick took 47.5 µs for 13 systems on the workstation. About 4 µs of that was the schedule itself, the time of `Run` minus the time inside the system bodies. That is about 300 ns per system before any system does work. Archeoids is a small demo, so its frame is not the argument. The cost per system is, because a schedule pays it once per system per run whatever the system does.

A probe then bound the same small systems into schedules of 16 to 1,024 systems. A chain without commands cost about 137 ns per system at every size. With a quarter of the systems declaring `Commands`, the cost per system rose from 181 ns at 16 systems to 6,032 ns at 1,024, and no command was recorded in any run. A run of 1,024 such systems took 6.2 ms for work that takes 0.14 ms.

Both were scratch programs on the workstation, and their output was not kept. They chose the area. One item under [What remains](#what-remains) cites a measurement that was not kept either, and says so. Every other measurement in this report has its files under [results/system-overhead](results/system-overhead).

## Result

Baseline `36dd22e` against `e93c062` on `mini`, five processes per side, default runtime settings. Each cell is the median ns per system per run before and after ([final-mini](results/system-overhead/final-mini/summary.md)). The ranges separate in all 26 cases.

| Case | 16 systems | 64 | 256 | 1,024 |
| --- | --- | --- | --- | --- |
| `empty-batch` | 27.4 to 12.9 | 26.7 to 12.6 | 26.5 to 12.6 | 26.4 to 12.6 |
| `empty-chain` | 37.2 to 11.2 | 36.5 to 10.8 | 37.0 to 10.8 | 36.5 to 10.7 |
| `each-chain` | 59.2 to 24.1 | 58.8 to 24.3 | 59.2 to 24.3 | 60.8 to 24.3 |
| `query-chain` | 66.2 to 24.9 | 66.7 to 26.4 | 67.1 to 26.7 | 68.7 to 27.2 |
| `commands25-chain` | 84.0 to 29.4 | 159 to 30.4 | 474 to 30.1 | 1,801 to 33.3 |
| `commands100-chain` | 188 to 46.0 | 508 to 46.6 | 1,833 to 47.1 | 7,074 to 50.5 |
| `each-fragmented` | 558 to 479 | | | |
| `query-fragmented` | 708 to 450 | | | |

- **Schedule size.** The cost per system no longer depends on how many systems the schedule holds. A chain whose systems all declare `Commands` costs 46.0 ns per system at 16 systems and 50.5 at 1,024, where it cost 188 and 7,074. One run of that 1,024-system schedule took 7.2 ms and now takes 0.052 ms.
- **Dispatch.** An empty system in a batch of its own costs 10.8 ns, down from 37.0. In a batch it shares with others it costs 12.6 ns, down from 26.5.
- **Small systems.** An `[Each]` system over 16 entities costs 24.3 ns, down from 59.2, and a named-query system 26.7 ns, down from 67.1.
- **Many archetypes.** A named-query system over 64 archetypes costs 450 ns, down from 708. The `[Each]` form costs 479 ns, down from 558.

The same comparison in three other configurations, median ns per system before and after ([final-mini-nopgo](results/system-overhead/final-mini-nopgo/summary.md), [final-fiji](results/system-overhead/final-fiji/summary.md), [final-win-aot](results/system-overhead/final-win-aot/summary.md)). The first column sets `DOTNET_TieredPGO=0` and ran three processes per side. `fiji` ran five and the NativeAOT comparison nine.

| Case | `mini`, no profile | `fiji` | NativeAOT, workstation |
| --- | --- | --- | --- |
| `empty-batch/256` | 30.6 to 12.5 | 60.5 to 31.2 | 43.0 to 17.8 |
| `empty-chain/256` | 46.1 to 11.6 | 101 to 25.8 | 80.1 to 17.2 |
| `each-chain/256` | 82.6 to 30.4 | 157 to 61.5 | 165 to 58.9 |
| `query-chain/256` | 127 to 68.0 | 169 to 65.8 | 200 to 86.6 |
| `commands25-chain/256` | 834 to 35.8 | 1,299 to 76.7 | 1,451 to 63.9 |
| `commands100-chain/256` | 2,611 to 53.4 | 5,266 to 118 | 5,612 to 93.8 |
| `commands100-chain/1024` | 9,941 to 55.7 | 24,821 to 122 | 25,212 to 102 |
| `each-fragmented/16` | 1,549 to 630 | 1,362 to 1,176, overlap | 2,609 to 1,111 |
| `query-fragmented/16` | 1,806 to 1,103 | 1,781 to 1,268 | 2,825 to 1,410 |

A build without a profile gains the most. As a NativeAOT image, an `[Each]` system costs 58.9 ns where it cost 165, and an empty system in a batch of its own 17.2 ns where it cost 80.1.

## Conditions and method

| Host | CPU | System | How each process ran |
| --- | --- | --- | --- |
| `mini` | AMD Ryzen AI 9 HX 370 | Debian 13, Linux 7.0.14, `performance` governor | `taskset -c 2,3`, two of the four fast cores. `taskset -c 0-3` for four workers |
| `fiji` | Intel Core i7-8559U | NixOS, Linux 7.3.1, `powersave` governor | `docker run --cpuset-cpus=2,3 debian:trixie-slim` |
| Workstation | AMD Ryzen 9 5950X | Windows 11 Pro 10.0.26300, in use | NativeAOT images only, not pinned |

.NET SDK 11.0.100-rc.3.26522.148 built every binary on the workstation. The Linux binaries are self-contained `linux-x64` publishes. The NativeAOT images are `win-x64` with `OptimizationPreference=Speed`.

`Archevolt.SystemOverhead` binds 16, 64, 256, and 1,024 systems through `BoundSchedule.Bind` and reports the cost of one run divided by the number of systems. Every system adds one `double` to another on each of 16 entities, so the row work is a few nanoseconds and none of it runs as a kernel.

| Case | Systems | Structure |
| --- | --- | --- |
| `empty-batch` | A body that does nothing | One batch that holds every system |
| `empty-chain` | A body that does nothing | Each system ordered after the one before it, so each is a batch of its own |
| `each-chain` | `[Each]` with a resource | Chain |
| `query-chain` | A named query with `foreach` | Chain |
| `commands25-chain` | `[Each]`, and every fourth one declares `Commands` | Chain, with a flush after each declaring system |
| `commands100-chain` | `[Each]` that declares `Commands` | Chain, with a flush after every system |
| `each-fragmented`, `query-fragmented` | 16 systems over entities in 64 archetypes of 4 rows | Chain |

The harness warms every schedule, then takes 31 samples of about a millisecond per case in rotating order and reports the median per process. `scripts/each-shapes.ps1 -Harness Archevolt.SystemOverhead` runs two builds in alternating process order and reports the median of the process medians with their range. After timing, each process runs every case over 37 entities and compares every entity bit for bit with a reference written without the ECS. One entity in five starts below a limit, so the first system that declares commands despawns it, and the check confirms that the flush after that system removed it. Each process also checks that no case allocates managed memory on the thread that runs the schedule. With four workers that count leaves the worker threads out.

Each change was compared with the build before it on `mini`. With default settings, steps 1 to 8 ran three processes per side, and steps 9 to 11 five. The harness was rebuilt against the runtime of each commit, so both sides of every comparison ran the same harness code.

Three limits of the instrument:

- **Noise.** Identical builds measured against each other differ by at most 4% on `mini`, and every case overlaps ([noise-mini](results/system-overhead/noise-mini/summary.md)). With four workers they differ by up to 6%, and one case of 26 separated at 1.02 ([noise-mini-parallel](results/system-overhead/noise-mini-parallel/summary.md)). `fiji` spreads by 15 to 20% between processes, so it confirms direction only.
- **Modes.** With default settings a process runs `query-chain` in one of two modes. The final build costs near 24 ns per system in some processes and near 27 in others, and the baseline near 60 or near 68. Three processes per side can land in one mode each, so a difference of that size in `query-chain` needs five.
- **The workstation.** It was in use during the NativeAOT runs. Three earlier runs of the final comparison, at the build of step 10, had the same 26 verdicts. Two of them had wider ranges. The ledger lists them.

### The harness needs several kinds of each system

The first version of the harness had one body class per kind of system. The executor calls every system through one virtual call. With four classes in the process, the JIT's profile picked one of them as the likely target of that call and inlined that system's whole `Run` into the executor. The pick changed from build to build, and it moved cases by 3 to 47% in either direction. One comparison showed the shared-batch case 47% slower after a change that does not touch its path.

A schedule of many different systems never gets that inlining, so the harness now has eight copies of each kind and a case uses them in turn. Every verdict below comes from that harness. The two changes that remove the growth with schedule size were first measured on the earlier one. They moved their cases by 3 to 100 times and measured the same on both. Steps 3 to 10 were also repeated with `DOTNET_TieredPGO=0`, which removes the profile altogether, and each held.

### Four workers need four equal cores

The first four-worker runs pinned each process to cores 2 to 5 of `mini`. Cores 0 to 3 are the fast ones and reach 5.2 GHz. Cores 4 to 11 are compact and reach 3.3 GHz. A process whose main thread landed on a compact core measured every chain 1.5 times slower. One build read 17.2, 18.0, and 11.3 ns for the same case in three processes. Every four-worker number in this report comes from cores 0 to 3. The runs with one thread always used cores 2 and 3.

## Changes

Each row compares a step with the build before it on `mini`, median ns per system before and after ([step1-mini](results/system-overhead/step1-mini/summary.md) to [step11-mini](results/system-overhead/step11-mini/summary.md), one directory per row). The column names give the case and the number of systems.

| Step | empty-batch/256 | empty-chain/256 | each-chain/256 | query-chain/256 | commands100-chain/1024 | each-fragmented/16 | query-fragmented/16 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 26.7 to 23.2 | 35.2 to 31.9 | 57.0 to 45.4 | 68.1 to 53.3 | 7,071 to 2,156 | 551 to 547, overlap | 703 to 685 |
| 2 | 23.3 to 22.2 | 34.6 to 33.5 | 49.2 to 47.1 | 58.1 to 55.8 | 2,154 to 74.8 | 540 to 549, overlap | 691 to 690, overlap |
| 3 | 22.0 to 19.4 | 33.8 to 29.3 | 48.2 to 43.3 | 54.9 to 51.8, overlap | 75.6 to 69.4, overlap | 544 to 543, overlap | 685 to 677, overlap |
| 4 | 19.3 to 16.0 | 30.2 to 18.2 | 41.3 to 34.5 | 44.9 to 45.1, overlap | 75.4 to 60.1 | 541 to 521 | 669 to 670, overlap |
| 5 | 15.7 to 12.4 | 17.8 to 17.0 | 34.3 to 35.1, overlap | 44.8 to 45.0, overlap | 60.4 to 60.9, overlap | 524 to 524, overlap | 664 to 670, overlap |
| 6 | 12.4 to 12.1 | 16.9 to 16.3 | 34.0 to 33.0 | 38.0 to 43.7, overlap | 60.5 to 61.3, overlap | 530 to 531, overlap | 666 to 675, overlap |
| 7 | 12.1 to 12.4, slower | 16.2 to 10.7 | 33.0 to 25.7 | 44.4 to 31.4 | 60.6 to 53.7 | 530 to 516, overlap | 668 to 653 |
| 8 | 12.7 to 12.6, overlap | 10.6 to 10.7, overlap | 25.6 to 24.8, overlap | 29.8 to 25.9 | 51.7 to 50.2, overlap | 512 to 482 | 651 to 445 |
| 9 | 12.7 to 12.7, overlap | 10.6 to 10.8, overlap | 24.8 to 24.7, overlap | 27.1 to 27.4, overlap | 51.0 to 51.7, overlap | 485 to 482, overlap | 442 to 449, overlap |
| 10 | 12.8 to 12.1, overlap | 10.7 to 10.5 | 24.5 to 24.9, overlap | 24.1 to 24.3, overlap | 50.3 to 50.1, overlap | 479 to 478, overlap | 447 to 440, overlap |
| 11 | 12.6 to 12.5, overlap | 10.8 to 10.8, overlap | 24.8 to 24.5, overlap | 24.5 to 25.0, overlap | 51.8 to 50.7, overlap | 483 to 478, overlap | 441 to 441, overlap |

### 1. Prepare systems only when the world has changed

A run prepared every system before its first step and again after every command flush. Preparing a system looks up each resource the system declares and checks each of its queries for new archetypes. A schedule of S systems with F flushes paid S times F + 1 of those passes per run. That product is the growth the probe found.

Both passes guard against events that are rare and that the world can count:

- **Resources.** A required resource can go missing only between runs, through `RemoveResource` or `TakeResource`. Both require an idle world. The world counts those calls, and a schedule checks its resources again only when the count has moved. A failed check is not remembered, so a schedule keeps failing until the resource returns.
- **Queries.** A query falls behind only when the world gains an archetype, between runs or in a command flush. A schedule refreshes its queries only when the archetype count has moved.

Six new test cases cover both: an archetype that a flush creates is visible to a later system in the same run and to a second schedule on the same world, and a removed or taken resource fails every run until it returns, whether a body or a run condition declares it. Removing the refresh after a flush fails the first test, and recording the removal count before the check fails the second.

### 2. Flush only the systems that ran since the last flush

A flush walked the command buffer of every system in the schedule, through the plan's read-only node list. Only the systems that ran since the flush before it can hold commands. Every other buffer is empty, because a run ends with a flush and a failed run discards every buffer.

`BoundSchedule` now lists the systems that declare commands in plan order and records, for each flush step, where its range of that list ends. Playback order is the same, because that range is exactly the non-empty part of the old walk. With this step the cost per system stops growing: a chain of 1,024 systems that all declare commands costs 74.8 ns per system, and a chain of 16 costs 72.0.

A new test spawns an entity, updates it, and despawns it from three systems with an observer after each. A flush range that ends one system early fails 14 test cases, this test among them.

### 3. Read the plan from arrays

`SchedulePlan` exposes its steps and batch nodes as read-only lists. The executor read them once per step and twice per system in every run, and each read was two interface calls. `BoundSchedule` copies both lists into arrays when it binds.

### 4. Run an owner batch without the worker pool

`WorkerPool.Run` hands work to its workers only when two items can run at once. It ran every other batch on the calling thread: every batch of a world without workers, a batch of one system, and a batch of owner-thread systems alone. For those batches the pool still cleared its error array, claimed each item with an interlocked increment, and called it through a delegate.

`BoundSchedule.RunOnOwner` now runs such a batch itself, in the pool's order and with its rules: owner-thread systems first, then the others, until one fails or the run is cancelled. A failure surfaces as the same `AggregateException` that holds one `SystemExecutionException`. `WorkerPool.Park` marks the workers as parked, as `Run` did for a batch of one, so spinning workers still block at once.

A new test runs three systems in one batch of a world without workers. The second one throws or cancels, and the test checks which bodies ran, which exception arrived, and that the schedule runs again after `Reset`. It passes on the pool path too. The 25 repeats of the scheduling tests passed.

### 5. Finish a work item without an interlocked decrement

Every work item decremented its system's counter with an interlocked operation. Only a partitioned system has several work items, which can finish on different threads at once. Every other system has one item, so one thread writes its counter, and the owner reads the counter only after it has joined the workers under the pool's lock. The gain is in the shared batch, where it is the only atomic operation left per system.

### 6. One counter says whether a system finished

`BoundSchedule` kept two arrays to decide whether a system's run counted: the work items still to finish and a failure flag. An item now decrements the count only when its body returns, so a count of zero already means that every item succeeded, and the failure flags are gone. This step is a simplification. No case ran slower.

### 7. Run a batch of one system without work lists

A system that is ordered after another, or that conflicts with it, is a batch of its own. `RunBatch` treated that batch as a list. It reset the per-system counters, wrote a work item, called `RunOnOwner` to loop over that one item, and looped again to complete the system.

`BoundSchedule` marks, when it binds, each batch that holds one system which never splits into partitions. `RunSolo` runs such a batch with the steps of `RunBatch` and `RunOnOwner` in their order: the run condition, `Begin`, the cancellation check, the body, `Complete`.

`RunSolo` has no cancellation check after the body. A mutation that removed it failed no test, because `Run` checks before the step that follows and a plan always ends with a flush step. So the check was deleted. The check before the body stays: a run condition can cancel the run, and a new test confirms that the body then does not run.

This step has a cost. The shared batch measured 3% slower, 12.1 to 12.4 ns, with ranges that barely separate. A second comparison of five processes measured 12.05 to 12.60 ns at 256 systems, and 4% at the other three sizes with ranges that overlap ([step7-mini-repeat](results/system-overhead/step7-mini-repeat/summary.md)). Without a profile the shared batch measures the same before and after, 12.56 ns at 256 systems ([step7-mini-nopgo](results/system-overhead/step7-mini-nopgo/summary.md)). So a system in a shared batch costs about half a nanosecond more after this step when the JIT has a profile. The step does not change the code that a shared batch runs, and the cause was not found.

### 8. Cursors and blocks keep the tick window, not the run

`QueryState.Begin` checks the lease of the system that calls it. A `QueryCursor` and its `BlockView` values then carried the whole 40-byte `SystemRun` and checked the lease again on every `Next` and on every column access. Both types are ref structs, so neither can outlive the system body that began the query, and the lease holds for as long as that body runs.

A cursor now keeps the tick window and the row range of the run, and a block keeps the tick window. `Next` and the block accessors keep only the check for a default struct, which throws as before.

### 9. Review edits

A review of steps 1 to 8 led to five edits. None of them was meant to change a measurement, and none did:

- **The pool's serial path.** Since step 4 the schedule calls `WorkerPool.Run` only for a batch in which two items can run at once. The serial path of `Run` could no longer be reached. It is gone, and `Run` always wakes the workers.
- **Producers.** A flush walks the command buffers of the systems that declare commands. The schedule listed the systems and reached each buffer through a nullable field. It now lists the buffers, and a buffer knows the name of its system for the playback error.
- **Owner-thread systems.** A batch of one owner-thread system takes `RunSolo` in a world with workers as well. `RunBatch` gives such a system one work item whether or not it is partitionable.
- **Exceptions.** `RunSolo` wraps any exception of its work item in the `AggregateException`, as `RunOnOwner` does.
- **Comments.** Nine comments that repeated the code beside them are deleted.

All 26 cases overlap with the build before, with a profile ([step9-mini](results/system-overhead/step9-mini/summary.md)), without one ([step9-mini-nopgo](results/system-overhead/step9-mini-nopgo/summary.md)), with four workers ([step9-mini-parallel](results/system-overhead/step9-mini-parallel/summary.md)), and as a NativeAOT image ([step9-win-aot](results/system-overhead/step9-win-aot/summary.md)).

### 10. Inline the column check of a block

A generated system binds each column of each block through `BlockView.Read`, `Write`, `Tracked`, or `TryRead`, and all four call `Column<T>` to check the term. With a profile the JIT inlines that chain, which is why this step measures the same in the table above. Without one, the compiler inlined the accessors and left `Column<T>` as a call per column. `Column<T>` is now marked `AggressiveInlining`. [Without a profile](#without-a-profile) has the measurements.

The first version of this step marked the four accessors and the `Entities` getter as well. It gave the same gains and was accepted. Then the row-throughput guard of the final build flagged one case, and comparing the steps one by one put it on this change ([rejected-accessors-shapes-mini](results/system-overhead/rejected-accessors-shapes-mini/summary.md)). A system that first ran on an empty world kept a named-query loop that cost 1.370 ns per row where it cost 0.994, in four of five processes. The JIT compiles such a system with a profile in which the loop never ran. A forced inline at a call site that the profile counts as cold is the likely cause. That is an inference.

That version was reverted. With `Column<T>` alone the same case measures 0.998 before and 1.008 after, and all 26 cases of the guard overlap ([step10-shapes-mini](results/system-overhead/step10-shapes-mini/summary.md)).

The narrower version gives one gain up. A probe ran the Archeoids late-sector workload as a NativeAOT image, five processes per build ([archeoids-aot.txt](results/system-overhead/archeoids-aot.txt)). The wider marking took `HitRocks`, which nests two named-query loops, from 11.3 to 8.0 µs per tick. With `Column<T>` alone it measured 11.0 µs before and 10.7 after, and the ranges overlap, at 10.9 to 11.8 against 10.5 to 12.2.

Two first runs of three processes per side flagged cases as slower: `empty-batch/64` at 1.02 with a profile, and four chain cases at 1.02 to 1.06 with four workers ([step10-mini-first](results/system-overhead/step10-mini-first/summary.md), [step10-mini-parallel-first](results/system-overhead/step10-mini-parallel-first/summary.md)). Neither repeated with five processes per side ([step10-mini](results/system-overhead/step10-mini/summary.md), [step10-mini-parallel](results/system-overhead/step10-mini-parallel/summary.md)).

### 11. Check for cancellation after the worker pool exists

A second model reviewed steps 1 to 8 and the first version of step 10. It looked for behavior that differs from the baseline in seven areas. It found one defect, in step 7, and none in the other six areas.

`RunSolo` checked for cancellation and then called `Workers.Park`. The first use of `Workers` creates the pool and waits for its threads to start. A cancellation that arrived in that time went unseen, and the body ran. Before this round, `WorkerPool.Run` made its check after the pool existed, so such a run threw `OperationCanceledException` and the body did not run.

`RunSolo` now parks first and checks second. `RunOnOwner` was not affected, because it checks before each work item. No test covers the fix. No caller code runs while the pool starts, so a test cannot place a cancellation there. All 26 cases overlap with the build before, with one thread ([step11-mini](results/system-overhead/step11-mini/summary.md)) and with four workers ([step11-mini-parallel](results/system-overhead/step11-mini-parallel/summary.md)).

## Without a profile

NativeAOT compiles without a profile, and so does the JIT with `DOTNET_TieredPGO=0`. After step 7, the executor was cheap in such a build. As a NativeAOT image, an empty system in a batch of its own cost 17.5 ns, but an `[Each]` system cost 83.9 ns and an `[Each]` system over 64 archetypes 2,439 ns. The compiler's listing of a generated `Run` showed where the difference went:

- `QueryCursor.Next` and `BlockView.Column<T>` were calls, one or two per block and one per column, and each repeated the lease check.
- The 40-byte `SystemRun` was copied four times: into a temporary, into the cursor, and into each block.
- `Run` zeroed 288 bytes of stack on entry to hold those structs.

Steps 8 and 10 remove the repeated checks, three of the copies, and the call per column. `QueryCursor.Next` stays a call. Steps 8 to 10 on `mini` with `DOTNET_TieredPGO=0`, median ns per system ([step8-mini-nopgo](results/system-overhead/step8-mini-nopgo/summary.md), [step9-mini-nopgo](results/system-overhead/step9-mini-nopgo/summary.md), [step10-mini-nopgo](results/system-overhead/step10-mini-nopgo/summary.md)):

| Step | empty-batch/256 | empty-chain/256 | each-chain/256 | query-chain/256 | commands100-chain/1024 | each-fragmented/16 | query-fragmented/16 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 8 | 12.7 to 12.7, overlap | 11.5 to 11.7, overlap | 45.3 to 31.9 | 89.0 to 75.3 | 73.2 to 55.3 | 1,452 to 785 | 1,763 to 1,131 |
| 9 | 12.7 to 12.7, overlap | 11.8 to 11.9, overlap | 31.6 to 31.7, overlap | 72.7 to 73.2, overlap | 56.5 to 55.9, overlap | 786 to 789, overlap | 1,183 to 1,187, overlap |
| 10 | 12.5 to 12.7, overlap | 11.6 to 11.8, overlap | 31.7 to 30.6 | 72.2 to 69.4 | 59.2 to 55.6 | 796 to 624 | 1,180 to 1,143 |

The same three steps as NativeAOT images on the workstation ([step8-win-aot](results/system-overhead/step8-win-aot/summary.md), [step9-win-aot](results/system-overhead/step9-win-aot/summary.md), [step10-win-aot](results/system-overhead/step10-win-aot/summary.md)):

| Step | empty-batch/256 | empty-chain/256 | each-chain/256 | query-chain/256 | commands100-chain/1024 | each-fragmented/16 | query-fragmented/16 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 8 | 17.7 to 17.9, overlap | 17.5 to 17.0, overlap | 83.9 to 64.1 | 114 to 95.2 | 132 to 112 | 2,439 to 1,490 | 2,869 to 1,911 |
| 9 | 19.6 to 18.7, overlap | 18.7 to 18.1, overlap | 66.4 to 63.9, overlap | 98.2 to 94.9, overlap | 118 to 121, overlap | 1,596 to 1,673, overlap | 2,051 to 1,931, overlap |
| 10 | 17.8 to 17.6, overlap | 16.8 to 16.8, overlap | 60.4 to 57.2 | 90.8 to 84.4 | 113 to 98.3 | 1,564 to 1,058 | 1,994 to 1,405 |

## Guards

### Row throughput

`Archevolt.EachShapes` measures the cost per row of 13 systems over 100,000 rows, after a populated start and after an empty one. The final build ran against the baseline with five processes per side on `mini` ([shapes-mini](results/system-overhead/shapes-mini/summary.md)). 24 of the 26 cases overlap and two are faster. `query-wrap` after an empty start went from 1.183 to 0.978 ns per row.

#### Slow processes come from the host

The guard ran 238 processes in this round, and [shapes-processes.tsv](results/system-overhead/shapes-processes.tsv) lists each one. Six of them ran their ten memory-bound cases three to four times slower, at about 0.45 ns per row against 0.11, and two more had one slow case. The medians ignore them. The first four slow processes were all builds of steps 9 and 10, so they got a closer look:

- **Timing.** All eight started between 3:58 and 6:21 minutes past a 20-minute mark. The guard runs covered that span five times, and four of the five had a slow process. Of the 238 processes, 76 started inside the span and 162 outside it. None of the 162 had a slow case.
- **Control.** The baseline measured against itself across the fifth window got two of the six slow processes ([shapes-mini-control](results/system-overhead/shapes-mini-control/summary.md)). So the baseline is affected as well, and the changes of this round are not the cause. A first control across the fourth window got none ([shapes-mini-control-first](results/system-overhead/shapes-mini-control-first/summary.md)).
- **Memory.** The added time follows the memory a case touches more than its code. Three cases move a position by a velocity: as a kernel, as hand-written vector code, and as an indexed scalar loop. In a slow process each gained 0.35 to 0.37 ns per row, and the named-query form gained 0.21 ns. The two cases that update one component gained 0.18 ns.
- **The host.** `mini` also runs a virtual machine. A monitor during the control logged its processor use at about 25% of a core, and at 110 to 166% for seven seconds inside the window ([host-top.txt](results/system-overhead/shapes-mini-control/host-top.txt)).

So something periodic on the host slows memory traffic for moments at a time. The likely path is the cache that the four fast cores share. That is an inference. A guard run on this host should stay clear of those windows or take enough processes for the median to ignore them.

### Four workers

The final build against the baseline with four workers on the four fast cores of `mini`, five processes per side ([final-mini-parallel](results/system-overhead/final-mini-parallel/summary.md)):

| Case | 16 systems | 64 | 256 | 1,024 |
| --- | --- | --- | --- | --- |
| `empty-batch` | 169 to 148 | 88.8 to 76.8, overlap | 75.1 to 66.4, overlap | 63.1 to 58.2, overlap |
| `empty-chain` | 35.9 to 11.4 | 36.3 to 11.1 | 36.5 to 11.0 | 36.4 to 11.0 |
| `each-chain` | 57.9 to 25.5 | 57.1 to 25.4 | 57.8 to 25.4 | 58.5 to 25.9 |
| `query-chain` | 66.1 to 24.4 | 66.0 to 25.0 | 67.4 to 24.9 | 68.6 to 26.4 |
| `commands25-chain` | 81.9 to 30.5 | 164 to 31.5 | 483 to 31.6 | 1,817 to 33.8 |
| `commands100-chain` | 189 to 47.4 | 512 to 47.5 | 1,842 to 48.2 | 7,081 to 51.8 |
| `each-fragmented` | 561 to 478 | | | |
| `query-fragmented` | 694 to 442 | | | |

A chain runs on the owner thread whatever the worker count, so the chains measure as they do with one thread. The shared batch is the one case that uses the workers, and this round did not change how they share a batch. It costs 58 to 148 ns per system with four workers and 12.6 ns with one. [What remains](#what-remains) has the reason.

## Compatibility

ApiCompat in strict mode reports no change in `Archevolt.dll`, `Archeoids.dll`, `Archeoids.Browser.dll`, or `Archevolt.Particles.dll` ([api-compat.txt](results/system-overhead/verification/api-compat.txt)). It reports two differences that are not changes to the library:

- **`Archevolt.Benchmarks.dll`.** The `[Benchmark]` attributes record source paths, and the baseline was built in another directory.
- **`Archevolt.Tests.dll`.** The `[Fact]` and `[Theory]` attributes record source paths and lines, and this round added six test methods.

The generator did not change, so it emits the same code for every consumer.

Two behaviors differ, and neither is reachable from a valid program:

- **Lease errors.** `QueryCursor.Next` and the `BlockView` accessors no longer throw the lease error. They could only do so after the system body that began the query had returned, and a ref struct cannot be used then. A default cursor or block throws as before.
- **Check order on a missing resource.** A run that finds a required resource missing no longer refreshes the queries of the systems that come before the failing one. Queries are refreshed on the next run that passes the check.

One internal state differs, and no API shows it. When a run is cancelled just before a system in a batch of its own, `RunSolo` marks the workers as parked and then throws. The pool used to throw first. Workers that were still spinning after a parallel batch then block at once, where they would have spun for up to 50 µs.

## Verification

`./scripts/verify.ps1` passed all nine stages at `e93c062` after `dotnet build-server shutdown` ([verify.txt](results/system-overhead/verification/verify.txt)). The stages are restore and build, 260 tests, headless Archeoids, pack, package contents, the JIT and NativeAOT package consumers, the NativeAOT particle sample, and the allocation smoke check. The browser and WebAssembly target was built but not run in a browser.

The round added six test methods, which run as 16 cases, and two assertions on a default block. Four more checks ran at `e93c062`:

- **Mutations.** Seven mutations each undo one behavior that a new test is meant to hold. Each one fails its test, and the unmutated code passes all 119 runtime tests ([mutations.txt](results/system-overhead/verification/mutations.txt)).
- **Repeats.** The 72 scheduling tests passed 25 times in a row ([scheduling-repeats.txt](results/system-overhead/verification/scheduling-repeats.txt)).
- **Each commit.** Every one of the 12 code commits builds without a warning and passes both test assemblies and the headless game on its own ([per-commit.txt](results/system-overhead/verification/per-commit.txt)).
- **Allocation on every thread.** The smoke check of `verify.ps1` measured zero managed bytes on all threads with one-way and with four-way concurrency ([performance-smoke.txt](results/system-overhead/verification/performance-smoke.txt)).

A second model, gpt-6.1-sol, reviewed the work three times. Two reviews read the code against a list of named risks, with the baseline as the reference. The first read steps 1 to 8 and the first version of step 10, and found the defect that step 11 fixes. The second read steps 9 to 11 and found no defect. It noted one difference in internal state, which [Compatibility](#compatibility) lists. The third read the ledger and this report against the result files. It matched more than 60 numbers and confirmed four gaps in the evidence: the allocation check that leaves worker threads out, mutation and repeat runs without a saved result, an Archeoids probe without a saved result, and one count in the guard section. This version closes them.

Every process of every comparison compared each case with its reference and checked for allocation on the thread that runs the schedule, as [Conditions and method](#conditions-and-method) describes. None failed. The harness does not count allocations on worker threads. The allocation smoke check of `verify.ps1` counts every thread. It runs an `[Each]` system and a named-query system over 100,000 entities in a world with four-way concurrency, and it passed.

## What remains

- **Parallel batches of small systems.** Each parallel batch wakes every worker, and the threads then claim one work item at a time through one shared counter. With four workers on `mini`, an empty system in a shared batch costs 148 ns in a batch of 16 and 58 ns in a batch of 1,024, against 12.6 ns with one thread. Handing out ranges of items would cut the cost per item. The partitions of a partitioned system must stay spread across the workers, so the ranges need care.
- **Block accessors in nested loops without a profile.** Marking the block accessors for inlining took a system with two nested named-query loops from 11.3 to 8.0 µs as a NativeAOT image, and slowed a named-query loop by 38% under the JIT after an empty start. Step 10 keeps neither effect. A form of the accessors that the compiler inlines on its own would keep the first without the second.
- **Cost per block.** With a profile, an `[Each]` system over 64 archetypes costs about 7.5 ns per block. `QueryCursor.Next` is still a call without a profile, and the generated `Run` calls `__Rows` once per block.
- **Lease checks in commands and messages.** `Commands`, `MessageWriter<T>`, and the `MessageReader<T>` enumerator check the lease on every call, twice for each message read. They are ref structs like the cursor, so the argument of step 8 applies. No case in this harness measures them.
- **Schedule size.** The chains still cost up to 13% more per system at 1,024 systems than at 16: 1.13 times where a quarter of the systems declare commands and 1.10 times where all do. A schedule of that size no longer fits the first-level cache, which is the likely cause. That is an inference.
- **Binding.** `SchedulePlanner` computes reachability in cubic time. In one measurement on the workstation that was not kept, binding a chain of 1,024 systems took 1.3 s, against 26 ms for 256. A schedule pays that once.
- **Float `%` in kernels.** In the Archeoids probe as a NativeAOT image, `Move` takes 34.6 of the 51.8 µs of a simulation tick at step 10 ([archeoids-aot.txt](results/system-overhead/archeoids-aot.txt)). It wraps positions with float `%`, which costs four calls into the runtime's remainder helper per entity and keeps the method out of the kernels. A vector remainder with the same results was not attempted.
- **The Intel host.** `fiji` spreads by 15 to 20% between processes. `each-fragmented` overlapped there, at 1,362 against 1,176 ns.

## Reproduce

```powershell
# Publish each side, for example at 36dd22e and at e93c062:
dotnet build-server shutdown
dotnet publish benchmarks/Archevolt.SystemOverhead -c Release -r linux-x64 --self-contained true -o <side>/overhead-linux
dotnet publish benchmarks/Archevolt.SystemOverhead -c Release -r win-x64 -p:SystemOverheadAot=true -o <side>/overhead-win-aot
# Copy both linux directories to the host, then:
./scripts/each-shapes.ps1 -Remote mini -Prefix 'taskset -c 2,3' -Harness Archevolt.SystemOverhead -Metric median_ns_per_system `
    -Modes serial -Rows 16 -Baseline archevolt-bench/base/overhead-linux -Candidate archevolt-bench/final/overhead-linux `
    -Launches 5 -Output artifacts/system-overhead/final-mini
./scripts/each-shapes.ps1 -Harness Archevolt.SystemOverhead -Metric median_ns_per_system -Modes serial -Rows 16 `
    -Baseline <baseline>/overhead-win-aot -Candidate <candidate>/overhead-win-aot -Launches 9 -Output artifacts/system-overhead/final-win-aot
```

The harness was added in this round, so the baseline side is the harness of this round built against the runtime of `36dd22e`. Add `-Prefix 'env DOTNET_TieredPGO=0 taskset -c 2,3'` to measure without a profile. For four workers, pass `-Modes parallel` and pin the processes to four equal cores, which on `mini` is `-Prefix 'taskset -c 0-3'`.
