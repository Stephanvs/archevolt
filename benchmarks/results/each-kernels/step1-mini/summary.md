| Case | Mode | Baseline ns/row | Candidate ns/row | Ratio | Ranges |
| --- | --- | --- | --- | ---: | --- |
| block-wrap | empty | 0.595 [0.589, 0.596] | 0.589 [0.588, 0.600] | 0.99 | overlap |
| block-wrap | populated | 0.591 [0.589, 0.594] | 0.613 [0.597, 0.784] | 1.04 | slower |
| each-bounce | empty | 1.482 [1.477, 1.492] | 0.677 [0.677, 0.684] | 0.46 | faster |
| each-bounce | populated | 0.680 [0.677, 0.683] | 0.682 [0.678, 0.684] | 1.00 | overlap |
| each-clamp | empty | 1.179 [1.178, 1.184] | 0.393 [0.390, 0.395] | 0.33 | faster |
| each-clamp | populated | 0.392 [0.390, 0.392] | 0.391 [0.390, 0.392] | 1.00 | overlap |
| each-delegating | empty | 2.594 [2.580, 2.597] | 0.591 [0.588, 0.594] | 0.23 | faster |
| each-delegating | populated | 0.591 [0.588, 0.592] | 0.589 [0.588, 0.591] | 1.00 | overlap |
| each-expire | empty | 6.889 [6.886, 6.931] | 0.919 [0.912, 0.950] | 0.13 | faster |
| each-expire | populated | 1.460 [1.449, 1.464] | 0.952 [0.917, 0.954] | 0.65 | faster |
| each-length | empty | 0.992 [0.989, 1.000] | 0.889 [0.883, 0.890] | 0.90 | faster |
| each-length | populated | 0.887 [0.884, 0.895] | 0.886 [0.882, 0.887] | 1.00 | overlap |
| each-plain | empty | 0.115 [0.113, 0.116] | 0.114 [0.114, 0.115] | 1.00 | overlap |
| each-plain | populated | 0.113 [0.112, 0.113] | 0.115 [0.115, 0.116] | 1.02 | slower |
| each-spin | empty | 2.162 [2.160, 2.179] | 0.394 [0.392, 0.394] | 0.18 | faster |
| each-spin | populated | 0.393 [0.392, 0.393] | 0.394 [0.393, 0.395] | 1.00 | overlap |
| each-wrap | empty | 2.588 [2.580, 2.596] | 0.590 [0.590, 0.594] | 0.23 | faster |
| each-wrap | populated | 0.591 [0.588, 0.591] | 0.590 [0.589, 0.590] | 1.00 | overlap |
| each-wrap-helper | empty | 2.620 [2.605, 2.631] | 0.543 [0.540, 0.545] | 0.21 | faster |
| each-wrap-helper | populated | 0.542 [0.541, 0.544] | 0.545 [0.542, 0.546] | 1.01 | overlap |
| query-wrap | empty | 1.183 [1.179, 1.192] | 1.175 [1.174, 1.198] | 0.99 | overlap |
| query-wrap | populated | 0.778 [0.771, 0.778] | 0.773 [0.746, 0.779] | 0.99 | overlap |
| vector-spin | empty | 0.107 [0.107, 0.108] | 0.107 [0.107, 0.107] | 1.00 | overlap |
| vector-spin | populated | 0.107 [0.106, 0.107] | 0.107 [0.107, 0.108] | 1.01 | overlap |
| vector-wrap | empty | 0.113 [0.112, 0.115] | 0.114 [0.113, 0.114] | 1.00 | overlap |
| vector-wrap | populated | 0.112 [0.112, 0.112] | 0.113 [0.113, 0.114] | 1.01 | slower |
