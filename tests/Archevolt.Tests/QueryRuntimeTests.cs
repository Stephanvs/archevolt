namespace Archevolt.Tests;

public sealed class QueryRuntimeTests
{
    private readonly record struct Position(int Value);
    private readonly record struct Velocity(int Value);
    private readonly record struct Extra(int Value);
    private readonly record struct Step(int Value);
    private readonly struct Frozen;
    private readonly record struct Particle(Position Position, Velocity Velocity) : IBundle<Particle>
    {
        public static BundleDescriptor Descriptor { get; } = BundleDescriptor.Create<Particle>(
            ComponentDescriptor.Of<Position>(tracked: true), ComponentDescriptor.Of<Velocity>());
        public static void Write(in Particle value, ref BundleWriter writer)
        {
            writer.Put(value.Position);
            writer.Put(value.Velocity);
        }
    }

    private delegate void QueryAction(SystemRun run, QueryState query);
    private sealed class QueryBody(SystemBinder binder, QueryAction action) : SystemBody
    {
        private readonly QueryState _query = binder.Query(0);
        protected override void Run(SystemRun run) => action(run, _query);
    }

    private sealed class Movement(SystemBinder binder) : SystemBody
    {
        private readonly QueryState _query = binder.Query(0);
        private readonly ResourceHandle<Step> _step = binder.Resource<Step>(0);
        protected override void Run(SystemRun run)
        {
            var step = _step.Read(run);
            var cursor = _query.Begin(run);
            while (cursor.Next(out var block))
            {
                var positions = block.Tracked<Position>(0);
                var velocities = block.Read<Velocity>(1);
                for (int row = 0; row < positions.Length; row++)
                {
                    ref Position position = ref positions.Write(row);
                    position = new Position(position.Value + velocities[row].Value * step.Value.Value);
                }
            }
        }
    }

    private sealed class OptionalResourceBody(SystemBinder binder, Action<int?> observe) : SystemBody
    {
        private readonly ResourceHandle<Step> _step = binder.Resource<Step>(0);
        protected override void Run(SystemRun run)
        {
            var step = _step.ReadOptional(run);
            observe(step.HasValue ? step.Value.Value : null);
            if (!step.HasValue)
            {
                bool rejected = false;
                try { _ = step.Value; } catch (InvalidOperationException) { rejected = true; }
                Assert.True(rejected);
            }
        }
    }

    private static World CreateWorld() => new WorldBuilder().AddBundle<Particle>().AddComponent<Extra>()
        .AddComponent<Frozen>(tag: true).AddResource<Step>().Build();

    [Fact]
    public void Bound_query_observes_new_archetypes_and_tag_membership_on_the_next_run()
    {
        using var world = CreateWorld();
        world.InsertResource(new Step(2));
        Entity[] entities = Enumerable.Range(0, 2000)
            .Select(i => world.Spawn(new Particle(new Position(i), new Velocity(3)))).ToArray();
        var shape = QueryShape.Create("moving").Write<Position>(0).Read<Velocity>(1).Without<Frozen>().Build();
        var descriptor = SystemDescriptor.Create("movement", static binder => new Movement(binder)).EachQuery(shape, false).Res<Step>().Build();
        using var schedule = BoundSchedule.Bind(world, descriptor);
        schedule.Run(TestContext.Current.CancellationToken);
        world.Insert(entities[0], default(Frozen));
        world.Insert(entities[1], new Extra(10));
        Entity late = world.Spawn(new Particle(new Position(7), new Velocity(3)));
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(6, world.Get<Position>(entities[0]).Value);
        Assert.Equal(13, world.Get<Position>(entities[1]).Value);
        Assert.Equal(13, world.Get<Position>(late).Value);
        world.Remove<Frozen>(entities[0]);
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(12, world.Get<Position>(entities[0]).Value);
        Assert.Equal(2005 + 12, world.Get<Position>(entities[^1]).Value);
    }

    [Fact]
    public void Tracking_observes_equal_writes_preserves_added_and_ignores_structural_migration()
    {
        using var world = CreateWorld();
        Entity entity = world.Spawn(new Particle(new Position(5), new Velocity(1)));
        var changed = QueryShape.Create("changed").Read<Position>(0).Changed<Position>(0).Build();
        var added = QueryShape.Create("added").Read<Position>(0).Added<Position>(0).Build();
        int changedCount = 0, addedCount = 0;
        QueryAction countChanged = (run, query) => { changedCount = Count(query.Begin(run)); };
        QueryAction countAdded = (run, query) => { addedCount = Count(query.Begin(run)); };
        using var changes = BoundSchedule.Bind(world, SystemDescriptor.Create("changed", b => new QueryBody(b, countChanged)).Query(changed).Build());
        using var additions = BoundSchedule.Bind(world, SystemDescriptor.Create("added", b => new QueryBody(b, countAdded)).Query(added).Build());
        changes.Run(TestContext.Current.CancellationToken);
        additions.Run(TestContext.Current.CancellationToken);
        Assert.Equal(1, changedCount);
        Assert.Equal(1, addedCount);
        world.Insert(entity, new Extra(2));
        world.Remove<Extra>(entity);
        changes.Run(TestContext.Current.CancellationToken);
        additions.Run(TestContext.Current.CancellationToken);
        Assert.Equal(0, changedCount);
        Assert.Equal(0, addedCount);
        world.Insert(entity, new Position(5));
        changes.Run(TestContext.Current.CancellationToken);
        additions.Run(TestContext.Current.CancellationToken);
        Assert.Equal(1, changedCount);
        Assert.Equal(0, addedCount);

        var write = QueryShape.Create("write").Write<Position>(0).Build();
        bool acquire = false;
        QueryAction touch = (run, query) =>
        {
            var cursor = query.Begin(run);
            while (cursor.Next(out var block))
            {
                var reference = block.Tracked<Position>(0).At(0);
                if (acquire)
                {
                    ref Position position = ref reference.Value;
                    position = position with { Value = position.Value };
                }
            }
        };
        using var writer = BoundSchedule.Bind(world, SystemDescriptor.Create("write", b => new QueryBody(b, touch)).Query(write).Build());
        writer.Run(TestContext.Current.CancellationToken);
        changes.Run(TestContext.Current.CancellationToken);
        Assert.Equal(0, changedCount);
        acquire = true;
        writer.Run(TestContext.Current.CancellationToken);
        changes.Run(TestContext.Current.CancellationToken);
        Assert.Equal(1, changedCount);
    }

    private static int Count(QueryCursor cursor)
    {
        int count = 0;
        while (cursor.Next(out var block))
            for (int row = 0; row < block.Count; row++)
                if (block.MatchesRow(row))
                    count++;
        return count;
    }

    [Fact]
    public void Optional_only_query_matches_absent_values_and_binds_presence_per_archetype()
    {
        using var world = new WorldBuilder().AddComponent<Extra>().Build();
        Entity absent = world.Spawn();
        Entity present = world.Spawn();
        world.Insert(present, new Extra(9));
        var observed = new Dictionary<Entity, int?>();
        QueryAction observe = (run, query) =>
        {
            var cursor = query.Begin(run);
            while (cursor.Next(out var block))
            {
                bool has = block.TryRead<Extra>(0, out var values);
                for (int row = 0; row < block.Count; row++)
                    observed[block.Entities[row]] = has ? values[row].Value : null;
            }
        };
        var shape = QueryShape.Create("optional").Optional<Extra>(0).Build();
        using var schedule = BoundSchedule.Bind(world, SystemDescriptor.Create("optional", b => new QueryBody(b, observe)).Query(shape).Build());
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(2, observed.Count);
        Assert.Null(observed[absent]);
        Assert.Equal(9, observed[present]);
    }

    [Fact]
    public void Execution_capabilities_reject_undeclared_access_and_immediate_mutation()
    {
        using var world = CreateWorld();
        Entity entity = world.Spawn(new Particle(new Position(4), new Velocity(1)));
        var shape = QueryShape.Create("read").Read<Position>(0).Build();
        int rejected = 0;
        QueryAction check = (run, query) =>
        {
            var cursor = query.Begin(run);
            Assert.True(cursor.Next(out var block));
            try { block.Write<Position>(0); } catch (InvalidOperationException) { rejected++; }
            try { block.Read<Extra>(0); } catch (InvalidOperationException) { rejected++; }
            try { query.Begin(default); } catch (InvalidOperationException) { rejected++; }
            Assert.Throws<InvalidOperationException>(() => world.Insert(entity, new Position(99)));
            Assert.Throws<InvalidOperationException>(() => world.Dispose());
        };
        using var schedule = BoundSchedule.Bind(world, SystemDescriptor.Create("read", b => new QueryBody(b, check)).Query(shape).Build());
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(3, rejected);
        Assert.Equal(4, world.Get<Position>(entity).Value);
        Assert.Throws<InvalidOperationException>(EnumerateDefault);
    }

    private static void EnumerateDefault()
    {
        QueryCursor cursor = default;
        cursor.Next(out _);
    }

    [Fact]
    public void Binding_rejects_aliasing_queries_and_accepts_supported_disjointness()
    {
        var left = QueryShape.Create("left").Write<Position>(0).With<Frozen>().Build();
        var overlap = QueryShape.Create("overlap").Optional<Position>(0).Build();
        Assert.Throws<ArgumentException>(() => SystemDescriptor.Create("bad", static b => new QueryBody(b, static (_, _) => { }))
            .Query(left).Query(overlap).Build());
        var right = QueryShape.Create("right").Write<Position>(0).Without<Frozen>().Build();
        using var world = CreateWorld();
        var valid = SystemDescriptor.Create("disjoint", static b => new QueryBody(b, static (_, _) => { })).Query(left).Query(right).Build();
        using var schedule = BoundSchedule.Bind(world, valid);
        schedule.Run(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Missing_required_resource_is_checked_again_after_idle_removal()
    {
        using var world = CreateWorld();
        world.InsertResource(new Step(1));
        var shape = QueryShape.Create("moving").Write<Position>(0).Read<Velocity>(1).Build();
        var descriptor = SystemDescriptor.Create("move", static b => new Movement(b)).Query(shape).Res<Step>().Build();
        using var schedule = BoundSchedule.Bind(world, descriptor);
        world.RemoveResource<Step>();
        Assert.Throws<InvalidOperationException>(() => schedule.Run(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Optional_resource_observes_idle_insertion_and_removal_without_rebinding()
    {
        using var world = new WorldBuilder().AddResource<Step>().Build();
        var values = new List<int?>();
        var descriptor = SystemDescriptor.Create("optional", b => new OptionalResourceBody(b, values.Add)).Res<Step>(optional: true).Build();
        using var schedule = BoundSchedule.Bind(world, descriptor);
        schedule.Run(TestContext.Current.CancellationToken);
        world.InsertResource(new Step(12));
        schedule.Run(TestContext.Current.CancellationToken);
        world.RemoveResource<Step>();
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(new int?[] { null, 12, null }, values);
    }
}
