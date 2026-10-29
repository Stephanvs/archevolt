using System.Runtime.InteropServices;

namespace Archevolt;

internal sealed unsafe class Table : IDisposable
{
    private readonly int[] _ids;
    private readonly nint[] _values;
    private readonly nint[] _added;
    private readonly nint[] _changed;
    private nint _slab;
    private nint _entities;
    internal readonly ComponentDescriptor[] Components;
    internal int Count;
    internal int Capacity { get; private set; }
    internal long Bytes { get; private set; }

    internal Table(int[] ids, ComponentDescriptor[] components)
    {
        _ids = ids;
        Components = components;
        _values = new nint[components.Length];
        _added = new nint[components.Length];
        _changed = new nint[components.Length];
    }

    internal Span<EntityKey> Entities => new((void*)_entities, Count);
    internal Span<T> Values<T>(int column) where T : unmanaged => new((void*)_values[column], Count);
    internal Span<ulong> Added(int column) => new((void*)_added[column], Count);
    internal Span<ulong> Changed(int column) => new((void*)_changed[column], Count);
    internal Span<byte> ValueBytes(int column, int row) =>
        new((byte*)_values[column] + (nint)row * Components[column].Size, Components[column].Size);

    internal int Append(EntityKey key)
    {
        if (Count == Capacity)
            Reallocate(Math.Max(8, checked(Capacity * 2)));
        int row = Count++;
        Entities[row] = key;
        return row;
    }

    internal void Reserve(int additional)
    {
        int needed = checked(Count + additional);
        if (needed > Capacity)
            Reallocate(Math.Max(needed, Math.Max(8, checked(Capacity * 2))));
    }

    internal void SwapRemove(int row, EntityTable entities)
    {
        int last = Count - 1;
        if (row != last)
        {
            for (int i = 0; i < Components.Length; i++)
            {
                if (!Components[i].IsTag)
                    ValueBytes(i, last).CopyTo(ValueBytes(i, row));
                if (Components[i].Tracked)
                {
                    Added(i)[row] = Added(i)[last];
                    Changed(i)[row] = Changed(i)[last];
                }
            }
            EntityKey moved = Entities[last];
            Entities[row] = moved;
            entities.Entry(moved.Index).Row = row;
        }
        Count--;
    }

    internal void CopyRowTo(int source, Table target, int destination)
    {
        for (int i = 0; i < _ids.Length; i++)
        {
            int targetColumn = Array.BinarySearch(target._ids, _ids[i]);
            if (targetColumn < 0)
                continue;
            if (!Components[i].IsTag)
                ValueBytes(i, source).CopyTo(target.ValueBytes(targetColumn, destination));
            if (Components[i].Tracked)
            {
                target.Added(targetColumn)[destination] = Added(i)[source];
                target.Changed(targetColumn)[destination] = Changed(i)[source];
            }
        }
    }

    internal void Trim()
    {
        if (Count != Capacity)
            Reallocate(Count);
    }

    private void Reallocate(int capacity)
    {
        long bytes = Place(capacity, 0, move: false);
        // Not cleared: every caller writes each value and tick of an appended row before anything reads it.
        nint slab = bytes == 0 ? 0 : (nint)NativeMemory.AlignedAlloc(checked((nuint)bytes), 64);
        Place(capacity, slab, move: true);
        NativeMemory.AlignedFree((void*)_slab);
        _slab = slab;
        Capacity = capacity;
        Bytes = bytes;
    }

    private long Place(int capacity, nint slab, bool move)
    {
        long offset = 0;
        Column(ref _entities, sizeof(EntityKey));
        for (int i = 0; i < Components.Length; i++)
        {
            if (!Components[i].IsTag)
                Column(ref _values[i], Components[i].Size);
            if (Components[i].Tracked)
            {
                Column(ref _added[i], sizeof(ulong));
                Column(ref _changed[i], sizeof(ulong));
            }
        }
        return offset;

        void Column(ref nint column, int size)
        {
            if (move)
            {
                nint target = slab + (nint)offset;
                Buffer.MemoryCopy((void*)column, (void*)target, (long)capacity * size, (long)Count * size);
                column = target;
            }
            offset = checked(offset + (long)capacity * size + 63) & ~63L;
        }
    }

    public void Dispose()
    {
        NativeMemory.AlignedFree((void*)_slab);
        _slab = 0;
        _entities = 0;
        Array.Clear(_values);
        Array.Clear(_added);
        Array.Clear(_changed);
        Count = 0;
        Capacity = 0;
        Bytes = 0;
    }
}

internal sealed class Archetype
{
    internal readonly int Index;
    internal readonly int[] Ids;
    internal readonly ulong[] Mask;
    internal readonly ComponentDescriptor[] Components;
    internal readonly Table Table;
    internal readonly Dictionary<int, Archetype> AddTransitions = [];
    internal readonly Dictionary<int, Archetype> RemoveTransitions = [];

    internal Archetype(int index, int[] ids, ComponentRegistry registry)
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
        Table = new Table(ids, Components);
    }

    internal bool Has(int component) => (Mask[component / 64] & (1UL << (component % 64))) != 0;
}
