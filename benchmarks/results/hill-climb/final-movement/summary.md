Baseline: artifacts\campaign\baseline\bench

Candidate: artifacts\campaign\E2b\bench

| Case | Baseline µs [range] | Candidate µs [range] | Candidate/baseline | Baseline spread | Verdict | Allocated B (base/cand) |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000&Concurrency=1)` | 0.25626 [0.24511, 0.30945] | 0.30093 [0.24128, 0.31411] | 1.174 | 25.1 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000&Concurrency=4)` | 18.942 [18.937, 19.004] | 1.1373 [1.0828, 1.2256] | 0.060 | 0.4 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=100000&Concurrency=1)` | 21.923 [21.087, 22.468] | 22.551 [21.411, 23] | 1.029 | 6.3 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=100000&Concurrency=4)` | 32.452 [29.135, 33.895] | 14.544 [12.753, 15.806] | 0.448 | 14.7 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000000&Concurrency=1)` | 292.31 [222.57, 384.53] | 401.07 [321.15, 615.6] | 1.372 | 55.4 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000000&Concurrency=4)` | 198.05 [150.2, 236.65] | 164.03 [162.19, 176.82] | 0.828 | 43.7 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000&Concurrency=1)` | 1.1321 [1.1263, 1.1769] | 1.1307 [1.1245, 1.4066] | 0.999 | 4.5 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000&Concurrency=4)` | 19.436 [19.122, 19.685] | 1.5455 [1.5017, 1.6535] | 0.080 | 2.9 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=100000&Concurrency=1)` | 102.67 [96.394, 111.29] | 102.52 [97.593, 106.1] | 0.999 | 14.5 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=100000&Concurrency=4)` | 74.863 [74.855, 77.626] | 54.966 [53.933, 58.103] | 0.734 | 3.7 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000000&Concurrency=1)` | 1032.8 [1010.2, 1041.8] | 1068.6 [1067.2, 1116.5] | 1.035 | 3.1 % | slower | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000000&Concurrency=4)` | 583.57 [545.44, 596.52] | 605.85 [576.94, 618.01] | 1.038 | 8.8 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedQuery(Count=1000&Concurrency=1)` | 1.0755 [1.05, 1.3328] | 1.0305 [1.0242, 1.05] | 0.958 | 26.3 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedQuery(Count=1000&Concurrency=4)` | 1.0366 [1.0216, 1.2612] | 1.0657 [1.0212, 1.0776] | 1.028 | 23.1 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedQuery(Count=100000&Concurrency=1)` | 85.67 [84.971, 86.282] | 85.001 [84.18, 86.006] | 0.992 | 1.5 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedQuery(Count=100000&Concurrency=4)` | 85.541 [85.056, 85.992] | 86.385 [83.543, 95.042] | 1.010 | 1.1 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedQuery(Count=1000000&Concurrency=1)` | 1021.5 [908.84, 1061.5] | 901.81 [888.2, 966.9] | 0.883 | 14.9 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedQuery(Count=1000000&Concurrency=4)` | 939.76 [870.59, 1012.5] | 905.59 [889.32, 1067.3] | 0.964 | 15.1 % | overlap | 0 / 0 |
