using System.Reflection;
using System.Runtime.Loader;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Archevolt.Generators.Tests;

[Trait("Category", "Generator")]
public sealed class GeneratorTests
{
	[Theory]
	[InlineData("Each", false, 1)]
	[InlineData("Each(Partitioned = false)", false, 1)]
	[InlineData("Each(Partitioned = true)", true, 4)]
	public void Each_partitioning_requires_explicit_opt_in(string attribute, bool requireOverlap, int expectedThreads)
	{
		var assembly = CompileAndLoad($$"""
			using Archevolt;
			using System;
			using System.Collections.Concurrent;
			using System.Threading;
			[Component] public readonly record struct Position(int Value);
			[Bundle] public readonly partial record struct Particle(Position Position);
			public static partial class Systems
			{
				public static readonly ConcurrentDictionary<int, byte> Threads = new();
				public static readonly Barrier Together = new(4);
				public static bool RequireOverlap;
				public static int Calls;
				[{{attribute}}]
				public static void Increment(scoped ref Position position)
				{
					bool firstOnThread = Threads.TryAdd(Environment.CurrentManagedThreadId, 0);
					if (RequireOverlap && firstOnThread && !Together.SignalAndWait(TimeSpan.FromSeconds(10)))
						throw new InvalidOperationException("The requested partitions did not overlap.");
					Interlocked.Increment(ref Calls);
					position = new(position.Value + 1);
				}
			}
			[EcsModule, IncludeSystems(typeof(Systems)), IncludeBundle<Particle>] public static partial class Game { }
			[Schedule, Run(typeof(Systems), nameof(Systems.Increment))] public partial class Simulation { }
			public static class Acceptance
			{
				public static int Run()
				{
					Systems.RequireOverlap = {{(requireOverlap ? "true" : "false")}};
					using var world = new WorldBuilder().UseParallelExecution(4).WithChunkBytes(256).AddModule(Game.Module).Build();
					var entities = new Entity[10000];
					for (int index = 0; index < entities.Length; index++) entities[index] = world.Spawn(new Particle(new(index)));
					using var simulation = Simulation.Create(world); simulation.Run();
					if (Systems.Calls != entities.Length) return -1;
					for (int index = 0; index < entities.Length; index++) if (world.Get<Position>(entities[index]).Value != index + 1) return -2;
					return Systems.Threads.Count;
				}
			}
			""");
		Execute(assembly).Should().Be(expectedThreads);
	}

	[Fact]
	public void Generated_declarations_preserve_global_file_block_and_nested_namespaces()
	{
		const string declarations = """
			[Archevolt.Component] public readonly record struct Position(int Value);
			[Archevolt.Bundle] public readonly partial record struct Particle(Position Position);
			[Archevolt.Query, Archevolt.Write<Position>] public readonly ref partial struct Moving { }
			public static partial class Systems
			{
				[Archevolt.EcsSystem] private static void Increment(scoped Moving moving)
				{ foreach (var row in moving) row.Position = new(row.Position.Value + 1); }
			}
			[Archevolt.EcsModule, Archevolt.IncludeSystems(typeof(Systems)), Archevolt.IncludeBundle<Particle>]
			public static partial class Game { }
			[Archevolt.Schedule, Archevolt.Run(typeof(Systems), "Increment")] public partial class Simulation { }
			public static class Acceptance
			{
				public static int Run()
				{
					using var world = new Archevolt.WorldBuilder().AddModule(Game.Module).Build();
					var entity = world.Spawn(new Particle(new(10)));
					using var schedule = Simulation.Create(world); schedule.Run();
					return world.Get<Position>(entity).Value;
				}
			}
			""";
		var assembly = CompileAndLoad(declarations, "namespace File;\n" + declarations,
			"namespace Block {\n" + declarations + "\n}",
			"namespace @class { public static partial class Outer {\n" + declarations + "\n} }");
		foreach (var type in new[] { "Acceptance", "File.Acceptance", "Block.Acceptance", "class.Outer+Acceptance" })
			Execute(assembly, type).Should().Be(11);
	}

	[Fact]
	public void Optional_reads_and_change_filters_observe_independent_system_intervals()
	{
		var assembly = CompileAndLoad("""
			using Archevolt;
			[Component, TrackChanges] public readonly record struct Position(int Value);
			[Component] public readonly record struct Velocity(int Value);
			[Bundle] public readonly partial record struct Single(Position Position);
			[Bundle] public readonly partial record struct Pair(Position Position, Velocity Velocity);
			[Query, Read<Position>, Optional<Velocity>, Changed<Position>] public readonly ref partial struct Changes { }
			public static partial class Systems
			{
				public static int Count, Sum;
				[EcsSystem] public static void Observe(scoped Changes changes)
				{ foreach (var row in changes) { Count++; Sum += row.Position.Value + (row.HasVelocity ? row.Velocity.Value : 0); } }
				[Each] public static void Write(scoped ref Position position) => position = new(position.Value + 1);
			}
			[EcsModule, IncludeSystems(typeof(Systems)), IncludeBundle<Single>, IncludeBundle<Pair>] public static partial class Game { }
			[Schedule, Run(typeof(Systems), nameof(Systems.Observe))] public partial class Observer { }
			[Schedule, Run(typeof(Systems), nameof(Systems.Write))] public partial class Writer { }
			public static class Acceptance
			{
				public static int Run()
				{
					using var world = new WorldBuilder().AddModule(Game.Module).Build();
					world.Spawn(new Single(new(1))); world.Spawn(new Pair(new(2), new(10)));
					using var observer = Observer.Create(world); using var writer = Writer.Create(world);
					observer.Run(); observer.Run();
					if (Systems.Count != 2 || Systems.Sum != 13) return -1;
					writer.Run(); writer.Run(); observer.Run();
					return Systems.Count * 100 + Systems.Sum;
				}
			}
			""");
		Execute(assembly).Should().Be(430);
	}

	[Fact]
	public void Semantic_aliases_allow_components_with_the_same_simple_name()
	{
		var assembly = CompileAndLoad("""
			using Archevolt;
			using FirstPosition = First.Position;
			namespace First { [Component] public readonly record struct Position(int Value); }
			namespace Second { [Component] public readonly record struct Position(int Value); }
			[Bundle] public readonly partial record struct Pair(FirstPosition First, Second.Position Second);
			[Query, Read<FirstPosition>(Name = "First"), Read<Second.Position>(Name = "Second")] public readonly ref partial struct Both { }
			public static partial class Systems
			{
				public static int Sum;
				[EcsSystem] public static void Read(scoped Both rows) { foreach (var row in rows) Sum += row.First.Value + row.Second.Value; }
			}
			[EcsModule, IncludeSystems(typeof(Systems)), IncludeBundle<Pair>] public static partial class Game { }
			[Schedule, Run(typeof(Systems), nameof(Systems.Read))] public partial class Simulation { }
			public static class Acceptance
			{
				public static int Run()
				{
					using var world = new WorldBuilder().AddModule(Game.Module).Build(); world.Spawn(new Pair(new(5), new(7)));
					using var simulation = Simulation.Create(world); simulation.Run(); return Systems.Sum;
				}
			}
			""");
		Execute(assembly).Should().Be(12);
	}

	[Fact]
	public void Message_resources_and_local_state_bind_independent_parameter_slots()
	{
		var assembly = CompileAndLoad("""
			using Archevolt;
			[Message] public readonly record struct Ping(int Value);
			[Resource] public readonly record struct Total(int Value);
			public static partial class Systems
			{
				[EcsSystem] public static void Produce(scoped Local<int> sequence, scoped MessageWriter<Ping> writer)
				{ sequence.Value++; writer.Send(new Ping(sequence.Value)); }
				[EcsSystem] public static void Consume(scoped MessageReader<Ping> reader, scoped ResMut<Total> total)
				{ foreach (var message in reader) total.Value = new Total(total.Value.Value + message.Value); }
			}
			[EcsModule, IncludeSystems(typeof(Systems))] public static partial class Game { }
			[Schedule, Run(typeof(Systems), nameof(Systems.Produce), Order = 0),
			 Run(typeof(Systems), nameof(Systems.Consume), Order = 1, AfterOwner = typeof(Systems), After = nameof(Systems.Produce))]
			public partial class Simulation { }
			[Schedule, Run(typeof(Systems), nameof(Systems.Consume))] public partial class OtherReader { }
			public static class Acceptance
			{
				public static int Run()
				{
					using var world = new WorldBuilder().AddModule(Game.Module).Build(); world.InsertResource(new Total(0));
					using var schedule = Simulation.Create(world); schedule.Run(); schedule.Run();
					if (world.GetResource<Total>().Value != 3) return -1;
					using var other = OtherReader.Create(world); other.Run(); return world.GetResource<Total>().Value;
				}
			}
			""");
		Execute(assembly).Should().Be(6);
	}

	[Fact]
	public void Generated_schedules_preserve_instance_keys_ordinals_and_explicit_edges()
	{
		var assembly = CompileAndLoad("""
			using Archevolt;
			public static partial class Systems
			{
				public static int Result;
				[EcsSystem] public static void Step(scoped Local<int> local) { local.Value++; Result = Result * 10 + local.Value; }
			}
			[EcsModule, IncludeSystems(typeof(Systems))] public static partial class Game { }
			[Schedule, Run(typeof(Systems), nameof(Systems.Step), Instance = "first", Order = 0, After = "second"),
			 Run(typeof(Systems), nameof(Systems.Step), Instance = "second", Order = 1)] public partial class Simulation { }
			public static class Acceptance
			{
				public static int Run()
				{
					using var world = new WorldBuilder().AddModule(Game.Module).Build(); using var simulation = Simulation.Create(world);
					if (simulation.Plan.NodeOrder[0] != 1) return -1;
					simulation.Run(); simulation.Run(); return Systems.Result;
				}
			}
			""");
		Execute(assembly).Should().Be(1122);
	}

	[Fact]
	public void Readonly_conditions_keep_the_skipped_body_change_cursor_and_private_invocation()
	{
		var assembly = CompileAndLoad("""
			using Archevolt;
			[Component, TrackChanges] public readonly record struct Position(int Value);
			[Resource] public readonly record struct Enabled(bool Value);
			[Bundle] public readonly partial record struct Particle(Position Position);
			[Query, Read<Position>, Changed<Position>] public readonly ref partial struct Changes { }
			public static partial class Systems
			{
				public static int Seen;
				[RunCondition] private static bool EnabledNow(scoped Res<Enabled> enabled, scoped Local<int> evaluations)
				{ evaluations.Value++; return enabled.Value.Value && evaluations.Value >= 2; }
				[EcsSystem] public static void Observe(scoped Changes changes) { foreach (var row in changes) Seen += row.Position.Value; }
			}
			[EcsModule, IncludeSystems(typeof(Systems)), IncludeBundle<Particle>] public static partial class Game { }
			[Schedule, Run(typeof(Systems), nameof(Systems.Observe), ConditionOwner = typeof(Systems), Condition = "EnabledNow")]
			public partial class Simulation { }
			public static class Acceptance
			{
				public static int Run()
				{
					using var world = new WorldBuilder().AddModule(Game.Module).Build(); world.InsertResource(new Enabled(false));
					var entity = world.Spawn(new Particle(new(1))); using var simulation = Simulation.Create(world); simulation.Run();
					world.Insert(entity, new Position(7)); world.InsertResource(new Enabled(true)); simulation.Run(); simulation.Run();
					return Systems.Seen;
				}
			}
			""");
		Execute(assembly).Should().Be(7);
	}

	[Fact]
	public void Commands_publish_spawned_bundles_before_explicitly_ordered_queries()
	{
		var assembly = CompileAndLoad("""
			using Archevolt;
			[Component, TrackChanges] public readonly record struct Position(int Value);
			[Bundle] public readonly partial record struct Particle(Position Position);
			[Query, Read<Position>, Added<Position>] public readonly ref partial struct AddedRows { }
			public static partial class Systems
			{
				public static int Seen;
				[EcsSystem] public static void Spawn(scoped Commands commands, scoped Local<int> sequence)
				{ sequence.Value++; commands.Spawn(new Particle(new Position(sequence.Value))); }
				[EcsSystem] public static void Observe(scoped AddedRows rows) { foreach (var row in rows) Seen += row.Position.Value; }
			}
			[EcsModule, IncludeSystems(typeof(Systems)), IncludeBundle<Particle>] public static partial class Game { }
			[Schedule, Run(typeof(Systems), nameof(Systems.Spawn), Order = 0),
			 Run(typeof(Systems), nameof(Systems.Observe), Order = 1, AfterOwner = typeof(Systems), After = nameof(Systems.Spawn))]
			public partial class Simulation { }
			public static class Acceptance
			{
				public static int Run()
				{
					using var world = new WorldBuilder().AddModule(Game.Module).Build(); using var simulation = Simulation.Create(world);
					simulation.Run(); simulation.Run(); return Systems.Seen;
				}
			}
			""");
		Execute(assembly).Should().Be(3);
	}

	[Fact]
	public void Stages_and_sets_expand_to_dependency_edges_before_conflict_planning()
	{
		var assembly = CompileAndLoad("""
			using Archevolt;
			public static partial class Systems
			{
				public static int Order;
				[EcsSystem] public static void First() => Order = Order * 10 + 1;
				[EcsSystem] public static void Second() => Order = Order * 10 + 2;
				[EcsSystem] public static void Third() => Order = Order * 10 + 3;
			}
			[EcsModule, IncludeSystems(typeof(Systems))] public static partial class Game { }
			[Schedule, Stage("update", Order = 0), Stage("finish", Order = 1), SystemSet("early", Order = 0), SystemSet("late", Order = 1),
			 Run(typeof(Systems), nameof(Systems.Third), Order = 0, Stage = "finish", Set = "late"),
			 Run(typeof(Systems), nameof(Systems.Second), Order = 1, Stage = "update", Set = "late"),
			 Run(typeof(Systems), nameof(Systems.First), Order = 2, Stage = "update", Set = "early")]
			public partial class Simulation { }
			public static class Acceptance
			{
				public static int Run()
				{
					using var world = new WorldBuilder().AddModule(Game.Module).Build(); using var simulation = Simulation.Create(world);
					simulation.Run(); return Systems.Order;
				}
			}
			""");
		Execute(assembly).Should().Be(123);
	}

	[Fact]
	public void Optional_resources_and_managed_resources_use_their_declared_capabilities()
	{
		var assembly = CompileAndLoad("""
			using Archevolt;
			[Resource] public readonly record struct OptionalValue(int Value);
			[Resource] public sealed class Owner { public int Value; }
			public static partial class Systems
			{
				[EcsSystem] public static void Read(scoped OptionalRes<OptionalValue> optional, scoped ManagedRes<Owner> owner)
				{ owner.Value.Value += optional.HasValue ? optional.Value.Value : 1; }
			}
			[EcsModule, IncludeSystems(typeof(Systems))] public static partial class Game { }
			[Schedule, Run(typeof(Systems), nameof(Systems.Read))] public partial class Simulation { }
			public static class Acceptance
			{
				public static int Run()
				{
					using var world = new WorldBuilder().AddModule(Game.Module).Build(); var owner = new Owner(); world.InsertResource(owner);
					using var simulation = Simulation.Create(world); simulation.Run(); world.InsertResource(new OptionalValue(7)); simulation.Run();
					world.RemoveResource<OptionalValue>(); simulation.Run(); return owner.Value;
				}
			}
			""");
		Execute(assembly).Should().Be(9);
	}

	[Fact]
	public void Included_modules_and_removed_readers_register_component_only_capabilities()
	{
		var assembly = CompileAndLoad("""
			using Archevolt;
			[Component] public readonly record struct Position(int Value);
			[Bundle] public readonly partial record struct Particle(Position Position);
			public static partial class Systems
			{
				public static int Seen;
				[ EcsSystem ] public static void Read(scoped RemovedComponents<Position> removed) { foreach (var removal in removed) Seen++; }
			}
			[EcsModule, IncludeBundle<Particle>] public static partial class BaseGame { }
			[EcsModule, IncludeModule(typeof(BaseGame)), IncludeSystems(typeof(Systems))] public static partial class Game { }
			[Schedule, Run(typeof(Systems), nameof(Systems.Read))] public partial class Simulation { }
			public static class Acceptance
			{
				public static int Run()
				{
					using var world = new WorldBuilder().AddModule(Game.Module).Build(); var entity = world.Spawn(new Particle(new(4)));
					using var simulation = Simulation.Create(world); world.Remove<Position>(entity); simulation.Run(); simulation.Run(); return Systems.Seen;
				}
			}
			""");
		Execute(assembly).Should().Be(1);
	}

	[Fact]
	public void Referenced_assembly_private_systems_use_exported_descriptors_and_access_metadata()
	{
		var library = Generate("""
			using Archevolt;
			namespace Library;
			[Component] public readonly record struct Position(int Value);
			[Bundle] public readonly partial record struct Particle(Position Position);
			public static partial class Systems { [Each] private static void Hidden(scoped ref Position position) => position = new(position.Value + 1); }
			[EcsModule, IncludeSystems(typeof(Systems)), IncludeBundle<Particle>] public static partial class Game { }
			""");
		library.Diagnostics.Should().BeEmpty();
		using var stream = new MemoryStream();
		var emission = library.Output.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
		emission.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
		var bytes = stream.ToArray();
		stream.Position = 0;
		AssemblyLoadContext.Default.LoadFromStream(stream);
		var consumer = GenerateCore(["""
			using Archevolt;
			using Library;
			[Schedule, Run(typeof(Systems), "Hidden")] public partial class Simulation { }
			public static class Acceptance
			{
				public static int Run()
				{
					using var world = new WorldBuilder().AddModule(Game.Module).Build(); var entity = world.Spawn(new Particle(new(10)));
					using var simulation = Simulation.Create(world); simulation.Run(); return world.Get<Position>(entity).Value;
				}
			}
			"""], [MetadataReference.CreateFromImage(bytes)]);
		consumer.Diagnostics.Should().BeEmpty();
		Execute(LoadCompilation(consumer.Output)).Should().Be(11);
	}

	[Fact]
	public void Unrelated_method_edits_preserve_equatable_generated_query_models()
	{
		var result = Generate("""
			using Archevolt;
			[Component] public readonly record struct Position(int Value);
			[Query, Read<Position>] public readonly ref partial struct Moving { }
			public static class Other { public static int Value() => 1; }
			""");
		var original = result.Input.SyntaxTrees.Single();
		var replacement = CSharpSyntaxTree.ParseText(original.GetText(TestContext.Current.CancellationToken).ToString().Replace("=> 1", "=> 2"),
			(CSharpParseOptions)original.Options, cancellationToken: TestContext.Current.CancellationToken);
		var modified = result.Input.ReplaceSyntaxTree(original, replacement);
		var driver = result.Driver.RunGeneratorsAndUpdateCompilation(modified, out _, out var diagnostics, TestContext.Current.CancellationToken);
		diagnostics.Should().BeEmpty();
		driver.GetRunResult().Results.Single().TrackedSteps["QueryAttributeModel"].SelectMany(static step => step.Outputs)
			.Select(static output => output.Reason).Should().OnlyContain(static reason => reason == IncrementalStepRunReason.Unchanged || reason == IncrementalStepRunReason.Cached);
	}

	[Fact]
	public void Each_moves_100000_entities_and_refreshes_membership_after_tag_changes()
	{
		var assembly = CompileAndLoad("""
			using Archevolt;
			[Component] public readonly record struct Position(int Value);
			[Component] public readonly record struct Velocity(int Value);
			[Component] public readonly struct Frozen { }
			[Bundle] public readonly partial record struct Particle(Position Position, Velocity Velocity);
			public static partial class Motion
			{
				[Each, Without<Frozen>]
				private static void Integrate(scoped ref Position position, scoped in Velocity velocity) => position = new(position.Value + velocity.Value);
			}
			[EcsModule, IncludeSystems(typeof(Motion)), IncludeBundle<Particle>]
			public static partial class Game { }
			[Schedule, Run(typeof(Motion), "Integrate", Order = 0)]
			public sealed partial class Simulation { }
			public static class Acceptance
			{
				public static int Run()
				{
					using var world = new WorldBuilder().AddModule(Game.Module).Build();
					var entities = new Entity[100000];
					for (int i = 0; i < entities.Length; i++) entities[i] = world.Spawn(new Particle(new(i), new(2)));
					using var simulation = Simulation.Create(world);
					simulation.Run();
					for (int i = 0; i < entities.Length; i++) if (world.Get<Position>(entities[i]).Value != i + 2) return -1;
					world.Insert(entities[0], new Frozen());
					simulation.Run();
					if (world.Get<Position>(entities[0]).Value != 2 || world.Get<Position>(entities[99999]).Value != 100003) return -2;
					world.Remove<Frozen>(entities[0]);
					simulation.Run();
					return world.Get<Position>(entities[0]).Value + world.Get<Position>(entities[99999]).Value;
				}
			}
			""");
		Execute(assembly).Should().Be(100009);
	}

	[Fact]
	public void Named_query_resources_and_nested_bundles_execute_the_plan_programming_model()
	{
		var assembly = CompileAndLoad("""
			using Archevolt;
			using System.Numerics;
			namespace Game;
			[Component] public readonly record struct Position(Vector3 Value);
			[Component] public readonly record struct Velocity(Vector3 Value);
			[Component] public readonly struct Frozen { }
			[Resource] public readonly record struct FrameTime(float DeltaSeconds);
			[Bundle] public readonly partial record struct MotionBundle(Position Position, Velocity Velocity);
			[Bundle] public readonly partial record struct Particle(MotionBundle Motion);
			[Query, Write<Position>, Read<Velocity>, Without<Frozen>]
			public readonly ref partial struct Moving { }
			public static partial class Motion
			{
				[EcsSystem]
				public static void Integrate(scoped Moving moving, scoped Res<FrameTime> time)
				{
					foreach (var row in moving) { ref var position = ref row.Position; position = position with { Value = position.Value + row.Velocity.Value * time.Value.DeltaSeconds }; }
				}
			}
			[EcsModule, IncludeSystems(typeof(Motion)), IncludeBundle<Particle>, IncludeResource<FrameTime>]
			public static partial class GameEcs { }
			[Schedule, Run(typeof(Motion), nameof(Motion.Integrate), Order = 0)] public sealed partial class Simulation { }
			public static class Acceptance
			{
				public static int Run()
				{
					using var world = new WorldBuilder().AddModule(GameEcs.Module).Build();
					world.InsertResource(new FrameTime(0.5f));
					var entity = world.Spawn(new Particle(new(new(Vector3.Zero), new(Vector3.UnitX))));
					using var simulation = Simulation.Create(world); simulation.Run(); simulation.Run();
					return (int)world.Get<Position>(entity).Value.X;
				}
			}
			""");
		Execute(assembly, "Game.Acceptance").Should().Be(1);
	}

	[Fact]
	public void Default_queries_reject_enumeration()
	{
		var assembly = CompileAndLoad("""
			using Archevolt;
			[Component] public readonly record struct Position(int Value);
			[Query, Read<Position>] public readonly ref partial struct Moving { }
			public static class Acceptance
			{
				public static int Run()
				{
					try { var query = default(Moving); query.GetEnumerator(); return 0; }
					catch (System.InvalidOperationException) { return 1; }
				}
			}
			""");
		Execute(assembly).Should().Be(1);
	}

	[Theory]
	[InlineData("[Component] public readonly record struct Position(string Value);", "AV002")]
	[InlineData("[Query] public readonly ref struct Moving { }", "AV001")]
	[InlineData("[Component] public readonly record struct P(int Value); [Query, Read<P>, Write<P>] public readonly ref partial struct Moving { }", "AV003")]
	[InlineData("[Component] public readonly record struct P(int Value); public static partial class S { [Each, Without<P>] public static void Run(scoped ref P value) {} }", "AV003")]
	[InlineData("[Component] public readonly record struct P(int Value); public static partial class S { [Each, Without<P>] public static void Run(Entity entity, scoped in P value) {} }", "AV003")]
	[InlineData("[Component] public readonly record struct P(int Value); [Query, Write<P>] public readonly ref partial struct Moving { } public static partial class S { [EcsSystem] public static void Run(scoped Moving a, scoped Moving b) {} }", "AV004")]
	[InlineData("[Component] public readonly record struct P(int Value); public static partial class S { [Each] public static void Run(P value) {} }", "AV005")]
	[InlineData("[Component] public readonly record struct P(int Value); [Query, Changed<P>] public readonly ref partial struct Moving { }", "AV006")]
	[InlineData("public static partial class S { } [Schedule, Run(typeof(S), \"Missing\")] public partial class ScheduleType { }", "AV007")]
	[InlineData("[Component] public readonly struct Tag { } [Query, Read<Tag>] public readonly ref partial struct Moving { }", "AV005")]
	[InlineData("[EcsModule, IncludeResource<int>] public static partial class Game { }", "AV002")]
	[InlineData("[Component] public readonly record struct P(int Value); [Bundle] public readonly partial record struct Invalid(P First, P Second);", "AV003")]
	[InlineData("[Component] public readonly record struct P(int Value); [Query, Read<P>] public readonly ref partial struct Rows { } public static partial class S { [EcsSystem] public static void Run(Rows rows) {} }", "AV005")]
	[InlineData("public static partial class S { [EcsSystem] public static async System.Threading.Tasks.Task Run() { await System.Threading.Tasks.Task.Yield(); } }", "AV005")]
	[InlineData("public static partial class S { [EcsSystem] public static void Run<T>() {} }", "AV005")]
	[InlineData("public static partial class S { [EcsSystem] public static void Run() {} public static void Run(int value) {} }", "AV005")]
	[InlineData("public readonly record struct P(int Value); [Query, Read<P>] public readonly ref partial struct Rows { }", "AV009")]
	[InlineData("[Component] public readonly record struct P(int Value); public static partial class S { [Each(Partitioned = true)] public static void Run(scoped ref P value, scoped Local<int> local) {} }", "AV010")]
	[InlineData("[Resource] public readonly record struct R(int Value); public static partial class S { [RunCondition] public static bool Enabled(scoped ResMut<R> value) => true; }", "AV010")]
	[InlineData("public static partial class S { [EcsSystem] public static void A() {} [EcsSystem] public static void B() {} } [Schedule, Run(typeof(S), \"A\", Order = 0, After = \"B\"), Run(typeof(S), \"B\", Order = 1, After = \"A\")] public partial class Simulation { }", "AV007")]
	[InlineData("[Resource] public readonly record struct R(int Value); public static partial class S { [EcsSystem] public static void A(scoped ResMut<R> value) {} [EcsSystem] public static void B(scoped ResMut<R> value) {} } [Schedule, Run(typeof(S), \"A\", Order = 0), Run(typeof(S), \"B\", Order = 1)] public partial class Simulation { }", "AV008")]
	[InlineData("public static partial class S { [EcsSystem] public static void A() {} } [Schedule, Stage(\"update\"), Run(typeof(S), \"A\")] public partial class Simulation { }", "AV007")]
	[InlineData("public static partial class S { [EcsSystem] public static void A() {} } [Schedule, Run(typeof(S), \"A\", Set = \"missing\")] public partial class Simulation { }", "AV007")]
	[InlineData("[Schedule, Stage(\"same\", Order = 0), Stage(\"same\", Order = 1)] public partial class Simulation { }", "AV007")]
	public void Invalid_declarations_report_actionable_diagnostics(string declaration, string expectedDiagnostic)
	{
		var result = Generate("using Archevolt;\n" + declaration);
		result.Diagnostics.Select(static diagnostic => diagnostic.Id).Should().Contain(expectedDiagnostic);
	}

	[Fact]
	public void Each_entity_parameter_allows_excluding_a_different_component()
	{
		var assembly = CompileAndLoad("""
			using Archevolt;
			[Component] public readonly record struct P(int Value);
			[Component] public readonly struct Hidden { }
			[Bundle] public readonly partial record struct Particle(P P);
			public static partial class Systems
			{
				[Each, Without<Hidden>]
				public static void Increment(Entity entity, scoped ref P value) => value = new(value.Value + 1);
			}
			[EcsModule, IncludeSystems(typeof(Systems)), IncludeBundle<Particle>] public static partial class Game { }
			[Schedule, Run(typeof(Systems), nameof(Systems.Increment))] public partial class Simulation { }
			public static class Acceptance
			{
				public static int Run()
				{
					using var world = new WorldBuilder().AddModule(Game.Module).Build();
					var visible = world.Spawn(new Particle(new(4)));
					var hidden = world.Spawn(new Particle(new(8)));
					world.Insert(hidden, new Hidden());
					using var simulation = Simulation.Create(world);
					simulation.Run();
					return world.Get<P>(visible).Value * 10 + world.Get<P>(hidden).Value;
				}
			}
			""");
		Execute(assembly).Should().Be(58);
	}

	[Fact]
	public void Readonly_query_values_cannot_be_replaced()
	{
		var result = Generate("""
			using Archevolt;
			[Component] public readonly record struct Position(int Value);
			[Query, Read<Position>] public readonly ref partial struct Moving { }
			public static partial class Systems
			{
				[EcsSystem] public static void Invalid(scoped Moving moving) { foreach (var row in moving) row.Position = new Position(2); }
			}
			""");
		result.Diagnostics.Should().BeEmpty();
		result.Output.GetDiagnostics(TestContext.Current.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
			.Select(static diagnostic => diagnostic.Id).Should().Contain("CS8331");
	}

	[Theory]
	[InlineData("static void Invalid(scoped Moving moving) { foreach (var row in moving) row.Position.Value = 2; }", "CS8852")]
	[InlineData("static (Moving, int) Invalid() => default;", "CS9244")]
	[InlineData("static Moving Invalid(scoped Moving moving) => moving;", "CS8352")]
	public void Generated_queries_preserve_csharp_initialization_tuple_and_scoped_escape_rules(string method, string diagnostic)
	{
		var result = Generate($$"""
			using Archevolt;
			[Component] public readonly record struct Position(int Value);
			[Query, Write<Position>] public readonly ref partial struct Moving { }
			public static class InvalidCode { {{method}} }
			""");
		result.Diagnostics.Should().BeEmpty();
		result.Output.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(static item => item.Severity == DiagnosticSeverity.Error)
			.Select(static item => item.Id).Should().Contain(diagnostic);
	}

	static int Execute(Assembly assembly, string type = "Acceptance") => (int)assembly.GetType(type)!.GetMethod("Run")!.Invoke(null, null)!;

	static Assembly CompileAndLoad(params string[] sources)
	{
		var result = Generate(sources);
		result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
		return LoadCompilation(result.Output);
	}

	static Assembly LoadCompilation(Compilation output)
	{
		using var stream = new MemoryStream();
		var emit = output.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
		emit.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
		emit.Success.Should().BeTrue();
		stream.Position = 0;
		return AssemblyLoadContext.Default.LoadFromStream(stream);
	}

	static (Compilation Output, IReadOnlyList<Diagnostic> Diagnostics, GeneratorDriver Driver, Compilation Input) Generate(params string[] sources) => GenerateCore(sources, []);

	static (Compilation Output, IReadOnlyList<Diagnostic> Diagnostics, GeneratorDriver Driver, Compilation Input) GenerateCore(string[] sources, MetadataReference[] additionalReferences)
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp14);
		var compilation = CSharpCompilation.Create("Consumer_" + Guid.NewGuid().ToString("N"),
			sources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions, cancellationToken: cancellationToken)), References().Concat(additionalReferences),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
		var driver = CSharpGeneratorDriver.Create([new ArchevoltGenerator().AsSourceGenerator()], parseOptions: parseOptions,
			driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true))
			.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics, cancellationToken);
		return (output, diagnostics, driver, compilation);
	}

	static IEnumerable<MetadataReference> References()
	{
		var frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
		return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
			.Where(path => Path.GetDirectoryName(path) == frameworkDirectory).Append(typeof(ComponentAttribute).Assembly.Location)
			.Select(static path => MetadataReference.CreateFromFile(path));
	}
}
