# Archevolt v1 run checklist

Routed to figure-it-out (large, cross-cutting, user reviews after the fact). No native todo tool, so this file is the todolist.

## Session pickup

1. Locate the prior trail.
2. Reconstruct operational state.
3. Diff done vs pending.
4. Route the remaining work to the matching playbook and pick the verdict.
5. Verify the inherited claims against the original goal on the real artifact.

The inherited trail was this checklist, archevolt.tsv, marker-only scaffold logs, and architecture candidates in E:/Code/archevolt-work/arena-contract. Pickup began on feat/archevolt-v1; the active checkout was main at commit time. Codex synthesized the designs and implemented the runtime, generator, and scheduler in local commit 5237bf2. Final Windows/Linux tests, package/JIT/AOT gates, review, documentation, and measurements passed as detailed in acceptance.md. Remaining validation limits are explicit. No push or PR was performed.

## Pickup throughput checkpoint

- [x] Blocking first steps. Recover and synthesize the existing architecture candidates before implementing the runtime contract.
- [x] Independent workstreams. One worker owns scaffold namespace corrections and verification. A read-only worker evaluates the inherited design.
- [x] Shared mutable state. Each writer owns disjoint files. Build and verification jobs run sequentially when they share artifacts.
- [x] Smallest safe decomposition. Implement storage first, then generate against its verified contract, then add schedule behavior. Keep each acceptance gate explicit.

Generated declarations must stay in the namespace of their source declaration. Global namespace stays global. No separate generated namespace.

## Parallel work

1. Frame
2. Fan out
3. Aggregate
4. Report

The coverage split is scaffold packaging, read-only contract synthesis, and the pure schedule planner. The planner has no storage dependencies and owns shared/Scheduling plus its own test file. Scaffold verification runs before planner builds to avoid concurrent writes to build artifacts. Runtime and generator implementation wait for the contract synthesis.

## Architecture

1. Ground
2. Sketch
3. Agree
4. Implement
5. Scrap

Ground and sketch reuse Claude's three candidates. Synthesis must correct unsafe public column access and execution-lease ownership. Agree has no user checkpoint because the request authorizes implementation.

## figure-it-out phases (verbatim)
- [x] Read the Principles section of the poteto-mode skill.
- [x] Phase A: Frame
- [x] Phase B: Design the workflow
- [x] Phase C: Run the loop
- [x] Phase D: Keep the audit trail
- [x] Phase E: Verify and hand back

## Feature playbook steps (verbatim, applied per stage)
1. `how` over the affected subsystem. skip: greenfield repo, PLAN.md is the only system.
2. `architect` for parallel design exploration.
3. Write the throughput checkpoint as four todo items.
4. Delegate code-writing to a subagent.
5. Verify on the matching surface.
6. Rebase into small, ordered commits. Stack follow-ups. Implementation and evidence use separate local Conventional Commits; no shared history rewritten.
7. If the design is contested, `interrogate` before shipping. skip: no unresolved design contest after synthesis and independent review.
8. Run **Opening a PR**. Review/comment/commit checks applied. Remote PR skipped for this local delivery.

## Designed phases (Phase B output)
- [x] P0 Scaffold and verification lever (slnx, CPM, props, pinned SDK, minimal generator, packed consumer, scripts/verify.ps1)
- [x] P1 Architect arena on the runtime/generator contract, synthesize stubs
- [x] P2 Stage 1 world/storage + reference-model trace tests
- [x] P3 Stage 2 generator: components, bundles, queries, [Each], systems, modules (vertical slice 100k entities)
- [x] P4 Stage 3 resources, locals, commands, tracking, messages, removal logs, serial schedules
- [x] P5 Stage 4 shared planner, declarative schedules, conditions, manifests, diagnostics, two-assembly consumer
- [x] P6 Stage 5 owned parallel scheduler + partitioned [Each]
- [x] P7 Stage 6 benchmarks, AOT/trim publish, package tests, docs
- [x] Final: verify implemented behavior end to end, trail audit, attention review, local Conventional Commits. Validation limits are recorded in acceptance.md. No push or PR.
