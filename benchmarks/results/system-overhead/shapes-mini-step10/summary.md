| Case | Mode | Baseline ns/row | Candidate ns/row | Ratio | Ranges |
| --- | --- | --- | --- | ---: | --- |
| block-wrap | empty | 0.590 [0.587, 0.595] | 0.597 [0.588, 0.938] | 1.01 | overlap |
| block-wrap | populated | 0.591 [0.588, 0.598] | 0.592 [0.590, 0.596] | 1.00 | overlap |
| each-bounce | empty | 0.117 [0.115, 0.118] | 0.115 [0.113, 0.479] | 0.98 | overlap |
| each-bounce | populated | 0.116 [0.115, 0.117] | 0.116 [0.115, 0.116] | 1.00 | overlap |
| each-clamp | empty | 0.100 [0.099, 0.100] | 0.099 [0.099, 0.277] | 0.99 | overlap |
| each-clamp | populated | 0.107 [0.105, 0.108] | 0.107 [0.106, 0.111] | 1.00 | overlap |
| each-delegating | empty | 0.113 [0.111, 0.114] | 0.111 [0.110, 0.449] | 0.99 | overlap |
| each-delegating | populated | 0.112 [0.111, 0.112] | 0.111 [0.111, 0.112] | 1.00 | overlap |
| each-expire | empty | 0.912 [0.912, 0.950] | 0.957 [0.914, 5.118] | 1.05 | overlap |
| each-expire | populated | 0.913 [0.911, 0.921] | 0.919 [0.910, 0.956] | 1.01 | overlap |
| each-length | empty | 0.882 [0.880, 0.885] | 0.888 [0.880, 0.960] | 1.01 | overlap |
| each-length | populated | 0.887 [0.882, 0.888] | 0.883 [0.879, 0.900] | 1.00 | overlap |
| each-plain | empty | 0.115 [0.112, 0.116] | 0.113 [0.111, 0.400] | 0.99 | overlap |
| each-plain | populated | 0.114 [0.113, 0.114] | 0.113 [0.113, 0.117] | 1.00 | overlap |
| each-spin | empty | 0.107 [0.106, 0.107] | 0.106 [0.105, 0.298] | 0.99 | overlap |
| each-spin | populated | 0.107 [0.106, 0.107] | 0.106 [0.105, 0.106] | 0.99 | overlap |
| each-wrap | empty | 0.113 [0.111, 0.114] | 0.112 [0.110, 0.443] | 0.99 | overlap |
| each-wrap | populated | 0.112 [0.111, 0.112] | 0.112 [0.111, 0.112] | 1.00 | overlap |
| each-wrap-helper | empty | 0.114 [0.111, 0.114] | 0.111 [0.110, 0.442] | 0.98 | overlap |
| each-wrap-helper | populated | 0.112 [0.111, 0.112] | 0.111 [0.111, 0.112] | 1.00 | overlap |
| query-wrap | empty | 1.176 [1.174, 1.178] | 1.004 [0.980, 1.146] | 0.85 | faster |
| query-wrap | populated | 0.774 [0.771, 0.781] | 0.774 [0.745, 0.780] | 1.00 | overlap |
| vector-spin | empty | 0.108 [0.107, 0.108] | 0.107 [0.106, 0.298] | 0.99 | overlap |
| vector-spin | populated | 0.107 [0.106, 0.108] | 0.106 [0.106, 0.107] | 0.99 | overlap |
| vector-wrap | empty | 0.113 [0.112, 0.114] | 0.112 [0.112, 0.448] | 1.00 | overlap |
| vector-wrap | populated | 0.113 [0.112, 0.114] | 0.113 [0.112, 0.113] | 1.00 | overlap |
