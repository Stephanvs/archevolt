using System.Numerics;
using Archevolt;

namespace Archeoids;

internal static class Arena
{
    public const float Width = 1280;
    public const float Height = 640;
    public static readonly Vector2 Center = new(Width / 2, Height / 2);

    public static Vector2 Wrap(Vector2 p) => new(Wrap(p.X, Width), Wrap(p.Y, Height));
    private static float Wrap(float value, float size) => (value % size + size) % size;

    public static Vector2 Difference(Vector2 a, Vector2 b)
    {
        Vector2 d = a - b;
        d.X -= MathF.Round(d.X / Width) * Width;
        d.Y -= MathF.Round(d.Y / Height) * Height;
        return d;
    }

    public static Vector2 Direction(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));
}

[Component] internal readonly record struct Position(Vector2 Value);
[Component] internal readonly record struct Velocity(Vector2 Value);
[Component] internal readonly record struct Rotation(float Angle, float Speed = 0);
[Component] internal readonly record struct Collider(float Radius);
[Component] internal readonly record struct Lifetime(float Remaining, float Total);
[Component] internal readonly record struct Ship(float ShotCooldown, float ExhaustCooldown);
[Component] internal readonly record struct Rock(int Size, bool Hit = false);
[Component] internal readonly record struct Shield(float Remaining);
[Component] internal readonly struct Player;
[Component] internal readonly struct Bullet;
[Component] internal readonly struct Invulnerable;

internal enum Shape { Ship, Rock, Bullet, Particle }
[Component] internal readonly record struct Appearance(Shape Shape, float Radius, uint Seed, Vector4 Color);

[Bundle]
internal readonly partial record struct ShipBundle(
    Position Position, Velocity Velocity, Rotation Rotation, Collider Collider,
    Ship Ship, Player Player, Invulnerable Invulnerable, Shield Shield, Appearance Appearance);

[Bundle]
internal readonly partial record struct RockBundle(
    Position Position, Velocity Velocity, Rotation Rotation, Collider Collider, Rock Rock, Appearance Appearance);

[Bundle]
internal readonly partial record struct BulletBundle(
    Position Position, Velocity Velocity, Rotation Rotation, Collider Collider,
    Bullet Bullet, Lifetime Lifetime, Appearance Appearance);

[Bundle]
internal readonly partial record struct ParticleBundle(
    Position Position, Velocity Velocity, Rotation Rotation, Lifetime Lifetime, Appearance Appearance);

[Resource] internal readonly record struct Controls(float Turn, bool Thrust, bool Fire);
[Resource] internal readonly record struct FrameTime(float DeltaSeconds);
[Resource]
internal struct Session
{
    public int Score;
    public int Lives;
    public int Wave;
    public float WaveDelay;
    public readonly bool GameOver => Lives == 0;
}

[Resource]
internal struct RandomSource(uint seed)
{
    private uint _state = seed;
    public uint Next()
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return _state;
    }
    public float Range(float min, float max) => min + (Next() >> 8) * (1f / 16777216) * (max - min);
    public Vector2 Direction() => Arena.Direction(Range(0, MathF.Tau));
}

[Message] internal readonly record struct RockDestroyed(Vector2 Position, Vector2 Velocity, int Size);
[Message] internal readonly record struct ShipDestroyed(Vector2 Position);

internal readonly record struct Sprite(Vector2 Position, float Angle, Appearance Appearance, float Opacity);

[Resource]
internal sealed class RenderFrame
{
    public List<Sprite> Sprites { get; } = new(2048);
    public int Particles { get; set; }
}

internal static class Factory
{
    public static readonly Vector4 Cyan = new(0.4f, 0.95f, 0.9f, 1);
    public static readonly Vector4 Amber = new(1, 0.65f, 0.26f, 1);

    public static ShipBundle Ship() => new(new(Arena.Center), new(Vector2.Zero), new(-MathF.PI / 2),
        new(12), new(0, 0), new(), new(), new(2.5f), new(Shape.Ship, 15, 0, Cyan));

    public static RockBundle Rock(Vector2 position, Vector2 velocity, int size, ref RandomSource random)
    {
        float radius = size switch { 3 => 46, 2 => 28, _ => 15 };
        return new(new(Arena.Wrap(position)), new(velocity), new(random.Range(0, MathF.Tau), random.Range(-1, 1)),
            new(radius * 0.85f), new(size), new(Shape.Rock, radius, random.Next(), new(0.62f, 0.75f, 0.8f, 1)));
    }

    public static ParticleBundle Particle(Vector2 position, Vector2 velocity, float seconds, Vector4 color, float radius = 2)
        => new(new(Arena.Wrap(position)), new(velocity), new(0), new(seconds, seconds), new(Shape.Particle, radius, 0, color));
}
