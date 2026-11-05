Baseline: C:\Users\stephanvs\scoop\persist\t3code\user\worktrees\archevolt\t3-eb9e405f\artifacts\campaign\E3\bench

Candidate: C:\Users\stephanvs\scoop\persist\t3code\user\worktrees\archevolt\t3-eb9e405f\artifacts\campaign\E3b\bench

| Case | Baseline µs [range] | Candidate µs [range] | Candidate/baseline | Baseline spread | Verdict | Allocated B (base/cand) |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| `EntityStorageBenchmarks.Get(Count=1000)` | 5.8689 [5.8391, 5.921] | 5.3076 [5.1094, 5.5947] | 0.904 | 1.4 % | faster | 0 / 0 |
| `EntityStorageBenchmarks.Get(Count=100000)` | 1412.8 [1403.9, 2553.2] | 1301.2 [1299.5, 1333.9] | 0.921 | 81.4 % | faster | 0 / 0 |
