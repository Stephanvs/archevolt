Baseline: artifacts\campaign\baseline\bench

Candidate: artifacts\campaign\E1a\bench

| Case | Baseline µs [range] | Candidate µs [range] | Candidate/baseline | Baseline spread | Verdict | Allocated B (base/cand) |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000&Concurrency=1)` | 0.22467 [0.22338, 0.25147] | 0.22861 [0.21765, 0.43022] | 1.018 | 12.5 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000&Concurrency=4)` | 13.986 [12.99, 23.893] | 14.576 [13.529, 21.328] | 1.042 | 78.0 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=100000&Concurrency=1)` | 21.056 [20.949, 21.402] | 21.124 [20.959, 26.771] | 1.003 | 2.1 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=100000&Concurrency=4)` | 28.46 [21.618, 31.78] | 26.133 [24.135, 38.25] | 0.918 | 35.7 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000000&Concurrency=1)` | 269.27 [268.78, 483.38] | 283.61 [271.06, 346.79] | 1.053 | 79.7 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000000&Concurrency=4)` | 173.61 [89.862, 213.84] | 100.05 [90.097, 202.94] | 0.576 | 71.4 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000&Concurrency=1)` | 1.0803 [1.0797, 1.21] | 1.1353 [1.0571, 1.4783] | 1.051 | 12.1 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000&Concurrency=4)` | 15.402 [13.344, 21.734] | 14.638 [14.001, 26.819] | 0.950 | 54.5 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=100000&Concurrency=1)` | 95.834 [95.741, 105.98] | 97.081 [95.716, 129.42] | 1.013 | 10.7 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=100000&Concurrency=4)` | 49.005 [44.708, 76.891] | 72.912 [45.752, 76.801] | 1.488 | 65.7 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000000&Concurrency=1)` | 1069.7 [1030.8, 1096.1] | 1072.5 [1053.5, 1146.5] | 1.003 | 6.1 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000000&Concurrency=4)` | 480.88 [344.91, 613.24] | 369.21 [321.46, 562.24] | 0.768 | 55.8 % | overlap | 0 / 0 |
