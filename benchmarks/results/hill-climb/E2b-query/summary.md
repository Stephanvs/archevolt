Baseline: artifacts\campaign\baseline\bench

Candidate: artifacts\campaign\E2b\bench

| Case | Baseline µs [range] | Candidate µs [range] | Candidate/baseline | Baseline spread | Verdict | Allocated B (base/cand) |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| `MovementBenchmarks.GeneratedQuery(Count=1000&Concurrency=1)` | 1.0314 [1.0125, 1.0938] | 1.006 [0.99982, 1.0432] | 0.975 | 7.9 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedQuery(Count=1000&Concurrency=4)` | 1.0714 [1.0507, 1.1555] | 1.0188 [1.005, 1.0292] | 0.951 | 9.8 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedQuery(Count=100000&Concurrency=1)` | 86.761 [86.409, 92.004] | 89.143 [84.421, 95.105] | 1.027 | 6.4 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedQuery(Count=100000&Concurrency=4)` | 88.228 [87.258, 91.094] | 89.94 [84.194, 105.04] | 1.019 | 4.3 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedQuery(Count=1000000&Concurrency=1)` | 905.63 [858.73, 1014.4] | 1078.4 [862.4, 1092.9] | 1.191 | 17.2 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedQuery(Count=1000000&Concurrency=4)` | 883.42 [837.1, 958.75] | 891.99 [865.36, 1016.6] | 1.010 | 13.8 % | overlap | 0 / 0 |
