| Case | Mode | Baseline ns/row | Candidate ns/row | Ratio | Ranges |
| --- | --- | --- | --- | ---: | --- |
| block-wrap | empty | 0.591 [0.591, 0.592] | 0.591 [0.590, 0.593] | 1.00 | overlap |
| block-wrap | populated | 0.589 [0.589, 0.591] | 0.593 [0.591, 0.608] | 1.01 | overlap |
| each-bounce | empty | 0.116 [0.116, 0.117] | 0.114 [0.114, 0.115] | 0.98 | faster |
| each-bounce | populated | 0.116 [0.115, 0.116] | 0.116 [0.115, 0.117] | 1.01 | overlap |
| each-clamp | empty | 0.099 [0.099, 0.100] | 0.099 [0.099, 0.099] | 1.00 | overlap |
| each-clamp | populated | 0.104 [0.104, 0.107] | 0.105 [0.105, 0.109] | 1.01 | overlap |
| each-delegating | empty | 0.112 [0.112, 0.112] | 0.110 [0.110, 0.111] | 0.98 | faster |
| each-delegating | populated | 0.111 [0.111, 0.111] | 0.112 [0.112, 0.114] | 1.00 | slower |
| each-expire | empty | 0.919 [0.918, 0.954] | 0.918 [0.916, 0.954] | 1.00 | overlap |
| each-expire | populated | 0.918 [0.911, 0.952] | 0.917 [0.912, 0.917] | 1.00 | overlap |
| each-length | empty | 0.896 [0.895, 0.899] | 0.888 [0.885, 0.898] | 0.99 | overlap |
| each-length | populated | 0.882 [0.880, 0.888] | 0.884 [0.882, 0.890] | 1.00 | overlap |
| each-plain | empty | 0.114 [0.113, 0.114] | 0.111 [0.111, 0.113] | 0.98 | faster |
| each-plain | populated | 0.114 [0.113, 0.114] | 0.114 [0.113, 0.114] | 1.00 | overlap |
| each-spin | empty | 0.106 [0.106, 0.106] | 0.106 [0.106, 0.106] | 1.00 | overlap |
| each-spin | populated | 0.106 [0.106, 0.106] | 0.106 [0.105, 0.106] | 1.00 | overlap |
| each-wrap | empty | 0.112 [0.111, 0.112] | 0.110 [0.109, 0.111] | 0.98 | faster |
| each-wrap | populated | 0.111 [0.111, 0.112] | 0.112 [0.112, 0.113] | 1.01 | slower |
| each-wrap-helper | empty | 0.112 [0.111, 0.112] | 0.110 [0.110, 0.111] | 0.98 | overlap |
| each-wrap-helper | populated | 0.111 [0.111, 0.111] | 0.112 [0.112, 0.113] | 1.01 | slower |
| query-wrap | empty | 0.988 [0.982, 0.993] | 1.009 [0.982, 1.009] | 1.02 | overlap |
| query-wrap | populated | 0.780 [0.739, 0.780] | 0.779 [0.776, 0.786] | 1.00 | overlap |
| vector-spin | empty | 0.107 [0.107, 0.107] | 0.107 [0.107, 0.107] | 1.00 | overlap |
| vector-spin | populated | 0.107 [0.106, 0.107] | 0.106 [0.106, 0.106] | 0.99 | overlap |
| vector-wrap | empty | 0.113 [0.113, 0.113] | 0.111 [0.111, 0.112] | 0.98 | faster |
| vector-wrap | populated | 0.112 [0.112, 0.113] | 0.113 [0.112, 0.114] | 1.01 | overlap |
