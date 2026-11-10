| Case | Mode | Baseline ns/row | Candidate ns/row | Ratio | Ranges |
| --- | --- | --- | --- | ---: | --- |
| block-wrap | empty | 1.194 [1.169, 1.216] | 1.223 [1.194, 1.224] | 1.02 | overlap |
| block-wrap | populated | 1.081 [1.081, 1.107] | 1.103 [1.077, 1.135] | 1.02 | overlap |
| each-bounce | empty | 4.359 [4.201, 4.429] | 2.119 [2.067, 2.180] | 0.49 | faster |
| each-bounce | populated | 1.402 [1.337, 1.724] | 1.919 [1.724, 2.030] | 1.37 | slower |
| each-clamp | empty | 2.326 [2.251, 2.360] | 0.697 [0.682, 0.708] | 0.30 | faster |
| each-clamp | populated | 0.648 [0.632, 0.665] | 0.633 [0.630, 0.663] | 0.98 | overlap |
| each-delegating | empty | 6.100 [5.871, 6.104] | 1.337 [1.305, 1.644] | 0.22 | faster |
| each-delegating | populated | 1.481 [1.450, 1.510] | 1.483 [1.214, 1.544] | 1.00 | overlap |
| each-expire | empty | 15.049 [14.446, 15.079] | 1.764 [1.717, 2.079] | 0.12 | faster |
| each-expire | populated | 3.150 [3.035, 3.215] | 1.702 [1.633, 1.827] | 0.54 | faster |
| each-length | empty | 2.616 [2.513, 2.636] | 0.812 [0.795, 0.833] | 0.31 | faster |
| each-length | populated | 0.754 [0.735, 0.775] | 0.738 [0.735, 0.776] | 0.98 | overlap |
| each-plain | empty | 0.321 [0.311, 0.328] | 0.328 [0.328, 0.331] | 1.02 | overlap |
| each-plain | populated | 0.298 [0.296, 0.299] | 0.295 [0.288, 0.312] | 0.99 | overlap |
| each-spin | empty | 4.505 [4.387, 4.574] | 1.063 [1.036, 1.091] | 0.24 | faster |
| each-spin | populated | 0.649 [0.635, 0.962] | 0.979 [0.961, 1.007] | 1.51 | overlap |
| each-wrap | empty | 5.939 [5.814, 6.045] | 1.357 [1.342, 1.658] | 0.23 | faster |
| each-wrap | populated | 1.453 [1.450, 1.476] | 1.483 [1.214, 1.567] | 1.02 | overlap |
| each-wrap-helper | empty | 6.015 [5.799, 6.081] | 1.105 [1.079, 1.135] | 0.18 | faster |
| each-wrap-helper | populated | 1.452 [1.449, 1.506] | 1.000 [0.996, 1.046] | 0.69 | faster |
| query-wrap | empty | 2.154 [2.061, 2.178] | 2.147 [2.119, 2.190] | 1.00 | overlap |
| query-wrap | populated | 1.688 [1.685, 1.726] | 1.726 [1.684, 1.785] | 1.02 | overlap |
| vector-spin | empty | 0.353 [0.334, 0.355] | 0.350 [0.342, 0.352] | 0.99 | overlap |
| vector-spin | populated | 0.316 [0.309, 0.317] | 0.319 [0.307, 0.328] | 1.01 | overlap |
| vector-wrap | empty | 0.376 [0.363, 0.384] | 0.387 [0.383, 0.393] | 1.03 | overlap |
| vector-wrap | populated | 0.349 [0.349, 0.351] | 0.344 [0.336, 0.367] | 0.99 | overlap |
