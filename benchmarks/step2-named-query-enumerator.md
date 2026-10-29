# Step 2: named-query enumerator

This file records the before and after measurements for [ROADMAP.md](../ROADMAP.md) step 2. Step 2 rewrote the enumerator that the generator emits for a named query. [step3-table-storage.md](step3-table-storage.md) traced step 3's named-query regression to that enumerator. When tiered PGO left `QueryCursor.Next` as a call, the enumerator was address-exposed. Each row then reloaded its span fields from the stack and copied a 72-byte `Row`.

## What changed

The enumerator holds a ref to each column's current element and the number of rows left in the block. `MoveNext` advances every ref by one element, then decrements and tests the row count. `NextBlock` copies the cursor to a local before calling `QueryCursor.Next`. An out-of-line `Next` then exposes that copy, not the enumerator, so the enumerator's fields stay in registers. `Row` holds a ref to each column's current element.

Two intermediate versions were measured with BenchmarkDotNet's short job and discarded:

- A base ref per column plus a row index gave a register-only row loop, but each row still recomputed `row * size`. It ran at 1.8 to 2.0 ns per row with one table, and the 16-table cases ran faster.
- Advancing the refs only when a row remained gave a loop without index arithmetic. It still ran at about 1.6 ns per row. `MoveNext` split into a test block and an advance block, and tiered PGO laid the loop out top-tested, with an unconditional jump back. A long same-process run of the same code got the bottom-tested layout and ran at 0.81 to 0.90 ns per row.

Advancing before the test leaves one hot block that ends in the loop's branch, and every measured process got the bottom-tested layout.

## Conditions

| Setting | Value |
| --- | --- |
| CPU | AMD Ryzen 9 5950X 16-Core Processor |
| OS | Windows 11 Pro 10.0.26300 |
| .NET | SDK 11.0.100-rc.3.26522.148, runtime 11.0.0-rc.3.26522.148, RyuJIT x86-64-v3, Release |
| BenchmarkDotNet | 0.16.0-preview.2, default job, three launches |
| Power plan | High performance, set for the whole sequence |
| Before | `de9fcfd` |
| After | `de9fcfd` with this change applied, uncommitted |

The SDK resolved to rc.3, not the rc.2 recorded for step 3, so these numbers are not comparable with step 3's.

Each side ran from a clean worktree outside the repository, one after the other on 2026-10-28 and 2026-10-29:

| Pass | Code | Started (UTC) | Mean total CPU | Peak total CPU |
| --- | --- | --- | ---: | ---: |
| Before A | `de9fcfd` | 23:07 | 13.8% | 24.6% |
| After | step 2 | 23:54 | 14.1% | 27.8% |
| Before B | `de9fcfd` | 00:42 | 14.3% | 25.6% |

Each pass ran `./scripts/baseline.ps1 -Classes TraversalMatrixBenchmarks, FilterBenchmarks, ReferenceComparisonBenchmarks.GeneratedEach, ReferenceComparisonBenchmarks.GeneratedQuery`. Only named-query and `[Each]` cases ran. Each cell is the median of three per-process means in microseconds, with the range in brackets. `./scripts/compare-baselines.ps1` produced the verdicts.

The machine was not idle. Total CPU was sampled every 30 seconds. About two of the 32 logical processors were busy throughout, mostly with an editor, a flight simulator, and a browser, in addition to the benchmark's own core. The load was steady across the three passes.

### Drift

Before A and Before B ran identical code. Of their 34 cases, 32 moved by 4% or less. Two moved more:

- The 1,000-entity named query with one archetype and the 256-byte payload ran at 2.156 µs in Before A and 1.148 µs in Before B. This is the old enumerator's PGO dependence: one process compiled the fast shape and the other did not.
- `WriteThenChangedAll` at 1,000 entities has a wide range in both passes, 2.067 to 3.065 µs and 2.007 to 2.735 µs.

The `[Each]` cases do not use the named-query enumerator, so they are a second control inside the After pass. Every `[Each]` case in After lies within 4% of Before A.

## Gate

| Gate item | Result |
| --- | --- |
| Named query within 1.2 times `[Each]` on the 100,000-entity movement case | Met. 84.59 µs against 98.44 µs, 0.86 times. |
| Wide-component case no slower | Met. Every 256-byte payload case ran faster than Before A, at 0.43 to 0.89 times. |
| Disassembly shows no per-row span reconstruction or `Row` copy | Met. See [Disassembly](#disassembly). |
| Named-query traversal beats the chunked numbers in step 3 at 1,000 and 100,000 entities, default runtime configuration | Met in all eight cases. These cross-run comparisons are on a different SDK and machine state. The narrowest margin, 1.050 against 1.180 µs, is within cross-run drift. |
| Runtime tests pass | Met. 158 of 158 pass, including a new test for tracked writes and `Entity` across two tables. |

## Named query, traversal matrix

| Entities | Archetypes | Payload | Before A | After | Before B | After/A | Chunked, step 3 |
| ---: | ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 1,000 | 1 | none | 2.065 [2.029–2.171] | 1.050 [1.036–1.147] | 2.062 [2.038–2.082] | 0.51 | 1.180 |
| 1,000 | 1 | 256 B | 2.156 [2.152–2.206] | 1.006 [0.9985–1.072] | 1.148 [1.144–1.158] | 0.47 | 1.353 |
| 1,000 | 16 | none | 1.396 [1.373–1.409] | 1.253 [1.244–1.274] | 1.379 [1.353–1.392] | 0.90 | 1.464 |
| 1,000 | 16 | 256 B | 1.456 [1.426–1.497] | 1.303 [1.255–1.340] | 1.404 [1.391–1.436] | 0.89 | 1.726 |
| 100,000 | 1 | none | 199.1 [193.0–200.3] | 85.53 [84.15–86.09] | 193.7 [192.5–200.1] | 0.43 | 208.0 |
| 100,000 | 1 | 256 B | 198.7 [193.1–200.7] | 84.78 [84.55–85.21] | 201.4 [199.6–260.9] | 0.43 | 161.5 |
| 100,000 | 16 | none | 197.5 [194.2–206.4] | 88.75 [88.51–89.14] | 200.4 [192.8–202.4] | 0.45 | 206.5 |
| 100,000 | 16 | 256 B | 198.8 [193.8–201.9] | 86.40 [86.40–88.43] | 198.3 [194.8–200.8] | 0.43 | 175.6 |
| 1,000,000 | 1 | none | 1,961 [1,958–1,980] | 888.4 [871.2–908.5] | 2,030 [2,001–2,047] | 0.45 | 2,339 |
| 1,000,000 | 1 | 256 B | 1,958 [1,950–1,963] | 888.8 [887.5–894.1] | 2,000 [1,996–2,018] | 0.45 | 3,935 |
| 1,000,000 | 16 | none | 2,031 [1,963–2,200] | 905.3 [902.5–905.7] | 1,993 [1,973–2,021] | 0.45 | 2,354 |
| 1,000,000 | 16 | 256 B | 2,011 [1,975–2,014] | 895.2 [893.9–896.1] | 2,009 [1,944–2,024] | 0.45 | 4,582 |

At 100,000 and 1,000,000 entities the named query now runs at 0.85 to 0.91 ns per row, slightly faster than `[Each]` at 0.97 to 1.03. The 16-archetype cases at 1,000 entities gained least, because there a block transition comes every 63 rows.

## Movement and filter cases

| Case | Entities | Before A | After | Before B | After/A |
| --- | ---: | ---: | ---: | ---: | ---: |
| Movement, named query | 100,000 | 197.9 [197.0–199.7] | 84.59 [84.50–86.22] | 196.8 [196.4–200.1] | 0.43 |
| Movement, `[Each]` | 100,000 | 97.94 [97.05–99.13] | 98.44 [96.86–99.03] | 96.86 [95.51–98.90] | 1.01 |
| `Changed<T>`, nothing changed | 1,000 | 1.104 [1.097–1.111] | 0.6351 [0.6151–0.7071] | 1.075 [1.071–1.097] | 0.58 |
| `Changed<T>`, nothing changed | 100,000 | 95.99 [94.27–96.21] | 48.74 [47.28–49.06] | 92.30 [92.06–94.94] | 0.51 |
| Write, then `Changed<T>` over all | 1,000 | 2.632 [2.067–3.065] | 2.353 [2.340–2.656] | 2.076 [2.007–2.735] | 0.89 |
| Write, then `Changed<T>` over all | 100,000 | 246.2 [240.7–246.6] | 172.8 [171.9–190.7] | 242.5 [236.8–243.5] | 0.70 |
| Optional term on half | 1,000 | 1.285 [1.275–1.303] | 1.298 [1.172–1.358] | 1.300 [1.188–1.382] | 1.01 |
| Optional term on half | 100,000 | 151.3 [134.8–152.3] | 125.6 [125.0–135.9] | 149.6 [136.6–150.0] | 0.83 |
| Tracked write, `[Each]` | 100,000 | 98.95 [98.79–102.3] | 97.00 [96.36–97.73] | 97.27 [96.55–97.83] | 0.98 |

`Write, then Changed<T> over all` includes the unchanged `[Each]` tracked write, about 97 µs at 100,000 entities. Its named-query half fell from about 147 to 76 µs. The 1,000-entity filter and optional cases lie inside the drift between the two controls.

## Disassembly

Tier-1 listings were captured with `--job short --launchCount 1 --envVars DOTNET_JitDisasm:Run` for the 100,000-entity movement case and all twelve traversal cases. In every process, the generated system body inlines the user method and the enumerator. The row loop of the 100,000-entity case with one archetype is:

```asm
G_M000_IG12:
       vmulss   xmm0, xmm6, dword ptr [r14]
       vaddss   xmm0, xmm0, dword ptr [rbp]
       vmovss   xmm1, dword ptr [rbp+0x04]
       vmulss   xmm2, xmm6, dword ptr [r14+0x04]
       vaddss   xmm1, xmm1, xmm2
       vmovss   xmm2, dword ptr [rbp+0x08]
       vmulss   xmm3, xmm6, dword ptr [r14+0x08]
       vaddss   xmm2, xmm2, xmm3
       vmovss   dword ptr [rbp], xmm0
       vmovss   dword ptr [rbp+0x04], xmm1
       vmovss   dword ptr [rbp+0x08], xmm2
       mov      r15d, eax
G_M000_IG13:
       add      rbp, 12
       add      r14, 12
       lea      ecx, [r15-0x01]
       mov      eax, ecx
       test     r15d, r15d
       jne      SHORT G_M000_IG12
```

The column refs, the row count, and `delta` stay in registers. The loop has no stack access, no bounds check, no span construction, and no `Row` copy. `QueryCursor.Next` remains a call in nine of the thirteen listings, including every 100,000- and 1,000,000-entity case, and its argument is the stack copy of the cursor, not the enumerator. The JIT inlined `Next` in three of the four 1,000-entity cases. The fourth, one archetype without payload, keeps the call and still ran at 1.050 µs, so the speed no longer depends on whether `Next` is inlined.

## Without PGO

With `DOTNET_TieredPGO=0`, BenchmarkDotNet's short job, and one launch, the named query ran at 84 to 101 µs at 100,000 entities and 866 to 1,069 µs at 1,000,000. The 16-archetype cases at 1,000 entities rose to about 1.85 µs, because `Next` is not inlined and those runs make one transition per 63 rows.

The same configuration slowed `[Each]` to 180 to 211 µs at 100,000 entities and 1.87 to 2.36 ms at 1,000,000, about twice its PGO timings, in two separate runs. Step 2 did not change `[Each]`. Its generated loop depends on the PGO profile in the same way the old named-query enumerator did.
