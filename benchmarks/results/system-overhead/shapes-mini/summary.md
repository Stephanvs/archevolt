| Case | Mode | Baseline ns/row | Candidate ns/row | Ratio | Ranges |
| --- | --- | --- | --- | ---: | --- |
| block-wrap | empty | 0.592 [0.588, 0.595] | 0.587 [0.586, 0.594] | 0.99 | overlap |
| block-wrap | populated | 0.599 [0.591, 0.645] | 0.588 [0.587, 0.591] | 0.98 | overlap |
| each-bounce | empty | 0.116 [0.115, 0.117] | 0.116 [0.115, 0.117] | 0.99 | overlap |
| each-bounce | populated | 0.116 [0.115, 0.117] | 0.115 [0.114, 0.116] | 0.99 | overlap |
| each-clamp | empty | 0.100 [0.099, 0.100] | 0.099 [0.099, 0.099] | 0.99 | overlap |
| each-clamp | populated | 0.108 [0.106, 0.113] | 0.107 [0.104, 0.111] | 0.99 | overlap |
| each-delegating | empty | 0.112 [0.111, 0.113] | 0.112 [0.110, 0.113] | 1.00 | overlap |
| each-delegating | populated | 0.112 [0.110, 0.113] | 0.111 [0.110, 0.112] | 0.98 | overlap |
| each-expire | empty | 0.917 [0.910, 0.950] | 0.911 [0.910, 0.961] | 0.99 | overlap |
| each-expire | populated | 0.917 [0.909, 0.948] | 0.914 [0.910, 0.949] | 1.00 | overlap |
| each-length | empty | 0.885 [0.883, 0.888] | 0.881 [0.878, 0.890] | 1.00 | overlap |
| each-length | populated | 0.884 [0.880, 0.887] | 0.884 [0.879, 0.888] | 1.00 | overlap |
| each-plain | empty | 0.114 [0.113, 0.114] | 0.113 [0.111, 0.115] | 0.99 | overlap |
| each-plain | populated | 0.113 [0.112, 0.115] | 0.112 [0.112, 0.114] | 0.99 | overlap |
| each-spin | empty | 0.106 [0.106, 0.107] | 0.106 [0.105, 0.106] | 0.99 | overlap |
| each-spin | populated | 0.106 [0.106, 0.107] | 0.106 [0.105, 0.107] | 1.00 | overlap |
| each-wrap | empty | 0.112 [0.111, 0.113] | 0.112 [0.110, 0.113] | 1.00 | overlap |
| each-wrap | populated | 0.112 [0.110, 0.113] | 0.111 [0.110, 0.112] | 0.98 | overlap |
| each-wrap-helper | empty | 0.112 [0.111, 0.112] | 0.112 [0.110, 0.112] | 1.00 | overlap |
| each-wrap-helper | populated | 0.112 [0.110, 0.113] | 0.111 [0.110, 0.112] | 0.99 | overlap |
| query-wrap | empty | 1.183 [1.174, 1.197] | 0.978 [0.977, 1.024] | 0.83 | faster |
| query-wrap | populated | 0.772 [0.740, 0.777] | 0.772 [0.735, 0.773] | 1.00 | overlap |
| vector-spin | empty | 0.108 [0.107, 0.108] | 0.107 [0.106, 0.107] | 0.99 | faster |
| vector-spin | populated | 0.107 [0.107, 0.108] | 0.107 [0.106, 0.107] | 1.00 | overlap |
| vector-wrap | empty | 0.113 [0.113, 0.114] | 0.113 [0.111, 0.114] | 1.00 | overlap |
| vector-wrap | populated | 0.113 [0.110, 0.114] | 0.111 [0.110, 0.112] | 0.99 | overlap |
