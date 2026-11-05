Baseline: artifacts\campaign\E2\bench

Candidate: artifacts\campaign\D1\bench

| Case | Baseline µs [range] | Candidate µs [range] | Candidate/baseline | Baseline spread | Verdict | Allocated B (base/cand) |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| `EntityStorageBenchmarks.Get(Count=1000)` | 6.4694 [6.3027, 7.0347] | 6.7457 [6.708, 7.5925] | 1.043 | 11.3 % | overlap | 0 / 0 |
| `EntityStorageBenchmarks.Get(Count=100000)` | 1507.5 [1423.1, 1586.9] | 1518.2 [1498.6, 1586.2] | 1.007 | 10.9 % | overlap | 0 / 0 |
