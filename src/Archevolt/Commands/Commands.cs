using System.Runtime.InteropServices;

namespace Archevolt;

internal enum CommandKind : byte { Spawn, InsertBundle, Insert, Remove, TryRemove, Despawn, TryDespawn }
internal readonly record struct CommandRecord(CommandKind Kind, Entity Target, int TypeKey, int Offset, int Length);

internal sealed class CommandBuffer
{
    private byte[] _bytes;
    private CommandRecord[] _records;
    internal int Count { get; private set; }
    private int _length;

    internal CommandBuffer(int capacity)
    {
        _bytes = new byte[capacity];
        _records = new CommandRecord[Math.Max(4, capacity / 32)];
    }

    internal Span<byte> Prepare(int bytes)
    {
        int needed = checked(_length + bytes);
        if (needed > _bytes.Length)
            Array.Resize(ref _bytes, Math.Max(needed, checked(Math.Max(64, _bytes.Length * 2))));
        if (Count == _records.Length)
            Array.Resize(ref _records, checked(_records.Length * 2));
        return _bytes.AsSpan(_length, bytes);
    }

    internal void Commit(CommandKind kind, Entity target, int key, int bytes)
    {
        _records[Count++] = new CommandRecord(kind, target, key, _length, bytes);
        _length += bytes;
    }

    internal CommandRecord Record(int index) => _records[index];
    internal ReadOnlySpan<byte> Payload(CommandRecord record) => _bytes.AsSpan(record.Offset, record.Length);
    internal void Clear() { Count = 0; _length = 0; }
}

public readonly ref partial struct SystemBinder
{
    public CommandsHandle Commands()
    {
        var instance = Instance;
        if (!instance.Descriptor.EmitsCommands)
            throw new InvalidOperationException("Commands access was not declared.");
        return new CommandsHandle(instance);
    }
}

public readonly struct CommandsHandle
{
    private readonly SystemInstance _owner;
    internal CommandsHandle(SystemInstance owner) => _owner = owner;
    public Commands Open(SystemRun run)
    {
        run.Require(_owner);
        if (run.IsPartitioned)
            throw new InvalidOperationException("Commands are not permitted in partitioned execution.");
        return new Commands(_owner, run);
    }
}

public readonly ref struct Commands
{
    private readonly SystemInstance _owner;
    private readonly SystemRun _run;
    internal Commands(SystemInstance owner, SystemRun run) { _owner = owner; _run = run; }

    public Entity Spawn<TBundle>(in TBundle bundle) where TBundle : unmanaged, IBundle<TBundle>
    {
        Validate();
        var binding = _owner.World.GetBundle<TBundle>();
        var buffer = _owner.Commands!;
        var writer = new BundleWriter(binding.Descriptor, buffer.Prepare(binding.Descriptor.PayloadBytes));
        TBundle.Write(in bundle, ref writer);
        writer.Complete();
        Entity entity = _owner.World.Entities.Reserve(_owner.World.Id);
        buffer.Commit(CommandKind.Spawn, entity, binding.Descriptor.TypeKey, binding.Descriptor.PayloadBytes);
        return entity;
    }

    public void InsertBundle<TBundle>(Entity target, in TBundle bundle) where TBundle : unmanaged, IBundle<TBundle>
    {
        Validate();
        var binding = _owner.World.GetBundle<TBundle>();
        var buffer = _owner.Commands!;
        var writer = new BundleWriter(binding.Descriptor, buffer.Prepare(binding.Descriptor.PayloadBytes));
        TBundle.Write(in bundle, ref writer);
        writer.Complete();
        buffer.Commit(CommandKind.InsertBundle, target, binding.Descriptor.TypeKey, binding.Descriptor.PayloadBytes);
    }

    public void Insert<T>(Entity target, in T value) where T : unmanaged
    {
        Validate();
        var component = _owner.World.Registry.Components[_owner.World.Registry.Id<T>()];
        var buffer = _owner.Commands!;
        Span<byte> payload = buffer.Prepare(component.Size);
        if (!component.IsTag)
            MemoryMarshal.Write(payload, in value);
        buffer.Commit(CommandKind.Insert, target, component.TypeKey, component.Size);
    }

    public void Remove<T>(Entity target) where T : unmanaged => Record<T>(CommandKind.Remove, target);
    public void TryRemove<T>(Entity target) where T : unmanaged => Record<T>(CommandKind.TryRemove, target);
    public void Despawn(Entity target) => Record(CommandKind.Despawn, target, 0);
    public void TryDespawn(Entity target) => Record(CommandKind.TryDespawn, target, 0);

    private void Record<T>(CommandKind kind, Entity target) where T : unmanaged
    {
        Validate();
        _owner.World.Registry.Id<T>();
        Record(kind, target, TypeKey<T>.Value);
    }

    private void Record(CommandKind kind, Entity target, int key)
    {
        Validate();
        var buffer = _owner.Commands!;
        buffer.Prepare(0);
        buffer.Commit(kind, target, key, 0);
    }

    private void Validate() => _run.Require(_owner);
}

public sealed class CommandPlaybackException : Exception
{
    internal CommandPlaybackException(string producer, int index, Exception inner)
        : base($"Command {index} from system '{producer}' failed.", inner)
    {
        Producer = producer;
        CommandIndex = index;
    }
    public string Producer { get; }
    public int CommandIndex { get; }
}

public sealed partial class World
{
    internal readonly int CommandCapacity;

    internal BundleBinding GetBundle<TBundle>() where TBundle : unmanaged, IBundle<TBundle> =>
        _bundles.TryGetValue(TypeKey<TBundle>.Value, out var binding)
            ? binding : throw new InvalidOperationException($"Bundle {typeof(TBundle).Name} is not registered.");

    internal void FlushCommands(SystemInstance[] instances, IReadOnlyList<int> nodeOrder, ulong tick)
    {
        for (int ordinal = 0; ordinal < nodeOrder.Count; ordinal++)
        {
            var instance = instances[nodeOrder[ordinal]];
            var buffer = instance.Commands;
            if (buffer is null)
                continue;
            for (int index = 0; index < buffer.Count; index++)
            {
                var command = buffer.Record(index);
                try { Apply(command, buffer.Payload(command), tick); }
                catch (Exception exception)
                {
                    DiscardCommands(instances, nodeOrder);
                    throw new CommandPlaybackException(instance.Key, index, exception);
                }
            }
            buffer.Clear();
        }
    }

    internal void DiscardCommands(SystemInstance[] instances, IReadOnlyList<int> nodeOrder)
    {
        for (int ordinal = 0; ordinal < nodeOrder.Count; ordinal++)
        {
            var buffer = instances[nodeOrder[ordinal]].Commands;
            if (buffer is null)
                continue;
            for (int i = 0; i < buffer.Count; i++)
            {
                var command = buffer.Record(i);
                if (command.Kind == CommandKind.Spawn)
                    Entities.Cancel(command.Target, Id);
            }
            buffer.Clear();
        }
    }

    private void Apply(CommandRecord command, ReadOnlySpan<byte> payload, ulong tick)
    {
        switch (command.Kind)
        {
            case CommandKind.Spawn:
                ApplySpawn(command.Target, _bundles[command.TypeKey], payload, tick);
                break;
            case CommandKind.InsertBundle:
                ApplyBundle(command.Target, _bundles[command.TypeKey], payload, tick);
                break;
            case CommandKind.Insert:
                ApplyInsert(command.Target, Registry.Id(command.TypeKey), payload, tick);
                break;
            case CommandKind.Remove:
            case CommandKind.TryRemove:
                ApplyRemove(command.Target, Registry.Id(command.TypeKey), tick, command.Kind == CommandKind.TryRemove);
                break;
            case CommandKind.Despawn:
            case CommandKind.TryDespawn:
                ApplyDespawn(command.Target, tick, command.Kind == CommandKind.TryDespawn);
                break;
            default:
                throw new InvalidOperationException("Unrecognized command kind.");
        }
    }

    private void ApplySpawn(Entity entity, BundleBinding binding, ReadOnlySpan<byte> payload, ulong tick)
    {
        ref var reservation = ref Entities.Entry(entity.Index);
        if (entity.WorldId != Id || reservation.Generation != entity.Generation || reservation.State != EntityState.Reserved)
            throw new InvalidOperationException("Spawn reservation is no longer valid.");
        var location = binding.Archetype.Append(_pool, new EntityKey(entity.Index, entity.Generation));
        WriteBundle(binding, payload, location.Chunk, location.Row, null, tick);
        Entities.Materialize(entity, binding.Archetype, location.Chunk, location.Row);
    }

    private void ApplyBundle(Entity entity, BundleBinding binding, ReadOnlySpan<byte> payload, ulong tick)
    {
        RequireAlive(entity);
        ref var entry = ref Entities.Entry(entity.Index);
        var source = entry.Archetype!;
        if (!binding.Destinations.TryGetValue(source.Index, out var destination))
        {
            destination = GetArchetype(source.Ids.Union(binding.Ids).Order().ToArray());
            binding.Destinations.Add(source.Index, destination);
        }
        if (source != destination)
            Move(entity, destination);
        WriteBundle(binding, payload, entry.Chunk!, entry.Row, source, tick);
    }

    private static void WriteBundle(BundleBinding binding, ReadOnlySpan<byte> payload, NativeChunk chunk, int row, Archetype? source, ulong tick)
    {
        int offset = 0;
        for (int i = 0; i < binding.Ids.Length; i++)
        {
            int column = Array.BinarySearch(chunk.Archetype.Ids, binding.Ids[i]);
            var component = binding.Descriptor.Components[i];
            if (!component.IsTag)
                payload.Slice(offset, component.Size).CopyTo(chunk.ValueBytes(column, row));
            Stamp(chunk, column, row, tick, source is null || !source.Has(binding.Ids[i]));
            offset += component.Size;
        }
    }

    private void ApplyInsert(Entity entity, int id, ReadOnlySpan<byte> payload, ulong tick)
    {
        RequireAlive(entity);
        if (payload.Length != Registry.Components[id].Size)
            throw new InvalidOperationException("Component payload size is invalid.");
        ref var entry = ref Entities.Entry(entity.Index);
        bool added = !entry.Archetype!.Has(id);
        if (added)
            Move(entity, Transition(entry.Archetype!, id, add: true));
        int column = Array.BinarySearch(entry.Archetype!.Ids, id);
        if (!Registry.Components[id].IsTag)
            payload.CopyTo(entry.Chunk!.ValueBytes(column, entry.Row));
        Stamp(entry.Chunk!, column, entry.Row, tick, added);
    }

    private bool ApplyRemove(Entity entity, int id, ulong tick, bool optional)
    {
        if (!Entities.IsAlive(entity, Id) || !Entities.Entry(entity.Index).Archetype!.Has(id))
        {
            if (optional)
                return false;
            throw new InvalidOperationException("Remove target or component is absent.");
        }
        var source = Entities.Entry(entity.Index).Archetype!;
        RecordRemoval(Registry.Components[id].TypeKey, entity, tick);
        Move(entity, Transition(source, id, add: false));
        return true;
    }

    private bool ApplyDespawn(Entity entity, ulong tick, bool optional)
    {
        if (!Entities.IsAlive(entity, Id))
        {
            if (optional)
                return false;
            throw new InvalidOperationException("Despawn target is stale or absent.");
        }
        ref var entry = ref Entities.Entry(entity.Index);
        foreach (var component in entry.Archetype!.Components)
            RecordRemoval(component.TypeKey, entity, tick);
        entry.Archetype!.Remove(entry.Chunk!, entry.Row, Entities);
        Entities.Release(entity.Index);
        return true;
    }
}
