| Case | Mode | Baseline ns/row | Candidate ns/row | Ratio | Ranges |
| --- | --- | --- | --- | ---: | --- |
| block-wrap | empty | 0.589 [0.588, 0.590] | 0.591 [0.587, 0.597] | 1.00 | overlap |
| block-wrap | populated | 0.600 [0.590, 0.782] | 0.593 [0.590, 0.595] | 0.99 | overlap |
| each-bounce | empty | 0.116 [0.114, 0.118] | 0.114 [0.114, 0.117] | 0.98 | overlap |
| each-bounce | populated | 0.116 [0.115, 0.117] | 0.115 [0.114, 0.117] | 0.99 | overlap |
| each-clamp | empty | 0.100 [0.100, 0.100] | 0.099 [0.099, 0.099] | 0.99 | faster |
| each-clamp | populated | 0.108 [0.107, 0.113] | 0.104 [0.104, 0.106] | 0.96 | faster |
| each-delegating | empty | 0.112 [0.110, 0.115] | 0.111 [0.111, 0.111] | 0.99 | overlap |
| each-delegating | populated | 0.112 [0.111, 0.114] | 0.110 [0.110, 0.112] | 0.99 | overlap |
| each-expire | empty | 0.913 [0.909, 0.951] | 0.912 [0.910, 0.958] | 1.00 | overlap |
| each-expire | populated | 0.920 [0.911, 0.949] | 0.912 [0.911, 0.963] | 0.99 | overlap |
| each-length | empty | 0.885 [0.883, 0.904] | 0.884 [0.882, 0.886] | 1.00 | overlap |
| each-length | populated | 0.882 [0.880, 0.885] | 0.884 [0.881, 0.893] | 1.00 | overlap |
| each-plain | empty | 0.114 [0.113, 0.115] | 0.113 [0.111, 0.114] | 0.99 | overlap |
| each-plain | populated | 0.114 [0.112, 0.115] | 0.111 [0.111, 0.114] | 0.97 | overlap |
| each-spin | empty | 0.107 [0.106, 0.107] | 0.106 [0.106, 0.107] | 0.99 | overlap |
| each-spin | populated | 0.106 [0.106, 0.107] | 0.105 [0.105, 0.106] | 0.99 | overlap |
| each-wrap | empty | 0.112 [0.111, 0.114] | 0.111 [0.110, 0.113] | 0.99 | overlap |
| each-wrap | populated | 0.112 [0.111, 0.114] | 0.110 [0.110, 0.112] | 0.98 | overlap |
| each-wrap-helper | empty | 0.112 [0.110, 0.115] | 0.111 [0.111, 0.111] | 0.99 | overlap |
| each-wrap-helper | populated | 0.112 [0.111, 0.114] | 0.110 [0.110, 0.112] | 0.98 | overlap |
| query-wrap | empty | 1.176 [1.174, 1.176] | 1.394 [1.094, 1.394] | 1.19 | overlap |
| query-wrap | populated | 0.775 [0.774, 0.782] | 0.777 [0.775, 0.783] | 1.00 | overlap |
| vector-spin | empty | 0.108 [0.107, 0.108] | 0.107 [0.107, 0.108] | 1.00 | overlap |
| vector-spin | populated | 0.107 [0.106, 0.107] | 0.106 [0.106, 0.107] | 0.99 | overlap |
| vector-wrap | empty | 0.112 [0.112, 0.115] | 0.113 [0.111, 0.113] | 1.00 | overlap |
| vector-wrap | populated | 0.113 [0.111, 0.115] | 0.113 [0.111, 0.113] | 1.00 | overlap |
