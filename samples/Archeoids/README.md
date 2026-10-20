# Archeoids

A small Asteroids game built with Archevolt and [Silk.NET](https://dotnet.github.io/Silk.NET/). Fly through wrapping sectors, split rocks, and survive with three lives. Ships, rocks, bullets, and particles are ECS entities. All shapes and text are generated in code.

![Archeoids gameplay with a cyan ship, drifting asteroids, and a score and lives HUD.](archeoids.png)

## Run

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

## Read the sample

- [GameModel.cs](GameModel.cs) defines unmanaged components, spawn bundles, resources, messages, and the wrapping arena.
- [GameSystems.cs](GameSystems.cs) contains generated queries, systems, and explicit schedule dependencies. Movement uses partitionable `[Each]`; this small sample runs serially.
- [Game.cs](Game.cs) owns the world and schedules. `Step` advances one fixed 1/120-second tick. `Capture` extracts a reusable list of sprites after simulation commands flush.
- [Program.cs](Program.cs) connects Silk.NET's window and keyboard to the fixed-step loop.
- [VectorRenderer.cs](VectorRenderer.cs) batches procedural shapes and a tiny bitmap font into OpenGL triangles. It reads the extracted frame and never accesses the world.
- [Verification.cs](Verification.cs) exercises the real simulation and captures the OpenGL framebuffer during a smoke run.

Structural changes use `Commands`. A hit marks its rock immediately before queuing despawns, preventing overlapping bullets from destroying it twice. Three independent readers consume `RockDestroyed` to split rocks, award points, and create particles. Adding and removing `Invulnerable` changes whether a ship matches the damage query. Expiration runs before collision detection, and both collisions and drawing handle screen seams.

The simulation uses a seeded random resource and a fixed timestep. Restart replaces the world and its schedules, clearing entities and message cursors. The playfield has its own area below the HUD, and resizing preserves its aspect ratio. Collision detection uses simple bullet-versus-rock loops; this sample is intended for a small arena.

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
