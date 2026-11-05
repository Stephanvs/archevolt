Baseline: artifacts\campaign\baseline\bench

Candidate: artifacts\campaign\baseline\bench

| Case | Baseline µs [range] | Candidate µs [range] | Candidate/baseline | Baseline spread | Verdict | Allocated B (base/cand) |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000&Concurrency=1)` | 0.25664 [0.23582, 0.27747] | 0.23961 [0.23418, 0.25523] | 0.934 | 16.2 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000&Concurrency=4)` | 19.363 [17.522, 25.246] | 20.812 [19.525, 21.929] | 1.075 | 39.9 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=100000&Concurrency=1)` | 22.65 [22.464, 22.829] | 21.554 [21.192, 21.792] | 0.952 | 1.6 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=100000&Concurrency=4)` | 36.118 [33.316, 36.899] | 33.137 [32.98, 35.043] | 0.917 | 9.9 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000000&Concurrency=1)` | 399.72 [283.35, 420.57] | 289.38 [212.35, 316.49] | 0.724 | 34.3 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000000&Concurrency=4)` | 352.24 [263.35, 383.49] | 280.19 [218.9, 422.81] | 0.795 | 34.1 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000&Concurrency=1)` | 1.1665 [1.1145, 1.1678] | 1.1203 [1.0895, 1.2054] | 0.960 | 4.6 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000&Concurrency=4)` | 21.971 [20.257, 24.748] | 21.145 [20.289, 23.794] | 0.962 | 20.4 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=100000&Concurrency=1)` | 100.96 [95.49, 103.18] | 99.972 [98.946, 105.63] | 0.990 | 7.6 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=100000&Concurrency=4)` | 92.394 [77.517, 93.308] | 85.064 [78.855, 88.611] | 0.921 | 17.1 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000000&Concurrency=1)` | 1082.3 [1011.4, 1515.3] | 1149.4 [1025.5, 1156.3] | 1.062 | 46.6 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000000&Concurrency=4)` | 679.12 [619.19, 686.65] | 681.68 [679.78, 743.01] | 1.004 | 9.9 % | overlap | 0 / 0 |
