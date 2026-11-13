using Archevolt;

[Component]
public readonly record struct Position(float X);

[Component]
public readonly record struct Velocity(float X);

public static partial class Motion
{
    [Each]
    public static void Move(scoped ref Position position, scoped in Velocity velocity)
    {
        position = position with { X = position.X + velocity.X };
    }
}

[Bundle]
public readonly partial record struct Particle(Position Position, Velocity Velocity);

[EcsModule, IncludeSystems(typeof(Motion)), IncludeBundle<Particle>]
public static partial class GameEcs;

[Schedule, Run(typeof(Motion), nameof(Motion.Move), Order = 0)]
public sealed partial class Simulation;

public static class Program
{
    public static void Main()
    {
        using var world = new WorldBuilder().AddModule(GameEcs.Module).Build();
        var entity = world.Spawn(new Particle(new Position(0), new Velocity(1)));

        using var simulation = Simulation.Create(world);
        simulation.Run();

        Console.WriteLine(world.Get<Position>(entity).X); // 1
    }
}
