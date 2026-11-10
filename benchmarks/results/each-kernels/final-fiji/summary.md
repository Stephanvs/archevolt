| Case | Mode | Baseline ns/row | Candidate ns/row | Ratio | Ranges |
| --- | --- | --- | --- | ---: | --- |
| block-wrap | empty | 1.224 [1.195, 1.242] | 1.227 [1.224, 1.263] | 1.00 | overlap |
| block-wrap | populated | 1.099 [1.080, 1.111] | 1.119 [1.077, 1.174] | 1.02 | overlap |
| each-bounce | empty | 4.445 [4.317, 4.553] | 0.388 [0.376, 0.401] | 0.09 | faster |
| each-bounce | populated | 1.772 [1.683, 1.813] | 0.348 [0.332, 0.368] | 0.20 | faster |
| each-clamp | empty | 2.355 [2.297, 2.409] | 0.456 [0.455, 0.469] | 0.19 | faster |
| each-clamp | populated | 0.664 [0.632, 0.678] | 0.416 [0.397, 0.431] | 0.63 | faster |
| each-delegating | empty | 6.194 [6.072, 6.264] | 0.397 [0.382, 0.410] | 0.06 | faster |
| each-delegating | populated | 1.523 [1.520, 1.685] | 0.352 [0.338, 0.384] | 0.23 | faster |
| each-expire | empty | 15.246 [14.928, 15.540] | 1.839 [1.719, 2.032] | 0.12 | faster |
| each-expire | populated | 3.468 [3.245, 3.488] | 1.785 [1.756, 1.950] | 0.51 | faster |
| each-length | empty | 2.688 [2.568, 2.713] | 0.827 [0.824, 0.852] | 0.31 | faster |
| each-length | populated | 0.773 [0.735, 0.793] | 0.759 [0.737, 0.809] | 0.98 | overlap |
| each-plain | empty | 0.332 [0.329, 0.339] | 0.338 [0.328, 0.348] | 1.02 | overlap |
| each-plain | populated | 0.296 [0.295, 0.303] | 0.302 [0.288, 0.327] | 1.02 | overlap |
| each-spin | empty | 4.652 [4.504, 4.719] | 0.354 [0.352, 0.364] | 0.08 | faster |
| each-spin | populated | 0.649 [0.619, 0.651] | 0.325 [0.307, 0.332] | 0.50 | faster |
| each-wrap | empty | 6.089 [5.914, 6.220] | 0.398 [0.383, 0.413] | 0.07 | faster |
| each-wrap | populated | 1.484 [1.450, 1.521] | 0.355 [0.338, 0.382] | 0.24 | faster |
| each-wrap-helper | empty | 6.209 [5.961, 6.259] | 0.398 [0.384, 0.410] | 0.06 | faster |
| each-wrap-helper | populated | 1.507 [1.449, 1.520] | 0.352 [0.338, 0.386] | 0.23 | faster |
| query-wrap | empty | 2.197 [2.140, 2.225] | 2.159 [2.157, 2.224] | 0.98 | overlap |
| query-wrap | populated | 1.725 [1.685, 1.749] | 1.743 [1.738, 1.776] | 1.01 | overlap |
| vector-spin | empty | 0.359 [0.345, 0.363] | 0.352 [0.350, 0.363] | 0.98 | overlap |
| vector-spin | populated | 0.322 [0.310, 0.326] | 0.323 [0.310, 0.342] | 1.00 | overlap |
| vector-wrap | empty | 0.393 [0.388, 0.402] | 0.398 [0.385, 0.410] | 1.01 | overlap |
| vector-wrap | populated | 0.347 [0.341, 0.355] | 0.354 [0.338, 0.387] | 1.02 | overlap |
