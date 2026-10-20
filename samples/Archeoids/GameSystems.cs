using System.Numerics;
using Archevolt;

namespace Archeoids;

[Query, Write<Rotation>, Write<Velocity>, Write<Ship>, Read<Position>, With<Player>]
internal readonly ref partial struct Pilots;
[Query, Write<Shield>, With<Invulnerable>]
internal readonly ref partial struct Shields;
[Query, Read<Position>, Read<Velocity>, Read<Collider>, Write<Rock>, Without<Player>]
internal readonly ref partial struct RockTargets;
[Query, Read<Position>, Read<Collider>, With<Bullet>]
internal readonly ref partial struct Bullets;
[Query, Read<Position>, Read<Collider>, With<Rock>, Without<Player>]
internal readonly ref partial struct RockColliders;
[Query, Write<Position>, Write<Velocity>, Write<Rotation>, Read<Collider>, With<Player>, Without<Invulnerable>]
internal readonly ref partial struct VulnerableShips;
[Query, With<Rock>]
internal readonly ref partial struct AllRocks;
[Query, Read<Position>, Read<Rotation>, Read<Appearance>, Optional<Lifetime>, Optional<Shield>]
internal readonly ref partial struct Visible;

internal static partial class GameSystems
{
    [EcsSystem]
    public static void ExpireShields(scoped Shields shields, scoped Res<FrameTime> time, scoped Commands commands)
    {
        foreach (var row in shields)
        {
            row.Shield = new(row.Shield.Remaining - time.Value.DeltaSeconds);
            if (row.Shield.Remaining > 0) continue;
            commands.Remove<Invulnerable>(row.Entity);
            commands.Remove<Shield>(row.Entity);
        }
    }

    [EcsSystem]
    public static void Pilot(scoped Pilots pilots, scoped Res<Controls> input, scoped Res<FrameTime> time,
        scoped ResMut<RandomSource> random, scoped Commands commands)
    {
        float dt = time.Value.DeltaSeconds;
        foreach (var row in pilots)
        {
            row.Rotation = row.Rotation with { Angle = row.Rotation.Angle + input.Value.Turn * 4.2f * dt };
            Vector2 forward = Arena.Direction(row.Rotation.Angle);
            Vector2 velocity = row.Velocity.Value * MathF.Exp(-0.38f * dt);
            if (input.Value.Thrust) velocity += forward * (350 * dt);
            if (velocity.LengthSquared() > 380 * 380) velocity = Vector2.Normalize(velocity) * 380;
            row.Velocity = new(velocity);

            float shot = row.Ship.ShotCooldown - dt;
            float exhaust = row.Ship.ExhaustCooldown - dt;
            if (input.Value.Fire && shot <= 0)
            {
                commands.Spawn(new BulletBundle(new(Arena.Wrap(row.Position.Value + forward * 20)),
                    new(velocity + forward * 680), new(row.Rotation.Angle), new(3), new(), new(1.15f, 1.15f),
                    new(Shape.Bullet, 5, 0, Factory.Cyan)));
                shot = 0.15f;
            }
            if (input.Value.Thrust && exhaust <= 0)
            {
                for (int i = 0; i < 2; i++)
                    commands.Spawn(Factory.Particle(row.Position.Value - forward * 13,
                        velocity - forward * random.Value.Range(80, 170) + random.Value.Direction() * 20,
                        random.Value.Range(0.15f, 0.4f), Factory.Amber, 1.8f));
                exhaust = 0.025f;
            }
            row.Ship = new(shot, exhaust);
        }
    }

    [Each(Partitioned = true)]
    public static void Move(scoped ref Position position, scoped in Velocity velocity, scoped Res<FrameTime> time)
        => position = new(Arena.Wrap(position.Value + velocity.Value * time.Value.DeltaSeconds));

    [Each(Partitioned = true)]
    public static void Spin(scoped ref Rotation rotation, scoped Res<FrameTime> time)
        => rotation = rotation with { Angle = rotation.Angle + rotation.Speed * time.Value.DeltaSeconds };

    [Each]
    public static void Expire(Entity entity, scoped ref Lifetime lifetime, scoped Res<FrameTime> time, scoped Commands commands)
    {
        lifetime = lifetime with { Remaining = lifetime.Remaining - time.Value.DeltaSeconds };
        if (lifetime.Remaining <= 0) commands.Despawn(entity);
    }

    [EcsSystem]
    public static void HitRocks(scoped Bullets bullets, scoped RockTargets rocks, scoped Commands commands,
        scoped MessageWriter<RockDestroyed> destroyed)
    {
        foreach (var bullet in bullets)
        {
            foreach (var rock in rocks)
            {
                if (rock.Rock.Hit || !Overlaps(bullet.Position, bullet.Collider, rock.Position, rock.Collider)) continue;
                // Commands play back after this system. Claim the rock now so another bullet cannot hit it twice.
                rock.Rock = rock.Rock with { Hit = true };
                destroyed.Send(new RockDestroyed(rock.Position.Value, rock.Velocity.Value, rock.Rock.Size));
                commands.Despawn(rock.Entity);
                commands.Despawn(bullet.Entity);
                break;
            }
        }
    }

    [EcsSystem]
    public static void HitShip(scoped VulnerableShips ships, scoped RockColliders rocks, scoped ResMut<Session> session,
        scoped Commands commands, scoped MessageWriter<ShipDestroyed> destroyed)
    {
        foreach (var ship in ships)
        {
            foreach (var rock in rocks)
            {
                if (!Overlaps(ship.Position, ship.Collider, rock.Position, rock.Collider)) continue;
                destroyed.Send(new ShipDestroyed(ship.Position.Value));
                session.Value.Lives--;
                if (session.Value.GameOver) commands.Despawn(ship.Entity);
                else
                {
                    ship.Position = new(Arena.Center);
                    ship.Velocity = new(Vector2.Zero);
                    ship.Rotation = new(-MathF.PI / 2);
                    commands.Insert(ship.Entity, new Invulnerable());
                    commands.Insert(ship.Entity, new Shield(2.5f));
                }
                break;
            }
        }
    }

    [EcsSystem]
    public static void SplitRocks(scoped MessageReader<RockDestroyed> destroyed, scoped ResMut<RandomSource> random,
        scoped Commands commands)
    {
        foreach (var hit in destroyed)
        {
            if (hit.Size == 1) continue;
            Vector2 direction = random.Value.Direction();
            for (int side = -1; side <= 1; side += 2)
                commands.Spawn(Factory.Rock(hit.Position + direction * (side * 12),
                    hit.Velocity + direction * (side * random.Value.Range(35, 65)), hit.Size - 1, ref random.Value));
        }
    }

    [EcsSystem]
    public static void Score(scoped MessageReader<RockDestroyed> destroyed, scoped ResMut<Session> session)
    {
        foreach (var hit in destroyed)
            session.Value.Score += hit.Size switch { 3 => 20, 2 => 50, _ => 100 };
    }

    [EcsSystem]
    public static void Explode(scoped MessageReader<RockDestroyed> rocks, scoped MessageReader<ShipDestroyed> ships,
        scoped ResMut<RandomSource> random, scoped Commands commands)
    {
        foreach (var hit in rocks) Burst(hit.Position, 16 + hit.Size * 6, Factory.Amber, ref random.Value, commands);
        foreach (var hit in ships) Burst(hit.Position, 55, Factory.Cyan, ref random.Value, commands);
    }

    private static void Burst(Vector2 position, int count, Vector4 color, ref RandomSource random, scoped Commands commands)
    {
        for (int i = 0; i < count; i++)
            commands.Spawn(Factory.Particle(position, random.Direction() * random.Range(30, 210),
                random.Range(0.25f, 0.9f), color, random.Range(1, 2.8f)));
    }

    [EcsSystem]
    public static void Waves(scoped AllRocks rocks, scoped ResMut<Session> session, scoped Res<FrameTime> time,
        scoped ResMut<RandomSource> random, scoped Commands commands)
    {
        if (session.Value.GameOver) return;
        foreach (var _ in rocks) return;
        session.Value.WaveDelay -= time.Value.DeltaSeconds;
        if (session.Value.WaveDelay > 0) return;
        session.Value.Wave++;
        session.Value.WaveDelay = 1.6f;
        int count = 3 + Math.Min(session.Value.Wave, 10);
        for (int i = 0; i < count; i++)
        {
            Vector2 direction = random.Value.Direction();
            Vector2 position = Arena.Wrap(Arena.Center + direction * 440);
            commands.Spawn(Factory.Rock(position, -direction * random.Value.Range(28, 55)
                + random.Value.Direction() * 20, 3, ref random.Value));
        }
    }

    [EcsSystem]
    public static void Extract(scoped Visible visible, scoped ManagedRes<RenderFrame> frame)
    {
        frame.Value.Sprites.Clear();
        frame.Value.Particles = 0;
        foreach (var row in visible)
        {
            float alpha = row.HasLifetime ? Math.Clamp(row.Lifetime.Remaining / row.Lifetime.Total, 0, 1) : 1;
            if (row.HasShield) alpha *= 0.35f + 0.65f * MathF.Abs(MathF.Sin(row.Shield.Remaining * 13));
            frame.Value.Sprites.Add(new(row.Position.Value, row.Rotation.Angle, row.Appearance, alpha));
            if (row.Appearance.Shape == Shape.Particle) frame.Value.Particles++;
        }
    }

    private static bool Overlaps(Position a, Collider ca, Position b, Collider cb)
        => Arena.Difference(a.Value, b.Value).LengthSquared() <= (ca.Radius + cb.Radius) * (ca.Radius + cb.Radius);
}

[EcsModule, IncludeSystems(typeof(GameSystems)), IncludeBundle<ShipBundle>, IncludeBundle<RockBundle>,
 IncludeBundle<BulletBundle>, IncludeBundle<ParticleBundle>]
internal static partial class GameEcs;

[Schedule]
[Run(typeof(GameSystems), nameof(GameSystems.ExpireShields), Order = 0)]
[Run(typeof(GameSystems), nameof(GameSystems.Pilot), Order = 1, After = nameof(GameSystems.ExpireShields))]
[Run(typeof(GameSystems), nameof(GameSystems.Move), Order = 2, After = nameof(GameSystems.Pilot))]
[Run(typeof(GameSystems), nameof(GameSystems.Spin), Order = 3, After = nameof(GameSystems.Move))]
[Run(typeof(GameSystems), nameof(GameSystems.Expire), Order = 4, After = nameof(GameSystems.Spin))]
[Run(typeof(GameSystems), nameof(GameSystems.HitRocks), Order = 5, After = nameof(GameSystems.Expire))]
[Run(typeof(GameSystems), nameof(GameSystems.HitShip), Order = 6, After = nameof(GameSystems.HitRocks))]
[Run(typeof(GameSystems), nameof(GameSystems.SplitRocks), Order = 7, After = nameof(GameSystems.HitShip))]
[Run(typeof(GameSystems), nameof(GameSystems.Score), Order = 8, After = nameof(GameSystems.SplitRocks))]
[Run(typeof(GameSystems), nameof(GameSystems.Explode), Order = 9, After = nameof(GameSystems.Score))]
[Run(typeof(GameSystems), nameof(GameSystems.Waves), Order = 10, After = nameof(GameSystems.Explode))]
internal sealed partial class Simulation;

[Schedule, Run(typeof(GameSystems), nameof(GameSystems.Extract), Order = 0)]
internal sealed partial class Presentation;
