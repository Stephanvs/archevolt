| Case | Mode | Baseline ns/row | Candidate ns/row | Ratio | Ranges |
| --- | --- | --- | --- | ---: | --- |
| block-wrap | empty | 0.589 [0.587, 0.593] | 0.591 [0.590, 0.620] | 1.00 | overlap |
| block-wrap | populated | 0.604 [0.590, 0.781] | 0.595 [0.593, 0.595] | 0.98 | overlap |
| each-bounce | empty | 0.677 [0.675, 0.682] | 0.116 [0.114, 0.118] | 0.17 | faster |
| each-bounce | populated | 0.679 [0.674, 0.680] | 0.115 [0.115, 0.116] | 0.17 | faster |
| each-clamp | empty | 0.392 [0.390, 0.393] | 0.099 [0.099, 0.100] | 0.25 | faster |
| each-clamp | populated | 0.393 [0.390, 0.394] | 0.106 [0.106, 0.107] | 0.27 | faster |
| each-delegating | empty | 0.590 [0.588, 0.592] | 0.112 [0.110, 0.114] | 0.19 | faster |
| each-delegating | populated | 0.590 [0.587, 0.593] | 0.112 [0.111, 0.112] | 0.19 | faster |
| each-expire | empty | 0.914 [0.913, 0.917] | 0.912 [0.912, 0.912] | 1.00 | faster |
| each-expire | populated | 0.916 [0.913, 0.951] | 0.951 [0.920, 0.954] | 1.04 | overlap |
| each-length | empty | 0.881 [0.880, 0.883] | 0.887 [0.881, 0.889] | 1.01 | overlap |
| each-length | populated | 0.894 [0.879, 0.896] | 0.885 [0.881, 0.893] | 0.99 | overlap |
| each-plain | empty | 0.114 [0.114, 0.115] | 0.114 [0.112, 0.116] | 0.99 | overlap |
| each-plain | populated | 0.113 [0.109, 0.114] | 0.113 [0.113, 0.114] | 1.01 | overlap |
| each-spin | empty | 0.395 [0.394, 0.395] | 0.106 [0.105, 0.106] | 0.27 | faster |
| each-spin | populated | 0.394 [0.393, 0.394] | 0.106 [0.106, 0.106] | 0.27 | faster |
| each-wrap | empty | 0.590 [0.588, 0.593] | 0.112 [0.110, 0.114] | 0.19 | faster |
| each-wrap | populated | 0.590 [0.587, 0.591] | 0.112 [0.111, 0.112] | 0.19 | faster |
| each-wrap-helper | empty | 0.543 [0.541, 0.545] | 0.112 [0.110, 0.114] | 0.21 | faster |
| each-wrap-helper | populated | 0.543 [0.539, 0.544] | 0.112 [0.111, 0.112] | 0.21 | faster |
| query-wrap | empty | 1.174 [1.172, 1.180] | 1.176 [1.175, 1.201] | 1.00 | overlap |
| query-wrap | populated | 0.772 [0.768, 0.780] | 0.775 [0.772, 0.782] | 1.00 | overlap |
| vector-spin | empty | 0.107 [0.106, 0.108] | 0.107 [0.106, 0.107] | 1.00 | overlap |
| vector-spin | populated | 0.106 [0.105, 0.107] | 0.107 [0.106, 0.107] | 1.01 | overlap |
| vector-wrap | empty | 0.113 [0.112, 0.113] | 0.113 [0.111, 0.114] | 1.00 | overlap |
| vector-wrap | populated | 0.112 [0.108, 0.112] | 0.114 [0.113, 0.114] | 1.01 | slower |
