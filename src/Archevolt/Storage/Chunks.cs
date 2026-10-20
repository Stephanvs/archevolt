using System.Runtime.InteropServices;

namespace Archevolt;

internal sealed unsafe class ChunkPool : IDisposable
{
    private readonly Dictionary<int, Stack<nint>> _free = [];
    private readonly HashSet<nint> _owned = [];
    internal long AllocatedBytes { get; private set; }

    internal nint Rent(int bytes)
    {
        nint address;
        if (_free.TryGetValue(bytes, out var available) && available.Count != 0)
            address = available.Pop();
        else
        {
            long allocatedBytes = checked(AllocatedBytes + bytes);
            address = (nint)NativeMemory.AlignedAlloc((nuint)bytes, 64);
            if (address == 0)
                throw new OutOfMemoryException();
            try { _owned.Add(address); }
            catch { NativeMemory.AlignedFree((void*)address); throw; }
            AllocatedBytes = allocatedBytes;
        }
        NativeMemory.Clear((void*)address, (nuint)bytes);
        return address;
    }

    internal void Return(nint address, int bytes)
    {
        if (!_free.TryGetValue(bytes, out var available))
            _free.Add(bytes, available = new Stack<nint>());
        if (available.Count < 64)
            available.Push(address);
        else
        {
            _owned.Remove(address);
            NativeMemory.AlignedFree((void*)address);
            AllocatedBytes -= bytes;
        }
    }

    public void Dispose()
    {
        foreach (nint address in _owned)
            NativeMemory.AlignedFree((void*)address);
        _owned.Clear();
        _free.Clear();
        AllocatedBytes = 0;
    }
}

internal sealed class ChunkLayout
{
    internal readonly int Capacity;
    internal readonly int Bytes;
    internal readonly int[] Values;
    internal readonly int[] Added;
    internal readonly int[] Changed;

    internal ChunkLayout(ComponentDescriptor[] components, int targetBytes)
    {
        long rowBytes = 8;
        foreach (var component in components)
            rowBytes = checked(rowBytes + component.Size + (component.Tracked ? 16 : 0));
        int capacity = (int)Math.Max(1, targetBytes / rowBytes);
        Values = new int[components.Length];
        Added = new int[components.Length];
        Changed = new int[components.Length];
        while (true)
        {
            int offset = Align(checked(capacity * 8));
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                Values[i] = component.IsTag ? -1 : offset;
                if (!component.IsTag)
                    offset = Align(checked(offset + checked(capacity * component.Size)));
                Added[i] = component.Tracked ? offset : -1;
                if (component.Tracked)
                    offset = Align(checked(offset + checked(capacity * 8)));
                Changed[i] = component.Tracked ? offset : -1;
                if (component.Tracked)
                    offset = Align(checked(offset + checked(capacity * 8)));
            }
            if (offset <= targetBytes || capacity == 1)
            {
                Capacity = capacity;
                Bytes = offset;
                return;
            }
            capacity--;
        }
    }

    private static int Align(int value) => checked(value + 63) & ~63;
}

internal sealed unsafe class NativeChunk : IDisposable
{
    private readonly ChunkPool _pool;
    private nint _address;
    internal readonly Archetype Archetype;
    internal int Count;

    internal NativeChunk(Archetype archetype, ChunkPool pool)
    {
        Archetype = archetype;
        _pool = pool;
        _address = pool.Rent(archetype.Layout.Bytes);
    }

    internal Span<EntityKey> Entities => new((void*)_address, Count);
    internal Span<T> Values<T>(int column) where T : unmanaged =>
        new((byte*)_address + Archetype.Layout.Values[column], Count);
    internal Span<ulong> Added(int column) => new((byte*)_address + Archetype.Layout.Added[column], Count);
    internal Span<ulong> Changed(int column) => new((byte*)_address + Archetype.Layout.Changed[column], Count);
    internal Span<byte> ValueBytes(int column, int row) =>
        new((byte*)_address + Archetype.Layout.Values[column] + row * Archetype.Components[column].Size,
            Archetype.Components[column].Size);

    internal void CopyRowTo(int source, NativeChunk target, int destination)
    {
        for (int i = 0; i < Archetype.Ids.Length; i++)
        {
            int targetColumn = Array.BinarySearch(target.Archetype.Ids, Archetype.Ids[i]);
            if (targetColumn < 0)
                continue;
            if (!Archetype.Components[i].IsTag)
                ValueBytes(i, source).CopyTo(target.ValueBytes(targetColumn, destination));
            if (Archetype.Components[i].Tracked)
            {
                target.Added(targetColumn)[destination] = Added(i)[source];
                target.Changed(targetColumn)[destination] = Changed(i)[source];
            }
        }
    }

    public void Dispose()
    {
        if (_address == 0)
            return;
        _pool.Return(_address, Archetype.Layout.Bytes);
        _address = 0;
        Count = 0;
    }
}

internal sealed class Archetype
{
    internal readonly int Index;
    internal readonly int[] Ids;
    internal readonly ulong[] Mask;
    internal readonly ComponentDescriptor[] Components;
    internal readonly ChunkLayout Layout;
    internal readonly List<NativeChunk> Chunks = [];
    internal readonly Dictionary<int, Archetype> AddTransitions = [];
    internal readonly Dictionary<int, Archetype> RemoveTransitions = [];

    internal Archetype(int index, int[] ids, ComponentRegistry registry, int targetBytes)
    {
        Index = index;
        Ids = ids;
        Mask = new ulong[(registry.Components.Length + 63) / 64];
        Components = new ComponentDescriptor[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            Mask[ids[i] / 64] |= 1UL << (ids[i] % 64);
            Components[i] = registry.Components[ids[i]];
        }
        Layout = new ChunkLayout(Components, targetBytes);
    }

    internal bool Has(int component) => (Mask[component / 64] & (1UL << (component % 64))) != 0;

    internal (NativeChunk Chunk, int Row) Append(ChunkPool pool, EntityKey key)
    {
        NativeChunk chunk;
        if (Chunks.Count == 0 || Chunks[^1].Count == Layout.Capacity)
        {
            chunk = new NativeChunk(this, pool);
            try { Chunks.Add(chunk); }
            catch { chunk.Dispose(); throw; }
        }
        else
            chunk = Chunks[^1];
        int row = chunk.Count++;
        chunk.Entities[row] = key;
        return (chunk, row);
    }

    internal void Remove(NativeChunk chunk, int row, EntityTable entities)
    {
        var last = Chunks[^1];
        int lastRow = last.Count - 1;
        if (chunk != last || row != lastRow)
        {
            last.CopyRowTo(lastRow, chunk, row);
            EntityKey moved = last.Entities[lastRow];
            chunk.Entities[row] = moved;
            entities.Relocate(moved.Index, this, chunk, row);
        }
        last.Count--;
        if (last.Count == 0)
        {
            Chunks.RemoveAt(Chunks.Count - 1);
            last.Dispose();
        }
    }
}
