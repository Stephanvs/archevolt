using System.Numerics;
using Archevolt;
using Silk.NET.OpenGL;

namespace Archeoids;

internal static class Verification
{
    public static int Run()
    {
        CollisionAndMessages();
        ExpirationBeforeCollision();
        WrappingAndSeamCollision();
        ShieldsAndGameOver();
        WavesAndRestart();
        Console.WriteLine("archeoids=ok checks=5");
        return 0;
    }

    private static void CollisionAndMessages()
    {
        using var world = Game.CreateWorld(42);
        using var simulation = Simulation.Create(world);
        using var presentation = Presentation.Create(world);
        var random = new RandomSource(7);
        Entity rock = world.Spawn(Factory.Rock(new(100, 100), Vector2.Zero, 3, ref random));
        Entity first = world.Spawn(BulletAt(new(100, 100)));
        Entity second = world.Spawn(BulletAt(new(100, 100)));
        simulation.Run();
        presentation.Run();
        RenderFrame frame = world.GetResource<RenderFrame>();
        Require(!world.Contains(rock), "A hit must despawn the original rock.");
        Require(world.Contains(first) != world.Contains(second), "One rock must consume exactly one of two overlapping bullets.");
        Require(Count(frame, Shape.Rock) == 2, "A large rock must split into two visible fragments at the command barrier.");
        Require(world.GetResource<Session>().Score == 20, "The score reader must award exactly 20 points for a large rock.");
        Require(frame.Particles == 34, "The independent effects reader must create 34 particles for the same hit.");
        world.Despawn(world.Contains(first) ? first : second);
        for (int i = 0; i < 3; i++) { world.AdvanceMessages(); simulation.Run(); }
        presentation.Run();
        Require(world.GetResource<Session>().Score == 20 && frame.Particles == 34,
            "Message readers must consume a hit once across later ticks and retention rotations.");
        Console.WriteLine("ok: single-hit splitting, deferred commands, independent message readers");
    }

    private static void ExpirationBeforeCollision()
    {
        using var world = Game.CreateWorld(42);
        using var simulation = Simulation.Create(world);
        var random = new RandomSource(7);
        Entity rock = world.Spawn(Factory.Rock(new(100, 100), Vector2.Zero, 1, ref random));
        Entity bullet = world.Spawn(BulletAt(new(100, 100)) with { Lifetime = new(0.001f, 1) });
        Entity particle = world.Spawn(Factory.Particle(new(100, 100), Vector2.Zero, 0.001f, Factory.Amber));
        simulation.Run();
        Require(world.Contains(rock) && !world.Contains(bullet) && !world.Contains(particle),
            "Expired entities must disappear before collision detection.");
        Require(world.GetResource<Session>().Score == 0, "An expired bullet must not score a hit.");
        Console.WriteLine("ok: expiration precedes collision");
    }

    private static void WrappingAndSeamCollision()
    {
        using var world = Game.CreateWorld(42);
        using var simulation = Simulation.Create(world);
        Entity moving = world.Spawn(BulletAt(new(Arena.Width - 1, 300)) with { Velocity = new(new(240, 0)) });
        simulation.Run();
        Require(Vector2.Distance(world.Get<Position>(moving).Value, new(1, 300)) < 0.001f,
            "Motion must wrap through the screen edge.");
        var random = new RandomSource(7);
        Entity rock = world.Spawn(Factory.Rock(new(1, 100), Vector2.Zero, 1, ref random));
        Entity bullet = world.Spawn(BulletAt(new(Arena.Width - 2, 100)));
        simulation.Run();
        Require(!world.Contains(rock) && !world.Contains(bullet) && world.GetResource<Session>().Score == 100,
            "Collisions must use the shortest distance across screen seams.");
        Console.WriteLine("ok: motion wrapping and collisions across seams");
    }

    private static void ShieldsAndGameOver()
    {
        using var world = Game.CreateWorld(42);
        using var simulation = Simulation.Create(world);
        Entity ship = world.Spawn(Factory.Ship());
        world.Remove<Invulnerable>(ship);
        world.Remove<Shield>(ship);
        var random = new RandomSource(7);
        world.Spawn(Factory.Rock(Arena.Center, Vector2.Zero, 1, ref random));
        world.Spawn(Factory.Rock(Arena.Center, Vector2.Zero, 1, ref random));
        simulation.Run();
        Require(world.GetResource<Session>().Lives == 2 && world.Has<Invulnerable>(ship),
            "Overlapping rocks must cost one life and add invulnerability.");
        simulation.Run();
        Require(world.GetResource<Session>().Lives == 2, "A protected ship must survive overlapping rocks.");
        world.Insert(ship, new Shield(0.001f));
        simulation.Run();
        Require(world.GetResource<Session>().Lives == 1, "Shield expiration must make a ship vulnerable again.");
        world.Insert(ship, new Shield(0.001f));
        simulation.Run();
        Require(world.GetResource<Session>().GameOver && !world.Contains(ship),
            "Losing the final life must end the game and remove the ship.");
        Console.WriteLine("ok: invulnerability archetype changes, life loss, game over");
    }

    private static void WavesAndRestart()
    {
        using (var world = Game.CreateWorld(42))
        {
            using var simulation = Simulation.Create(world);
            using var presentation = Presentation.Create(world);
            world.InsertResource(new Session { Lives = 3 });
            simulation.Run();
            presentation.Run();
            Require(world.GetResource<Session>().Wave == 1 && Count(world.GetResource<RenderFrame>(), Shape.Rock) == 4,
                "An empty sector must start a wave with four visible rocks.");
        }
        using var game = new Game();
        for (int i = 0; i < 360; i++) game.Step(new(0.3f, true, true));
        game.Capture();
        Require(game.State.Wave == 1 && game.Frame.Sprites.Count > 1, "The normal game must advance and spawn entities.");
        game.Restart();
        Require(game.State is { Lives: 3, Score: 0, Wave: 0 } && game.Frame.Sprites.Count == 1
            && game.Frame.Sprites[0].Position == Arena.Center,
            "Restart must reset the game and discard old entities, commands, and message cursors.");
        Console.WriteLine("ok: wave creation and full restart");
    }

    private static BulletBundle BulletAt(Vector2 position) => new(new(position), new(Vector2.Zero), new(0),
        new(3), new(), new(1, 1), new(Shape.Bullet, 5, 0, Factory.Cyan));

    private static int Count(RenderFrame frame, Shape shape) => frame.Sprites.Count(sprite => sprite.Appearance.Shape == shape);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static unsafe void Capture(GL gl, int width, int height, string path)
    {
        int stride = (width * 3 + 3) & ~3;
        byte[] pixels = new byte[stride * height];
        gl.PixelStore(PixelStoreParameter.PackAlignment, 4);
        gl.Finish();
        fixed (byte* data = pixels) gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Bgr, PixelType.UnsignedByte, data);
        int bright = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int offset = y * stride + x * 3;
                if (pixels[offset] > 90 || pixels[offset + 1] > 90 || pixels[offset + 2] > 90) bright++;
            }
        Require(bright > 2000, "The OpenGL framebuffer must contain rendered foreground geometry.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0x4D42);
        writer.Write(54 + pixels.Length);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        writer.Write(0);
        writer.Write(pixels.Length);
        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        writer.Write(pixels);
    }
}
