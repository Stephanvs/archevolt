# Archeoids browser AOT measurement

Measured on Windows x64 with an AMD Ryzen 9 5950X, an NVIDIA RTX 3090, headless Chrome, and .NET SDK `11.0.100-rc.3.26522.148` on 2026-11-05. The display limited the AOT run to approximately 240 FPS.

Both runs used the shared game and renderer with seed `0xA2C401D`, sector ten, Overdrive, and three lives. A temporary browser fixture set the initial session before the first frame. The comparison changes interpreted execution to Release-published WebAssembly AOT without changing entity settings. Playwright held Right, Up, and Space for twenty seconds while Chrome's CPU profiler sampled the page.

Each rendered HUD update recorded its timestamp, entity count, FPS, and simulation tick time. Dense frame intervals require both consecutive frames to contain at least 800 entities. The archived summaries contain the count and average FPS of those intervals, their median duration, the peak-entity HUD sample, and the lowest positive dense HUD FPS sample. HUD tick time is an exponential moving average of simulation work; frame intervals also include extraction, geometry, JavaScript, and presentation.

| Measurement | Interpreter | AOT |
| --- | ---: | ---: |
| Dense frame intervals | 117 | 4,224 |
| Average dense FPS | 5.64 | 239.66 |
| Median dense frame interval | 180.5 ms | 4.2 ms |
| Peak concurrent entities | 1,751 | 1,756 |

The native WebAssembly runtime asset increased from 3.15 MB to 8.61 MB before compression. AOT also adds native compilation to publishing. Debug development runs remain interpreted.

Compare the archived measurements and check the 60 FPS median budget:

```powershell
node scripts/compare-archeoids-browser.mjs benchmarks/results/archeoids-browser/baseline.json benchmarks/results/archeoids-browser/aot.json
```

The comparison rejects insufficient samples, a peak load below 800 entities, a candidate peak load reduced by more than 5%, and a candidate median frame interval above 16.67 ms. Reproduce measurements on target hardware before using these results as a performance expectation. Chrome CPU profiles and the temporary fixture remain under `artifacts/archeoids-perf/` and `artifacts/archeoids-browser-review/` in the measurement workspace.
