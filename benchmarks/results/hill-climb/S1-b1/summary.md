Baseline: C:\Users\stephanvs\scoop\persist\t3code\user\worktrees\archevolt\t3-eb9e405f\artifacts\campaign\E3b\bench

Candidate: C:\Users\stephanvs\scoop\persist\t3code\user\worktrees\archevolt\t3-eb9e405f\artifacts\campaign\S1\bench

| Case | Baseline µs [range] | Candidate µs [range] | Candidate/baseline | Baseline spread | Verdict | Allocated B (base/cand) |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| `FilterBenchmarks.TrackedWrite(Count=1000)` | 1.1362 [1.0844, 1.1799] | 0.45692 [0.38256, 0.48567] | 0.402 | 8.4 % | faster | 0 / 0 |
| `FilterBenchmarks.TrackedWrite(Count=100000)` | 94.199 [93.463, 95.393] | 31.016 [29.936, 32.025] | 0.329 | 2.0 % | faster | 0 / 0 |
| `FilterBenchmarks.WriteThenChangedAll(Count=1000)` | 2.0357 [1.9946, 2.486] | 1.58 [1.3109, 1.5829] | 0.776 | 24.1 % | faster | 0 / 0 |
| `FilterBenchmarks.WriteThenChangedAll(Count=100000)` | 166.96 [164.55, 170.68] | 126.12 [103.12, 130.43] | 0.755 | 3.7 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000&Concurrency=1)` | 0.23237 [0.20465, 0.23452] | 0.27208 [0.20822, 0.34101] | 1.171 | 12.9 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000&Concurrency=4)` | 0.93712 [0.91586, 0.98828] | 1.0051 [0.89089, 1.1206] | 1.073 | 7.7 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=100000&Concurrency=1)` | 21.303 [21.029, 21.773] | 21.893 [20.966, 21.896] | 1.028 | 3.5 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=100000&Concurrency=4)` | 7.8561 [7.2038, 9.0455] | 11.811 [7.7414, 14.821] | 1.503 | 23.4 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000000&Concurrency=1)` | 384.84 [312.57, 433.04] | 270.86 [220.4, 372.69] | 0.704 | 31.3 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000000&Concurrency=4)` | 222.68 [123.91, 814.21] | 112 [66.123, 131.31] | 0.503 | 310.0 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000&Concurrency=1)` | 1.0803 [1.0548, 1.2044] | 0.23722 [0.21365, 0.24843] | 0.220 | 13.8 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000&Concurrency=4)` | 1.2125 [1.1976, 1.2827] | 0.92347 [0.91844, 1.3015] | 0.762 | 7.0 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=100000&Concurrency=1)` | 96.188 [94.623, 97.566] | 21.411 [20.944, 22.16] | 0.223 | 3.1 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=100000&Concurrency=4)` | 30.106 [27.716, 61.794] | 11.963 [7.6839, 14.535] | 0.397 | 113.2 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000000&Concurrency=1)` | 1036.6 [1024.7, 1121.3] | 325.49 [233.14, 356.25] | 0.314 | 9.3 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000000&Concurrency=4)` | 468.18 [342.22, 594.09] | 70.92 [66.583, 114.69] | 0.151 | 53.8 % | faster | 0 / 0 |
| `MovementBenchmarks.Handwritten(Count=1000&Concurrency=1)` | 0.88602 [0.82941, 0.99913] | 1.01 [0.98375, 1.2128] | 1.140 | 19.2 % | overlap | 0 / 0 |
| `MovementBenchmarks.Handwritten(Count=1000&Concurrency=4)` | 0.83452 [0.82008, 0.87194] | 1.0394 [0.8171, 1.1046] | 1.246 | 6.2 % | overlap | 0 / 0 |
| `MovementBenchmarks.Handwritten(Count=100000&Concurrency=1)` | 81.714 [81.046, 81.917] | 94.138 [83.362, 97.804] | 1.152 | 1.1 % | slower | 0 / 0 |
| `MovementBenchmarks.Handwritten(Count=100000&Concurrency=4)` | 84.413 [82.747, 91.417] | 87.317 [82.272, 87.967] | 1.034 | 10.3 % | overlap | 0 / 0 |
| `MovementBenchmarks.Handwritten(Count=1000000&Concurrency=1)` | 999.76 [966.12, 1042.8] | 970.19 [913.75, 1052.9] | 0.970 | 7.7 % | overlap | 0 / 0 |
| `MovementBenchmarks.Handwritten(Count=1000000&Concurrency=4)` | 1022.1 [1015.6, 1180.6] | 955.66 [909.91, 1009.4] | 0.935 | 16.1 % | faster | 0 / 0 |
