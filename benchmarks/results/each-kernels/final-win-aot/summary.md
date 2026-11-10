| Case | Mode | Baseline ns/row | Candidate ns/row | Ratio | Ranges |
| --- | --- | --- | --- | ---: | --- |
| block-wrap | populated | 0.943 [0.922, 1.477] | 0.923 [0.920, 0.926] | 0.98 | overlap |
| each-bounce | populated | 2.151 [2.069, 3.075] | 0.312 [0.310, 0.313] | 0.15 | faster |
| each-clamp | populated | 1.659 [1.606, 2.371] | 0.456 [0.456, 0.457] | 0.27 | faster |
| each-delegating | populated | 2.617 [2.551, 4.023] | 0.274 [0.273, 0.275] | 0.10 | faster |
| each-expire | populated | 1.335 [1.195, 2.079] | 0.974 [0.973, 0.975] | 0.73 | faster |
| each-length | populated | 1.634 [1.610, 2.469] | 1.146 [1.140, 1.148] | 0.70 | faster |
| each-plain | populated | 0.239 [0.215, 0.279] | 0.213 [0.213, 0.214] | 0.89 | faster |
| each-spin | populated | 0.496 [0.467, 0.727] | 0.233 [0.232, 0.234] | 0.47 | faster |
| each-wrap | populated | 2.551 [2.531, 4.024] | 0.272 [0.271, 0.273] | 0.11 | faster |
| each-wrap-helper | populated | 2.684 [2.546, 4.028] | 0.272 [0.272, 0.272] | 0.10 | faster |
| query-wrap | populated | 1.196 [1.153, 1.675] | 1.150 [1.148, 1.151] | 0.96 | faster |
| vector-spin | populated | 0.265 [0.240, 0.508] | 0.234 [0.233, 0.235] | 0.88 | faster |
| vector-wrap | populated | 0.302 [0.270, 0.510] | 0.255 [0.254, 0.256] | 0.84 | faster |
