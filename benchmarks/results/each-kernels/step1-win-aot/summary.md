| Case | Mode | Baseline ns/row | Candidate ns/row | Ratio | Ranges |
| --- | --- | --- | --- | ---: | --- |
| block-wrap | populated | 1.270 [0.925, 1.351] | 0.927 [0.919, 1.371] | 0.73 | overlap |
| each-bounce | populated | 2.714 [2.097, 2.796] | 1.222 [1.213, 1.859] | 0.45 | faster |
| each-clamp | populated | 1.964 [1.608, 2.038] | 0.506 [0.502, 0.730] | 0.26 | faster |
| each-delegating | populated | 3.663 [2.579, 3.708] | 2.417 [2.323, 3.615] | 0.66 | overlap |
| each-expire | populated | 1.847 [1.178, 1.975] | 1.020 [0.984, 1.574] | 0.55 | overlap |
| each-length | populated | 2.109 [1.613, 2.234] | 1.145 [1.144, 1.150] | 0.54 | faster |
| each-plain | populated | 0.267 [0.218, 0.281] | 0.218 [0.214, 0.280] | 0.82 | overlap |
| each-spin | populated | 0.668 [0.463, 0.672] | 0.541 [0.507, 1.000] | 0.81 | overlap |
| each-wrap | populated | 3.679 [2.578, 4.074] | 0.974 [0.935, 1.746] | 0.26 | faster |
| each-wrap-helper | populated | 3.479 [2.605, 3.696] | 0.918 [0.915, 1.381] | 0.26 | faster |
| query-wrap | populated | 1.498 [1.144, 1.617] | 1.151 [1.150, 1.524] | 0.77 | overlap |
| vector-spin | populated | 0.489 [0.242, 0.495] | 0.251 [0.234, 0.493] | 0.51 | overlap |
| vector-wrap | populated | 0.472 [0.270, 0.492] | 0.276 [0.259, 0.494] | 0.58 | overlap |
