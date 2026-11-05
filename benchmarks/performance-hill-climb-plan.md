# Performance hill-climbing handoff

Prepared 5 November 2026. Planning only; no optimizations or new benchmark runs were performed when writing this document.

## Assignment

Improve Archevolt's measured performance through generator and runtime implementation changes while keeping the public API and observable behavior unchanged. Work autonomously on reversible internal experiments. Retain only demonstrated improvements. Public API changes require discussion with the user before implementation, including additive changes.

Do not promise Bevy parity. Treat it as a direction, not a result that must be manufactured. A well-supported finding that an approach cannot improve under the existing contract is useful evidence.

Read AGENTS.md, ROADMAP.md and its amendments, benchmarks/results.md, benchmarks/baseline.md, benchmarks/step2-named-query-enumerator.md, benchmarks/step7-block-change-summaries.md, and benchmarks/query-partition-threshold.md before modifying the corresponding subsystem. Historical reports describe their measured commits, not necessarily current HEAD.

## Fixed constraints

- Freeze existing public/protected signatures, attributes, generic constraints, generated public members, reference mutability/lifetimes, diagnostics contracts, and supported consumer usage. Internal fields, generator architecture, and private generated helpers may change.
- Public runtime support types used by generated consumer assemblies are part of this freeze: QueryState, QueryCursor, BlockView, TrackedColumn, StampedRef, and related types. A new helper exposed publicly is an API addition even if intended only for generated code.
- Preserve change-tracking timing, Added/Changed windows, optional access, entity/world/generation validation, disposal and stale-handle behavior, exception behavior, command ordering, scheduling dependencies, cancellation, and concurrency guarantees. Preserve any observable ordering promised by the current contract.
- Do not require consumers to rewrite callbacks, switch from Each to EachBlock, annotate new purity guarantees, change compiler flags, or install a newer runtime to receive the claimed gain.
- Do not remove validation from public paths, change tracking from selective to unconditional or vice versa, narrow ticks, shrink the public entity handle, or weaken floating-point semantics to improve a benchmark.
- Keep supported JIT, NativeAOT and browser/WASM consumers working. Hardware-specific code needs the existing portable fallback contract; never silently raise the minimum ISA.
- Do not change benchmark work, payload, tracking, entity counts, or timing boundaries to make a production change win. Legitimate benchmark repairs are separate changes applied equally to both sides.
- Use conventional Commitizen-style messages, for example `perf(generator): reduce per-row column access overhead`. Do not publish or merge without authorization.

If an API change becomes necessary, park that experiment. Present the exact proposed signature/contract change, why internal alternatives failed, evidence, expected benefit, compatibility impact, and alternatives. Continue independent internal experiments while awaiting discussion.

## Starting position and priorities

The 2 November full comparison already used .NET 11 RC3. It identifies ordinary scalar traversal, tracked writes, all-changed scans and random Get as weaknesses. Lifecycle operations, unchanged-filter skipping and explicit SIMD movement are strengths to protect. The 4 November dispatch study has an unresolved large-parallel-workload performance result.

There is no need to rerun the complete Bevy suite before starting. Preserve the starting commit and build outputs, then establish fresh, narrow baseline measurements for each experiment. Historical numbers identify priorities; they cannot prove a new improvement against a different binary or host state.

Do not equate the explicit Vector256 movement result with automatic vectorization of arbitrary callbacks. User callbacks may have side effects, cross-row dependencies, aliasing, exceptions and floating-point behavior that prevent transformations. Start by improving generated loop overhead and JIT visibility. Any later vectorization research must prove the exact supported semantics, with unchanged fallback for all other cases.

## Phase 1: Make experiments trustworthy

1. Record starting commit, working-tree changes, SDK/runtime, CPU, OS, power plan and environment variables. Keep the SDK/runtime fixed for the campaign even if repository configuration normally floats. Isolate unrelated user changes.
2. Preserve complete runnable baseline outputs, including dependencies and generated consumer assemblies. A generator experiment requires rebuilding consumers on both sides; replacing only the runtime DLL cannot measure changed generated code. Record binary and source hashes.
3. Reuse scripts/baseline.ps1, scripts/measure.ps1 and scripts/compare-baselines.ps1. Inspect their current behavior before adding tooling. Add only the missing facility for serial, alternating runs of preserved baseline/candidate outputs if needed.
4. Capture the public API from the baseline and compare against each candidate using a metadata-aware API compatibility check. Include generated public APIs from representative consumer fixtures. Recompile unchanged consumers and run the packaged consumer checks. Use comparison of previously compiled consumers where binary compatibility applies. Source compilation alone does not prove an unchanged API.
5. Establish a small correctness and performance guard set per target before changing it. Existing behavioral tests own the contract; add coverage only for a credible gap. Keep timing assertions out of ordinary unit tests.

Existing entry points, from repository root:

```powershell
./scripts/baseline.ps1 -Runs 3 -Classes ReferenceComparisonBenchmarks,TraversalMatrixBenchmarks
./scripts/baseline.ps1 -Runs 3 -Classes FilterBenchmarks,SparseChangeBenchmarks
./scripts/baseline.ps1 -Runs 3 -Classes EntityStorageBenchmarks
./scripts/measure.ps1 -Mode Disassembly -Output artifacts/performance/disassembly
./scripts/measure.ps1 -Mode Matrix -Output artifacts/performance/matrix
./scripts/measure.ps1 -Mode Partition -Runs 3 -RuntimeDefaults -Output artifacts/performance/partition
./scripts/verify.ps1
```

These commands are entry points, not an alternating A/B runner. baseline.ps1 builds the current checkout. measure.ps1's Matrix and Disassembly modes disable tiering/ReadyToRun; Bdn uses a short job. Those are diagnostics/screens, not sufficient acceptance evidence under default tiered PGO. Capture warmed Tier-1/PGO disassembly separately. Partition's BaselineAssembly facility is useful for its supported comparison, not automatically a generic generator comparison.

## Phase 2: Prioritized experiment queue

Each item is a hypothesis to inspect and measure, not a prescribed optimization. Move to another item when evidence contradicts it.

### A. Scalar Each generation and JIT visibility

Owners: src/Archevolt.Generators/SystemGeneration.cs; benchmark families ReferenceComparison, TraversalMatrix and Movement.

Inspect emitted C# and optimized assembly for row bounds checks, callback inlining, repeated field loads, register spills, resource access and loop layout. Compare with the handwritten scalar loop to separate generator overhead from the scalar-versus-vectorized kernel gap.

Try one change at a time: bind reusable state outside the row loop, use a different induction/ref progression, split cold setup from the hot loop, or adjust private helper boundaries. Use targeted inlining decisions justified by assembly, not blanket AggressiveInlining/AggressiveOptimization. Preserve callback invocation semantics and public validation.

Exercise callbacks with different arities, Entity parameters, resources, tracking, branches and nontrivial bodies. A movement-only win that penalizes ordinary consumers is not a general improvement. Do not copy or rewrite arbitrary user method bodies as an unproven shortcut.

### B. Tracked writes and change-filtered iteration

Owners: SystemGeneration.cs, QueryGeneration.cs, src/Archevolt/Queries/QueryState.cs, storage summaries.

The unfiltered Each path already raises the summary once per block. Filtered Each raises it on passing rows. Named-query property access and StampedRef.Value stamp on access. Do not repeat the earlier optimization or replace these distinct semantics with unconditional bulk stamping.

Investigate redundant summary reads, tick/window reloads, per-row branches and inlining budgets. Test private specialization by known query shape. Explore vectorized filter scanning only if profiling supports it and row order, tick-window semantics, exact passing rows and fallback behavior remain intact.

Guard unchanged, all-changed, sparse, Added, combined filters, filtered writes, exceptions mid-loop and partitioned writes. Never stamp unvisited rows ahead of a callback that may throw, or mark a named-query row changed merely because it was enumerated.

### C. Named-query enumeration and optional terms

Owner: QueryGeneration.cs and QueryState.cs.

The current enumerator already advances refs unconditionally, has a local cursor copy at block transitions, and uses zero strides for absent optional columns. Earlier alternatives caused PGO-sensitive regressions. Read the step 2 report first.

Inspect Current/row materialization, unnecessary state, cursor escape, register pressure and optional-term overhead. Vary term count and archetype fragmentation. Any private specialization must preserve Entity access, optional presence checks, ref returns and early exit. Do not remove publicly available row capabilities because one benchmark does not use them.

### D. Entity lookup

Owners: src/Archevolt/World.cs, Entities/Entity.cs, Storage/Tables.cs and Descriptors.cs; EntityStorageBenchmarks.

Trace the full lookup chain before changing it. Integer directory entries and dense component-to-column lookup already landed. Look for remaining duplicate validation, avoidable indirections, generic lookup costs or poorly placed cold failure paths. Keep world, generation, liveness and component-presence checks intact.

Separate cached 1,000-entity cases from random 100,000-entity cases. Include misses, stale/foreign entities and structural moves. Do not accept a favorable outlier from the historically bimodal large Get case.

### E. Dispatch, storage and structural operations

Owners: Execution/BoundSchedule.cs, Execution/WorkerPool.cs, Storage/Tables.cs, World.cs and Commands/Commands.cs.

Pursue only after profiling identifies a concrete cost or A-D plateau. The single-work-item owner path already exists. Resolve measurement stability before attributing the reported large-vector regression to dispatch code. Do not add a universal row-count threshold: callback cost varies widely.

Possible investigations include proven repeated work in schedule execution, contention/false sharing, row movement and command playback. Preserve owner/worker overlap, cancellation, exception aggregation, cleanup and reuse. Protect spawn/batch/despawn and tag/wide round-trip performance. Any storage redesign needs a lifetime and native-memory ownership argument before implementation.

## Phase 3: The hill-climbing loop

Maintain an experiment ledger with ID, parent commit, hypothesis, predicted assembly/runtime effect, changed files, primary cases, guard cases, commands, artifacts, results and verdict. Keep rejected and inconclusive experiments in the record. A simple TSV and short summaries are sufficient; no new orchestration framework is needed.

For every experiment:

1. Predeclare the hypothesis, primary workload family and acceptance gate. Inspect relevant generated source/assembly or profile evidence.
2. Make the smallest coherent internal change. Do not bundle independent tweaks.
3. Run focused correctness, API checks and a cheap timing screen. Reject incorrect or clearly slower candidates early.
4. For a promising candidate, alternate baseline and candidate fresh processes on an otherwise idle host. Start with three launches per side and balance order, such as A-B-B-A-A-B. Use identical workloads and adequate warmup. Never run benchmark jobs concurrently.
5. Report each process result, median, range, absolute time, ratio and allocations. Inspect all processes, not the fastest one. Confirm on an independent second batch; escalate noisy cases to five or more launches per side if useful.
6. Compare against both the previous accepted commit and the original campaign baseline. Confirm improvements still compose before retaining a stack of changes.
7. Keep the candidate only if it passes the gates below. Otherwise remove only that experiment's changes, preserve evidence, and move on.

Default acceptance policy, fixed before viewing candidate results:

- Aim for at least 5% less time in the declared primary workload family in both independent batches. Gains must exceed observed baseline-versus-baseline variation and retain their direction across processes. A smaller gain can qualify only with stronger repeated evidence; it must not be declared a win from noise.
- No reproducible regression above 3% in affected guard cases. Treat smaller measurable regressions as explicit tradeoffs rather than hiding them in an average. If host noise prevents resolving the gate, verdict is INCONCLUSIVE, not accepted. Changes with meaningful cross-workload regressions require user discussion.
- Zero new steady-state managed allocation where the baseline is zero, including worker threads. Record native allocation/retained memory separately; managed allocation counters do not cover it. Reject unbounded caches or growth.
- Correctness and API checks pass. Record code size, generated output growth, build/JIT startup and memory costs when an optimization trades these for throughput. Specialization is not free.
- Default runtime settings are the primary performance gate. Tiering-disabled runs diagnose causes; they do not replace the default-runtime result. Verify NativeAOT compatibility and representative performance when changed codegen could affect it.

Use small/empty inputs, 1,000, 100,000 and 1,000,000 entities as appropriate; one and multiple archetypes; narrow/wide payloads; one/four workers for affected scheduling paths. Include odd counts and table/partition boundaries for ref or SIMD work. Do not exhaustively cross-product every dimension on every trial.

After two unsuccessful variants based on the same premise, stop tweaking it. Write down what the evidence disproved, inspect another layer, and choose a different hypothesis. After three measurement attempts that cannot separate signal from host noise, park that experiment and report the limitation; do not keep running indefinitely.

## Phase 4: Consolidation and handback

Run scripts/verify.ps1 without skipped stages on the retained candidate, the API compatibility checks, relevant allocation matrix and a broader affected benchmark sweep. Resolve failures; list any unavailable platform validation explicitly. Do not infer performance from passing correctness tests.

No full Bevy rerun is required for each trial. Report improvements against the preserved campaign baseline. Label old Bevy figures as historical context; do not divide fresh Archevolt measurements by old Bevy times and call that a fresh comparison. A later matched Bevy run can support a new competitive claim.

Deliver:

- Independently reviewable commits for verified improvements, with conventional messages.
- A report of before/after per workload and runtime, dispersion, allocations, memory/code-size tradeoffs, exact commits/toolchains and reproduction commands.
- Generated-source and optimized-assembly examples explaining why accepted changes help.
- The experiment ledger, including failures and inconclusive cases.
- Evidence that public/runtime-support/generated APIs and unchanged consumers remain compatible.
- Remaining bottlenecks and any parked API proposals, clearly separated from implemented work.

Stop after the prioritized queue has no further supported candidates, or when progress needs a stable host, unavailable platform, or API decision. Do not expand into unrelated features or promise an arbitrary speedup. A plateau report is a valid outcome; an unverified optimization is not.

## Skills to use selectively

The following available skills were inspected for this handoff. Load their current full instructions when applying them, including any referenced compatibility contract; do not assume every installed skill composes automatically.

- `blazing-fast-dotnet` — primary guide for profiling, optimized JIT disassembly, inlining, cache behavior and AOT checks. Apply measurement guidance, not speculative environment knobs. It discourages unsafe code; existing ref/native code still needs project-specific bounds and lifetime reasoning.
- `diagnose` — useful for unexplained regressions or noisy/bimodal cases: isolate a reproduction and test hypotheses. It is not a reason to add a regression test for every harmless loop experiment.
- `test-audit` — apply when adding/changing tests. Protect observable behavior with independent expected results, not snapshots that freeze generated loop spelling.
- `pstack:principle-prove-it-works` — verify actual generated consumers and real workload results.
- `pstack:principle-attack-the-premise` — use when repeated experiments depend on the same failing assumption.
- `pstack:unslop` — keep conclusions and experiment records precise.

`pstack:figure-it-out` and `pstack:show-me-your-work` also fit extended autonomous campaigns, but carry broader workflow/delegation requirements. Their useful ideas here are explicit hypotheses and a durable decision trail. They are optional, not prerequisites; this handoff does not require subagents or a multi-model review. Keep all benchmark execution serial even if separately authorized code investigation is parallelized.
