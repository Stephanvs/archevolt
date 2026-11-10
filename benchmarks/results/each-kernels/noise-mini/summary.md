| Case | Mode | Baseline ns/row | Candidate ns/row | Ratio | Ranges |
| --- | --- | --- | --- | ---: | --- |
| block-wrap | empty | 0.599 [0.594, 0.601] | 0.591 [0.587, 0.591] | 0.99 | faster |
| block-wrap | populated | 0.619 [0.587, 0.787] | 0.588 [0.588, 0.622] | 0.95 | overlap |
| each-bounce | empty | 1.490 [1.484, 1.494] | 1.474 [1.467, 1.482] | 0.99 | faster |
| each-bounce | populated | 0.787 [0.782, 0.789] | 0.782 [0.677, 0.783] | 0.99 | overlap |
| each-clamp | empty | 1.181 [1.181, 1.181] | 1.178 [1.172, 1.187] | 1.00 | overlap |
| each-clamp | populated | 0.393 [0.392, 0.395] | 0.390 [0.390, 0.392] | 0.99 | overlap |
| each-delegating | empty | 2.649 [2.633, 2.655] | 2.605 [2.554, 2.640] | 0.98 | overlap |
| each-delegating | populated | 0.591 [0.587, 0.594] | 0.590 [0.588, 0.590] | 1.00 | overlap |
| each-expire | empty | 6.935 [6.885, 6.939] | 6.881 [6.846, 6.886] | 0.99 | overlap |
| each-expire | populated | 1.447 [1.376, 1.619] | 1.448 [1.447, 1.460] | 1.00 | overlap |
| each-length | empty | 0.994 [0.993, 0.995] | 0.988 [0.983, 0.990] | 0.99 | faster |
| each-length | populated | 0.887 [0.883, 0.890] | 0.884 [0.883, 0.890] | 1.00 | overlap |
| each-plain | empty | 0.115 [0.115, 0.115] | 0.114 [0.114, 0.115] | 0.99 | overlap |
| each-plain | populated | 0.113 [0.112, 0.115] | 0.114 [0.113, 0.114] | 1.01 | overlap |
| each-spin | empty | 2.177 [2.168, 2.179] | 2.159 [2.147, 2.165] | 0.99 | faster |
| each-spin | populated | 0.394 [0.393, 0.395] | 0.391 [0.390, 0.396] | 0.99 | overlap |
| each-wrap | empty | 2.649 [2.642, 2.654] | 2.606 [2.555, 2.646] | 0.98 | overlap |
| each-wrap | populated | 0.591 [0.589, 0.594] | 0.589 [0.588, 0.589] | 1.00 | faster |
| each-wrap-helper | empty | 2.629 [2.618, 2.652] | 2.604 [2.591, 2.617] | 0.99 | faster |
| each-wrap-helper | populated | 0.545 [0.544, 0.589] | 0.542 [0.541, 0.543] | 0.99 | faster |
| query-wrap | empty | 1.188 [1.186, 1.210] | 1.179 [1.173, 1.197] | 0.99 | overlap |
| query-wrap | populated | 0.779 [0.773, 0.781] | 0.773 [0.772, 0.776] | 0.99 | overlap |
| vector-spin | empty | 0.108 [0.107, 0.108] | 0.108 [0.107, 0.108] | 1.00 | overlap |
| vector-spin | populated | 0.107 [0.106, 0.107] | 0.106 [0.106, 0.107] | 0.99 | overlap |
| vector-wrap | empty | 0.114 [0.113, 0.114] | 0.113 [0.113, 0.114] | 0.99 | overlap |
| vector-wrap | populated | 0.112 [0.111, 0.114] | 0.113 [0.112, 0.113] | 1.00 | overlap |
