| Case | Mode | Baseline ns/row | Candidate ns/row | Ratio | Ranges |
| --- | --- | --- | --- | ---: | --- |
| block-wrap | empty | 0.598 [0.590, 0.607] | 0.591 [0.588, 0.601] | 0.99 | overlap |
| block-wrap | populated | 0.594 [0.587, 0.783] | 0.589 [0.587, 0.624] | 0.99 | overlap |
| each-bounce | empty | 1.484 [1.481, 1.496] | 0.117 [0.116, 0.119] | 0.08 | faster |
| each-bounce | populated | 0.781 [0.674, 0.966] | 0.116 [0.112, 0.118] | 0.15 | faster |
| each-clamp | empty | 1.183 [1.175, 1.186] | 0.100 [0.099, 0.100] | 0.08 | faster |
| each-clamp | populated | 0.393 [0.392, 0.596] | 0.106 [0.103, 0.107] | 0.27 | faster |
| each-delegating | empty | 2.626 [2.599, 2.651] | 0.112 [0.112, 0.115] | 0.04 | faster |
| each-delegating | populated | 0.589 [0.586, 0.946] | 0.113 [0.108, 0.114] | 0.19 | faster |
| each-expire | empty | 6.941 [6.889, 6.959] | 0.915 [0.911, 0.955] | 0.13 | faster |
| each-expire | populated | 1.466 [1.446, 1.828] | 0.911 [0.908, 0.959] | 0.62 | faster |
| each-length | empty | 0.994 [0.992, 1.013] | 0.889 [0.883, 0.891] | 0.89 | faster |
| each-length | populated | 0.888 [0.879, 0.993] | 0.884 [0.882, 0.889] | 1.00 | overlap |
| each-plain | empty | 0.115 [0.114, 0.116] | 0.114 [0.113, 0.116] | 0.99 | overlap |
| each-plain | populated | 0.113 [0.111, 0.422] | 0.114 [0.110, 0.116] | 1.01 | overlap |
| each-spin | empty | 2.170 [2.165, 2.185] | 0.107 [0.106, 0.107] | 0.05 | faster |
| each-spin | populated | 0.394 [0.391, 0.468] | 0.106 [0.105, 0.107] | 0.27 | faster |
| each-wrap | empty | 2.631 [2.595, 2.654] | 0.112 [0.112, 0.114] | 0.04 | faster |
| each-wrap | populated | 0.591 [0.587, 0.945] | 0.113 [0.108, 0.115] | 0.19 | faster |
| each-wrap-helper | empty | 2.629 [2.619, 2.708] | 0.112 [0.112, 0.114] | 0.04 | faster |
| each-wrap-helper | populated | 0.544 [0.539, 0.921] | 0.113 [0.108, 0.114] | 0.21 | faster |
| query-wrap | empty | 1.189 [1.185, 1.204] | 1.181 [1.173, 1.203] | 0.99 | overlap |
| query-wrap | populated | 0.773 [0.770, 0.856] | 0.772 [0.735, 0.785] | 1.00 | overlap |
| vector-spin | empty | 0.108 [0.106, 0.108] | 0.108 [0.107, 0.108] | 1.00 | overlap |
| vector-spin | populated | 0.106 [0.105, 0.319] | 0.106 [0.105, 0.107] | 1.00 | overlap |
| vector-wrap | empty | 0.114 [0.113, 0.115] | 0.113 [0.113, 0.116] | 1.00 | overlap |
| vector-wrap | populated | 0.112 [0.110, 0.447] | 0.114 [0.110, 0.116] | 1.01 | overlap |
