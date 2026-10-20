namespace Archevolt.Tests;

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

    [Fact]
    public void Structural_trace_matches_dictionary_across_chunk_migration_and_swap_removal()
    {
        using var world = new WorldBuilder().AddBundle<Particle>().AddComponent<Extra>()
            .AddComponent<Frozen>(tag: true).WithChunkBytes(256).Build();
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
