using Archevolt;
using System.Numerics;

namespace Archevolt.Particles;

[Component] public readonly record struct Position(Vector3 Value);
[Component] public readonly record struct Velocity(Vector3 Value);
[Component] public readonly struct Frozen;
[Resource] public readonly record struct FrameTime(float DeltaSeconds);
[Bundle] public readonly partial record struct Particle(Position Position, Velocity Velocity);

[Query, Write<Position>, Read<Velocity>, Without<Frozen>]
public readonly ref partial struct Moving;

public static partial class Motion
{
	[EcsSystem]
	public static void Integrate(scoped Moving moving, scoped Res<FrameTime> time)
	{
		foreach (var row in moving)
		{
			ref var position = ref row.Position;
			position = position with { Value = position.Value + row.Velocity.Value * time.Value.DeltaSeconds };
		}
	}
}

public static partial class MotionKernel
{
	[Each(Partitioned = true), Without<Frozen>]
	public static void Integrate(scoped ref Position position, scoped in Velocity velocity, scoped Res<FrameTime> time) =>
		position = position with { Value = position.Value + velocity.Value * time.Value.DeltaSeconds };
}

[EcsModule, IncludeSystems(typeof(Motion)), IncludeSystems(typeof(MotionKernel)), IncludeBundle<Particle>, IncludeResource<FrameTime>]
public static partial class GameEcs;
[Schedule, Run(typeof(Motion), nameof(Motion.Integrate), Order = 0)] public sealed partial class Simulation;
[Schedule, Run(typeof(MotionKernel), nameof(MotionKernel.Integrate), Order = 0)] public sealed partial class KernelSimulation;

public static class Program
{
	public static void Main()
	{
		Run(1);
		Run(4);
		Console.WriteLine("ecs=ok");
	}

	static void Run(int concurrency)
	{
		using var world = new WorldBuilder().UseParallelExecution(concurrency).AddModule(GameEcs.Module).Build();
		world.InsertResource(new FrameTime(1));
		var entities = new Entity[100_000];
		for (int index = 0; index < entities.Length; index++)
			entities[index] = world.Spawn(new Particle(new Position(Vector3.Zero), new Velocity(Vector3.UnitX)));
		using var kernel = KernelSimulation.Create(world);
		kernel.Run();
		foreach (var entity in entities)
			if (world.Get<Position>(entity).Value != Vector3.UnitX) throw new InvalidOperationException("Each failed to update an entity.");
		world.Insert(entities[0], new Frozen());
		kernel.Run();
		if (world.Get<Position>(entities[0]).Value.X != 1 || world.Get<Position>(entities[^1]).Value.X != 2)
			throw new InvalidOperationException("Adding a tag did not update query membership.");
		world.Remove<Frozen>(entities[0]);
		using var simulation = Simulation.Create(world);
		simulation.Run();
		if (world.Get<Position>(entities[0]).Value.X != 2 || world.Get<Position>(entities[^1]).Value.X != 3)
			throw new InvalidOperationException("Named queries did not observe the updated archetypes.");
	}
}
