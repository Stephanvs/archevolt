# Step 3: table storage

This file records the before and after measurements for [ROADMAP.md](../ROADMAP.md) step 3. Step 3 replaced 16 KiB chunks with one growing native slab per archetype. Tables keep their capacity until `World.TrimExcess()`, and parallel runs split matched rows into contiguous ranges.

## Conditions

| Setting | Value |
| --- | --- |
| CPU | AMD Ryzen 9 5950X 16-Core Processor |
| OS | Windows 11 Pro 10.0.26300 |
| .NET | SDK 11.0.100-rc.2.26504.105, runtime 11.0.0-rc.2.26504.105, Release |
| BenchmarkDotNet | 0.16.0-preview.2, default job, three launches |
| Rust | rustc 1.98.0, LLVM 22.1.8, `-C target-cpu=native` |
| Bevy | `bevy_ecs` 0.20.0 |
| Before | `ffabaeb`, chunked storage |
| After | `e8621ff`, table storage |

Both sides ran `./scripts/baseline.ps1` from clean worktrees on 2026-10-27, one after the other, with nothing else running. `./scripts/compare-baselines.ps1` produced the verdicts. "faster" and "slower" mean the after median lies outside the before range. Each cell is the median of three per-process means in microseconds, with the range in brackets.

The step 0 numbers in [baseline.md](baseline.md) were recorded on .NET 10. They are not comparable with these.

The Bevy code is identical on both sides, so its two runs measure machine drift. Most Bevy cases moved by less than 5%, and a few by up to 24%, for example `tracked_write/1000` at 1.19 and `traversal/archetypes_1_payload_0/1000` at 1.24. Archevolt changes smaller than that are not evidence.

## Gate

| Gate item | Result |
| --- | --- |
| Named query with an unread 256-byte component is faster than the chunked baseline | Met at 1,000,000 entities. Not met at 100,000, or at 1,000 with one archetype. |
| Tag round trip is faster than the chunked baseline | Met at both sizes. |
| No ordinary query regresses outside the baseline range | Not met. The named query at 1,000 entities with one archetype, `WriteThenChangedAll`, and `OptionalHalfPresent` at 100,000 regress. |
| Growth, migration, disposal, tracking, and four-worker tests pass | Met. 155 of 155 tests pass, and `scripts/verify.ps1` passes. |

Every regression is a named query. `[Each]` and `[EachBlock]` improved in every case.

### Named query, traversal matrix

| Entities | Archetypes | Payload | Before | After | After/before |
| ---: | ---: | --- | ---: | ---: | ---: |
| 1,000 | 1 | none | 1.180 [1.167–1.189] | 2.065 [2.059–2.065] | 1.75 |
| 1,000 | 1 | 256 B | 1.353 [1.352–1.409] | 2.027 [2.020–2.062] | 1.50 |
| 1,000 | 16 | none | 1.464 [1.463–1.476] | 1.354 [1.349–1.366] | 0.92 |
| 1,000 | 16 | 256 B | 1.726 [1.715–1.778] | 1.398 [1.378–1.428] | 0.81 |
| 100,000 | 1 | none | 208.0 [111.2–209.0] | 198.7 [194.2–200.3] | 0.96 |
| 100,000 | 1 | 256 B | 161.5 [160.6–163.7] | 198.9 [190.3–199.3] | 1.23 |
| 100,000 | 16 | none | 206.5 [206.5–209.1] | 200.9 [192.9–201.6] | 0.97 |
| 100,000 | 16 | 256 B | 175.6 [175.3–177.7] | 196.6 [193.8–197.8] | 1.12 |
| 1,000,000 | 1 | none | 2,339 [2,319–2,356] | 1,968 [1,952–2,036] | 0.84 |
| 1,000,000 | 1 | 256 B | 3,935 [3,896–4,005] | 1,919 [1,916–1,995] | 0.49 |
| 1,000,000 | 16 | none | 2,354 [2,311–2,392] | 1,982 [1,928–2,004] | 0.84 |
| 1,000,000 | 16 | 256 B | 4,582 [4,524–4,595] | 2,007 [2,004–2,016] | 0.44 |

### Why the named query regressed

The named query runs in one of two JIT shapes. In the fast shape, `Movement.Query` inlines `QueryCursor.Next` and the `BlockView` accessors, and the enumerator's fields live in registers. In the slow shape, `Next` stays a call. The enumerator is then address-exposed, so each row reloads four span fields from the stack, stores `_row` back to the stack, and zeroes a 72-byte stack temporary. The fast shape costs about 1.2 ns per row and the slow shape about 2 ns.

The Tier-1 listings of all twelve traversal processes, captured with `--envVars DOTNET_JitDisasm:Query`, show which shape each case received:

| Case | Before | After |
| --- | --- | --- |
| 1,000, one archetype, with or without payload | registers | stack |
| 1,000, 16 archetypes, with or without payload | registers | registers |
| 100,000, no payload | stack | stack |
| 100,000, 256 B payload | registers | stack |

The timings follow the shape in every case. That includes the chunked 100,000-entity payload cases, which were faster than the same cases without the payload. The inference is that tiered PGO inlines `Next` only when the block transition is hot. Small chunks made it hot. One table per archetype makes it cold.

With `DOTNET_TieredPGO=0`, every process on both sides compiles the register shape. Then the tables are never slower. Each cell is the mean of three launches under BenchmarkDotNet's short job, in microseconds:

| Case | Before | After |
| --- | ---: | ---: |
| Named query, 1,000, 1 archetype, none | 2.900 | 2.837 |
| Named query, 1,000, 1 archetype, 256 B | 3.507 | 2.986 |
| Named query, 100,000, 1 archetype, none | 278.6 | 300.5 (median 270.4, one outlier launch) |
| Named query, 100,000, 1 archetype, 256 B | 380.5 | 269.5 |
| Named query, 1,000,000, 16 archetypes, 256 B | 6,876 | 2,655 |
| `WriteThenChangedAll`, 100,000 | 482.5 | 430.1 |
| `OptionalHalfPresent`, 100,000 | 238.2 | 212.5 |

So the regression comes from enumerator codegen that depends on the PGO profile, not from table storage. Step 2 rewrites that enumerator. Marking `QueryCursor.Next` `AggressiveInlining` took the 1,000-entity case from 2.07 to 1.57 µs and left the 100,000-entity cases unchanged, so this step does not include it.

## Other Archevolt cases

| Case | Entities | Before | After | After/before |
| --- | ---: | ---: | ---: | ---: |
| Tag round trip | 1,000 | 277.0 [275.6–290.9] | 99.57 [96.53–103.0] | 0.36 |
| Tag round trip | 100,000 | 29,130 [28,890–29,440] | 9,893 [9,816–10,860] | 0.34 |
| Wide round trip | 1,000 | 292.2 [287.2–297.6] | 106.1 [104.3–107.3] | 0.36 |
| Wide round trip | 100,000 | 31,570 [31,100–31,910] | 11,270 [11,040–11,280] | 0.36 |
| Spawn | 1,000 | 57.03 [55.77–58.91] | 49.04 [48.52–49.53] | 0.86 |
| Spawn | 100,000 | 7,499 [7,163–7,537] | 6,412 [6,143–6,429] | 0.86 |
| Despawn | 1,000 | 45.07 [44.50–45.43] | 29.26 [27.79–33.72] | 0.65 |
| Despawn | 100,000 | 6,168 [6,093–6,186] | 4,929 [4,284–4,943] | 0.80 |
| `Get<T>` | 1,000 | 8.278 [8.176–9.215] | 8.345 [8.206–8.376] | 1.01 |
| `Get<T>` | 100,000 | 1,958 [1,957–2,089] | 1,676 [1,672–1,688] | 0.86 |
| `[Each]` traversal, 1 archetype, none | 100,000 | 119.1 [118.0–121.2] | 99.14 [98.32–99.28] | 0.83 |
| `[Each]` traversal, 1 archetype, 256 B | 100,000 | 174.8 [173.3–177.6] | 98.47 [96.26–103.1] | 0.56 |
| `[Each]` traversal, 16 archetypes, 256 B | 1,000,000 | 4,809 [4,691–4,992] | 1,020 [999.8–1,021] | 0.21 |
| Tracked write | 100,000 | 115.2 [111.5–115.5] | 97.78 [97.16–98.38] | 0.85 |
| `Changed<T>`, nothing changed | 100,000 | 102.3 [102.2–104.7] | 92.53 [91.50–95.64] | 0.90 |
| Write, then `Changed<T>` over all | 100,000 | 218.7 [216.2–220.7] | 244.1 [243.9–245.4] | 1.12 |
| Optional term on half | 100,000 | 136.5 [123.4–136.6] | 148.1 [133.3–148.8] | 1.09 |
| Movement `GeneratedEach` | 100,000 | 120.6 [119.4–122.4] | 98.12 [96.50–100.4] | 0.81 |
| Movement `GeneratedBlock` | 100,000 | 110.7 [109.2–111.2] | 84.46 [83.78–85.53] | 0.76 |
| Movement `GeneratedBlockVector` | 100,000 | 28.74 [28.71–29.28] | 20.62 [20.51–20.67] | 0.72 |
| Movement `Handwritten` | 100,000 | 85.27 [83.96–87.03] | 84.51 [84.07–85.09] | 0.99 |

The swap-remove that fills a despawned or moved row now copies columns index for index within one table. Step 6 planned that change, so part of the round-trip gain belongs to step 6. The cross-archetype copy still binary-searches each column.

## Against Bevy, after step 3

The Bevy medians come from the after run. Treat them as context, per the measurement rules.

| Case | Entities | Archevolt | Bevy | Archevolt/Bevy |
| --- | ---: | ---: | ---: | ---: |
| Movement, `GeneratedBlockVector` vs `bevy_for_each` | 100,000 | 20.62 | 23.78 | 0.87 |
| Movement, `GeneratedBlockVector` vs `bevy_contiguous` | 100,000 | 20.62 | 24.66 | 0.84 |
| Traversal `[Each]`, 1 archetype, 256 B | 100,000 | 98.47 | 24.63 | 4.00 |
| Tag round trip | 100,000 | 9,893 | 11,090 | 0.89 |
| Wide round trip | 100,000 | 11,270 | 11,520 | 0.98 |
| Despawn | 100,000 | 4,929 | 9,893 | 0.50 |
| Spawn | 100,000 | 6,412 | 4,774 | 1.34 |
| `Get<T>` | 100,000 | 1,676 | 830.3 | 2.02 |
| Tracked write | 100,000 | 97.78 | 28.70 | 3.41 |

The step 1 slice form now beats Bevy's `for_each` on the movement case. PR #5 measured it 21% slower on chunked storage.
