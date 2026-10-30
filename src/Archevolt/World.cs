using System.Buffers;
using System.Runtime.InteropServices;

namespace Archevolt;

public sealed partial class World : IDisposable
{
    private static readonly object s_identityGate = new();
    private static long s_nextWorld;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<ulong[], Archetype> _archetypeByMask = new(ArchetypeMaskComparer.Instance);
    private readonly Dictionary<int, BundleBinding> _bundles = [];
    private readonly Archetype _empty;
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
        _empty = GetArchetype(NewMask());
        foreach (var descriptor in builder.Bundles)
        {
            if (_bundles.ContainsKey(descriptor.TypeKey))
                continue;
            int[] ids = descriptor.Components.Select(c => Registry.Id(c.TypeKey)).ToArray();
            var mask = NewMask();
            foreach (int id in ids)
                mask[id / 64] |= 1UL << (id % 64);
            _bundles.Add(descriptor.TypeKey, new BundleBinding(descriptor, GetArchetype(mask), ids));
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
            int row = _empty.Table.Append(new EntityKey(entity.Index, entity.Generation));
            Entities.Materialize(entity, _empty.Index, row);
            return entity;
        }
        catch { Entities.Release(entity.Index); throw; }
    }

    // Writes the bundle straight into its new row. If the write fails, the row and the reservation are removed.
    // The world counts as running during the write, so a bundle's Write cannot reach the world and move the row.
    public Entity Spawn<TBundle>(in TBundle bundle) where TBundle : unmanaged, IBundle<TBundle>
    {
        RequireIdle();
        var binding = GetBundle<TBundle>();
        var table = binding.Archetype.Table;
        ulong tick = checked(_tick + 1);
        Entity entity = Entities.Reserve(Id);
        int row = table.Count;
        _running = true;
        try
        {
            table.Append(new EntityKey(entity.Index, entity.Generation));
            var writer = new BundleWriter(binding.Descriptor, table, binding.Columns, row, tick);
            TBundle.Write(in bundle, ref writer);
            writer.Complete();
        }
        catch
        {
            table.Count = row;
            Entities.Release(entity.Index);
            throw;
        }
        finally { _running = false; }
        _tick = tick;
        Entities.Materialize(entity, binding.Archetype.Index, row);
        return entity;
    }

    // Spawns bundles[i] as entities[i] with one reservation and one table growth for the whole batch.
    // If any bundle fails to write, no entity from the batch is spawned and every handle is cleared.
    public void SpawnBatch<TBundle>(ReadOnlySpan<TBundle> bundles, Span<Entity> entities) where TBundle : unmanaged, IBundle<TBundle>
    {
        RequireIdle();
        if (entities.Length != bundles.Length)
            throw new ArgumentException("Provide one entity slot per bundle.", nameof(entities));
        var binding = GetBundle<TBundle>();
        if (bundles.IsEmpty)
            return;
        var archetype = binding.Archetype;
        var table = archetype.Table;
        ulong tick = checked(_tick + 1);
        Entities.Reserve(Id, entities);
        int first = table.Count;
        _running = true;
        try
        {
            table.Reserve(bundles.Length);
            for (int i = 0; i < bundles.Length; i++)
            {
                int row = table.Append(new EntityKey(entities[i].Index, entities[i].Generation));
                var writer = new BundleWriter(binding.Descriptor, table, binding.Columns, row, tick);
                TBundle.Write(in bundles[i], ref writer);
                writer.Complete();
            }
        }
        catch
        {
            table.Count = first;
            foreach (Entity entity in entities)
                Entities.Release(entity.Index);
            entities.Clear();
            throw;
        }
        finally { _running = false; }
        _tick = tick;
        for (int i = 0; i < entities.Length; i++)
            Entities.Materialize(entities[i], archetype.Index, first + i);
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
        if (!Entities.TryLocate(entity, Id, out int archetypeId, out int row))
            return false;
        int id = Registry.Id<T>();
        if (Registry.Components[id].IsTag)
            throw new InvalidOperationException("Tags have no stored value. Use Has<T>.");
        var archetype = Archetypes[archetypeId];
        int column = archetype.Columns[id];
        if (column < 0)
            return false;
        value = archetype.Table.Values<T>(column)[row];
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

    internal void ExitRun()
    {
        Entities.Flush();
        _running = false;
    }
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

    private ulong[] NewMask() => new ulong[(Registry.Components.Length + 63) / 64];

    // The dictionary keeps the mask as its key, so callers pass a mask they no longer write to.
    private Archetype GetArchetype(ulong[] mask)
    {
        if (_archetypeByMask.TryGetValue(mask, out var result))
            return result;
        result = new Archetype(Archetypes.Count, mask, Registry);
        _archetypeByMask.Add(mask, result);
        Archetypes.Add(result);
        return result;
    }

    private ArchetypeEdge Transition(Archetype source, int id, bool add)
    {
        var cache = add ? source.AddTransitions : source.RemoveTransitions;
        if (cache.TryGetValue(id, out var edge))
            return edge;
        var mask = (ulong[])source.Mask.Clone();
        if (add)
            mask[id / 64] |= 1UL << (id % 64);
        else
            mask[id / 64] &= ~(1UL << (id % 64));
        edge = source.EdgeTo(GetArchetype(mask));
        cache.Add(id, edge);
        return edge;
    }

    private void Move(Entity entity, ArchetypeEdge edge)
    {
        ref var entry = ref Entities.Entry(entity.Index);
        var source = Archetypes[entry.ArchetypeId].Table;
        var target = edge.Target;
        int sourceRow = entry.Row;
        int row = target.Table.Append(new EntityKey(entity.Index, entity.Generation));
        source.CopyRowTo(sourceRow, target.Table, row, edge.TargetColumns);
        source.SwapRemove(sourceRow, Entities);
        Entities.Relocate(entity.Index, target.Index, row);
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
        // Columns[i] is the bundle archetype's table column for member i, so an immediate spawn writes without lookup.
        internal readonly int[] Columns = Array.ConvertAll(Ids, id => Archetype.Columns[id]);
        internal readonly Dictionary<int, ArchetypeEdge> Destinations = [];
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
