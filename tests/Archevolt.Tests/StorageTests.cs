namespace Archevolt.Tests;

[Trait("Category", "Storage")]
public sealed class StorageTests
{
    private readonly record struct Position(int Value);
    private readonly record struct Velocity(int Value);
    private readonly record struct Extra(long Value);
    private readonly struct Frozen;
    private readonly struct Marker<T, U> where T : unmanaged where U : unmanaged;
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

    private sealed class Expected(int position, int velocity)
    {
        internal int Position = position;
        internal int Velocity = velocity;
        internal long? Extra;
        internal bool Frozen;
    }

    private sealed class CollectPositions(SystemBinder binder, List<int> seen) : SystemBody
    {
        private readonly QueryState _query = binder.Query(0);
        protected override void Run(SystemRun run)
        {
            var cursor = _query.Begin(run);
            while (cursor.Next(out var block))
            {
                var positions = block.Read<Position>(0);
                for (int row = 0; row < block.Count; row++)
                    if (block.MatchesRow(row))
                        seen.Add(positions[row].Value);
            }
        }
    }

    private static BoundSchedule Collect(World world, QueryShape shape, List<int> seen) =>
        BoundSchedule.Bind(world, SystemDescriptor.Create(shape.Name, b => new CollectPositions(b, seen)).Query(shape).Build());

    [Fact]
    public void Table_growth_preserves_values_and_change_ticks_of_existing_rows()
    {
        using var world = new WorldBuilder().AddBundle<Particle>().Build();
        var changed = new List<int>();
        var added = new List<int>();
        using var changes = Collect(world, QueryShape.Create("changed").Read<Position>(0).Changed<Position>(0).Build(), changed);
        using var additions = Collect(world, QueryShape.Create("added").Read<Position>(0).Added<Position>(0).Build(), added);
        var entities = new List<Entity>();
        for (int i = 0; i < 5; i++)
            entities.Add(world.Spawn(new Particle(new Position(i), new Velocity(-i))));
        changes.Run(TestContext.Current.CancellationToken);
        additions.Run(TestContext.Current.CancellationToken);
        Assert.Equal([0, 1, 2, 3, 4], changed.Order());
        Assert.Equal([0, 1, 2, 3, 4], added.Order());
        changed.Clear();
        added.Clear();

        world.Insert(entities[3], new Position(1003));
        for (int i = 5; i < 1000; i++)
            entities.Add(world.Spawn(new Particle(new Position(i), new Velocity(-i))));
        changes.Run(TestContext.Current.CancellationToken);
        additions.Run(TestContext.Current.CancellationToken);

        Assert.Equal(Enumerable.Range(5, 995).Append(1003), changed.Order());
        Assert.Equal(Enumerable.Range(5, 995), added.Order());
        for (int i = 0; i < entities.Count; i++)
        {
            Assert.Equal(i == 3 ? 1003 : i, world.Get<Position>(entities[i]).Value);
            Assert.Equal(-i, world.Get<Velocity>(entities[i]).Value);
        }
    }

    [Fact]
    public void Emptied_table_retains_native_memory_until_trimmed()
    {
        using var world = new WorldBuilder().AddBundle<Particle>().AddComponent<Extra>().Build();
        Entity survivor = world.Spawn(new Particle(new Position(-1), new Velocity(-2)));
        world.Insert(survivor, new Extra(42));
        var entities = Enumerable.Range(0, 1000).Select(i => world.Spawn(new Particle(new Position(i), new Velocity(i * 2)))).ToArray();
        long full = world.NativeBytes;

        foreach (var entity in entities)
            Assert.True(world.Despawn(entity));
        Assert.Equal(full, world.NativeBytes);
        entities = Enumerable.Range(0, 1000).Select(i => world.Spawn(new Particle(new Position(i), new Velocity(i * 2)))).ToArray();
        Assert.Equal(full, world.NativeBytes);
        foreach (var entity in entities)
            Assert.True(world.Despawn(entity));

        world.TrimExcess();
        long trimmed = world.NativeBytes;
        Assert.InRange(trimmed, 1, full - 1);
        Assert.Equal(-1, world.Get<Position>(survivor).Value);
        Assert.Equal(-2, world.Get<Velocity>(survivor).Value);
        Assert.Equal(42, world.Get<Extra>(survivor).Value);

        var respawned = Enumerable.Range(0, 20).Select(i => world.Spawn(new Particle(new Position(i + 100), new Velocity(i)))).ToArray();
        Assert.True(world.NativeBytes > trimmed);
        Assert.Equal(Enumerable.Range(100, 20), respawned.Select(e => world.Get<Position>(e).Value));
        Assert.Equal(Enumerable.Range(0, 20), respawned.Select(e => world.Get<Velocity>(e).Value));
        Assert.Equal(-1, world.Get<Position>(survivor).Value);
    }

    [Fact]
    public void Trimming_a_world_with_no_live_entities_releases_all_native_memory()
    {
        using var world = new WorldBuilder().AddBundle<Particle>().AddComponent<Extra>().AddComponent<Frozen>(tag: true).Build();
        var entities = new List<Entity>();
        for (int i = 0; i < 300; i++)
        {
            Entity entity = i % 3 == 0 ? world.Spawn() : world.Spawn(new Particle(new Position(i), new Velocity(i)));
            if (i % 5 == 0)
                world.Insert(entity, new Extra(i));
            if (i % 7 == 0)
                world.Insert(entity, default(Frozen));
            entities.Add(entity);
        }
        long held = world.NativeBytes;
        foreach (var entity in entities)
            Assert.True(world.Despawn(entity));
        Assert.Equal(held, world.NativeBytes);

        world.TrimExcess();

        Assert.Equal(0, world.NativeBytes);
        Entity again = world.Spawn(new Particle(new Position(5), new Velocity(6)));
        Assert.Equal(5, world.Get<Position>(again).Value);
        Assert.Equal(6, world.Get<Velocity>(again).Value);
    }

    [Fact]
    public void Structural_trace_matches_dictionary_across_table_growth_migration_and_swap_removal()
    {
        using var world = new WorldBuilder().AddBundle<Particle>().AddComponent<Extra>()
            .AddComponent<Frozen>(tag: true).Build();
        var expected = new Dictionary<Entity, Expected>();
        var random = new Random(82637);
        for (int i = 0; i < 128; i++)
        {
            Entity entity = world.Spawn(new Particle(new Position(i), new Velocity(-i)));
            expected.Add(entity, new Expected(i, -i));
        }
        for (int operation = 0; operation < 4000; operation++)
        {
            int choice = expected.Count == 0 ? 0 : random.Next(7);
            Entity entity = expected.Count == 0 ? default : expected.Keys.ElementAt(random.Next(expected.Count));
            if (choice == 0)
            {
                int x = random.Next(-1000, 1000);
                int v = random.Next(-1000, 1000);
                entity = world.Spawn(new Particle(new Position(x), new Velocity(v)));
                expected.Add(entity, new Expected(x, v));
            }
            else if (choice == 1)
            {
                int value = random.Next();
                world.Insert(entity, new Position(value));
                expected[entity].Position = value;
            }
            else if (choice == 2)
            {
                long value = random.NextInt64();
                world.Insert(entity, new Extra(value));
                expected[entity].Extra = value;
            }
            else if (choice == 3)
            {
                Assert.Equal(expected[entity].Extra.HasValue, world.Remove<Extra>(entity));
                expected[entity].Extra = null;
            }
            else if (choice == 4)
            {
                world.Insert(entity, default(Frozen));
                expected[entity].Frozen = true;
            }
            else if (choice == 5)
            {
                Assert.Equal(expected[entity].Frozen, world.Remove<Frozen>(entity));
                expected[entity].Frozen = false;
            }
            else
            {
                Assert.True(world.Despawn(entity));
                expected.Remove(entity);
                Assert.False(world.Contains(entity));
                Assert.False(world.TryGet<Position>(entity, out _));
            }

            if (operation % 31 != 0 && operation != 3999)
                continue;
            foreach (var (handle, model) in expected)
            {
                Assert.True(world.Contains(handle));
                Assert.Equal(model.Position, world.Get<Position>(handle).Value);
                Assert.Equal(model.Velocity, world.Get<Velocity>(handle).Value);
                Assert.Equal(model.Frozen, world.Has<Frozen>(handle));
                Assert.Equal(model.Extra.HasValue, world.TryGet<Extra>(handle, out var extra));
                if (model.Extra.HasValue)
                    Assert.Equal(model.Extra.Value, extra.Value);
            }
        }
    }

    [Fact]
    public void Stale_cross_world_and_default_handles_cannot_alias_a_live_entity()
    {
        using var first = new WorldBuilder().AddComponent<Position>().Build();
        using var second = new WorldBuilder().AddComponent<Position>().Build();
        Entity old = first.Spawn();
        Assert.True(first.Despawn(old));
        Entity replacement = first.Spawn();
        first.Insert(replacement, new Position(42));
        Entity foreign = second.Spawn();
        Assert.Equal(old.Index, replacement.Index);
        Assert.NotEqual(old.Generation, replacement.Generation);
        foreach (Entity invalid in new[] { old, foreign, default(Entity) })
        {
            Assert.False(first.Contains(invalid));
            Assert.False(first.TryGet<Position>(invalid, out _));
            Assert.Throws<InvalidOperationException>(() => first.Insert(invalid, new Position(7)));
        }
        Assert.Equal(42, first.Get<Position>(replacement).Value);
    }

    [Fact]
    public void Membership_extends_beyond_one_machine_word()
    {
        var builder = new WorldBuilder();
        RegisterTags<byte>(builder);
        RegisterTags<sbyte>(builder);
        RegisterTags<short>(builder);
        RegisterTags<ushort>(builder);
        RegisterTags<int>(builder);
        RegisterTags<uint>(builder);
        RegisterTags<long>(builder);
        RegisterTags<ulong>(builder);
        RegisterTags<decimal>(builder);
        using var world = builder.Build();
        Entity entity = world.Spawn();
        world.Insert(entity, default(Marker<byte, byte>));
        world.Insert(entity, default(Marker<decimal, ulong>));
        Assert.True(world.Has<Marker<byte, byte>>(entity));
        Assert.True(world.Has<Marker<decimal, ulong>>(entity));
        world.Remove<Marker<byte, byte>>(entity);
        Assert.True(world.Has<Marker<decimal, ulong>>(entity));
        Assert.False(world.Has<Marker<byte, byte>>(entity));
    }

    private static void RegisterTags<T>(WorldBuilder builder) where T : unmanaged
    {
        builder.AddComponent<Marker<T, byte>>(tag: true).AddComponent<Marker<T, sbyte>>(tag: true)
            .AddComponent<Marker<T, short>>(tag: true).AddComponent<Marker<T, ushort>>(tag: true)
            .AddComponent<Marker<T, int>>(tag: true).AddComponent<Marker<T, uint>>(tag: true)
            .AddComponent<Marker<T, long>>(tag: true).AddComponent<Marker<T, ulong>>(tag: true);
    }

    [Fact]
    public void World_checks_owner_thread_and_rejects_operations_after_disposal()
    {
        var world = new WorldBuilder().AddComponent<Position>().Build();
        Entity entity = world.Spawn();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { world.Insert(entity, new Position(1)); }
            catch (Exception exception) { failure = exception; }
        });
        thread.Start();
        thread.Join();
        Assert.IsType<InvalidOperationException>(failure);
        world.Dispose();
        world.Dispose();
        Assert.Throws<ObjectDisposedException>(() => world.Contains(entity));
        Assert.Throws<ObjectDisposedException>(() => world.Spawn());
    }

    [Fact]
    public void Conflicting_component_policies_are_rejected_before_a_world_is_created()
    {
        var builder = new WorldBuilder().AddComponent<Position>().AddComponent<Position>(tracked: true);
        Assert.Throws<ArgumentException>(() => builder.Build());
    }

    [Fact]
    public void Duplicate_modules_are_rejected_while_distinct_modules_can_share_components()
    {
        var first = new ModuleBuilder("first").Component<Position>().Build();
        var duplicate = new ModuleBuilder("first").Component<Velocity>().Build();
        var second = new ModuleBuilder("second").Component<Position>().Component<Velocity>().Build();
        var builder = new WorldBuilder().AddModule(first);
        Assert.Throws<ArgumentException>(() => builder.AddModule(first));
        Assert.Throws<ArgumentException>(() => builder.AddModule(duplicate));

        using var world = builder.AddModule(second).Build();
        var entity = world.Spawn();
        world.Insert(entity, new Position(7));
        world.Insert(entity, new Velocity(3));
        Assert.Equal(7, world.Get<Position>(entity).Value);
        Assert.Equal(3, world.Get<Velocity>(entity).Value);
    }

    private sealed class Owner(bool throws = false) : IDisposable
    {
        internal int Disposals;
        public void Dispose()
        {
            Disposals++;
            if (throws)
                throw new InvalidOperationException("cleanup failure");
        }
    }
    private sealed class OtherOwner : IDisposable
    {
        internal int Disposals;
        public void Dispose() => Disposals++;
    }

    [Fact]
    public void Resource_ownership_replacement_take_and_failed_cleanup_preserve_single_ownership()
    {
        var module = new ModuleBuilder("resources").ManagedResource<Owner>().ManagedResource<OtherOwner>().Build();
        var world = new WorldBuilder().AddModule(module).Build();
        var borrowed = new Owner();
        world.InsertResource(borrowed);
        var owned = new Owner();
        world.InsertOwnedResource(owned);
        Assert.Equal(0, borrowed.Disposals);
        Assert.Throws<InvalidOperationException>(() => world.InsertResource(owned));
        Assert.Same(owned, world.TakeResource<Owner>());
        Assert.Equal(0, owned.Disposals);
        var replaced = new Owner();
        world.InsertOwnedResource(replaced);
        var failing = new Owner(throws: true);
        world.InsertOwnedResource(failing);
        Assert.Equal(1, replaced.Disposals);
        var other = new OtherOwner();
        world.InsertOwnedResource(other);
        Assert.Throws<AggregateException>(() => world.Dispose());
        world.Dispose();
        Assert.Equal(1, failing.Disposals);
        Assert.Equal(1, other.Disposals);
        Assert.Equal(0, owned.Disposals);
    }
}
