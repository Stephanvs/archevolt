Baseline: artifacts\campaign\baseline\bench

Candidate: artifacts\campaign\E2\bench

| Case | Baseline µs [range] | Candidate µs [range] | Candidate/baseline | Baseline spread | Verdict | Allocated B (base/cand) |
| --- | ---: | ---: | ---: | ---: | --- | ---: |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000&Concurrency=1)` | 0.24295 [0.24082, 0.49787] | 0.2416 [0.24086, 0.29625] | 0.994 | 105.8 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000&Concurrency=4)` | 19.835 [19.227, 21.12] | 1.0794 [1.0425, 1.329] | 0.054 | 9.5 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=100000&Concurrency=1)` | 22.523 [21.677, 25.151] | 20.944 [20.475, 22.701] | 0.930 | 15.4 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=100000&Concurrency=4)` | 34.801 [29.401, 35.486] | 7.9753 [7.8507, 16.399] | 0.229 | 17.5 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000000&Concurrency=1)` | 310.22 [232.5, 313.94] | 477.47 [301.5, 527.76] | 1.539 | 26.3 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedBlockVector(Count=1000000&Concurrency=4)` | 160.23 [150.64, 191.7] | 362.06 [118.22, 397.37] | 2.260 | 25.6 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000&Concurrency=1)` | 1.3237 [1.1179, 1.4016] | 1.1096 [1.067, 1.1219] | 0.838 | 21.4 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000&Concurrency=4)` | 20.77 [19.17, 27.218] | 1.2681 [1.2643, 1.7818] | 0.061 | 38.8 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=100000&Concurrency=1)` | 108.95 [99.84, 114.03] | 95.849 [94.788, 101.15] | 0.880 | 13.0 % | overlap | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=100000&Concurrency=4)` | 81.408 [77.177, 87.895] | 27.948 [27.207, 59.379] | 0.343 | 13.2 % | faster | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000000&Concurrency=1)` | 1074 [984.37, 1091.4] | 1154.2 [1115.7, 1182.5] | 1.075 | 10.0 % | slower | 0 / 0 |
| `MovementBenchmarks.GeneratedEach(Count=1000000&Concurrency=4)` | 551.61 [543.05, 552.68] | 369.13 [327.8, 652.58] | 0.669 | 1.7 % | overlap | 0 / 0 |
