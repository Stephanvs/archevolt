# Archeoids

A small Asteroids game built with Archevolt. Fly through wrapping sectors, split rocks, and survive with three lives. Ships, rocks, bullets, and particles are ECS entities. The desktop host uses [Silk.NET](https://dotnet.github.io/Silk.NET/) and OpenGL. The browser host uses plain .NET WebAssembly and WebGPU, with browser-rendered text.

![Archeoids gameplay with a cyan ship, drifting asteroids, and a score and lives HUD.](archeoids.png)

## Run on desktop

From the repository root:

```powershell
dotnet run --project samples/Archeoids -c Release
```

Use the .NET SDK pinned in `global.json`. The desktop game needs a graphical session and an OpenGL 3.3 driver. NuGet restores Silk.NET's windowing, input, OpenGL bindings, and native GLFW dependencies. This project opts out of the other samples' NativeAOT publishing default.

| Key | Action |
| --- | --- |
| A / D or Left / Right | Turn |
| W or Up | Thrust |
| Space | Fire |
| P | Pause or resume |
| R | Restart |
| Escape | Quit |

The HUD shows score, sector, remaining lives, visible entity count, and smoothed simulation time per tick. A new ship flashes during its temporary invulnerability. Clear every rock to start the next sector.

## Run in a browser

From the repository root, using the SDK selected by `global.json`:

```powershell
dotnet run --project samples/Archeoids.Browser -c Release
```

Open the HTTP localhost URL printed by the development host. Use a browser with WebGPU enabled and a compatible graphics adapter. WebGPU requires HTTPS or localhost. If WebGPU or the adapter is unavailable, the page displays an error.

The browser project uses `Microsoft.NET.Sdk.WebAssembly` with `browser-wasm`. It explicitly disables the samples' inherited `PublishAot` setting, browser `RunAOTCompilation`, and threads. The SDK restores the managed runtime packs through NuGet. This interpreter configuration does not require a separate workload, JavaScript packages, or a bundler. If you opt into WebAssembly AOT or native runtime relinking, install the workload matching the selected SDK first:

```powershell
dotnet workload install wasm-tools
```

Browser AOT uses `RunAOTCompilation`; desktop `PublishAot` is a different setting. Neither host changes Archevolt's public API.

## Publish the browser game

```powershell
dotnet publish samples/Archeoids.Browser -c Release -o artifacts/archeoids-browser
```

## Read the sample

- [GameModel.cs](GameModel.cs) defines unmanaged components, spawn bundles, resources, messages, and the wrapping arena.
- [GameSystems.cs](GameSystems.cs) contains generated queries, systems, and explicit schedule dependencies. Movement uses partitionable `[Each]`; this small sample runs serially.
- [Game.cs](Game.cs) owns the world and schedules. `Step` advances one fixed 1/120-second tick. `Capture` extracts a reusable list of sprites after simulation commands flush.
- [Program.cs](Program.cs) connects Silk.NET's window and keyboard to the fixed-step loop.
- [FrameGeometry.cs](FrameGeometry.cs) builds the reusable coloured triangle batch: stars, sprites, translucent effects, HUD panels, hull icons, and pause/game-over shading. It reads the extracted frame and never accesses the world.
- [VectorRenderer.cs](VectorRenderer.cs) uploads that batch to OpenGL and appends the desktop bitmap text.
- [Archeoids.Browser.csproj](../Archeoids.Browser/Archeoids.Browser.csproj) links the same `Game.cs`, `GameModel.cs`, `GameSystems.cs`, and `FrameGeometry.cs` source files. There is one simulation implementation. Silk.NET and desktop verification stay outside the browser dependency graph.
- The browser [Program.cs](../Archeoids.Browser/Program.cs) receives animation frames through `[JSExport]`, runs the fixed-step accumulator, and sends a `Span<byte>` through `[JSImport]`. The [page host](../Archeoids.Browser/wwwroot/main.js) owns keyboard and focus events. The [renderer](../Archeoids.Browser/wwwroot/renderer.js) copies the active binary batch into reusable staging storage and uploads it to a reusable vertex buffer. One WGSL pipeline draws the triangles with alpha blending, followed by the Canvas 2D HUD. There is one call into C# and one render call back to JavaScript per frame.
- [Verification.cs](Verification.cs) exercises the real simulation and captures the OpenGL framebuffer during a smoke run.

Structural changes use `Commands`. A hit marks its rock immediately before queuing despawns, preventing overlapping bullets from destroying it twice. Three independent readers consume `RockDestroyed` to split rocks, award points, and create particles. Adding and removing `Invulnerable` changes whether a ship matches the damage query. Expiration runs before collision detection, and both collisions and drawing handle screen seams.

The simulation uses a seeded random resource and a fixed timestep. Both hosts cap incoming elapsed time at 100 ms and advance at 120 ticks per second. Restart replaces the world and its schedules, clearing entities and message cursors. The playfield has its own area below the HUD, and resizing preserves its aspect ratio. Collision detection uses simple bullet-versus-rock loops; this sample is intended for a small arena.

## Verify

Run deterministic gameplay checks without a window:

```powershell
dotnet run --project samples/Archeoids -c Release -- --verify
```

The checks cover one-hit rock splitting, independent message readers, command visibility, expiration before collision, wrapping, seam collisions, invulnerability, game over, waves, and restart. Success prints `archeoids=ok checks=5`. The repository's `scripts/verify.ps1` includes these headless checks.

Run six seconds of scripted play through the real window and renderer:

```powershell
dotnet run --project samples/Archeoids -c Release -- --smoke-test
```

The smoke run checks that the framebuffer contains foreground geometry, writes `artifacts/archeoids/smoke.bmp` relative to the working directory, prints `graphics=ok`, and closes automatically. It requires a graphical session. Gameplay checks can run in headless CI.
