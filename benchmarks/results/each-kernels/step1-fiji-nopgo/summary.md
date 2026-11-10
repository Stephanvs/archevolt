| Case | Mode | Baseline ns/row | Candidate ns/row | Ratio | Ranges |
| --- | --- | --- | --- | ---: | --- |
| block-wrap | populated | 1.284 [1.149, 1.294] | 1.274 [1.205, 1.327] | 0.99 | overlap |
| each-bounce | populated | 2.306 [2.281, 2.332] | 1.486 [1.484, 1.492] | 0.64 | faster |
| each-clamp | populated | 1.563 [1.450, 1.604] | 0.667 [0.649, 0.706] | 0.43 | faster |
| each-delegating | populated | 4.901 [4.606, 4.954] | 4.870 [4.718, 5.235] | 0.99 | overlap |
| each-expire | populated | 2.095 [1.947, 2.130] | 1.881 [1.834, 1.987] | 0.90 | overlap |
| each-length | populated | 1.579 [1.472, 1.624] | 0.791 [0.775, 0.837] | 0.50 | faster |
| each-plain | populated | 0.322 [0.300, 0.331] | 0.310 [0.303, 0.338] | 0.96 | overlap |
| each-spin | populated | 0.656 [0.620, 0.667] | 0.986 [0.986, 1.036] | 1.50 | slower |
| each-wrap | populated | 4.927 [4.632, 4.959] | 1.544 [1.510, 1.619] | 0.31 | faster |
| each-wrap-helper | populated | 4.909 [4.607, 4.933] | 1.603 [1.565, 1.694] | 0.33 | faster |
| query-wrap | populated | 1.383 [1.305, 1.399] | 1.315 [1.312, 1.463] | 0.95 | overlap |
| vector-spin | populated | 0.280 [0.264, 0.281] | 0.280 [0.265, 0.298] | 1.00 | overlap |
| vector-wrap | populated | 0.377 [0.348, 0.388] | 0.362 [0.356, 0.398] | 0.96 | overlap |
