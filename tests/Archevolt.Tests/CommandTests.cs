using Archevolt.Scheduling;

namespace Archevolt.Tests;

public sealed class CommandTests
{
    private readonly record struct Position(int Value);
    private readonly record struct Velocity(int Value);
    private readonly record struct Extra(int Value);
    private readonly record struct Particle(Position Position) : IBundle<Particle>
    {
        public static BundleDescriptor Descriptor { get; } = BundleDescriptor.Create<Particle>(ComponentDescriptor.Of<Position>(tracked: true));
        public static void Write(in Particle value, ref BundleWriter writer) => writer.Put(value.Position);
    }
    private readonly record struct Augmented(Position Position, Extra Extra) : IBundle<Augmented>
    {
        public static BundleDescriptor Descriptor { get; } = BundleDescriptor.Create<Augmented>(
            ComponentDescriptor.Of<Position>(tracked: true), ComponentDescriptor.Of<Extra>());
        public static void Write(in Augmented value, ref BundleWriter writer)
        {
            writer.Put(value.Position);
            writer.Put(value.Extra);
        }
    }

    private delegate void ActionBody(SystemRun run, Commands commands, QueryState? query);
    private sealed class CommandBody(SystemBinder binder, ActionBody action, bool query = false) : SystemBody
    {
        private readonly CommandsHandle _commands = binder.Commands();
        private readonly QueryState? _query = query ? binder.Query(0) : null;
        protected override void Run(SystemRun run) => action(run, _commands.Open(run), _query);
    }
    private delegate void QueryAction(SystemRun run, QueryState query);
    private sealed class QueryBody(SystemBinder binder, QueryAction action) : SystemBody
    {
        private readonly QueryState _query = binder.Query(0);
        protected override void Run(SystemRun run) => action(run, _query);
    }
    private sealed class FalseCondition : ConditionBody
    {
        protected override bool Evaluate(SystemRun run) => false;
    }
    private sealed class CountingBody(Action count) : SystemBody
    {
        protected override void Run(SystemRun run) => count();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Deferred_spawn_is_visible_through_transitive_skipped_dependency_and_next_producer_window(int concurrency)
    {
        using var world = new WorldBuilder().AddBundle<Particle>().UseParallelExecution(concurrency).Build();
        var added = QueryShape.Create("added").Read<Position>(0).Added<Position>(0).Build();
        var handles = new List<Entity>();
        int producerSaw = -1, consumerSaw = -1, consumerValue = -1, skipped = 0;
        ActionBody produce = (run, commands, query) =>
        {
            producerSaw = Count(query!.Begin(run));
            var entity = commands.Spawn(new Particle(new Position(1)));
            Assert.False(query.TryLocate(run, entity, out _, out _));
            commands.Insert(entity, new Position(7));
            handles.Add(entity);
        };
        QueryAction consume = (run, query) =>
        {
            consumerSaw = 0;
            var cursor = query.Begin(run);
            while (cursor.Next(out var block))
                for (int row = 0; row < block.Count; row++)
                    if (block.MatchesRow(row))
                    {
                        consumerSaw++;
                        consumerValue = block.Read<Position>(0)[row].Value;
                    }
        };
        var producer = SystemDescriptor.Create("producer", b => new CommandBody(b, produce, query: true)).Query(added).Commands().Build();
        var bridge = SystemDescriptor.Create("bridge", _ => new CountingBody(() => skipped++)).Build();
        var consumer = SystemDescriptor.Create("consumer", b => new QueryBody(b, consume)).Query(added).Build();
        var condition = SystemDescriptor.Create("false", _ => new FalseCondition()).AsCondition().Build();
        using var schedule = BoundSchedule.Bind(world, [producer, bridge, consumer],
            [new EdgeInput(0, 1), new EdgeInput(1, 2)], conditions: [null, condition, null]);
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(0, producerSaw);
        Assert.Equal(1, consumerSaw);
        Assert.Equal(7, consumerValue);
        Assert.True(world.Contains(handles[0]));
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(1, producerSaw);
        Assert.Equal(1, consumerSaw);
        Assert.Equal(0, skipped);
        Assert.Equal(2, handles.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void An_archetype_created_by_a_flush_is_visible_to_later_systems_and_other_schedules(int concurrency)
    {
        using var world = new WorldBuilder().AddBundle<Particle>().AddComponent<Velocity>().UseParallelExecution(concurrency).Build();
        Entity entity = world.Spawn(new Particle(new Position(3)));
        var moving = QueryShape.Create("moving").Read<Position>(0).Read<Velocity>(1).Build();
        int sameRun = -1, otherSchedule = -1;
        bool insert = false;
        ActionBody produce = (_, commands, _) => { if (insert) commands.Insert(entity, new Velocity(5)); };
        QueryAction consume = (run, query) => sameRun = Count(query.Begin(run));
        QueryAction observe = (run, query) => otherSchedule = Count(query.Begin(run));
        var producer = SystemDescriptor.Create("producer", b => new CommandBody(b, produce)).Commands().Build();
        var consumer = SystemDescriptor.Create("consumer", b => new QueryBody(b, consume)).Query(moving).Build();
        using var schedule = BoundSchedule.Bind(world, [producer, consumer], [new EdgeInput(0, 1)]);
        using var observer = BoundSchedule.Bind(world, SystemDescriptor.Create("observer", b => new QueryBody(b, observe)).Query(moving).Build());

        schedule.Run(TestContext.Current.CancellationToken);
        observer.Run(TestContext.Current.CancellationToken);
        Assert.Equal(0, sameRun);
        Assert.Equal(0, otherSchedule);

        insert = true;
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(1, sameRun);
        observer.Run(TestContext.Current.CancellationToken);
        Assert.Equal(1, otherSchedule);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Each_flush_applies_the_commands_recorded_since_the_flush_before_it(int concurrency)
    {
        using var world = new WorldBuilder().AddBundle<Particle>().UseParallelExecution(concurrency).Build();
        var positions = QueryShape.Create("positions").Read<Position>(0).Build();
        var sums = new List<int>();
        Entity spawned = default;
        ActionBody spawn = (_, commands, _) => spawned = commands.Spawn(new Particle(new Position(1)));
        ActionBody update = (_, commands, _) => commands.Insert(spawned, new Position(2));
        ActionBody despawn = (_, commands, _) => commands.Despawn(spawned);
        QueryAction observe = (run, query) =>
        {
            int sum = 0;
            var cursor = query.Begin(run);
            while (cursor.Next(out var block))
                foreach (var position in block.Read<Position>(0)) sum += position.Value;
            sums.Add(sum);
        };
        SystemDescriptor Producer(string name, ActionBody action) => SystemDescriptor.Create(name, b => new CommandBody(b, action)).Commands().Build();
        SystemDescriptor Observer(string name) => SystemDescriptor.Create(name, b => new QueryBody(b, observe)).Query(positions).Build();
        using var schedule = BoundSchedule.Bind(world,
            [Producer("spawn", spawn), Observer("after spawn"), Producer("update", update), Observer("after update"), Producer("despawn", despawn), Observer("after despawn")],
            Enumerable.Range(0, 5).Select(i => new EdgeInput(i, i + 1)).ToArray());

        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal([1, 2, 0], sums);
        Entity first = spawned;
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal([1, 2, 0, 1, 2, 0], sums);
        Assert.False(world.Contains(first));
        Assert.False(world.Contains(spawned));
    }

    [Fact]
    public void Playback_failure_keeps_completed_writes_and_cancels_the_unapplied_spawn()
    {
        using var world = new WorldBuilder().AddBundle<Particle>().AddComponent<Velocity>().Build();
        Entity existing = world.Spawn(new Particle(new Position(3)));
        Entity reserved = default;
        ActionBody write = (_, commands, _) =>
        {
            commands.Insert(existing, new Position(99));
            commands.Remove<Velocity>(existing);
            reserved = commands.Spawn(new Particle(new Position(8)));
        };
        using var schedule = BoundSchedule.Bind(world,
            SystemDescriptor.Create("producer", b => new CommandBody(b, write)).Commands().Build());
        var error = Assert.Throws<CommandPlaybackException>(() => schedule.Run(TestContext.Current.CancellationToken));
        Assert.Equal("producer", error.Producer);
        Assert.Equal(1, error.CommandIndex);
        Assert.Equal(99, world.Get<Position>(existing).Value);
        Assert.False(world.Contains(reserved));
        Assert.True(schedule.IsFaulted);
        Entity replacement = world.Spawn(new Particle(new Position(4)));
        Assert.Equal(reserved.Index, replacement.Index);
        Assert.NotEqual(reserved.Generation, replacement.Generation);
    }

    [Fact]
    public void Failed_producer_discards_commands_and_invalidates_its_reservation()
    {
        using var world = new WorldBuilder().AddBundle<Particle>().UseParallelExecution(3).Build();
        Entity reserved = default;
        bool fail = true;
        ActionBody produce = (_, commands, _) =>
        {
            reserved = commands.Spawn(new Particle(new Position(8)));
            if (fail)
                throw new ArithmeticException("producer failure");
        };
        using var schedule = BoundSchedule.Bind(world, SystemDescriptor.Create("producer", b => new CommandBody(b, produce)).Commands().Build());
        Assert.Throws<AggregateException>(() => schedule.Run(TestContext.Current.CancellationToken));
        Entity failed = reserved;
        Assert.False(world.Contains(failed));
        fail = false;
        schedule.Reset();
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.True(world.Contains(reserved));
        Assert.NotEqual(failed, reserved);
        Assert.False(world.Contains(failed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancelled_singleton_producer_discards_commands_preserves_failures_and_can_run_after_reset(bool throwAfterCancellation)
    {
        using var world = new WorldBuilder().AddBundle<Particle>().UseParallelExecution(4).Build();
        using var cancellation = new CancellationTokenSource();
        Entity reserved = default;
        bool cancel = true;
        var originalFailure = new ArithmeticException("producer failure");
        ActionBody produce = (_, commands, _) =>
        {
            reserved = commands.Spawn(new Particle(new Position(8)));
            if (!cancel) return;
            cancellation.Cancel();
            if (throwAfterCancellation) throw originalFailure;
        };
        var descriptor = SystemDescriptor.Create("producer", b => new CommandBody(b, produce)).Commands().Build();
        using var schedule = BoundSchedule.Bind(world, [descriptor], [], ["producer instance"]);

        if (throwAfterCancellation)
        {
            var error = Assert.Throws<AggregateException>(() => schedule.Run(cancellation.Token));
            var failure = Assert.IsType<SystemExecutionException>(Assert.Single(error.InnerExceptions));
            Assert.Equal("producer instance", failure.InstanceKey);
            Assert.Equal("producer", failure.SystemName);
            Assert.Equal(0, failure.FirstRow);
            Assert.Equal(int.MaxValue, failure.RowCount);
            Assert.Same(originalFailure, failure.InnerException);
        }
        else
        {
            var error = Assert.Throws<OperationCanceledException>(() => schedule.Run(cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
        Entity discarded = reserved;
        Assert.False(discarded.IsNull);
        Assert.False(world.Contains(discarded));
        Assert.True(schedule.IsFaulted);

        cancel = false;
        schedule.Reset();
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.False(schedule.IsFaulted);
        Assert.Equal(8, world.Get<Position>(reserved).Value);
        Assert.NotEqual(discarded, reserved);
        Assert.False(world.Contains(discarded));
    }

    [Fact]
    public void Try_commands_allow_absent_targets_while_strict_duplicate_despawn_fails()
    {
        using var world = new WorldBuilder().AddBundle<Particle>().AddComponent<Velocity>().Build();
        Entity entity = world.Spawn(new Particle(new Position(5)));
        ActionBody attempt = (_, commands, _) =>
        {
            commands.TryRemove<Velocity>(entity);
            commands.TryRemove<Position>(default);
            commands.TryDespawn(entity);
            commands.TryDespawn(entity);
        };
        using var harmless = BoundSchedule.Bind(world, SystemDescriptor.Create("try", b => new CommandBody(b, attempt)).Commands().Build());
        harmless.Run(TestContext.Current.CancellationToken);
        Assert.False(world.Contains(entity));
        Entity live = world.Spawn(new Particle(new Position(2)));
        ActionBody twice = (_, commands, _) => { commands.Despawn(live); commands.Despawn(live); };
        using var strict = BoundSchedule.Bind(world, SystemDescriptor.Create("strict", b => new CommandBody(b, twice)).Commands().Build());
        var error = Assert.Throws<CommandPlaybackException>(() => strict.Run(TestContext.Current.CancellationToken));
        Assert.Equal(1, error.CommandIndex);
        Assert.False(world.Contains(live));
    }

    [Fact]
    public void Bundle_insertion_replaces_members_and_preserves_unrelated_values_and_added_ticks()
    {
        using var world = new WorldBuilder().AddBundle<Particle>().AddBundle<Augmented>().AddComponent<Velocity>().Build();
        Entity entity = world.Spawn(new Particle(new Position(1)));
        world.Insert(entity, new Velocity(6));
        int additions = 0;
        var shape = QueryShape.Create("added").Read<Position>(0).Added<Position>(0).Build();
        QueryAction observe = (run, query) => additions = Count(query.Begin(run));
        using var observer = BoundSchedule.Bind(world, SystemDescriptor.Create("observe", b => new QueryBody(b, observe)).Query(shape).Build());
        observer.Run(TestContext.Current.CancellationToken);
        Assert.Equal(1, additions);
        ActionBody insert = (_, commands, _) => commands.InsertBundle(entity, new Augmented(new Position(9), new Extra(4)));
        using var schedule = BoundSchedule.Bind(world, SystemDescriptor.Create("insert", b => new CommandBody(b, insert)).Commands().Build());
        schedule.Run(TestContext.Current.CancellationToken);
        observer.Run(TestContext.Current.CancellationToken);
        Assert.Equal(0, additions);
        Assert.Equal(9, world.Get<Position>(entity).Value);
        Assert.Equal(4, world.Get<Extra>(entity).Value);
        Assert.Equal(6, world.Get<Velocity>(entity).Value);
    }

    [Fact]
    public void Parallel_producers_reserve_unique_handles_across_entity_page_growth()
    {
        using var world = new WorldBuilder().AddBundle<Particle>().UseParallelExecution(4).Reserve(commandBytesPerProducer: 100_000).Build();
        var handles = new Entity[4][];
        var systems = new SystemDescriptor[4];
        for (int producer = 0; producer < systems.Length; producer++)
        {
            int slot = producer;
            handles[slot] = new Entity[800];
            ActionBody spawn = (_, commands, _) =>
            {
                for (int i = 0; i < handles[slot].Length; i++)
                    handles[slot][i] = commands.Spawn(new Particle(new Position(slot * 1000 + i)));
            };
            systems[slot] = SystemDescriptor.Create($"producer-{slot}", b => new CommandBody(b, spawn)).Commands().Build();
        }
        using var schedule = BoundSchedule.Bind(world, systems);
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(3200, handles.SelectMany(h => h).Distinct().Count());
        for (int producer = 0; producer < handles.Length; producer++)
            for (int i = 0; i < handles[producer].Length; i++)
                Assert.Equal(producer * 1000 + i, world.Get<Position>(handles[producer][i]).Value);
    }

    [Fact]
    public void Parallel_producers_claim_free_slots_before_fresh_ones_and_discarded_claims_return_them()
    {
        using var world = new WorldBuilder().AddBundle<Particle>().UseParallelExecution(4).Reserve(commandBytesPerProducer: 4096).Build();
        var freed = new Entity[40];
        for (int i = 0; i < freed.Length; i++)
            freed[i] = world.Spawn(new Particle(new Position(-1)));
        Entity kept = world.Spawn(new Particle(new Position(-2)));
        foreach (Entity entity in freed)
            Assert.True(world.Despawn(entity));

        var handles = new Entity[4][];
        bool fail = true;
        var systems = new SystemDescriptor[4];
        for (int producer = 0; producer < systems.Length; producer++)
        {
            int slot = producer;
            handles[slot] = new Entity[25];
            ActionBody spawn = (_, commands, _) =>
            {
                for (int i = 0; i < handles[slot].Length; i++)
                    handles[slot][i] = commands.Spawn(new Particle(new Position(slot * 1000 + i)));
                if (fail && slot == 0)
                    throw new ArithmeticException("producer failure");
            };
            systems[slot] = SystemDescriptor.Create($"producer-{slot}", b => new CommandBody(b, spawn)).Commands().Build();
        }
        using var schedule = BoundSchedule.Bind(world, systems);
        Assert.Throws<AggregateException>(() => schedule.Run(TestContext.Current.CancellationToken));
        // A failure skips producers that have not started, so the number of discarded claims varies.
        var discarded = handles.SelectMany(h => h).Where(e => !e.IsNull).ToArray();
        Assert.Equal(discarded.Length, discarded.Distinct().Count());
        Assert.Equal(Math.Min(discarded.Length, freed.Length), discarded.Count(e => e.Index < freed.Length));
        Assert.All(discarded, e => Assert.False(world.Contains(e)));

        fail = false;
        schedule.Reset();
        schedule.Run(TestContext.Current.CancellationToken);
        var spawned = handles.SelectMany(h => h).ToArray();
        Assert.Empty(spawned.Intersect(discarded));
        Assert.Equal(Enumerable.Range(0, 101).Where(i => i != kept.Index), spawned.Select(e => e.Index).Order());
        for (int producer = 0; producer < handles.Length; producer++)
            for (int i = 0; i < handles[producer].Length; i++)
                Assert.Equal(producer * 1000 + i, world.Get<Position>(handles[producer][i]).Value);
        Assert.Equal(-2, world.Get<Position>(kept).Value);
        Entity idle = world.Spawn(new Particle(new Position(5)));
        Assert.DoesNotContain(idle.Index, spawned.Select(e => e.Index));
        Assert.Equal(5, world.Get<Position>(idle).Value);
    }

    private static int Count(QueryCursor cursor)
    {
        int result = 0;
        while (cursor.Next(out var block))
            for (int row = 0; row < block.Count; row++)
                if (block.MatchesRow(row))
                    result++;
        return result;
    }
}
