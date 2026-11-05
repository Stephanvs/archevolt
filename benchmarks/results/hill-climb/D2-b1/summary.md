Baseline: C:\Users\stephanvs\scoop\persist\t3code\user\worktrees\archevolt\t3-eb9e405f\artifacts\campaign\E3\bench

Candidate: C:\Users\stephanvs\scoop\persist\t3code\user\worktrees\archevolt\t3-eb9e405f\artifacts\campaign\E3b\bench

| Case | Baseline µs [range] | Candidate µs [range] | Candidate/baseline | Baseline spread | Verdict | Allocated B (base/cand) |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| `EntityStorageBenchmarks.Get(Count=1000)` | 5.7727 [5.7567, 5.8989] | 5.1207 [5.0254, 5.302] | 0.887 | 2.5 % | faster | 0 / 0 |
| `EntityStorageBenchmarks.Get(Count=100000)` | 1391 [1376, 1406] | 1298.4 [1262.4, 1306] | 0.933 | 2.2 % | faster | 0 / 0 |
