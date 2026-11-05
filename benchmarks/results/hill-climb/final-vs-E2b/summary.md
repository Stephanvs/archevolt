Baseline: artifacts\campaign\E2b\bench

Candidate: artifacts\campaign\final\bench

GeneratedEach, 1000 rows, concurrency 4, 2000 runs per process

| Gap | Side | Median µs [range] | Cores busy [range] | Interleaved median µs | Pair median µs |
| --- | --- | ---: | ---: | ---: | ---: |
| none | baseline | 1.30 [1.20, 1.30] | 0.000 [0.000, 0.000] | 0.00 | 1.30 |
| none | candidate | 1.30 [1.20, 1.30] | 0.000 [0.000, 0.000] | 0.00 | 1.30 |
| sleep:2 | baseline | 30.80 [30.30, 30.80] | 0.015 [0.009, 0.015] | 0.00 | 30.80 |
| sleep:2 | candidate | 31.40 [30.60, 31.40] | 0.018 [0.011, 0.018] | 0.00 | 31.40 |
