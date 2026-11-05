Baseline: C:\Users\stephanvs\scoop\persist\t3code\user\worktrees\archevolt\t3-eb9e405f\artifacts\campaign\E3b\bench

Candidate: C:\Users\stephanvs\scoop\persist\t3code\user\worktrees\archevolt\t3-eb9e405f\artifacts\campaign\S1\bench

| Case | Baseline µs [range] | Candidate µs [range] | Candidate/baseline | Baseline spread | Verdict | Allocated B (base/cand) |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| `FilterBenchmarks.TrackedWrite(Count=1000)` | 1.1091 [1.0833, 1.1378] | 0.32696 [0.3257, 0.44453] | 0.295 | 4.9 % | faster | 0 / 0 |
| `FilterBenchmarks.TrackedWrite(Count=100000)` | 95.689 [93.091, 99.528] | 35.388 [29.49, 37.264] | 0.370 | 6.7 % | faster | 0 / 0 |
| `FilterBenchmarks.WriteThenChangedAll(Count=1000)` | 2.005 [1.9962, 2.0778] | 1.427 [1.2676, 1.747] | 0.712 | 4.1 % | faster | 0 / 0 |
| `FilterBenchmarks.WriteThenChangedAll(Count=100000)` | 169.71 [167.69, 179.76] | 136.99 [102.98, 141.55] | 0.807 | 7.1 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000&Concurrency=1)` | 0.21197 [0.21037, 0.22945] | 0.22832 [0.21494, 0.32364] | 1.077 | 9.0 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000&Concurrency=4)` | 1.0174 [0.92118, 1.5519] | 0.9544 [0.92431, 1.3545] | 0.938 | 62.0 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=100000&Concurrency=1)` | 21.417 [20.939, 23.317] | 21.722 [21.501, 27.847] | 1.014 | 11.1 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=100000&Concurrency=4)` | 7.3076 [7.2285, 7.4126] | 8.8449 [7.3631, 17.48] | 1.210 | 2.5 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000000&Concurrency=1)` | 439.02 [248.52, 796.12] | 723.81 [295.44, 1942.1] | 1.649 | 124.7 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000000&Concurrency=4)` | 360.37 [90.47, 451.35] | 389.63 [290.8, 829.29] | 1.081 | 100.1 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000&Concurrency=1)` | 1.0994 [1.0652, 1.1322] | 0.30508 [0.28718, 0.31227] | 0.277 | 6.1 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000&Concurrency=4)` | 1.4145 [1.2769, 2.2716] | 1.0886 [0.94049, 1.7001] | 0.770 | 70.3 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=100000&Concurrency=1)` | 97.429 [97.347, 100.92] | 22.035 [21.644, 40.025] | 0.226 | 3.7 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=100000&Concurrency=4)` | 29.313 [27.913, 35.255] | 8.5488 [7.8969, 16.124] | 0.292 | 25.0 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000000&Concurrency=1)` | 1044.8 [1037.9, 1132.5] | 701.34 [285.46, 1527.7] | 0.671 | 9.1 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000000&Concurrency=4)` | 405.69 [280.35, 605.24] | 449.54 [178.66, 1059.3] | 1.108 | 80.1 % | overlap | 0 / 0 |
| `MovementBenchmarks.Handwritten(Count=1000&Concurrency=1)` | 0.85619 [0.8559, 0.8648] | 0.8655 [0.82617, 1.1031] | 1.011 | 1.0 % | overlap | 0 / 0 |
| `MovementBenchmarks.Handwritten(Count=1000&Concurrency=4)` | 0.83174 [0.82397, 0.99108] | 0.88114 [0.83384, 1.1124] | 1.059 | 20.1 % | overlap | 0 / 0 |
| `MovementBenchmarks.Handwritten(Count=100000&Concurrency=1)` | 84.937 [84.186, 88.361] | 89.415 [88.254, 125.77] | 1.053 | 4.9 % | overlap | 0 / 0 |
| `MovementBenchmarks.Handwritten(Count=100000&Concurrency=4)` | 83.439 [83.366, 85.207] | 87.711 [82.74, 120.62] | 1.051 | 2.2 % | overlap | 0 / 0 |
| `MovementBenchmarks.Handwritten(Count=1000000&Concurrency=1)` | 954.83 [909.01, 1115.7] | 1195.9 [1094.4, 2690.7] | 1.252 | 21.7 % | overlap | 0 / 0 |
| `MovementBenchmarks.Handwritten(Count=1000000&Concurrency=4)` | 964.41 [960.09, 992.18] | 1429.4 [921.47, 1964.9] | 1.482 | 3.3 % | overlap | 0 / 0 |
