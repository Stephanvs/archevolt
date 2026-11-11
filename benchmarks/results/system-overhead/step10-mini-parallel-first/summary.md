| Case | Mode | Baseline ns/system | Candidate ns/system | Ratio | Ranges |
| --- | --- | --- | --- | ---: | --- |
| commands100-chain/1024 | parallel | 51.630 [50.770, 51.960] | 52.850 [50.200, 53.230] | 1.02 | overlap |
| commands100-chain/16 | parallel | 47.110 [46.430, 47.770] | 48.600 [46.410, 49.320] | 1.03 | overlap |
| commands100-chain/256 | parallel | 48.100 [47.440, 48.550] | 49.650 [47.660, 50.090] | 1.03 | overlap |
| commands100-chain/64 | parallel | 47.770 [47.720, 50.980] | 49.130 [46.950, 49.990] | 1.03 | overlap |
| commands25-chain/1024 | parallel | 34.170 [33.080, 34.230] | 33.850 [33.700, 35.470] | 0.99 | overlap |
| commands25-chain/16 | parallel | 30.090 [29.810, 30.460] | 31.390 [30.820, 31.410] | 1.04 | slower |
| commands25-chain/256 | parallel | 31.580 [31.120, 31.630] | 32.120 [31.940, 32.510] | 1.02 | slower |
| commands25-chain/64 | parallel | 31.390 [31.240, 31.490] | 32.120 [31.440, 32.320] | 1.02 | overlap |
| each-chain/1024 | parallel | 25.980 [25.790, 26.290] | 27.060 [26.000, 27.100] | 1.04 | overlap |
| each-chain/16 | parallel | 25.180 [24.960, 25.250] | 26.800 [25.560, 27.090] | 1.06 | slower |
| each-chain/256 | parallel | 25.600 [25.320, 25.610] | 26.620 [25.740, 26.730] | 1.04 | slower |
| each-chain/64 | parallel | 25.420 [25.160, 25.850] | 26.690 [25.460, 26.760] | 1.05 | overlap |
| each-fragmented/16 | parallel | 479.720 [469.710, 481.460] | 485.130 [475.510, 485.190] | 1.01 | overlap |
| empty-batch/1024 | parallel | 58.880 [58.030, 59.450] | 56.480 [50.330, 59.880] | 0.96 | overlap |
| empty-batch/16 | parallel | 137.940 [137.900, 167.560] | 135.460 [131.650, 141.620] | 0.98 | overlap |
| empty-batch/256 | parallel | 65.550 [63.400, 66.490] | 63.820 [63.040, 67.710] | 0.97 | overlap |
| empty-batch/64 | parallel | 77.880 [77.070, 78.420] | 77.170 [76.330, 78.010] | 0.99 | overlap |
| empty-chain/1024 | parallel | 11.180 [10.830, 11.430] | 11.400 [10.730, 13.930] | 1.02 | overlap |
| empty-chain/16 | parallel | 11.600 [11.180, 11.820] | 11.880 [11.550, 14.340] | 1.02 | overlap |
| empty-chain/256 | parallel | 11.180 [10.790, 11.360] | 11.440 [10.770, 14.000] | 1.02 | overlap |
| empty-chain/64 | parallel | 11.330 [10.930, 11.600] | 11.580 [10.930, 14.000] | 1.02 | overlap |
| query-chain/1024 | parallel | 25.480 [25.020, 28.880] | 28.890 [24.350, 29.090] | 1.13 | overlap |
| query-chain/16 | parallel | 24.990 [24.260, 27.420] | 26.350 [23.950, 27.730] | 1.05 | overlap |
| query-chain/256 | parallel | 25.050 [24.700, 28.740] | 28.300 [24.310, 28.310] | 1.13 | overlap |
| query-chain/64 | parallel | 25.100 [24.920, 28.890] | 28.130 [24.460, 28.570] | 1.12 | overlap |
| query-fragmented/16 | parallel | 441.900 [432.800, 447.380] | 452.130 [433.590, 460.900] | 1.02 | overlap |
