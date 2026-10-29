using System.Buffers;
using System.Runtime.InteropServices;

namespace Archevolt;

public sealed partial class World : IDisposable
{
    private static readonly object s_identityGate = new();
    private static long s_nextWorld;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<string, Archetype> _archetypeByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<int, BundleBinding> _bundles = [];
    private ulong _tick;
    private bool _disposed;
    private bool _running;
    private WorkerPool? _workers;
    internal readonly long Id;
    internal readonly ComponentRegistry Registry;
    internal readonly EntityTable Entities;
    internal readonly List<Archetype> Archetypes = [];
    internal readonly HashSet<BoundSchedule> Schedules = [];
    internal readonly int MaxConcurrency;
    public long NativeBytes
    {
        get
        {
            RequireIdle();
            long bytes = 0;
            foreach (var archetype in Archetypes)
                bytes += archetype.Table.Bytes;
            return bytes;
        }
    }

    internal World(WorldBuilder builder)
    {
        lock (s_identityGate)
            Id = s_nextWorld = checked(s_nextWorld + 1);
        MaxConcurrency = builder.MaxConcurrency;
        CommandCapacity = builder.CommandBytes;
        Registry = new ComponentRegistry(builder.Components);
        Entities = new EntityTable(builder.EntityCapacity);
        foreach (var descriptor in builder.Bundles)
        {
            if (_bundles.ContainsKey(descriptor.TypeKey))
                continue;
            int[] ids = descriptor.Components.Select(c => Registry.Id(c.TypeKey)).ToArray();
            int[] sorted = (int[])ids.Clone();
            Array.Sort(sorted);
            _bundles.Add(descriptor.TypeKey, new BundleBinding(descriptor, GetArchetype(sorted), ids));
        }
        InitializeResources(builder.Resources);
        InitializeMessages(builder.Messages, builder.MessageCapacity);
    }

    public Entity Spawn()
    {
        RequireIdle();
        NextTick();
        Entity entity = Entities.Reserve(Id);
        try
        {
            var archetype = GetArchetype([]);
            int row = archetype.Table.Append(new EntityKey(entity.Index, entity.Generation));
            Entities.Materialize(entity, archetype.Index, row);
            return entity;
        }
        catch { Entities.Release(entity.Index); throw; }
    }

    public Entity Spawn<TBundle>(in TBundle bundle) where TBundle : unmanaged, IBundle<TBundle>
    {
        RequireIdle();
        if (!_bundles.TryGetValue(TypeKey<TBundle>.Value, out var binding))
            throw new InvalidOperationException($"Bundle {typeof(TBundle).Name} is not registered.");
        byte[]? rented = null;
        Span<byte> payload = binding.Descriptor.PayloadBytes <= 1024
            ? stackalloc byte[binding.Descriptor.PayloadBytes]
            : (rented = ArrayPool<byte>.Shared.Rent(binding.Descriptor.PayloadBytes)).AsSpan(0, binding.Descriptor.PayloadBytes);
        try
        {
            var writer = new BundleWriter(binding.Descriptor, payload);
            TBundle.Write(in bundle, ref writer);
            writer.Complete();
            ulong tick = NextTick();
            Entity entity = Entities.Reserve(Id);
            try
            {
                ApplySpawn(entity, binding, payload, tick);
                return entity;
            }
            catch { Entities.Release(entity.Index); throw; }
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public void InsertBundle<TBundle>(Entity entity, in TBundle bundle) where TBundle : unmanaged, IBundle<TBundle>
    {
        RequireIdle();
        RequireAlive(entity);
        var binding = GetBundle<TBundle>();
        byte[]? rented = null;
        Span<byte> payload = binding.Descriptor.PayloadBytes <= 1024
            ? stackalloc byte[binding.Descriptor.PayloadBytes]
            : (rented = ArrayPool<byte>.Shared.Rent(binding.Descriptor.PayloadBytes)).AsSpan(0, binding.Descriptor.PayloadBytes);
        try
        {
            var writer = new BundleWriter(binding.Descriptor, payload);
            TBundle.Write(in bundle, ref writer);
            writer.Complete();
            ApplyBundle(entity, binding, payload, NextTick());
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public bool Contains(Entity entity)
    {
        RequireIdle();
        return Entities.IsAlive(entity, Id);
    }

    public bool Has<T>(Entity entity) where T : unmanaged
    {
        RequireIdle();
        RequireAlive(entity);
        return Archetypes[Entities.Entry(entity.Index).ArchetypeId].Has(Registry.Id<T>());
    }

    public bool TryGet<T>(Entity entity, out T value) where T : unmanaged
    {
        RequireIdle();
        value = default;
        if (!Entities.IsAlive(entity, Id))
            return false;
        int id = Registry.Id<T>();
        if (Registry.Components[id].IsTag)
            throw new InvalidOperationException("Tags have no stored value. Use Has<T>.");
        ref var entry = ref Entities.Entry(entity.Index);
        var archetype = Archetypes[entry.ArchetypeId];
        int column = Array.BinarySearch(archetype.Ids, id);
        if (column < 0)
            return false;
        value = archetype.Table.Values<T>(column)[entry.Row];
        return true;
    }

    public T Get<T>(Entity entity) where T : unmanaged =>
        TryGet<T>(entity, out var value) ? value : throw new InvalidOperationException("Entity or component does not exist.");

    public void Insert<T>(Entity entity, in T value) where T : unmanaged
    {
        RequireIdle();
        RequireAlive(entity);
        int id = Registry.Id<T>();
        ReadOnlySpan<byte> payload = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1));
        ApplyInsert(entity, id, payload[..Registry.Components[id].Size], NextTick());
    }

    public bool Remove<T>(Entity entity) where T : unmanaged
    {
        RequireIdle();
        RequireAlive(entity);
        int id = Registry.Id<T>();
        var source = Archetypes[Entities.Entry(entity.Index).ArchetypeId];
        if (!source.Has(id))
            return false;
        return ApplyRemove(entity, id, NextTick(), optional: true);
    }

    public bool Despawn(Entity entity)
    {
        RequireIdle();
        if (!Entities.IsAlive(entity, Id))
            return false;
        return ApplyDespawn(entity, NextTick(), optional: true);
    }

    public void TrimExcess()
    {
        RequireIdle();
        foreach (var archetype in Archetypes)
            archetype.Table.Trim();
    }

    internal void RequireIdle()
    {
        RequireOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_running)
            throw new InvalidOperationException("This operation requires an idle world.");
    }

    internal void RequireOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("This operation must run on the world's owner thread.");
    }

    internal void RequireAlive(Entity entity)
    {
        if (!Entities.IsAlive(entity, Id))
            throw new InvalidOperationException("The entity is stale, reserved, or belongs to another world.");
    }

    internal ulong NextTick() => _tick = checked(_tick + 1);

    internal void EnterRun()
    {
        RequireIdle();
        _running = true;
    }

    internal void ExitRun() => _running = false;
    internal bool IsRunning => _running && !_disposed;
    internal WorkerPool Workers
    {
        get
        {
            RequireOwner();
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _workers ??= new WorkerPool(MaxConcurrency);
        }
    }

    private Archetype GetArchetype(int[] ids)
    {
        string key = string.Join(",", ids);
        if (_archetypeByKey.TryGetValue(key, out var result))
            return result;
        result = new Archetype(Archetypes.Count, ids, Registry);
        _archetypeByKey.Add(key, result);
        Archetypes.Add(result);
        return result;
    }

    private Archetype Transition(Archetype source, int id, bool add)
    {
        var cache = add ? source.AddTransitions : source.RemoveTransitions;
        if (cache.TryGetValue(id, out var result))
            return result;
        result = GetArchetype(TransitionIds(source, id, add));
        cache.Add(id, result);
        return result;
    }

    // Kept out of Transition: a lambda capturing id would allocate its closure on every call, including cache hits.
    private static int[] TransitionIds(Archetype source, int id, bool add) =>
        add ? source.Ids.Append(id).Order().ToArray() : source.Ids.Where(i => i != id).ToArray();

    private void Move(Entity entity, Archetype destination)
    {
        ref var entry = ref Entities.Entry(entity.Index);
        var source = Archetypes[entry.ArchetypeId].Table;
        int sourceRow = entry.Row;
        int row = destination.Table.Append(new EntityKey(entity.Index, entity.Generation));
        source.CopyRowTo(sourceRow, destination.Table, row);
        source.SwapRemove(sourceRow, Entities);
        Entities.Relocate(entity.Index, destination.Index, row);
    }

    private static void Stamp(Table table, int column, int row, ulong tick, bool added)
    {
        if (!table.Components[column].Tracked)
            return;
        if (added)
            table.Added(column)[row] = tick;
        table.Changed(column)[row] = tick;
    }

    public void Dispose()
    {
        RequireOwner();
        if (_disposed)
            return;
        RequireIdle();
        if (Schedules.Count != 0)
            throw new InvalidOperationException("Dispose all bound schedules before disposing their world.");
        _workers?.Dispose();
        foreach (var archetype in Archetypes)
            archetype.Table.Dispose();
        _disposed = true;
        DisposeResources();
    }

    internal sealed record BundleBinding(BundleDescriptor Descriptor, Archetype Archetype, int[] Ids)
    {
        internal readonly Dictionary<int, Archetype> Destinations = [];
    }
}

public sealed partial class WorldBuilder
{
    private readonly HashSet<string> _modules = new(StringComparer.Ordinal);
    internal readonly List<ComponentDescriptor> Components = [];
    internal readonly List<BundleDescriptor> Bundles = [];
    internal readonly List<ResourceDescriptor> Resources = [];
    internal int EntityCapacity;
    internal int MaxConcurrency = 1;
    internal int CommandBytes = 4096;

    public WorldBuilder AddModule(ModuleDescriptor module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (!_modules.Add(module.Name))
            throw new ArgumentException($"Module '{module.Name}' is already registered.", nameof(module));
        Components.AddRange(module.Components);
        Bundles.AddRange(module.Bundles);
        Resources.AddRange(module.Resources);
        Messages.AddRange(module.Messages);
        return this;
    }

    public WorldBuilder AddComponent<T>(bool tracked = false, bool tag = false) where T : unmanaged
    {
        Components.Add(ComponentDescriptor.Of<T>(tracked: tracked, tag: tag));
        return this;
    }

    public WorldBuilder AddBundle<T>() where T : unmanaged, IBundle<T>
    {
        Bundles.Add(T.Descriptor);
        Components.AddRange(T.Descriptor.Components);
        return this;
    }

    public WorldBuilder AddResource<T>(bool tracked = false, bool ownerThread = false) where T : unmanaged
    {
        Resources.Add(ResourceDescriptor.Of<T>(tracked, ownerThread));
        return this;
    }

    public WorldBuilder AddManagedResource<T>(bool tracked = false) where T : class
    {
        Resources.Add(ResourceDescriptor.OfManaged<T>(tracked));
        return this;
    }

    public WorldBuilder UseParallelExecution(int maxConcurrency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
        MaxConcurrency = maxConcurrency;
        return this;
    }

    public WorldBuilder Reserve(int entities = 0, int commandBytesPerProducer = 0, int messagesPerType = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entities);
        ArgumentOutOfRangeException.ThrowIfNegative(commandBytesPerProducer);
        ArgumentOutOfRangeException.ThrowIfNegative(messagesPerType);
        EntityCapacity = entities;
        CommandBytes = commandBytesPerProducer;
        MessageCapacity = messagesPerType;
        return this;
    }

    public World Build() => new(this);
}
