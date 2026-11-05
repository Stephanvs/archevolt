Baseline: artifacts\campaign\baseline\bench

Candidate: artifacts\campaign\E2\bench

| Case | Baseline µs [range] | Candidate µs [range] | Candidate/baseline | Baseline spread | Verdict | Allocated B (base/cand) |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000&Concurrency=1)` | 0.36486 [0.33208, 0.46896] | 0.32624 [0.32467, 0.36111] | 0.894 | 37.5 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000&Concurrency=4)` | 20.607 [20.337, 25.43] | 1.4431 [1.2115, 1.5723] | 0.070 | 24.7 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=100000&Concurrency=1)` | 24.709 [21.354, 35.823] | 23.882 [22.886, 24.017] | 0.967 | 58.6 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=100000&Concurrency=4)` | 38.541 [25.33, 42.13] | 15.233 [12.55, 17.508] | 0.395 | 43.6 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000000&Concurrency=1)` | 580.69 [522.64, 614.97] | 447.06 [330.13, 838.5] | 0.770 | 15.9 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000000&Concurrency=4)` | 328.02 [281.65, 621.76] | 331.18 [146.03, 429.44] | 1.010 | 103.7 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000&Concurrency=1)` | 1.4856 [1.1268, 1.8255] | 1.2797 [1.2203, 1.3643] | 0.861 | 47.0 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000&Concurrency=4)` | 21.836 [21.238, 24.299] | 1.5762 [1.5452, 2.318] | 0.072 | 14.0 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=100000&Concurrency=1)` | 136.6 [96.613, 140.42] | 120.55 [114.79, 120.88] | 0.882 | 32.1 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=100000&Concurrency=4)` | 78.403 [76.829, 86.623] | 54.006 [52.786, 62.805] | 0.689 | 12.5 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000000&Concurrency=1)` | 1406.4 [1076.6, 1561.3] | 1340.8 [1041.2, 1605.8] | 0.953 | 34.5 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000000&Concurrency=4)` | 769.95 [670.28, 791.7] | 620.73 [572.61, 639.69] | 0.806 | 15.8 % | faster | 0 / 0 |
